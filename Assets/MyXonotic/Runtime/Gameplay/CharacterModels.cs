using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Runtime access to the original player models baked by
    /// IqmCharacterImporter into Resources/Characters (static idle pose).
    /// Returns false when a model is absent so callers keep their capsule.
    /// </summary>
    public static class CharacterModels
    {
        public static readonly string[] Names =
        {
            "erebus", "gak", "gakmasked", "ignis", "ignismasked", "megaerebus",
            "nyx", "pyria", "seraphina", "seraphinamasked", "umbra"
        };

        /// <summary>Xonotic player origin sits 24 units (0.75 m) above the feet.</summary>
        public const float OriginAboveFeet = 24f / 32f;

        public static bool Exists(string name) => Resources.Load<Mesh>("Characters/" + name + "_Mesh") != null;

        public static bool TryAttach(Transform parent, string name, out GameObject body)
        {
            body = null;
            var mesh = Resources.Load<Mesh>("Characters/" + name + "_Mesh");
            if (mesh == null) return false;
            var mats = new System.Collections.Generic.List<Material>();
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var m = Resources.Load<Material>("Characters/" + name + "_Material_" + i);
                mats.Add(m);
            }
            body = new GameObject("Model_" + name);
            body.transform.SetParent(parent, false);
            body.transform.localPosition = new Vector3(0f, OriginAboveFeet, 0f);
            // Mesh faces Quake +X; the actor faces Unity +Z.
            body.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            body.AddComponent<MeshRenderer>().sharedMaterials = mats.ToArray();
            return true;
        }

        /// <summary>Pick the i-th available model (cycling), or null when none was imported.</summary>
        public static string PickForIndex(int i)
        {
            var available = new System.Collections.Generic.List<string>();
            foreach (var n in Names) if (Exists(n)) available.Add(n);
            if (available.Count == 0) return null;
            return available[((i % available.Count) + available.Count) % available.Count];
        }
    }
}
