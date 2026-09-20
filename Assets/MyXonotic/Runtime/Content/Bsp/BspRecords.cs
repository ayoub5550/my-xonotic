namespace MyXonotic.Content.Bsp
{
    /// <summary>
    /// Lump indices for the IBSP (Quake III family, version 46) format.
    /// This is a de-facto public file-format layout (documented in numerous
    /// independent third-party references); no GPL engine source was
    /// consulted or copied to write this parser.
    /// </summary>
    public enum BspLump
    {
        Entities = 0,
        Shaders = 1,
        Planes = 2,
        Nodes = 3,
        Leafs = 4,
        LeafFaces = 5,
        LeafBrushes = 6,
        Models = 7,
        Brushes = 8,
        BrushSides = 9,
        Vertexes = 10,
        MeshVerts = 11,
        Effects = 12,
        Faces = 13,
        LightMaps = 14,
        LightVols = 15,
        VisData = 16,

        Count = 17,
    }

    public enum BspFaceType
    {
        Polygon = 1,
        Patch = 2,
        Mesh = 3,
        Billboard = 4,
    }

    public struct BspLumpDirEntry
    {
        public int Offset;
        public int Length;
    }

    public struct BspShader
    {
        public string Name;
        public int ContentFlags;
        public int SurfaceFlags;
    }

    public struct BspVertex
    {
        public BspVec3 Position;
        public BspVec2 SurfaceUv;
        public BspVec2 LightmapUv;
        public BspVec3 Normal;
        public BspColor32 Color;
    }

    public struct BspFace
    {
        public int Texture;
        public int Effect;
        public int Type;
        public int Vertex;
        public int NumVertexes;
        public int MeshVert;
        public int NumMeshVerts;
        public int LightmapIndex;
        public int PatchWidth;
        public int PatchHeight;
    }

    public struct BspModel
    {
        public BspVec3 Mins;
        public BspVec3 Maxs;
        public int Face;
        public int NumFaces;
        public int Brush;
        public int NumBrushes;
    }

    /// <summary>
    /// Parsed "{ key value }" block from the entities lump.
    /// </summary>
    public sealed class BspEntity
    {
        public readonly System.Collections.Generic.Dictionary<string, string> Properties =
            new System.Collections.Generic.Dictionary<string, string>();

        public string Get(string key, string fallback = null)
        {
            string value;
            return Properties.TryGetValue(key, out value) ? value : fallback;
        }
    }
}
