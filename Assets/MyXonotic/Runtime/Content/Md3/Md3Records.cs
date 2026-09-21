using MyXonotic.Content.Bsp;

namespace MyXonotic.Content.Md3
{
    /// <summary>
    /// One MD3 surface (a model is a small set of independently-shaded
    /// triangle meshes sharing the same skeleton of per-vertex-animated
    /// frames — e.g. a player model's head/upper/lower, or a weapon's body
    /// plus a separate flash/attachment piece). Only frame 0 is decoded:
    /// see <see cref="Md3StaticModel"/> for why.
    /// </summary>
    public sealed class Md3Surface
    {
        public string Name;

        /// <summary>
        /// Shader (skin) names embedded in the file for this surface, in
        /// file order. Real MD3 files store full content-relative paths
        /// here (e.g. "models/weapons/v_rl.tga"); index 0 is the default
        /// skin. Empty when the file declares zero shaders for this surface
        /// (some tools omit them and rely on an external .skin file, which
        /// this reader does not parse).
        /// </summary>
        public string[] ShaderNames;

        /// <summary>Frame-0 vertex positions, already converted to this project's Unity coordinate convention.</summary>
        public BspVec3[] Positions;

        /// <summary>Frame-0 vertex normals decoded from MD3's compressed lat/long encoding, converted to Unity axes.</summary>
        public BspVec3[] Normals;

        /// <summary>Per-vertex texture coordinates (shared by every frame in a real MD3 file).</summary>
        public BspVec2[] TexCoords;

        /// <summary>Flat triangle index list, 3 ints per triangle, in the source file's winding order.</summary>
        public int[] Triangles;
    }

    /// <summary>
    /// Result of reading the static (first-frame-only) subset of one MD3
    /// model. A genuine MD3 file's animation IS its per-frame vertex
    /// positions (there is no separate skeleton/pose format like IQM's);
    /// this project has no runtime frame-blending/animation playback yet,
    /// so only frame 0 (the model's bind/reference pose) is decoded and any
    /// additional frames are left unread. This is a deliberate static-first
    /// limitation, not a claim that the source file itself lacks animation.
    /// Tags (attachment points, e.g. weapon-to-hand) are present in real MD3
    /// files but are not parsed here; nothing in this project's rendering
    /// path consumes them yet.
    /// </summary>
    public sealed class Md3StaticModel
    {
        public string Name;
        public Md3Surface[] Surfaces;
    }
}
