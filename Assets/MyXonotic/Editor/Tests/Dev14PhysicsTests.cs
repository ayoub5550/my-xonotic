using System;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// dev.14: numeric checks for the Xonotic movement port (XonoticPhysics),
    /// weapon balance sync and health regen/rot. Scene-free; called from LocalTests.Run.
    /// </summary>
    public static class Dev14PhysicsTests
    {
        public static void Run(Action<bool, string> check)
        {
            const float dt = 1f / 60f;
            Vector3 fwd = Vector3.forward;

            // Constants come from physicsX.cfg, converted with 32 qu = 1 m.
            check(Mathf.Approximately(XonoticPhysics.MaxSpeed, 11.25f), "physics maxspeed 360 qu/s = 11.25 m/s");
            check(Mathf.Approximately(XonoticPhysics.JumpVelocity, 8.125f), "physics jumpvelocity 260 qu/s");
            check(Mathf.Approximately(XonoticPhysics.Gravity, 25f), "physics gravity 800 qu/s²");
            float jumpHeight = XonoticPhysics.JumpVelocity * XonoticPhysics.JumpVelocity / (2f * XonoticPhysics.Gravity);
            check(Mathf.Abs(jumpHeight - 42.25f / 32f) < 1e-4f, "jump height 42.25 qu (physicsX.cfg comment)");

            // Ground: from rest, holding forward reaches ~maxspeed within a second and never exceeds it.
            Vector3 v = Vector3.zero;
            float peak = 0f;
            for (int i = 0; i < 120; i++) { v = XonoticPhysics.GroundMove(v, fwd, XonoticPhysics.MaxSpeed, dt); peak = Mathf.Max(peak, v.magnitude); }
            check(v.magnitude > XonoticPhysics.MaxSpeed * 0.97f && peak <= XonoticPhysics.MaxSpeed + 1e-3f, "ground run converges to maxspeed");

            // Ground friction: releasing the stick stops within ~1 s (sv_friction 6, stopspeed 100).
            for (int i = 0; i < 60; i++) v = XonoticPhysics.GroundMove(v, Vector3.zero, 0f, dt);
            check(v.magnitude < 0.05f, "ground friction stops the player in a second");
            check(XonoticPhysics.GroundFriction(new Vector3(10, 7, 0), dt).y == 7f, "ground friction keeps vertical speed");

            // Friction replica is dt-independent: 1×(1/30) ≈ 2×(1/60).
            Vector3 a = XonoticPhysics.GroundFriction(new Vector3(8, 0, 0), 1f / 30f);
            Vector3 b = XonoticPhysics.GroundFriction(XonoticPhysics.GroundFriction(new Vector3(8, 0, 0), 1f / 60f), 1f / 60f);
            check(Mathf.Abs(a.x - b.x) < 0.01f, "friction is frame-rate independent");

            // Air, holding forward at maxspeed: sv_airaccel_qw -0.8 leaves 20 % of the
            // acceleration past maxspeed (dv/dt = accel·maxspeed·(1−qw) ≈ 144 qu/s²), no loss.
            v = fwd * XonoticPhysics.MaxSpeed;
            for (int i = 0; i < 60; i++) v = XonoticPhysics.AirMove(v, Vector2.up, fwd, XonoticPhysics.MaxSpeed, dt);
            float fwdGain = v.magnitude * 32f - 360f;
            check(fwdGain > 100f && fwdGain < 190f, "air: forward alone gains ~144 qu/s per second (" + fwdGain.ToString("0") + ")");

            // Air strafing: forward+right input with wishdir 45° off the velocity gains speed (bunny-hop).
            v = fwd * XonoticPhysics.MaxSpeed;
            Vector3 wish45 = (Vector3.forward + Vector3.right).normalized;
            for (int i = 0; i < 60; i++) v = XonoticPhysics.AirMove(v, Vector2.right, wish45, XonoticPhysics.MaxSpeed, dt);
            check(v.magnitude > XonoticPhysics.MaxSpeed * 1.08f, "air strafe accelerates past maxspeed (" + (v.magnitude * 32f).ToString("0") + " qu/s)");
            check(v.magnitude < XonoticPhysics.AirSpeedLimitNonQw, "air strafe stays under sv_airspeedlimit_nonqw");

            // CPM air control: forward-only input turns the velocity towards the view without losing speed.
            v = fwd * XonoticPhysics.MaxSpeed;
            Vector3 wishTurn = Quaternion.Euler(0, 40, 0) * fwd;
            for (int i = 0; i < 30; i++) v = XonoticPhysics.AirControlStep(v, Vector2.up, wishTurn, XonoticPhysics.MaxSpeed, dt);
            float turned = Vector3.Angle(fwd, new Vector3(v.x, 0, v.z));
            check(turned > 8f && turned <= 40.01f && Mathf.Abs(v.magnitude - XonoticPhysics.MaxSpeed) < 1e-3f, "air control turns velocity (" + turned.ToString("0.0") + "°) at constant speed");
            v = fwd * XonoticPhysics.MaxSpeed;
            for (int i = 0; i < 30; i++) v = XonoticPhysics.AirControlStep(v, Vector2.right, wishTurn, XonoticPhysics.MaxSpeed, dt);
            check(Vector3.Angle(fwd, v) < 1e-3f, "air control only while holding forward (strafe keys excluded)");

            // Air stop: pressing back against the motion slows down faster than plain airaccelerate.
            v = fwd * XonoticPhysics.MaxSpeed;
            for (int i = 0; i < 30; i++) v = XonoticPhysics.AirMove(v, Vector2.down, -fwd, XonoticPhysics.MaxSpeed, dt);
            check(v.magnitude < XonoticPhysics.MaxSpeed * 0.85f, "airstopaccelerate brakes when pushing backwards");

            // Key mix helpers.
            check(XonoticPhysics.IsMoveInDirection(Vector2.up, 0f) == 1f && XonoticPhysics.IsMoveInDirection(Vector2.right, 0f) == 0f, "IsMoveInDirection forward/strafe");
            check(Mathf.Approximately(XonoticPhysics.GeomLerp(2f, 0.5f, 8f), 4f), "GeomLerp geometric midpoint");

            // Weapon balance synced to bal-wep-xonotic.cfg.
            var dev = WeaponDef.Devastator();
            check(dev.Primary.Damage == 80 && dev.Primary.EdgeDamage == 40 && Mathf.Approximately(dev.Primary.Knockback, 400f / 32f) && Mathf.Approximately(dev.Primary.Refire, 1.1f) && dev.Primary.AmmoCost == 4, "devastator balance");
            var bls = WeaponDef.Blaster();
            check(Mathf.Approximately(bls.Primary.Speed, 6000f / 32f) && Mathf.Approximately(bls.Primary.Knockback, 375f / 32f), "blaster speed 6000 / force 375");
            check(bls.Primary.Knockback > XonoticPhysics.JumpVelocity, "laser jump pushes harder than a jump");
            var sg = WeaponDef.Shotgun();
            check(sg.Primary.Shots == 12 && sg.Primary.Damage == 4, "shotgun 12 × 4");
            var cry = WeaponDef.Crylink();
            check(cry.Primary.Knockback < 0f && cry.Primary.Shots == 6, "crylink pulls (negative force), 6 shots");
            check(Mathf.Approximately(WeaponDef.Electro().Primary.Refire, 0.6f), "electro refire 0.6");
            check(Mathf.Approximately(WeaponDef.Arc().Primary.Damage / WeaponDef.Arc().Primary.Refire, 100f), "arc beam 100 dps");
            // Edge damage falloff: centre = damage, edge = edgedamage, midway = mean.
            check(Mathf.Approximately(ArenaMath.SplashDamage(0f, 4f, 80f, 40f), 80f) && Mathf.Approximately(ArenaMath.SplashDamage(2f, 4f, 80f, 40f), 60f) && ArenaMath.SplashDamage(4f, 4f, 80f, 40f) == 0f, "splash edge damage falloff");

            // Health regen/rot (scene-free Actor on a temp object).
            var go = new GameObject("Dev14Actor");
            try
            {
                var actor = go.AddComponent<Actor>();
                actor.ResetForSpawn();
                actor.TakeDamage(50, Vector3.zero, null);
                int hurt = actor.Health;
                for (int i = 0; i < 60 * 4; i++) actor.TickRegen(dt);
                check(actor.Health == hurt, "no regen during the 5 s damage pause");
                for (int i = 0; i < 60 * 10; i++) actor.TickRegen(dt);
                check(actor.Health > hurt && actor.Health <= 100, "health regenerates towards 100 (" + actor.Health + ")");
                actor.AddHealth(100);
                for (int i = 0; i < 60 * 20; i++) actor.TickRegen(dt);
                check(actor.Health < 200 && actor.Health >= 100, "health above 100 rots (" + actor.Health + ")");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
