using System.Collections.Generic;

namespace MyXonotic.Content.Bsp
{
    public enum BspSurfaceKind
    {
        Polygon,
        Mesh,
        Patch,
        Unsupported,
    }

    /// <summary>
    /// Engine-agnostic triangle soup ready to be handed to a Unity Mesh.
    /// Coordinates are already converted to Unity space and winding is
    /// already reversed for the handedness flip.
    /// </summary>
    public sealed class BspMeshData
    {
        public readonly List<BspVec3> Positions = new List<BspVec3>();
        public readonly List<BspColor32> Colors = new List<BspColor32>();
        public readonly List<int> Triangles = new List<int>();

        /// <summary>
        /// Indices into Positions for surfaces that come from
        /// surfaces considered solid/collidable. Stored as a second flat
        /// triangle list re-using the same Positions array so a
        /// MeshCollider can be built for eligible geometry only.
        /// </summary>
        public readonly List<int> CollisionTriangles = new List<int>();
    }

    /// <summary>
    /// Converts the polygon/mesh/patch faces of a single BSP model (normally
    /// model 0 = worldspawn) into renderable + collidable triangle data.
    /// Quake III face meshverts are already triangulated by the map
    /// compiler (each consecutive triple of meshvert-resolved indices is one
    /// triangle) — this is the documented, public structure of the format,
    /// not an algorithm lifted from engine source. Patch surfaces are
    /// re-tessellated here from their control-point grid using a generic
    /// biquadratic Bezier evaluation (De Casteljau), a standard published
    /// technique, independently implemented.
    /// </summary>
    public static class BspGeometryBuilder
    {
        /// <summary>Number of subdivisions per 3x3 patch segment (level+1 samples per axis).</summary>
        public const int PatchTessellationLevel = 4;

        private const int ContentsSolid = 0x1;
        private const int SurfaceNoDraw = 0x80;
        // SURF_SKIP is 0x200 (0x10 is SURF_NOIMPACT, a different flag: an
        // earlier revision of this file used the wrong constant).
        private const int SurfaceSkip = 0x200;
        // A face can be both invisible (NODRAW) and solid at the same time
        // (e.g. caulk over structural brushwork) — visibility and collision
        // are independent axes. SURF_NONSOLID is the flag that actually
        // opts a face out of collision regardless of its shader's
        // CONTENTS_SOLID bit.
        private const int SurfaceNonSolid = 0x4000;

        private const int MaxPatchDimension = 129; // matches the Q3 map-compiler ceiling for control grid width/height.
        private const int MaxGeneratedVerticesPerModel = 4_000_000;
        private const int MaxGeneratedIndicesPerModel = 8_000_000;

        public static BspMeshData BuildModel(BspDocument doc, int modelIndex, List<string> warnings)
        {
            var mesh = new BspMeshData();
            if (modelIndex < 0 || modelIndex >= doc.Models.Length)
            {
                warnings.Add(string.Format("Requested model #{0} does not exist; nothing built.", modelIndex));
                return mesh;
            }

            var model = doc.Models[modelIndex];
            for (int fi = model.Face; fi < model.Face + model.NumFaces; fi++)
            {
                var face = doc.Faces[fi];
                bool eligibleForCollision = IsCollisionEligible(doc, face, warnings, fi);
                bool skipDraw = IsNoDraw(doc, face);
                if (skipDraw && !eligibleForCollision) continue;

                switch (face.Type)
                {
                    case (int)BspFaceType.Polygon:
                    case (int)BspFaceType.Mesh:
                        AppendIndexedFace(doc, face, mesh, skipDraw, eligibleForCollision);
                        break;

                    case (int)BspFaceType.Patch:
                        AppendPatchFace(doc, face, mesh, skipDraw, eligibleForCollision, warnings, fi);
                        break;

                    case (int)BspFaceType.Billboard:
                        warnings.Add(string.Format(
                            "Face #{0} is a billboard/flare surface; unsupported, skipped (no geometry emitted).", fi));
                        break;

                    default:
                        warnings.Add(string.Format(
                            "Face #{0} has unrecognized type {1}; unsupported, skipped.", fi, face.Type));
                        break;
                }
            }

            return mesh;
        }

        private static void RequireBudget(BspMeshData mesh, long vertices, long indices, bool draw, bool collide)
        {
            if (mesh.Positions.Count + vertices > MaxGeneratedVerticesPerModel ||
                (draw && mesh.Triangles.Count + indices > MaxGeneratedIndicesPerModel) ||
                (collide && mesh.CollisionTriangles.Count + indices > MaxGeneratedIndicesPerModel))
                throw new BspFormatException("Model exceeds the generated geometry budget; no partial arena was imported.");
        }

        private static bool IsNoDraw(BspDocument doc, BspFace face)
        {
            if (face.Texture < 0 || face.Texture >= doc.Shaders.Length) return false;
            int flags = doc.Shaders[face.Texture].SurfaceFlags;
            return (flags & SurfaceNoDraw) != 0 || (flags & SurfaceSkip) != 0;
        }

