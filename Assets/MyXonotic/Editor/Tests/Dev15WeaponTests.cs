using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// dev.15: Xonotic weapon mechanics — Devastator ramp/guidance/remote,
    /// mortar/electro speed_up + bounce, electro combo, Hagar load, machinegun
    /// sustained spread, Vortex charge, Arc heat, mine proximity, melee delay.
    /// Scene-free; drives WeaponController via TickMechanics.
    /// </summary>
    public static class Dev15WeaponTests
    {
        const float Q = 1f / 32f;

        public static void Run(Action<bool, string> check)
        {
            // ---------------- balance data (bal-wep-xonotic.cfg) ----------------
            var dev = WeaponDef.Devastator().Primary;
            check(Mathf.Approximately(dev.SpeedStart, 1000f * Q) && Mathf.Approximately(dev.SpeedAccel, 1300f * Q) && Mathf.Approximately(dev.Speed, 1300f * Q), "devastator speedstart/accel/speed");
            check(dev.Guided && dev.RemoteDamage == 70 && dev.RemoteEdgeDamage == 35 && Mathf.Approximately(dev.RemoteRadius, 110f * Q) && Mathf.Approximately(dev.RemoteKnockback, 300f * Q), "devastator guided + remote 70/35/300/110");

            var mortar = WeaponDef.Mortar();
            check(Mathf.Approximately(mortar.Primary.SpeedUp, 225f * Q) && Mathf.Approximately(mortar.Secondary.SpeedUp, 150f * Q), "mortar speed_up 225/150");
            check(Mathf.Approximately(mortar.Secondary.BounceFactor, 0.5f) && Mathf.Approximately(mortar.Secondary.LifetimeAfterBounce, 0.5f) && Mathf.Approximately(mortar.Secondary.FuseSeconds, 20f), "mortar secondary bounce 0.5, lifetime_bounce 0.5");

            var electro = WeaponDef.Electro();
            check(electro.Secondary.Shots == 3 && Mathf.Approximately(electro.Secondary.BurstInterval, 0.2f) && Mathf.Approximately(electro.Secondary.SpeedUp, 200f * Q) && Mathf.Approximately(electro.Secondary.BounceFactor, 0.3f), "electro secondary 3 balls / 0.2 s / speed_up 200 / bounce 0.3");
            check(WeaponDef.ElectroComboDamage == 50 && WeaponDef.ElectroComboEdgeDamage == 25 && Mathf.Approximately(WeaponDef.ElectroComboRadius, 300f * Q) && Mathf.Approximately(WeaponDef.ElectroComboBlastRadius, 150f * Q), "electro combo 50/25, comboradius 300, radius 150");

            var hagar = WeaponDef.Hagar().Secondary;
            check(hagar.Mode == FireMode.Load && hagar.LoadMax == 4 && Mathf.Approximately(hagar.LoadTime, 0.5f) && Mathf.Approximately(hagar.LoadHold, 4f) && hagar.AmmoCost == 1, "hagar load 4 × 0.5 s, hold 4 s, 1 rocket each");

            var mg = WeaponDef.MachineGun();
            check(mg.Secondary.Shots == 3 && Mathf.Approximately(mg.Secondary.BurstInterval, 0.06f) && Mathf.Approximately(mg.Secondary.Refire, 0.45f) && mg.Secondary.AmmoCost == 3, "machinegun burst 3 × 0.06 s, refire2 0.45, ammo 3");

            var arc = WeaponDef.Arc().Primary;
            check(Mathf.Approximately(arc.Refire, 0.25f) && Mathf.Approximately(arc.Damage / arc.Refire, 100f) && Mathf.Approximately(arc.Speed, 1500f * Q) && Mathf.Approximately(arc.Knockback / arc.Refire, 600f * Q), "arc beam 100 dps, force 600/s, range 1500, tick 0.25");

            var mine = WeaponDef.Minelayer().Primary;
            check(Mathf.Approximately(mine.FuseSeconds, 10f) && mine.RemoteDamage == 45 && mine.RemoteEdgeDamage == 40 && Mathf.Approximately(mine.RemoteRadius, 200f * Q) && Mathf.Approximately(WeaponDef.MineProximityRadius, 150f * Q), "minelayer lifetime 10, remote 45/40/200, proximity 150");

            check(Mathf.Approximately(WeaponDef.Shotgun().Secondary.Delay, 0.25f), "shotgun melee delay 0.25");

            // ---------------- pure functions ----------------
            Vector3 v = Vector3.forward * 40f;
            Vector3 steered = Projectile.SteerTowards(v, Vector3.right, 90f * (1f / 60f));
            check(Mathf.Abs(steered.magnitude - 40f) < 1e-3f && Mathf.Abs(Vector3.Angle(v, steered) - 1.5f) < 0.01f, "guidance turns 1.5° per frame at 90°/s, keeps speed");
            Vector3 full = Projectile.SteerTowards(v, Vector3.right, 180f);
            check(Vector3.Angle(full, Vector3.right) < 0.01f, "guidance never overshoots the goal");

            // ---------------- controller behaviour ----------------
            var go = new GameObject("Dev15Gunner");
            var spawned = new List<GameObject> { go };
            try
            {
                var owner = go.AddComponent<Actor>();
                var wc = go.AddComponent<WeaponController>();
                wc.Owner = owner;
                wc.ResetLoadout();
                wc.GiveAll();
                wc.UpdateAim(Vector3.up * 50f, Vector3.forward); // far from any geometry

                // Vortex charge: full at rest, drops to 0.5 on firing, regrows 0.6/s while held.
                wc.SwitchTo(WeaponType.Vortex);
                check(Mathf.Approximately(wc.VortexCharge, 1f), "vortex starts fully charged");
                int cellsBefore = wc.GetAmmo(AmmoType.Cells);
                check(wc.TryFire(Vector3.up * 50f, Vector3.forward, false), "vortex fires");
                check(Mathf.Approximately(wc.VortexCharge, 0.5f), "vortex charge resets to 0.5 after a shot");
                check(wc.GetAmmo(AmmoType.Cells) == cellsBefore - 6, "vortex costs 6 cells");
                wc.TickMechanics(0.5f);
                check(Mathf.Abs(wc.VortexCharge - 0.8f) < 1e-4f, "vortex recharges 0.6/s");
                wc.TickMechanics(1f);
                check(Mathf.Approximately(wc.VortexCharge, 1f), "vortex charge caps at 1");
                wc.SwitchTo(WeaponType.Blaster);
                wc.ResetCooldownForTest();
                check(wc.TryFire(Vector3.up * 50f, Vector3.forward, false), "blaster fires");
                wc.SwitchTo(WeaponType.Vortex);
                float chargeAfterSwitch = wc.VortexCharge;
                wc.TickMechanics(0.1f);
                check(wc.VortexCharge >= chargeAfterSwitch, "charge only grows while Vortex is held");

                // Machinegun: first shot 0.03 spread, then sustained spread grows and caps.
                wc.SwitchTo(WeaponType.MachineGun);
                wc.ResetCooldownForTest();
                wc.TickMechanics(1f);
                check(Mathf.Approximately(wc.MachineGunSpreadDegrees, WeaponDef.SpreadDegFromRatio(0.03f)), "mg first shot spread 0.03");
                check(wc.TryFire(Vector3.up * 50f, Vector3.forward, false), "mg fires first shot");
                check(Mathf.Approximately(wc.MachineGunSpreadDegrees, WeaponDef.SpreadDegFromRatio(0.032f)), "mg second shot spread 0.02 + 0.012");
                for (int i = 0; i < 6; i++) { wc.ResetCooldownForTest(); wc.TickMechanics(0.1f); wc.TryFire(Vector3.up * 50f, Vector3.forward, false); }
                check(Mathf.Approximately(wc.MachineGunSpreadDegrees, WeaponDef.SpreadDegFromRatio(0.05f)), "mg sustained spread caps at 0.05");
                wc.TickMechanics(0.5f);
                check(Mathf.Approximately(wc.MachineGunSpreadDegrees, WeaponDef.SpreadDegFromRatio(0.03f)), "mg spread resets after a pause");

                // Hagar load: press loads one, holding adds one per 0.5 s up to 4, release fires the volley.
                wc.SwitchTo(WeaponType.Hagar);
                wc.ResetCooldownForTest();
                int rockets = wc.GetAmmo(AmmoType.Rockets);
                wc.SetSecondaryHeld(true);
                check(!wc.TryFire(Vector3.up * 50f, Vector3.forward, true) && wc.HagarLoaded == 1 && wc.GetAmmo(AmmoType.Rockets) == rockets - 1, "hagar press loads the first rocket");
                wc.TickMechanics(0.5f);
                check(wc.HagarLoaded == 2, "hagar loads a second rocket after 0.5 s");
                wc.TickMechanics(0.5f); wc.TickMechanics(0.5f); wc.TickMechanics(0.5f);
                check(wc.HagarLoaded == 4 && wc.GetAmmo(AmmoType.Rockets) == rockets - 4, "hagar caps at 4 loaded rockets");
                int live = UnityEngine.Object.FindObjectsOfType<Projectile>().Length;
                wc.SetSecondaryHeld(false);
                wc.TickMechanics(0.016f);
                check(wc.HagarLoaded == 0 && UnityEngine.Object.FindObjectsOfType<Projectile>().Length == live + 4, "hagar release fires all 4 rockets");
                check(!wc.IsReady, "hagar volley starts the 0.5 s refire");

                // Hagar: a full load held longer than load_hold auto-fires.
                wc.ResetCooldownForTest();
                wc.SetSecondaryHeld(true);
                wc.TryFire(Vector3.up * 50f, Vector3.forward, true);
                for (int i = 0; i < 3; i++) wc.TickMechanics(0.5f);
                check(wc.HagarLoaded == 4, "hagar reloaded to 4");
                live = UnityEngine.Object.FindObjectsOfType<Projectile>().Length;
                wc.TickMechanics(4.01f);
                check(wc.HagarLoaded == 0 && UnityEngine.Object.FindObjectsOfType<Projectile>().Length == live + 4, "hagar auto-fires after holding a full load 4 s");
                wc.SetSecondaryHeld(false);

                // Arc heat: FireBeam is driven through TryFire; 20 ticks of 0.25 s overheat, cooldown lasts 2.5 s.
                wc.SwitchTo(WeaponType.Arc);
                wc.SetPrimaryHeld(true);
                int fired = 0;
                for (int i = 0; i < 25; i++) { wc.ResetCooldownForTest(); if (wc.TryFire(Vector3.up * 50f, Vector3.forward, false)) fired++; }
                check(fired == 20 && wc.ArcOverheated && Mathf.Approximately(wc.ArcHeat, 1f), "arc overheats after 5 s of beam (20 ticks)");
                wc.SetPrimaryHeld(false);
                wc.TickMechanics(1.25f);
                check(wc.ArcOverheated && Mathf.Abs(wc.ArcHeat - 0.5f) < 1e-3f, "arc cools 5 s of heat in 2.5 s");
                wc.TickMechanics(1.3f);
                check(!wc.ArcOverheated && wc.ArcHeat == 0f, "arc ready again after the cooldown");
                int cells = wc.GetAmmo(AmmoType.Cells);
                wc.SetPrimaryHeld(true);
                for (int i = 0; i < 4; i++) { wc.ResetCooldownForTest(); wc.TryFire(Vector3.up * 50f, Vector3.forward, false); }
                check(cells - wc.GetAmmo(AmmoType.Cells) == 6, "arc uses 6 cells per second of beam");
                wc.SetPrimaryHeld(false);

                // Devastator: guidance flag follows the held trigger; spawned rocket starts at speedstart and ramps.
                wc.SwitchTo(WeaponType.Devastator);
                wc.SetPrimaryHeld(true);
                check(wc.GuideActive, "devastator guidance active while primary held");
                wc.SetPrimaryHeld(false);
                check(!wc.GuideActive, "devastator guidance stops on release");
                var rocket = Projectile.Spawn(Vector3.up * 50f, Vector3.forward, dev, owner, WeaponType.Devastator);
                spawned.Add(rocket.gameObject);
                check(Mathf.Approximately(rocket.Velocity.magnitude, 1000f * Q), "rocket leaves at speedstart 1000");
                rocket.MarkRemoteForTest();
                var blast = rocket.EffectiveBlast();
                check(blast.damage == 70 && blast.edge == 35 && Mathf.Approximately(blast.radius, 110f * Q), "remote detonation uses remote stats");
                var rocket2 = Projectile.Spawn(Vector3.up * 50f, Vector3.forward, dev, owner, WeaponType.Devastator);
                spawned.Add(rocket2.gameObject);
                check(rocket2.EffectiveBlast().damage == 80, "impact detonation uses full stats");

                // Mortar: speed_up adds vertical velocity to a horizontal shot.
                var nade = Projectile.Spawn(Vector3.up * 50f, Vector3.forward, mortar.Primary, owner, WeaponType.Mortar);
                spawned.Add(nade.gameObject);
                check(Mathf.Approximately(nade.Velocity.y, 225f * Q) && Mathf.Approximately(nade.Velocity.z, 1900f * Q), "mortar grenade gets speed_up 225");

                // Electro ball identification for the combo.
                var ball = Projectile.Spawn(Vector3.up * 50f, Vector3.forward, electro.Secondary, owner, WeaponType.Electro);
                spawned.Add(ball.gameObject);
                var bolt = Projectile.Spawn(Vector3.up * 50f, Vector3.forward, electro.Primary, owner, WeaponType.Electro);
                spawned.Add(bolt.gameObject);
                check(ball.IsElectroBall && !bolt.IsElectroBall, "electro ball vs bolt classification");
            }
            finally
            {
                foreach (var s in spawned) if (s != null) UnityEngine.Object.DestroyImmediate(s);
                foreach (var p in UnityEngine.Object.FindObjectsOfType<Projectile>()) if (p != null) UnityEngine.Object.DestroyImmediate(p.gameObject);
            }
        }
    }
}
