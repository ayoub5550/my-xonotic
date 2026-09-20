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
    /// Quake's right-handed Z-up system to Unity's left-handed Y-up system),
    /// so every triangle's winding order must also be reversed
    /// (index order a,b,c -> a,c,b) or normals/backface culling come out
    /// inside-out. This is applied once, centrally, in
    /// <see cref="BspGeometryBuilder"/>.
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
