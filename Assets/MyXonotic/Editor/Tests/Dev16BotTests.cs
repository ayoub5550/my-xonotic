using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// dev.16: NavMesh bots, Game Loop, adaptive resolution.
    ///  - bot_ai_* constants match xonotic-server.cfg
    ///  - weapon priority tables follow bot_ai_custom_weapon_priority_{close,mid,far}
    ///  - skill → aim error / think cadence / bunnyhop
    ///  - BotNavigator corner following (pure)
    ///  - NavMeshBake on synthetic geometry: a floor with a wall produces a path that goes around
    ///  - GameLoopManifest.Patch inserts the TEST_LOOP filter exactly once
    ///  - AdaptiveResolution.Decide hysteresis
    /// </summary>
    public static class Dev16BotTests
    {
        const float Q = 1f / 32f;

        public static void Run(Action<bool, string> check)
        {
            // ---------------- xonotic-server.cfg values ----------------
            check(Mathf.Approximately(Bot.StrategyInterval, 7f) && Mathf.Approximately(Bot.StrategyIntervalMoving, 5.5f), "bot_ai_strategyinterval 7 / 5.5");
            check(Mathf.Approximately(Bot.EnemyDetectionInterval, 2f) && Mathf.Approximately(Bot.EnemyDetectionSticking, 4f), "bot_ai_enemydetectioninterval 2 / 4");
            check(Mathf.Approximately(Bot.SightRange, 10000f * Q), "bot_ai_enemydetectionradius 10000 qu");
            check(Mathf.Approximately(Bot.ChooseWeaponInterval, 0.5f) && Mathf.Approximately(Bot.IgnoreGoalTimeout, 3f), "chooseweaponinterval 0.5, ignoregoal_timeout 3");
            check(Mathf.Approximately(Bot.CloseRange, 300f * Q) && Mathf.Approximately(Bot.FarRange, 850f * Q), "weapon priority distances 300 / 850 qu");
            check(Mathf.Approximately(Bot.FriendsAwarePickupRadius, 500f * Q), "friends_aware_pickup_radius 500 qu");
            check(Bot.BunnyhopSkill == 7 && Bot.DefaultSkill == 8, "bunnyhop_skilloffset 7, skill 8");

            // ---------------- weapon priorities ----------------
            check(Bot.PriorityFor(5f) == Bot.PriorityClose && Bot.PriorityFor(15f) == Bot.PriorityMid && Bot.PriorityFor(40f) == Bot.PriorityFar, "priority table by distance");
            check(Bot.PriorityClose[0] == WeaponType.Vortex && Bot.PriorityClose[1] == WeaponType.Shotgun && Bot.PriorityClose[2] == WeaponType.MachineGun && Bot.PriorityClose[3] == WeaponType.Arc, "close: vortex shotgun machinegun arc …");
            check(Bot.PriorityMid[0] == WeaponType.Devastator && Bot.PriorityMid[1] == WeaponType.Vortex && Bot.PriorityMid[2] == WeaponType.Fireball && Bot.PriorityMid[3] == WeaponType.Mortar, "mid: devastator vortex fireball mortar …");
            check(Bot.PriorityFar[0] == WeaponType.Vortex && Bot.PriorityFar[1] == WeaponType.Rifle && Bot.PriorityFar[2] == WeaponType.Electro && Bot.PriorityFar[3] == WeaponType.Devastator, "far: vortex rifle electro devastator …");
            check(Array.IndexOf(Bot.PriorityClose, WeaponType.Hook) < 0 && Array.IndexOf(Bot.PriorityFar, WeaponType.Minelayer) < 0, "bots never pick hook / minelayer from priorities");

            // ---------------- skill ----------------
            check(Mathf.Approximately(Bot.AimErrorFor(10), 0f) && Mathf.Approximately(Bot.AimErrorFor(5), 1.8f) && Bot.AimErrorFor(1) > Bot.AimErrorFor(4), "aim error 0° at skill 10, 1.8° at 5, grows downward");
            check(Bot.ThinkIntervalFor(10) < Bot.ThinkIntervalFor(4) && Mathf.Approximately(Bot.ThinkIntervalFor(10), 0.05f), "think interval scales with skill");
            check(Bot.BunnyhopsAt(7) && Bot.BunnyhopsAt(10) && !Bot.BunnyhopsAt(6), "bunnyhop from skill 7");
            MatchSettings.OverrideBotSkillForTest(8); // dev.16 shipped a fixed 8/6/4; dev.18 derives it from the difficulty setting
            check(MatchSettings.BotSkillFor(0) == 8 && MatchSettings.BotSkillFor(1) == 7 && MatchSettings.BotSkillFor(2) == 6 && MatchSettings.BotSkillFor(3) == 8, "bot skills cycle base/base-1/base-2");
            MatchSettings.OverrideBotSkillForTest(MatchSettings.DefaultBotSkill);

            // ---------------- item want ----------------
            check(Bot.ItemWant(PickupType.Health, WeaponType.Blaster, false, 30, 0, 1, 0) > Bot.ItemWant(PickupType.Health, WeaponType.Blaster, false, 60, 0, 1, 0), "low health wants health more");
            check(Bot.ItemWant(PickupType.Weapon, WeaponType.Vortex, false, 100, 0, 1, 0) > Bot.ItemWant(PickupType.Weapon, WeaponType.Vortex, true, 100, 0, 1, 0), "unowned weapon preferred");

            // ---------------- navigator corner following ----------------
            var corners = new[] { new Vector3(0, 0, 0), new Vector3(5, 0, 0), new Vector3(5, 0, 5), new Vector3(10, 0, 5) };
            check(BotNavigator.AdvanceCorner(corners, 0, Vector3.zero, 0.6f) == 1, "corner 0 (own position) is skipped");
            check(BotNavigator.AdvanceCorner(corners, 1, new Vector3(5, 0, 0.2f), 0.6f) == 2, "reached corner advances");
            var tight = new[] { new Vector3(0, 0, 0), new Vector3(5, 0, 0), new Vector3(5, 0, 0.5f), new Vector3(10, 0, 5) };
            check(BotNavigator.AdvanceCorner(tight, 1, new Vector3(5, 0, 0.2f), 0.6f) == 3, "several reached corners skipped");
            check(BotNavigator.AdvanceCorner(corners, 1, new Vector3(5, 0, 4.9f), 0.6f) == 1, "far corner is not skipped");
            check(BotNavigator.AdvanceCorner(corners, 3, new Vector3(10, 0, 5), 0.6f) == 3, "last corner sticks");
            check(BotNavigator.Flat(new Vector3(0, 5, 0)) == Vector3.zero && Mathf.Approximately(BotNavigator.Flat(new Vector3(3, 9, 4)).magnitude, 1f), "Flat drops Y and normalises");

            // ---------------- navmesh bake on synthetic geometry ----------------
            RunNavMeshBakeTest(check);

            // ---------------- game loop ----------------
            string manifest = "<?xml version=\"1.0\" encoding=\"utf-8\"?><manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"com.ayoub.myxonotic\"><application><activity android:name=\"com.unity3d.player.UnityPlayerActivity\"><intent-filter><action android:name=\"android.intent.action.MAIN\"/><category android:name=\"android.intent.category.LAUNCHER\"/></intent-filter></activity></application></manifest>";
            string patched = GameLoopManifest.Patch(manifest);
            check(patched.Contains(GameLoop.Action) && patched.Contains("application/javascript") && patched.Contains(GameLoopManifest.LoopsMetaData), "manifest gets TEST_LOOP filter + loops meta-data");
            check(GameLoopManifest.Patch(patched) == patched, "patching twice is idempotent");
            check(patched.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>"), "patched manifest declares utf-8");
            string stale = patched.Replace("encoding=\"utf-8\"", "encoding=\"utf-16\"");
            check(GameLoopManifest.Patch(stale).StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>"), "stale utf-16 header is repaired even when already patched");
            check(CountOccurrences(patched, "android.intent.category.LAUNCHER") == 1 && CountOccurrences(patched, GameLoop.Action) == 1, "launcher filter kept, loop filter once");
            check(!GameLoop.Active, "game loop inactive in the editor");
            GameLoop.OverrideForTest(true, 2);
            check(GameLoop.Active && GameLoop.DurationSeconds == 180, "scenario 2 = 180 s");
            string report = GameLoop.BuildReport(120f, 7200, 0.05f);
            check(report.Contains("avg_fps=60.0") && report.Contains("worst_frame_ms=50") && report.Contains("scenario=2"), "game loop report fields");
            GameLoop.OverrideForTest(false, 1);

            // ---------------- adaptive resolution ----------------
            check(AdaptiveResolution.Decide(0, 30f) == 1 && AdaptiveResolution.Decide(1, 30f) == 2, "slow frames step resolution down");
            check(AdaptiveResolution.Decide(3, 30f) == 3, "lowest level is a floor");
            check(AdaptiveResolution.Decide(0, 16f) == 0 && AdaptiveResolution.Decide(2, 16f) == 2, "in-budget frames keep the level (hysteresis)");
            check(AdaptiveResolution.Decide(2, 10f) == 1 && AdaptiveResolution.Decide(0, 10f) == 0, "headroom steps up, never above native");
        }

        static int CountOccurrences(string s, string sub)
        {
            int n = 0, i = 0;
            while ((i = s.IndexOf(sub, i, StringComparison.Ordinal)) >= 0) { n++; i += sub.Length; }
            return n;
        }

        /// Floor 20×20 m with a 12 m wall across the middle (gap at one end): the path
        /// from one side to the other must bend around the wall and be longer than the straight line.
        static void RunNavMeshBakeTest(Action<bool, string> check)
        {
            var root = new GameObject("NavTestRoot");
            var made = new List<GameObject>();
            try
            {
                made.Add(Box(root.transform, "floor", new Vector3(0, -0.5f, 0), new Vector3(20, 1, 20)));
                made.Add(Box(root.transform, "wall", new Vector3(0, 1, -2), new Vector3(0.5f, 2, 14)));
                var trigger = Box(root.transform, "trigger", new Vector3(5, 1, 5), new Vector3(2, 2, 2));
                trigger.GetComponent<Collider>().isTrigger = true;
                made.Add(trigger);
                Physics.SyncTransforms();

                var result = NavMeshBake.Build(root.transform);
                check(result.Sources == 2, "bake collects non-trigger colliders only (" + result.Sources + ")");
                check(result.Data != null && result.Polygons > 0, "bake produces polygons (" + result.Polygons + ")");
                check(result.WalkableArea > 300f && result.WalkableArea < 400f, "walkable area ≈ floor minus agent margins (" + result.WalkableArea.ToString("0") + " m²)");

                var instance = NavMesh.AddNavMeshData(result.Data);
                MapNavMesh.SetAvailableForTest(true);
                try
                {
                    var path = new NavMeshPath();
                    Vector3 a = new Vector3(-6, 0, -2), b = new Vector3(6, 0, -2);
                    bool ok = NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path);
                    check(ok && path.status == NavMeshPathStatus.PathComplete, "path across the wall is complete");
                    float length = 0f;
                    for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                    check(path.corners.Length >= 3 && length > 14f, "path bends around the wall (" + path.corners.Length + " corners, " + length.ToString("0.0") + " m)");
                    check(path.corners[path.corners.Length - 1].z > 3f || Mathf.Abs(path.corners[1].z - (-2f)) > 3f, "detour passes the open end of the wall");

                    var nav = new BotNavigator();
                    nav.SetGoal(b);
                    bool jump;
                    Vector3 steer = nav.Steer(a, out jump);
                    check(steer != Vector3.zero && Mathf.Abs(steer.z) > 0.3f, "navigator steers sideways around the wall first, not straight through");
                    check(!nav.GoalUnreachable && nav.HasPath, "navigator has a valid path");

                    nav.SetGoal(new Vector3(0, 0, 40)); // off the mesh (beyond SampleRadius): sampled onto the edge, still reachable
                    nav.Steer(a, out jump);
                    check(nav.HasGoal, "navigator keeps goals that are off the mesh");
                }
                finally
                {
                    instance.Remove();
                    MapNavMesh.SetAvailableForTest(false);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            return go;
        }
    }
}
