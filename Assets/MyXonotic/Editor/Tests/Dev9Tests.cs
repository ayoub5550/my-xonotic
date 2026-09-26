using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// dev.9 self-tests (no Play Mode): teams + powerups on Actor, extra
    /// weapons / bar slots / mine cap on WeaponController, Mover geometry and
    /// door state machine, CTF flag rules, and the skeletal rig conversion
    /// (bone-matrix skinning in Unity space must reproduce the CPU-baked
    /// static frame). Run from GameplayIntegrationTests; throws on failure.
    /// </summary>
    public static class Dev9Tests
    {
        static readonly List<GameObject> Spawned = new List<GameObject>();
        public static readonly List<string> Passed = new List<string>();

        [MenuItem("Plasma Verge/Tests - dev.9 checks")]
        public static void RunSelfTests()
        {
            Spawned.Clear();
            Passed.Clear();
            bool testModeBefore = ArenaBootstrap.TestMode;
            ArenaBootstrap.TestMode = true;
            try
            {
                Teams_NoFriendlyFire_SelfDamageStillApplies();
                Powerups_StrengthTriplesShieldDivides_AndExpire();
                Weapons_FourteenDefs_SlotMapping_ExtrasNotAutoSwitched();
                Movers_DirectionTravel_AndDoorCycle();
                Ctf_TakeDropReturnCapture();
                Rig_ConversionMatchesBakedSkinning();
                MatchSettings_Names();
                Debug.Log("[Dev9Tests] PASS " + Passed.Count);
            }
            finally
            {
                ArenaBootstrap.TestMode = testModeBefore;
                foreach (var go in Spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
                Spawned.Clear();
            }
        }

        static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("[Dev9Tests] FAIL: " + name);
            Passed.Add("PASS dev9: " + name);
        }

        static Actor NewActor(string name, Team team = Team.None)
        {
            var go = new GameObject(name);
            Spawned.Add(go);
            var a = go.AddComponent<Actor>();
            a.Team = team;
            return a;
        }

        // ------------------------------------------------------------------ actor

        static void Teams_NoFriendlyFire_SelfDamageStillApplies()
        {
            var red1 = NewActor("red1", Team.Red);
            var red2 = NewActor("red2", Team.Red);
            var blue = NewActor("blue", Team.Blue);
            Check(!red1.IsEnemyOf(red2) && red1.IsEnemyOf(blue) && !red1.IsEnemyOf(red1), "IsEnemyOf: teammate false, enemy true, self false");
            red2.TakeDamage(30, Vector3.zero, red1);
            Check(red2.Health == Actor.StartHealth, "teammate damage ignored");
            red2.TakeDamage(30, Vector3.zero, blue);
            Check(red2.Health == Actor.StartHealth - 30, "enemy damage applies");
            red2.TakeDamage(10, Vector3.zero, red2);
            Check(red2.Health == Actor.StartHealth - 40, "self damage still applies in team mode");
            var dm1 = NewActor("dm1"); var dm2 = NewActor("dm2");
            dm2.TakeDamage(5, Vector3.zero, dm1);
            Check(dm2.Health == Actor.StartHealth - 5, "no teams: everyone is an enemy");
        }

        static void Powerups_StrengthTriplesShieldDivides_AndExpire()
        {
            var a = NewActor("attacker"); var v = NewActor("victim");
            Check(!a.HasStrength && !v.HasShield, "no powerups at start");
            a.GiveStrength(5f);
            v.TakeDamage(10, Vector3.zero, a);
            Check(v.Health == Actor.StartHealth - 30, "strength triples damage (10 -> 30)");
            v.GiveShield(5f);
            v.TakeDamage(9, Vector3.zero, a);
            Check(v.Health == Actor.StartHealth - 30 - 9, "strength x3 then shield /3 (9 -> 27 -> 9)");
            a.TickPowerups(4.9f);
            Check(a.HasStrength && Mathf.Abs(a.StrengthRemaining - 0.1f) < 0.01f, "strength counts down");
            a.TickPowerups(0.2f);
            Check(!a.HasStrength && a.StrengthRemaining == 0f, "strength expires at zero");
            a.TickPowerups(float.NaN); a.TickPowerups(-1f);
            Check(v.HasShield, "invalid deltas ignored");
            v.TakeDamage(10000, Vector3.zero, a);
            Check(v.IsDead && !v.HasShield, "death clears powerups");
            var pickup = new GameObject("strength").AddComponent<SphereCollider>().gameObject; Spawned.Add(pickup);
            var p = pickup.AddComponent<Pickup>(); p.Type = PickupType.Strength; p.Amount = 30;
            var c = NewActor("collector");
            Check(p.TryCollect(c) && c.HasStrength && Mathf.Abs(c.StrengthRemaining - 30f) < 0.01f, "strength pickup grants 30 s");
            Check(p.Label == "Strength", "strength label");
        }

        // ------------------------------------------------------------------ weapons

        static void Weapons_FourteenDefs_SlotMapping_ExtrasNotAutoSwitched()
        {
            Check(WeaponController.WeaponCount == 14 && WeaponController.CoreWeaponCount == 9, "14 weapons, 9 core");
            for (int i = 0; i < WeaponController.WeaponCount; i++)
            {
                var d = WeaponController.GetDef((WeaponType)i);
                Check(d.Type == (WeaponType)i && !string.IsNullOrEmpty(d.Name) && d.Primary.Refire > 0f, "def " + (WeaponType)i + " consistent");
            }
            Check(WeaponController.GetDef(WeaponType.Arc).Primary.Mode == FireMode.Beam, "Arc primary is a beam");
            Check(WeaponController.GetDef(WeaponType.Minelayer).Primary.Mode == FireMode.Mine && WeaponController.GetDef(WeaponType.Minelayer).Secondary.Mode == FireMode.Detonate, "Minelayer mine + detonate");
            Check(WeaponController.GetDef(WeaponType.Hook).Primary.Mode == FireMode.Hook, "Hook primary is a hook");
            Check(WeaponController.GetDef(WeaponType.Fireball).Ammo == AmmoType.None, "Fireball needs no ammo");
            foreach (var w in new[] { WeaponType.Rifle, WeaponType.Minelayer, WeaponType.Arc, WeaponType.Fireball, WeaponType.Hook })
                Check(WeaponAudio.Sources.ContainsKey(w), "audio sources for " + w);

            var owner = NewActor("gunner");
            var wc = owner.gameObject.AddComponent<WeaponController>();
            wc.Owner = owner;
            wc.ResetLoadout();
            Check(wc.VisibleSlotCount == 9, "bar shows 9 slots without extras");
            WeaponType t;
            Check(wc.SlotToWeapon(0, out t) && t == WeaponType.Blaster && wc.SlotToWeapon(8, out t) && t == WeaponType.Devastator, "core slots map 1:1");
            Check(!wc.SlotToWeapon(9, out t), "slot 10 empty without extras");
            wc.Current = WeaponType.Shotgun;
            wc.GiveWeapon(WeaponType.Hook);
            Check(wc.Has(WeaponType.Hook) && wc.Current == WeaponType.Shotgun, "extra weapon pickup does not auto-switch");
            Check(wc.VisibleSlotCount == 10 && wc.SlotToWeapon(9, out t) && t == WeaponType.Hook, "owned extra appears as slot 10");
            wc.GiveWeapon(WeaponType.Rifle);
            Check(wc.SlotToWeapon(9, out t) && t == WeaponType.Rifle && wc.SlotToWeapon(10, out t) && t == WeaponType.Hook, "extras keep enum order");
            wc.GiveWeapon(WeaponType.Devastator);
            Check(wc.Current == WeaponType.Devastator, "core weapon pickup still auto-switches");
            Check(wc.BestUsable() <= WeaponType.Devastator, "BestUsable never picks an extra");
            Check(wc.GetAmmo(WeaponType.Rifle) == 40, "rifle pickup ammo 40 bullets");
            wc.GiveAll();
            Check(wc.OwnedCount == 14 && wc.VisibleSlotCount == 14, "GiveAll owns all 14");
        }

        // ------------------------------------------------------------------ movers

        static void Movers_DirectionTravel_AndDoorCycle()
        {
            Check(Mover.DirectionFromAngle(-1f) == Vector3.up && Mover.DirectionFromAngle(-2f) == Vector3.down, "angle -1/-2 = up/down");
            Check((Mover.DirectionFromAngle(0f) - Vector3.right).magnitude < 1e-4f, "angle 0 = Quake +X = Unity +X");
            Check((Mover.DirectionFromAngle(90f) - Vector3.forward).magnitude < 1e-4f, "angle 90 = Quake +Y = Unity +Z");
            var b = new Bounds(Vector3.zero, new Vector3(2f, 3f, 0.5f));
            Check(Mathf.Abs(Mover.DoorTravel(b, Vector3.up, 0.25f) - 2.75f) < 1e-4f, "door travel = extent - lip");
            Mover.Kind k;
            Check(Mover.KindFor("func_door", out k) && k == Mover.Kind.Door && Mover.KindFor("func_rotating", out k) && k == Mover.Kind.Rotating
                  && Mover.KindFor("func_bobbing", out k) && k == Mover.Kind.Bobbing && !Mover.KindFor("func_wall", out k), "mover kinds");

            var go = new GameObject("door"); Spawned.Add(go);
            go.transform.position = new Vector3(5f, 1f, 5f);
            var sub = go.AddComponent<MyXonotic.Content.ImportedSubmodel>();
            sub.classname = "func_door"; sub.angle = -1f; sub.speed = 64f; sub.wait = 1f; sub.lip = 8f;
            sub.localMin = new Vector3(-1f, 0f, -0.1f); sub.localMax = new Vector3(1f, 2f, 0.1f);
            var m = go.AddComponent<Mover>();
            m.Configure(sub, Mover.Kind.Door);
            Check((m.TravelVector - Vector3.up * (2f - 0.25f)).magnitude < 1e-4f, "configured door travels up by height - lip");
            Vector3 rest = go.transform.position;
            m.StepDoor(0.5f, false);
            Check(m.Progress == 0f && go.transform.position == rest, "closed door stays put without actors");
            m.StepDoor(0.5f, true);
            Check(m.Progress > 0f && m.IsMoving, "door opens when an actor is near");
            for (int i = 0; i < 20; i++) m.StepDoor(0.25f, true);
            Check(m.IsOpen && (go.transform.position - (rest + m.TravelVector)).magnitude < 1e-3f, "door reaches the open position (2 m/s over 1.75 m)");
            m.StepDoor(0.5f, false); m.StepDoor(0.6f, false); m.StepDoor(0.1f, false);
            Check(m.Progress < 1f, "door closes after wait when nobody is near");
            m.StepDoor(0.1f, true);
            for (int i = 0; i < 10; i++) m.StepDoor(0.25f, true);
            Check(m.IsOpen, "closing door reopens when an actor re-enters");
            for (int i = 0; i < 40; i++) m.StepDoor(0.25f, false);
            Check(m.Progress == 0f && (go.transform.position - rest).magnitude < 1e-3f, "door returns exactly to rest");

            var rot = new GameObject("rot"); Spawned.Add(rot);
            var rsub = rot.AddComponent<MyXonotic.Content.ImportedSubmodel>();
            rsub.classname = "func_rotating"; rsub.speed = 90f; rsub.spawnflags = 0;
            var rm = rot.AddComponent<Mover>(); rm.Configure(rsub, Mover.Kind.Rotating);
            rm.Step(1f);
            Check(rm.RotationAxis == Vector3.up && Mathf.Abs(Mathf.DeltaAngle(rot.transform.eulerAngles.y, -90f)) < 0.5f, "rotating: 90 deg/s Quake Z -> -90 deg Unity Y after 1 s");

            var bob = new GameObject("bob"); Spawned.Add(bob);
            var bsub = bob.AddComponent<MyXonotic.Content.ImportedSubmodel>();
            bsub.classname = "func_bobbing"; bsub.height = 32f; bsub.speed = 4f;
            var bm = bob.AddComponent<Mover>(); bm.Configure(bsub, Mover.Kind.Bobbing);
            bm.Step(1f);
            Check(Mathf.Abs(bob.transform.position.y - 1f) < 1e-3f && Mathf.Abs(bm.LastDelta.y - 1f) < 1e-3f, "bobbing: quarter period = +height (1 m) and LastDelta reports it");
        }

        // ------------------------------------------------------------------ CTF

        static void Ctf_TakeDropReturnCapture()
        {
            CtfFlag.ResetScores();
            var redGO = new GameObject("redflag"); Spawned.Add(redGO);
            var blueGO = new GameObject("blueflag"); Spawned.Add(blueGO);
            var red = redGO.AddComponent<CtfFlag>(); var blue = blueGO.AddComponent<CtfFlag>();
            red.Init(Team.Red, new Vector3(0f, 0f, 0f), null);
            blue.Init(Team.Blue, new Vector3(50f, 0f, 0f), null);
            var r1 = NewActor("r1", Team.Red); var b1 = NewActor("b1", Team.Blue);
            Check(!red.Touch(r1) && red.State == CtfFlag.FlagState.AtBase, "own flag at base: nothing happens");
            Check(blue.Touch(r1) && blue.State == CtfFlag.FlagState.Carried && blue.Carrier == r1, "enemy takes the blue flag");
            Check(!blue.Touch(b1), "carried flag cannot be touched");
            Check(red.Touch(r1) && CtfFlag.RedCaptures == 1 && blue.State == CtfFlag.FlagState.AtBase && (blue.transform.position - blue.Home).magnitude < 1e-4f, "carrier touching own base flag captures; blue flag returns home");
            Check(blue.Touch(r1), "take again");
            blue.Drop(new Vector3(20f, 0f, 0f), r1);
            Check(blue.State == CtfFlag.FlagState.Dropped && blue.DroppedTimer == CtfFlag.ReturnAfterSeconds, "dropped flag starts return timer");
            Check(blue.Touch(b1) && blue.State == CtfFlag.FlagState.AtBase, "teammate touching dropped flag returns it");
            blue.Touch(r1); blue.Drop(new Vector3(20f, 0f, 0f), r1);
            blue.Step(CtfFlag.ReturnAfterSeconds + 0.1f);
            Check(blue.State == CtfFlag.FlagState.AtBase, "dropped flag auto-returns after timeout");
            blue.Touch(r1);
            r1.TakeDamage(10000, Vector3.zero, b1);
            blue.Step(0.01f);
            Check(blue.State == CtfFlag.FlagState.Dropped && CtfFlag.CarriedBy(r1) == null, "carrier death drops the flag");
            CtfFlag.ResetScores();
            Check(CtfFlag.RedCaptures == 0, "scores reset");
        }

        // ------------------------------------------------------------------ rig

        static void Rig_ConversionMatchesBakedSkinning()
        {
            var resolver = new XonoticContentResolver();
            string path = resolver.FindFile("models/player/erebus.iqm");
            if (path == null)
            {
                Passed.Add("PASS dev9: rig conversion skipped (models/player/erebus.iqm not in content roots)");
                return;
            }
            var doc = IqmSkinnedDocument.Read(File.ReadAllBytes(path), path);
            int n = doc.JointCount;
            Check(n > 0 && doc.FrameCount > 0 && doc.AnimCount > 0, "erebus IQM has joints/frames/anims");
            int frame = doc.FindAnimStart("run", out _);
            Vector3[] baked, bakedN;
            doc.SkinFrame(frame, out baked, out bakedN);
            Vector3[] bind, bindN;
            doc.SkinFrame(-1, out bind, out bindN);
            var weights = doc.BuildBoneWeights();

            // Unity-space bind and frame world matrices from converted local TRS.
            var bindWorld = new Matrix4x4[n]; var frameWorld = new Matrix4x4[n];
            for (int j = 0; j < n; j++)
            {
                doc.GetJoint(j, out _, out int parent, out Vector3 t, out Quaternion r, out Vector3 s);
                var local = Matrix4x4.TRS(ToUnityT(t), ToUnityQ(r), ToUnityS(s));
                bindWorld[j] = parent >= 0 ? bindWorld[parent] * local : local;
                doc.GetFramePose(frame, j, out Vector3 ft, out Quaternion fr, out Vector3 fs);
                var flocal = Matrix4x4.TRS(ToUnityT(ft), ToUnityQ(fr), ToUnityS(fs));
                frameWorld[j] = parent >= 0 ? frameWorld[parent] * flocal : flocal;
            }
            float maxErr = 0f;
            for (int v = 0; v < bind.Length; v += 7)
            {
                var w = weights[v];
                Vector3 p = Vector3.zero;
                p += (frameWorld[w.boneIndex0] * bindWorld[w.boneIndex0].inverse).MultiplyPoint3x4(bind[v]) * w.weight0;
                p += (frameWorld[w.boneIndex1] * bindWorld[w.boneIndex1].inverse).MultiplyPoint3x4(bind[v]) * w.weight1;
                p += (frameWorld[w.boneIndex2] * bindWorld[w.boneIndex2].inverse).MultiplyPoint3x4(bind[v]) * w.weight2;
                p += (frameWorld[w.boneIndex3] * bindWorld[w.boneIndex3].inverse).MultiplyPoint3x4(bind[v]) * w.weight3;
                maxErr = Mathf.Max(maxErr, (p - baked[v]).magnitude);
            }
            Check(maxErr < 0.002f, "Unity-space bone skinning reproduces the CPU-baked run frame (max error " + maxErr.ToString("F5") + " m)");
        }

        const float Units = MyXonotic.Content.Bsp.BspCoordinateSpace.SourceUnitsPerUnityUnit;
        static Vector3 ToUnityT(Vector3 t) => new Vector3(t.x / Units, t.z / Units, t.y / Units);
        static Quaternion ToUnityQ(Quaternion q)
        {
            if (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w < 1e-8f) return Quaternion.identity;
            return new Quaternion(-q.x, -q.z, -q.y, q.w).normalized;
        }
        static Vector3 ToUnityS(Vector3 s) => new Vector3(s.x, s.z, s.y);

        static void MatchSettings_Names()
        {
            Check(MatchSettings.ModeShort(GameMode.CaptureTheFlag) == "CTF" && MatchSettings.ModeShort(GameMode.TeamDeathmatch) == "TDM" && MatchSettings.ModeShort(GameMode.Deathmatch) == "DM", "mode short names");
            Check(MatchSettings.Opponent(Team.Red) == Team.Blue && MatchSettings.Opponent(Team.Blue) == Team.Red && MatchSettings.Opponent(Team.None) == Team.None, "opponent team");
        }
    }
}