        private static bool IsCollisionEligible(BspDocument doc, BspFace face, List<string> warnings, int faceIndex)
        {
            if (face.Texture < 0 || face.Texture >= doc.Shaders.Length)
            {
                warnings.Add(string.Format(
                    "Face #{0} references shader index {1} which is out of range ({2} shaders); treated as non-solid, texture missing.",
                    faceIndex, face.Texture, doc.Shaders.Length));
                return false;
            }
            var shader = doc.Shaders[face.Texture];
            if (string.IsNullOrEmpty(shader.Name))
            {
                warnings.Add(string.Format("Face #{0} uses an unnamed shader; texture missing.", faceIndex));
            }
            // Visibility (NODRAW/SKIP) and solidity (CONTENTS_SOLID) are
            // independent: an invisible caulk face can still be solid, and
            // an explicitly SURF_NONSOLID face never collides even if the
            // shader's content flags say CONTENTS_SOLID. Do NOT gate
            // collision on NODRAW/SKIP.
            if ((shader.SurfaceFlags & SurfaceNonSolid) != 0)
            {
                return false;
            }
            return (shader.ContentFlags & ContentsSolid) != 0;
        }

        private static void AppendIndexedFace(BspDocument doc, BspFace face, BspMeshData mesh, bool skipDraw, bool collisionEligible)
        {
            if (face.NumMeshVerts == 0 || face.NumMeshVerts % 3 != 0)
            {
                // Not fatal: some tools emit degenerate 0-length faces; a
                // non-multiple-of-3 count means truncated/corrupt data.
                return;
            }
            RequireBudget(mesh, face.NumVertexes, face.NumMeshVerts, !skipDraw, collisionEligible);

            int baseVertexOut = mesh.Positions.Count;
            for (int i = 0; i < face.NumVertexes; i++)
            {
                var v = doc.Vertexes[face.Vertex + i];
                mesh.Positions.Add(BspCoordinateSpace.QuakeToUnity(v.Position));
                mesh.Colors.Add(v.Color);
            }

            for (int i = 0; i < face.NumMeshVerts; i += 3)
            {
                int a = doc.MeshVerts[face.MeshVert + i + 0];
                int b = doc.MeshVerts[face.MeshVert + i + 1];
                int c = doc.MeshVerts[face.MeshVert + i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= face.NumVertexes || b >= face.NumVertexes || c >= face.NumVertexes)
                {
                    continue; // already bounds-validated at the lump level; extra guard for local slice.
                }

                int ia = baseVertexOut + a;
                int ib = baseVertexOut + b;
                int ic = baseVertexOut + c;

                if (!skipDraw)
                {
                    // Reverse winding (b,c swapped) for the Y/Z handedness flip.
                    mesh.Triangles.Add(ia);
                    mesh.Triangles.Add(ic);
                    mesh.Triangles.Add(ib);
                }
                if (collisionEligible)
                {
                    mesh.CollisionTriangles.Add(ia);
                    mesh.CollisionTriangles.Add(ic);
                    mesh.CollisionTriangles.Add(ib);
                }
            }
        }

        private static void AppendPatchFace(
            BspDocument doc, BspFace face, BspMeshData mesh, bool skipDraw, bool collisionEligible,
            List<string> warnings, int faceIndex)
        {
            int w = face.PatchWidth;
            int h = face.PatchHeight;
            if (w < 3 || h < 3 || w % 2 == 0 || h % 2 == 0 || w > MaxPatchDimension || h > MaxPatchDimension)
            {
                warnings.Add(string.Format(
                    "Face #{0} is a patch with unsupported control grid {1}x{2} (need odd dimensions in [3,{3}]); skipped.",
                    faceIndex, w, h, MaxPatchDimension));
                return;
            }
            // w*h is bounded by MaxPatchDimension^2 above, so this cannot overflow.
            if (w * h != face.NumVertexes)
            {
                warnings.Add(string.Format(
                    "Face #{0} patch grid {1}x{2} does not match vertex count {3}; skipped.",
                    faceIndex, w, h, face.NumVertexes));
                return;
            }

            int patchesX = (w - 1) / 2;
            int patchesY = (h - 1) / 2;
            int level = PatchTessellationLevel;
            int samples = level + 1;
            long segments = (long)patchesX * patchesY;
            RequireBudget(mesh, segments * samples * samples, segments * level * level * 6,
                !skipDraw, collisionEligible);

            for (int py = 0; py < patchesY; py++)
            {
                for (int px = 0; px < patchesX; px++)
                {
                    var control = new BspVertex[3, 3];
                    for (int cy = 0; cy < 3; cy++)
                    {
                        for (int cx = 0; cx < 3; cx++)
                        {
                            int gx = px * 2 + cx;
                            int gy = py * 2 + cy;
                            control[cy, cx] = doc.Vertexes[face.Vertex + gy * w + gx];
                        }
                    }

                    var grid = new int[samples, samples];
                    int baseVertexOut = mesh.Positions.Count;
                    for (int sy = 0; sy < samples; sy++)
                    {
                        float v = (float)sy / level;
                        for (int sx = 0; sx < samples; sx++)
                        {
                            float u = (float)sx / level;
                            var vertex = EvaluateBiquadratic(control, u, v);
                            mesh.Positions.Add(BspCoordinateSpace.QuakeToUnity(vertex.Position));
                            mesh.Colors.Add(vertex.Color);
                            grid[sy, sx] = baseVertexOut + sy * samples + sx;
                        }
                    }

                    for (int sy = 0; sy < level; sy++)
                    {
                        for (int sx = 0; sx < level; sx++)
                        {
                            int a = grid[sy, sx];
                            int b = grid[sy, sx + 1];
                            int c = grid[sy + 1, sx];
                            int d = grid[sy + 1, sx + 1];

                            AddQuadTriangles(mesh, a, b, c, d, skipDraw, collisionEligible);
                        }
                    }
                }
            }
        }

