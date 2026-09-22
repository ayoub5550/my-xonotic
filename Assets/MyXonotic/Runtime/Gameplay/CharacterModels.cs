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

        /// True when the skeletal variant (rig + skinned mesh) was imported for <paramref name="name"/>.
        public static bool HasRig(string name) =>
            Resources.Load<CharacterRig>("Characters/" + name + "_Rig") != null &&
            Resources.Load<Mesh>("Characters/" + name + "_Skinned") != null;

        public static bool TryAttach(Transform parent, string name, out GameObject body) => TryAttach(parent, name, out body, out _);

        /// <summary>
        /// Attaches the character: the animated skinned rig when available
        /// (<paramref name="animator"/> set), else the static idle mesh.
        /// </summary>
        public static bool TryAttach(Transform parent, string name, out GameObject body, out CharacterAnimator animator)
        {
            body = null;
            animator = null;
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

            var rig = Resources.Load<CharacterRig>("Characters/" + name + "_Rig");
            var skinned = Resources.Load<Mesh>("Characters/" + name + "_Skinned");
            if (rig != null && skinned != null && rig.JointCount > 0 && skinned.bindposes != null && skinned.bindposes.Length == rig.JointCount)
            {
                animator = CharacterAnimator.Create(body.transform, rig, skinned, mats.ToArray());
                return true;
            }
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
