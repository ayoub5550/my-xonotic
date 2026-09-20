using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Cached built-in primitive meshes. This slice ships no imported meshes/assets,
    /// so all visible geometry reuses Unity's stock primitives.
    /// </summary>
    public static class ArenaPrimitives
    {
        static Mesh _cube;
        static Mesh _sphere;
        static Mesh _capsule;
        static Mesh _cylinder;

        public static Mesh CubeMesh => _cube != null ? _cube : (_cube = Grab(PrimitiveType.Cube));
        public static Mesh SphereMesh => _sphere != null ? _sphere : (_sphere = Grab(PrimitiveType.Sphere));
        public static Mesh CapsuleMesh => _capsule != null ? _capsule : (_capsule = Grab(PrimitiveType.Capsule));
        public static Mesh CylinderMesh => _cylinder != null ? _cylinder : (_cylinder = Grab(PrimitiveType.Cylinder));

        static Mesh Grab(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            go.SetActive(false);
            Object.Destroy(go);
            return mesh;
        }
    }
}