        private static void AddQuadTriangles(BspMeshData mesh, int a, int b, int c, int d, bool skipDraw, bool collisionEligible)
        {
            // a b
            // c d
            if (!skipDraw)
            {
                mesh.Triangles.Add(a); mesh.Triangles.Add(c); mesh.Triangles.Add(b);
                mesh.Triangles.Add(b); mesh.Triangles.Add(c); mesh.Triangles.Add(d);
            }
            if (collisionEligible)
            {
                mesh.CollisionTriangles.Add(a); mesh.CollisionTriangles.Add(c); mesh.CollisionTriangles.Add(b);
                mesh.CollisionTriangles.Add(b); mesh.CollisionTriangles.Add(c); mesh.CollisionTriangles.Add(d);
            }
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static BspVec3 Lerp(BspVec3 a, BspVec3 b, float t) =>
            new BspVec3(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t), Lerp(a.Z, b.Z, t));

        private static BspVertex EvaluateBiquadratic(BspVertex[,] control, float u, float v)
        {
            // Reduce along u for each of the 3 rows (De Casteljau, degree 2).
            var rowPos = new BspVec3[3];
            var rowUv = new BspVec2[3];
            var rowColor = new float[3, 4];
            for (int row = 0; row < 3; row++)
            {
                var p0 = control[row, 0];
                var p1 = control[row, 1];
                var p2 = control[row, 2];
                var q0 = Lerp(p0.Position, p1.Position, u);
                var q1 = Lerp(p1.Position, p2.Position, u);
                rowPos[row] = Lerp(q0, q1, u);

                var uv0 = new BspVec2(Lerp(p0.SurfaceUv.X, p1.SurfaceUv.X, u), Lerp(p0.SurfaceUv.Y, p1.SurfaceUv.Y, u));
                var uv1 = new BspVec2(Lerp(p1.SurfaceUv.X, p2.SurfaceUv.X, u), Lerp(p1.SurfaceUv.Y, p2.SurfaceUv.Y, u));
                rowUv[row] = new BspVec2(Lerp(uv0.X, uv1.X, u), Lerp(uv0.Y, uv1.Y, u));

                for (int ch = 0; ch < 4; ch++)
                {
                    float c0 = GetColorChannel(p0.Color, ch);
                    float c1 = GetColorChannel(p1.Color, ch);
                    float c2 = GetColorChannel(p2.Color, ch);
                    float d0 = Lerp(c0, c1, u);
                    float d1 = Lerp(c1, c2, u);
                    rowColor[row, ch] = Lerp(d0, d1, u);
                }
            }

            var pos0 = Lerp(rowPos[0], rowPos[1], v);
            var pos1 = Lerp(rowPos[1], rowPos[2], v);
            var finalPos = Lerp(pos0, pos1, v);

            var uvA = new BspVec2(Lerp(rowUv[0].X, rowUv[1].X, v), Lerp(rowUv[0].Y, rowUv[1].Y, v));
            var uvB = new BspVec2(Lerp(rowUv[1].X, rowUv[2].X, v), Lerp(rowUv[1].Y, rowUv[2].Y, v));
            var finalUv = new BspVec2(Lerp(uvA.X, uvB.X, v), Lerp(uvA.Y, uvB.Y, v));

            byte[] finalColor = new byte[4];
            for (int ch = 0; ch < 4; ch++)
            {
                float c0 = Lerp(rowColor[0, ch], rowColor[1, ch], v);
                float c1 = Lerp(rowColor[1, ch], rowColor[2, ch], v);
                float final = Lerp(c0, c1, v);
                finalColor[ch] = (byte)System.Math.Max(0, System.Math.Min(255, final));
            }

            return new BspVertex
            {
                Position = finalPos,
                SurfaceUv = finalUv,
                LightmapUv = default(BspVec2),
                Normal = new BspVec3(0, 1, 0),
                Color = new BspColor32(finalColor[0], finalColor[1], finalColor[2], finalColor[3]),
            };
        }

        private static float GetColorChannel(BspColor32 c, int channel)
        {
            switch (channel)
            {
                case 0: return c.R;
                case 1: return c.G;
                case 2: return c.B;
                default: return c.A;
            }
        }
    }
}
