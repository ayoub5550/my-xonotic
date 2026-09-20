using System.Collections.Generic;

namespace MyXonotic.Content.Bsp
{
    /// <summary>
    /// Fully parsed, in-memory representation of one IBSP v46 file. Contains
    /// only the lumps this importer understands (geometry + entities);
    /// visibility/collision-tree lumps (planes/nodes/leafs/brushes) are
    /// intentionally not modeled here — see BspReader warnings.
    /// </summary>
    public sealed class BspDocument
    {
        public int Version;
        public List<BspEntity> Entities = new List<BspEntity>();
        public BspShader[] Shaders = System.Array.Empty<BspShader>();
        public BspVertex[] Vertexes = System.Array.Empty<BspVertex>();
        public int[] MeshVerts = System.Array.Empty<int>();
        public BspFace[] Faces = System.Array.Empty<BspFace>();
        public BspModel[] Models = System.Array.Empty<BspModel>();
        /// <summary>Internal 128x128 RGB lightmap blocks (empty when the map uses external lightmaps).</summary>
        public byte[][] Lightmaps = System.Array.Empty<byte[]>();
        public List<string> Warnings = new List<string>();
    }
}
