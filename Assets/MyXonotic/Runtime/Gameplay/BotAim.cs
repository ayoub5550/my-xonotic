using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// dev.18: skill-driven bot aiming, an original re-implementation of the model in
    /// Xonotic's <c>qcsrc/server/bot/default/aim.qc</c> (bot_aimdir / bot_aim). The Xonotic
    /// bot never snaps onto the target: it keeps a *desired* angle that carries a random
    /// "bad aim" offset, a slow "mouse" angle that chases it in discrete think steps, and a
    /// view angle that turns towards the mouse with a skill-limited rate. Fire is only
    /// allowed while the view is within a distance/skill dependent tolerance cone and a
    /// fire window is open. Every number below is quoted from xonotic-server.cfg
    /// (bot_ai_aimskill_*) or aim.qc and named after its cvar; no value was tuned by feel.
    ///
    /// Angle space: Unity degrees, pitch (x, positive = looking down like Xonotic's
    /// v_angle.x after the sign flip) and yaw (y). Distances are metres (32 qu = 1 m).
    /// The class is pure C# state (no Unity lifecycle) so the Editor tests can drive it
    /// with a fixed random sequence.
    /// </summary>
    public sealed class BotAim
    {
        // xonotic-server.cfg
        public const float AimSkillOffset = 1.8f;      // bot_ai_aimskill_offset: degrees of induced error
        public const float AimSkillThink = 1f;         // bot_ai_aimskill_think
        public const float AimSkillFixedRate = 15f;    // bot_ai_aimskill_fixedrate
        public const float AimSkillBlendRate = 2f;     // bot_ai_aimskill_blendrate
        public const float AimSkillMouse = 1f;         // bot_ai_aimskill_mouse
        public const bool FireTolerance = true;        // bot_ai_aimskill_firetolerance 1

        /// Xonotic "skill" cvar range; the server default is 8, our touch default is 3 (MatchSettings).
        public const int MinSkill = 1, MaxSkill = 10;

        public int Skill { get; private set; }

        /// bot_badaimoffset: random error refreshed every 0.2–0.5 s (aim.qc "bot_badaimtime").
        public Vector2 BadAimOffset { get; private set; }
        float _badAimTime;
        /// bot_mouseaim: the slow hand.
        public Vector2 MouseAim { get; private set; }
        float _aimThinkTime;
        /// v_angle: where the bot actually looks/shoots.
        public Vector2 ViewAngles { get; private set; }
        Vector2 _oldDesired;
        bool _initialised;
        /// bot_firetimer: fire is allowed while Time < FireUntil.
        public float FireUntil { get; private set; }

        public System.Func<float> Random01 = () => UnityEngine.Random.value;
        public System.Func<Vector3> RandomVec = () => UnityEngine.Random.insideUnitSphere;

        public BotAim(int skill) { SetSkill(skill); }

        public void SetSkill(int skill) => Skill = Mathf.Clamp(skill, MinSkill, MaxSkill);

        /// Reset to look straight along <paramref name="forward"/> (spawn / new target).
        public void Reset(Vector3 forward)
        {
            var a = AnglesOf(forward);
            ViewAngles = a; MouseAim = a; _oldDesired = a;
            BadAimOffset = Vector2.zero;
            _badAimTime = 0f; _aimThinkTime = 0f; FireUntil = 0f;
            _initialised = true;
        }

        /// Turn towards <paramref name="wanted"/> (world direction to the lead point) like
        /// bot_aimdir: returns the direction the bot looks along after this tick.
        /// <paramref name="hasEnemy"/> switches the enemy_factor (5 vs 2) and the walking
        /// minimum skill 4 from aim.qc ("allow turning in a more natural way when bot is walking").
        public Vector3 Update(Vector3 wanted, bool hasEnemy, float time, float deltaTime)
        {
            if (!_initialised) Reset(wanted);
            if (wanted.sqrMagnitude < 1e-6f || deltaTime <= 0f) return DirectionOf(ViewAngles);
            int skill = hasEnemy ? Skill : Mathf.Max(4, Skill);

            // bad aim offset: f = bound(0, 1 - 0.1*skill, 1); offset = randomvec * f * bot_ai_aimskill_offset; x *= 0.7
            if (time >= _badAimTime)
            {
                _badAimTime = Mathf.Max(_badAimTime + 0.2f + 0.3f * Random01(), time);
                float f = Mathf.Clamp01(1f - 0.1f * skill);
                Vector3 rv = RandomVec() * f * AimSkillOffset;
                BadAimOffset = new Vector2(rv.x * 0.7f, rv.y);
            }
            float enemyFactor = hasEnemy ? 5f : 2f;
            Vector2 desired = AnglesOf(wanted) + BadAimOffset * enemyFactor;
            desired.x = Mathf.Clamp(desired.x, -90f, 90f);
            _oldDesired = desired;

            // (the 5-stage prediction filter chain of aim.qc is omitted: its mix weights are
            //  0.01–0.075 and the lead point is already predicted by the caller)

            // mouse aim: moves in think steps of 0.5 - 0.05*skill seconds, by a random fraction
            if (time >= _aimThinkTime)
            {
                _aimThinkTime = Mathf.Max(_aimThinkTime + 0.5f - 0.05f * skill, time);
                Vector2 diff = Wrap(desired - MouseAim);
                float frac = 1f - Random01() * 0.1f * Mathf.Clamp(10 - skill, 1, 10);
                MouseAim = ClampPitch(Wrap(MouseAim + diff * frac));
            }
            Vector2 target = desired + Wrap(MouseAim - desired) * Mathf.Clamp01(AimSkillThink);

            // turn: r = bound(dt, max(fixedrate/dist, blendrate) * dt * (2 + skill^3*0.005 - random), 1)
            Vector2 turn = Wrap(target - ViewAngles);
            float dist = Mathf.Max(1f, Mathf.Min(1000f, turn.magnitude));
            float rate = Mathf.Max(AimSkillFixedRate / dist, AimSkillBlendRate);
            float r = Mathf.Clamp(rate * deltaTime * (2f + skill * skill * skill * 0.005f - Random01()), deltaTime, 1f);
            ViewAngles = ClampPitch(Wrap(ViewAngles + turn * (r + (1f - r) * Mathf.Clamp01(1f - AimSkillMouse))));
            return DirectionOf(ViewAngles);
        }

        /// bot_aim: the max angular deviation (degrees) at which the bot may pull the trigger.
        /// Empirical Xonotic curve 1000/(dist_qu - 9) - 0.35 widened by (1.6 + (10-skill)*0.3 capped at 3).
        public static float MaxFireDeviation(float distMeters, int skill, bool shotAccurate = false)
        {
            float distQu = Mathf.Max(10f, distMeters * 32f);
            float dev = 1000f / (distQu - 9f) - 0.35f;
            float f = (shotAccurate ? 1f : 1.6f) + Mathf.Clamp((10 - Mathf.Clamp(skill, MinSkill, MaxSkill)) * 0.3f, 0f, 3f);
            return Mathf.Min(90f, dev * f);
        }

        /// bot_aimdir fire decision: when the view is inside the tolerance cone, open a fire
        /// window of bound(0.1, 0.5 - skill*0.05, 0.5) s — low skill bots also skip windows at
        /// random ("random*random > skill*0.05"). Returns true while firing is allowed.
        public bool UpdateFire(Vector3 wanted, float distMeters, float time)
        {
            float maxDev = MaxFireDeviation(distMeters, Skill);
            if (!FireTolerance) { FireUntil = time + 0.2f; return true; }
            Vector2 dev = Wrap(AnglesOf(wanted) - ViewAngles);
            if (Mathf.Abs(dev.x) < maxDev && Mathf.Abs(dev.y) < maxDev)
            {
                // aim.qc: fire when the target is closer than 500 + 500*skill qu OR the random gate passes.
                bool near = distMeters * 32f < 500f + 500f * Skill;
                if (near || Random01() * Random01() > Mathf.Clamp01(Skill * 0.05f))
                    FireUntil = time + Mathf.Clamp(0.5f - Skill * 0.05f, 0.1f, 0.5f);
            }
            return time < FireUntil;
        }

        // ---------------------------------------------------------------- angle helpers

        /// Unity direction → (pitch, yaw) degrees; pitch positive = down.
        public static Vector2 AnglesOf(Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-8f) return Vector2.zero;
            dir.Normalize();
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
            return new Vector2(pitch, yaw);
        }

        public static Vector3 DirectionOf(Vector2 angles)
        {
            float p = angles.x * Mathf.Deg2Rad, y = angles.y * Mathf.Deg2Rad;
            float c = Mathf.Cos(p);
            return new Vector3(Mathf.Sin(y) * c, -Mathf.Sin(p), Mathf.Cos(y) * c);
        }

        /// Wrap yaw into [-180,180); pitch is left alone (callers clamp absolute pitch to ±90).
        public static Vector2 Wrap(Vector2 a)
        {
            a.y -= Mathf.Floor(a.y / 360f) * 360f;
            if (a.y >= 180f) a.y -= 360f;
            return a;
        }

        static Vector2 ClampPitch(Vector2 a) { a.x = Mathf.Clamp(a.x, -90f, 90f); return a; }
    }
}
