namespace MyXonotic.Content.Bsp
{
    /// <summary>
    /// Source maps use Z-up; this PROJECT chooses 32 source units per Unity
    /// unit. This is not an assertion about physical units in the original game.
    /// Unity is Y-up. This class documents and implements
    /// the single conversion used everywhere in the importer so there is one
    /// place to audit:
    ///
    ///   unity.x =  quake.x / 32
    ///   unity.y =  quake.z / 32   (Quake "up" becomes Unity "up")
    ///   unity.z =  quake.y / 32
    ///
    /// Swapping Y and Z is a mirroring transform (it flips handedness from
    /// Quake's right-handed Z-up system to Unity's left-handed Y-up system).
    /// A theory-only reading of that says every triangle's winding order
    /// must also be reversed for normals/backface culling to come out
    /// right-side-out — an earlier revision of this importer did exactly
    /// that (index order a,b,c -> a,c,b) for polygon/mesh faces. That was
    /// measured (numerically, against real compiled Xonotic maps: cross
    /// product of the emitted triangle vs. the source's own per-vertex
    /// normal, both axis-converted) to be backwards: it produced
    /// inward-facing world geometry for ~99.9% of a real map's faces
    /// (players fell through the floor onto its underside). Polygon/mesh
    /// faces are now emitted in the SAME order the source meshverts define
    /// (no reversal). Patch faces (this project's own from-scratch grid
    /// tessellation, not a meshvert triangle-soup from the map compiler)
    /// were independently measured to need the opposite convention and are
    /// unchanged. See BspGeometryBuilder.AppendIndexedFace /
    /// AddQuadTriangles for the emission code and tests/csharp/ParserTests.cs
    /// for the regression guard against real map data. Trust a numeric
    /// check against real data over an isolated theoretical argument.
    ///
    /// Player-origin note: source "origin" is the player hull origin, NOT
    /// feet or eye height. The nominal hull bottom is origin.z - 24.
    /// This importer only emits spawn markers (see BspSpawnPoint); it does
    /// not create a player, so no eye-height offset is applied here. Any
    /// runtime player controller consuming these markers is responsible for
    /// converting to its own convention. ContentBridge subtracts 24/32
    /// from Y for our feet-based CharacterController.
    /// </summary>
    public static class BspCoordinateSpace
    {
        public const float SourceUnitsPerUnityUnit = 32f;

        public static BspVec3 QuakeToUnity(BspVec3 q)
        {
            return new BspVec3(q.X / SourceUnitsPerUnityUnit, q.Z / SourceUnitsPerUnityUnit,
                q.Y / SourceUnitsPerUnityUnit);
        }

        /// <summary>Direction/normal conversion: same axis swap, no scale.</summary>
        public static BspVec3 QuakeDirectionToUnity(BspVec3 q)
        {
            return new BspVec3(q.X, q.Z, q.Y);
        }

        /// <summary>
        /// Quake's yaw is degrees around the up axis, 0 = +X, increasing
        /// counter-clockwise looking from above. Unity's Transform.eulerAngles.y
        /// is degrees around +Y, increasing clockwise looking from above
        /// (i.e. from +Y down). Converting requires negating and re-basing:
        /// unityYaw = 90 - quakeYaw (kept unnormalized; Unity wraps it).
        /// </summary>
        public static float QuakeYawToUnity(float quakeYawDegrees)
        {
            return 90f - quakeYawDegrees;
        }
    }
}
