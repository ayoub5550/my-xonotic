using UnityEngine;
using UnityEngine.AI;

namespace MyXonotic
{
    /// <summary>
    /// dev.16: registers the NavMesh baked for this map at prepare-maps
    /// (FullGameBuild.BakeNavMesh) so bots can path-find. The asset lives in
    /// Generated/NavMesh/ and is referenced by the MatchRules object of every
    /// map scene. Without it (DevelopmentArena, tests without a bake) bots fall
    /// back to straight-line steering.
    /// </summary>
    public sealed class MapNavMesh : MonoBehaviour
    {
        public NavMeshData Data;
        /// Walkable area in m² measured at bake time (diagnostics / tests).
        public float WalkableArea;
        /// Number of polygons in the baked mesh (diagnostics).
        public int Polygons;

        NavMeshDataInstance _instance;

        /// True while any NavMesh is loaded (baked map or test-built).
        public static bool Available { get; private set; }

        void OnEnable()
        {
            if (Data == null) return;
            _instance = NavMesh.AddNavMeshData(Data);
            Available = true;
        }

        void OnDisable()
        {
            if (_instance.valid) _instance.Remove();
            Available = false;
        }

        /// Test hook: mark availability explicitly (e.g. after NavMesh.AddNavMeshData in an Editor test).
        public static void SetAvailableForTest(bool available) => Available = available;

        /// Xonotic agent: player bbox 32x32x56 qu (Q = 1/32 m) -> radius 0.5 m, height 1.75 m;
        /// step 31 qu (sv_stepheight 31 in physicsX.cfg) -> 0.97 m; slope 45° (Quake default).
        public static NavMeshBuildSettings AgentSettings()
        {
            var s = NavMesh.GetSettingsByID(0);
            s.agentRadius = 0.5f;
            s.agentHeight = 1.75f;
            s.agentClimb = 31f / 32f;
            s.agentSlope = 45f;
            // Coarser voxels than the default radius/3 keep a 29-map bake on one
            // CPU within minutes; 0.2 m still resolves every Xonotic corridor (>= 64 qu = 2 m).
            s.overrideVoxelSize = true;
            s.voxelSize = 0.2f;
            s.overrideTileSize = true;
            s.tileSize = 128;
            s.minRegionArea = 2f;
            return s;
        }
    }
}
