using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// dev.16: bakes a NavMesh for an imported map (all non-trigger colliders under
    /// the arena root) and saves it as an asset next to the map scene. Runs inside
    /// FullGameBuild.ImportMapScene, so every packaged map ships a navmesh and
    /// bots can path-find (roadmap dev.16). Uses the runtime NavMeshBuilder API,
    /// therefore also usable from Editor tests on synthetic geometry.
    /// </summary>
    public static class NavMeshBake
    {
        public const string Folder = "Assets/MyXonotic/Generated/NavMesh";

        public struct Result
        {
            public NavMeshData Data;
            public int Sources;
            public int Polygons;
            public float WalkableArea;
            public float Seconds;
        }

        /// Build NavMeshData for every non-trigger collider under <paramref name="root"/> (world space).
        public static Result Build(Transform root)
        {
            var t0 = System.DateTime.UtcNow;
            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            NavMeshBuilder.CollectSources(root, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);
            // Triggers (jump pads, teleporters, item volumes) are not floors.
            sources.RemoveAll(s => s.component is Collider c && c.isTrigger);

            var bounds = new Bounds(root.position, Vector3.zero);
            bool any = false;
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger) continue;
                if (!any) { bounds = col.bounds; any = true; }
                else bounds.Encapsulate(col.bounds);
            }
            bounds.Expand(2f);

            var result = new Result { Sources = sources.Count };
            if (sources.Count == 0) return result;

            result.Data = NavMeshBuilder.BuildNavMeshData(MapNavMesh.AgentSettings(), sources, bounds, Vector3.zero, Quaternion.identity);
            if (result.Data != null)
            {
                var instance = NavMesh.AddNavMeshData(result.Data);
                var tri = NavMesh.CalculateTriangulation();
                result.Polygons = tri.indices.Length / 3;
                result.WalkableArea = Area(tri);
                instance.Remove();
            }
            result.Seconds = (float)(System.DateTime.UtcNow - t0).TotalSeconds;
            return result;
        }

        /// Pure (tested): total triangle area of a triangulation.
        public static float Area(NavMeshTriangulation tri)
        {
            float area = 0f;
            for (int i = 0; i + 2 < tri.indices.Length; i += 3)
            {
                Vector3 a = tri.vertices[tri.indices[i]], b = tri.vertices[tri.indices[i + 1]], c = tri.vertices[tri.indices[i + 2]];
                area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            }
            return area;
        }

        /// Bake for a map scene and attach a MapNavMesh to <paramref name="holder"/>; returns the result.
        public static Result BakeForScene(Transform arenaRoot, GameObject holder, string sceneName)
        {
            var result = Build(arenaRoot);
            if (result.Data == null) return result;
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/MyXonotic/Generated")) AssetDatabase.CreateFolder("Assets/MyXonotic", "Generated");
                AssetDatabase.CreateFolder("Assets/MyXonotic/Generated", "NavMesh");
            }
            string path = Folder + "/" + sceneName + ".asset";
            result.Data.name = sceneName + "-navmesh";
            AssetDatabase.CreateAsset(result.Data, path);
            var nav = holder.AddComponent<MapNavMesh>();
            nav.Data = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
            nav.WalkableArea = result.WalkableArea;
            nav.Polygons = result.Polygons;
            return result;
        }
    }
}
