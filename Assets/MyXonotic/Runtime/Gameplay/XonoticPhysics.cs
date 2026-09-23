using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Pure, side-effect-free port of the Xonotic 0.8.6 player movement model
    /// (xonotic-data.pk3dir: qcsrc/common/physics/player.qc and
    /// qcsrc/ecs/systems/physics.qc, GPL-2.0-or-later, same licence as this
    /// project's Xonotic content). All constants come from
    /// <c>physicsX.cfg</c> ("current Xonotic physics") and are converted from
    /// quake units to metres with 32 qu = 1 m. Kept free of MonoBehaviour so the
    /// Editor test runner can check the numbers without a scene.
    ///
    /// Frame model (matches sys_phys_simulate):
    ///   ground: friction (k9er dt-independent replica) → PM_Accelerate(15)
    ///   air:    PM_Accelerate with QW-style speed clamp (airaccel_qw −0.8,
    ///           stretchfactor 2), strafe blend (airstrafeaccelerate 18 /
    ///           maxairstrafespeed 100), airstopaccelerate 3, then CPM air control
    ///           (aircontrol 100, power 2) — this is what makes bunny-hopping and
    ///           mid-air turning feel like the original.
    ///   jump:   +jumpvelocity (260 qu/s) and holding jump keeps hopping on landing
    ///           (sv_track_canjump 0 → auto-hop, ideal for a touch JUMP button).
    /// </summary>
    public static class XonoticPhysics
    {
        public const float Qu = 1f / 32f;

        // physicsX.cfg (qu/s and qu/s² → m/s, m/s²)
        public const float Gravity = 800f * Qu;            // sv_gravity
        public const float MaxSpeed = 360f * Qu;           // sv_maxspeed
        public const float MaxAirSpeed = 360f * Qu;        // sv_maxairspeed
        public const float StopSpeed = 100f * Qu;          // sv_stopspeed
        public const float Accelerate = 15f;               // sv_accelerate (1/s, dimensionless w.r.t. wishspeed)
        public const float AirAccelerate = 2f;             // sv_airaccelerate
        public const float Friction = 6f;                  // sv_friction
        public const float JumpVelocity = 260f * Qu;       // sv_jumpvelocity
        public const float AirAccelQw = -0.8f;             // sv_airaccel_qw
        public const float AirAccelQwStretchFactor = 2f;   // sv_airaccel_qw_stretchfactor
        public const float AirAccelSidewaysFriction = 0f;  // sv_airaccel_sideways_friction
        public const float AirStopAccelerate = 3f;         // sv_airstopaccelerate
        public const float AirStrafeAccelerate = 18f;      // sv_airstrafeaccelerate
        public const float MaxAirStrafeSpeed = 100f * Qu;  // sv_maxairstrafespeed
        public const float AirStrafeAccelQw = -0.95f;      // sv_airstrafeaccel_qw
        public const float AirControl = 100f;              // sv_aircontrol
        public const float AirControlPenalty = 0f;         // sv_aircontrol_penalty
        public const float AirControlPower = 2f;           // sv_aircontrol_power
        public const float AirSpeedLimitNonQw = 900f * Qu; // sv_airspeedlimit_nonqw
        public const float FrictionOnLand = 0f;            // sv_friction_on_land
        public const float StepHeight = 31f * Qu;          // sv_stepheight
        /// PHYS_FRICTION_REPLICA_DT: the historical server frame the friction curve replicates.
        public const float FrictionReplicaDt = 1f / 64f;

        /// Xonotic's IsMoveInDirection: how much of the 2D move input points along
        /// <paramref name="angleDeg"/> (0 = forward, ±90 = strafe). 1 when exactly
        /// aligned, 0 when 90° or more away.
        public static float IsMoveInDirection(Vector2 move, float angleDeg)
        {
            if (move.sqrMagnitude < 1e-8f) return 0f;
            // Xonotic movement vector: x = forward, y = right. Our Vector2: x = right, y = forward.
            float ang = Mathf.Rad2Deg * Mathf.Atan2(move.x, move.y);
            ang = Mathf.DeltaAngle(angleDeg, ang) / 45f;
            return ang > 1f ? 0f : ang < -1f ? 0f : 1f - Mathf.Abs(ang);
        }

        /// Geometric interpolation from player.qc.
        public static float GeomLerp(float a, float lerp, float b)
        {
            if (a == 0f) return lerp < 1f ? 0f : b;
            if (b == 0f) return lerp > 0f ? 0f : a;
            return a * Mathf.Pow(Mathf.Abs(b / a), lerp);
        }

        static float AdjustAirAccelQw(float accelqw, float factor)
        {
            float v = Mathf.Clamp(1f - (1f - Mathf.Abs(accelqw)) * factor, 0.000001f, 1f);
            return accelqw < 0f ? -v : v;
        }

        /// Ground friction, dt-independent replica of the 64 Hz server curve
        /// (physics.qc "k9er" block). Horizontal only.
        public static Vector3 GroundFriction(Vector3 velocity, float dt)
        {
            float vy = velocity.y;
            velocity.y = 0f;
            float f = velocity.magnitude;
            if (f <= 0f || Friction <= 0f) return new Vector3(0f, vy, 0f);
            const float S = StopSpeed;
            float independentGeometric = Mathf.Pow(1f - Friction * FrictionReplicaDt, dt / FrictionReplicaDt);
            float scale;
            if (S < f && f < S / independentGeometric)
            {
                float newSpeed = S - S * Friction * (dt - (FrictionReplicaDt * Mathf.Log(S / f)) / Mathf.Log(1f - Friction * FrictionReplicaDt));
                velocity = velocity / f;
                scale = newSpeed;
            }
            else if (f >= S) scale = independentGeometric;
            else scale = 1f - Friction * dt * S / f;
            scale = Mathf.Max(0f, scale);
            Vector3 r = velocity * scale;
            r.y = vy;
            return r;
        }

        /// Ground acceleration (Quake style, used by sys_phys_simulate on ground).
        public static Vector3 GroundAccelerate(Vector3 velocity, Vector3 wishDir, float wishSpeed, float dt)
        {
            float addSpeed = wishSpeed - Vector3.Dot(velocity, wishDir);
            if (addSpeed <= 0f) return velocity;
            float accelSpeed = Mathf.Min(Accelerate * dt * wishSpeed, addSpeed);
            return velocity + wishDir * accelSpeed;
        }

        /// Port of PM_Accelerate (player.qc). Horizontal components only; y is preserved.
        public static Vector3 PmAccelerate(Vector3 velocity, Vector3 wishDir, float wishSpeed, float wishSpeed0,
            float accel, float accelqw, float stretchFactor, float sideFric, float speedLimit, float dt)
        {
            float speedClamp = stretchFactor > 0f ? stretchFactor : accelqw < 0f ? 1f : -1f;
            accelqw = Mathf.Abs(accelqw);

            var dir2 = new Vector2(wishDir.x, wishDir.z);
            var velXy = new Vector2(velocity.x, velocity.z);
            float velStraight = Vector2.Dot(velXy, dir2);
            float velY = velocity.y;
            Vector2 velPerpend = velXy - velStraight * dir2;

            float step = accel * dt * wishSpeed0;
            float velXyCurrent = velXy.magnitude;
            if (speedLimit > 0f)
                accelqw = AdjustAirAccelQw(accelqw, (speedLimit - Mathf.Clamp(velXyCurrent, wishSpeed, speedLimit)) / Mathf.Max(1f * Qu, speedLimit - wishSpeed));
            float velXyForward = velXyCurrent + Mathf.Clamp(wishSpeed - velXyCurrent, 0f, step) * accelqw + step * (1f - accelqw);
            velStraight = velStraight + Mathf.Clamp(wishSpeed - velStraight, 0f, step) * accelqw + step * (1f - accelqw);

            velPerpend *= Mathf.Max(0f, 1f - dt * wishSpeed * sideFric);

            velXy = velStraight * dir2 + velPerpend;
            if (speedClamp >= 0f)
            {
                float preClamp = velXy.magnitude;
                if (preClamp > 0f)
                {
                    velXyCurrent += (velXyForward - velXyCurrent) * speedClamp;
                    if (velXyCurrent < preClamp) velXy *= velXyCurrent / preClamp;
                }
            }
            return new Vector3(velXy.x, velY, velXy.y);
        }

        /// Port of CPM_PM_Aircontrol: turns the horizontal velocity towards wishDir
        /// while holding only forward (movity), keeping speed (penalty 0).
        public static Vector3 AirControlStep(Vector3 velocity, Vector2 move, Vector3 wishDir, float wishSpeed, float dt)
        {
            float movity = IsMoveInDirection(move, 0f);
            float k = 2f * movity - 1f;
            if (k <= 0f) return velocity;
            k *= Mathf.Clamp01(wishSpeed / MaxAirSpeed);

            float vy = velocity.y;
            var v = new Vector3(velocity.x, 0f, velocity.z);
            float xySpeed = v.magnitude;
            if (xySpeed <= 0f) return velocity;
            v /= xySpeed;
            float dot = Vector3.Dot(v, wishDir);
            if (dot > 0f)
            {
                k *= Mathf.Pow(dot, AirControlPower) * dt;
                xySpeed = Mathf.Max(0f, xySpeed - AirControlPenalty * Mathf.Sqrt(Mathf.Max(0f, 1f - dot * dot)) * k);
                k *= 32f * Mathf.Abs(AirControl) * Qu; // 32 qu in the original → metres
                v = (v * xySpeed + wishDir * k).normalized;
            }
            v *= xySpeed;
            v.y = vy;
            return v;
        }

        /// Full Xonotic air-movement step (sys_phys_simulate, com_phys_air branch).
        /// <paramref name="move"/> is the raw 2D input (x right, y forward), used for
        /// the strafe/forward key mix; <paramref name="wishDir"/> is its world direction.
        public static Vector3 AirMove(Vector3 velocity, Vector2 move, Vector3 wishDir, float wishSpeed, float dt)
        {
            float airaccelqw = AirAccelQw;
            float wishSpeed0 = wishSpeed;
            wishSpeed = Mathf.Min(wishSpeed, MaxAirSpeed);
            float airaccel = AirAccelerate;
            float wishSpeed2 = wishSpeed;

            if (AirStopAccelerate > 0f)
            {
                var vxz = new Vector3(velocity.x, 0f, velocity.z);
                if (vxz.sqrMagnitude > 1e-8f)
                {
                    float dot = Vector3.Dot(vxz.normalized, wishDir);
                    if (dot < 0f) airaccel += (airaccel - AirStopAccelerate) * dot; // sinusoidal slow-down
                }
            }

            float strafity = IsMoveInDirection(move, -90f) + IsMoveInDirection(move, 90f);
            if (MaxAirStrafeSpeed > 0f) wishSpeed = Mathf.Min(wishSpeed, GeomLerp(MaxAirSpeed, strafity, MaxAirStrafeSpeed));
            if (AirStrafeAccelerate > 0f) airaccel = GeomLerp(airaccel, strafity, AirStrafeAccelerate);
            if (AirStrafeAccelQw != 0f)
                airaccelqw = ((strafity > 0.5f ? AirStrafeAccelQw : AirAccelQw) >= 0f ? 1f : -1f)
                             * (1f - GeomLerp(1f - Mathf.Abs(AirAccelQw), strafity, 1f - Mathf.Abs(AirStrafeAccelQw)));

            float sideFric = MaxAirSpeed > 0f ? AirAccelSidewaysFriction / MaxAirSpeed : 0f;
            velocity = PmAccelerate(velocity, wishDir, wishSpeed, wishSpeed0, airaccel, airaccelqw,
                AirAccelQwStretchFactor, sideFric, AirSpeedLimitNonQw, dt);
            if (AirControl != 0f) velocity = AirControlStep(velocity, move, wishDir, wishSpeed2, dt);
            return velocity;
        }

        /// Full ground step: friction then acceleration (sys_phys_simulate, com_phys_ground).
        public static Vector3 GroundMove(Vector3 velocity, Vector3 wishDir, float wishSpeed, float dt)
        {
            velocity = GroundFriction(velocity, dt);
            if (wishSpeed > 0f) velocity = GroundAccelerate(velocity, wishDir, wishSpeed, dt);
            return velocity;
        }
    }
}
