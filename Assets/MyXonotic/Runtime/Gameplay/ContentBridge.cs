using System.Collections.Generic;
using MyXonotic.Content;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>Typed content bridge, safe under IL2CPP stripping (no reflection).</summary>
    public static class ContentBridge
    {
        public struct SpawnInfo
        {
            public Vector3 Position;
            public float Yaw;
            public string SourceClass;
        }

        public static bool HasImportedArena() =>
            Object.FindObjectOfType<ImportedArena>() != null;

        public static List<SpawnInfo> FindBspSpawnPoints()
        {
            var result = new List<SpawnInfo>();
            foreach (var marker in Object.FindObjectsOfType<BspSpawnPoint>())
            {
                // Source spawn origin is centre-based with nominal mins.z = -24.
                // Our CharacterController transform is feet-based. Keep this
                // conversion explicit instead of silently using origin as feet.
                result.Add(new SpawnInfo
                {
                    Position = marker.transform.position - Vector3.up * (24f / 32f),
                    Yaw = marker.yaw,
                    SourceClass = marker.sourceClass
                });
            }
            return result;
        }
    }
}
