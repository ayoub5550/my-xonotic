using UnityEngine;

namespace MyXonotic.Content
{
    /// <summary>
    /// Marks a visible inline brush submodel (func_wall, func_door, ...) that
    /// the import pipeline placed at its BSP rest position, together with the
    /// entity keys a mover needs (angle/speed/lip/wait/height/spawnflags) and
    /// the submodel's local bounds in Unity space. <c>MyXonotic.Mover</c> reads
    /// this at runtime; no BSP re-read is needed.
    /// </summary>
    public sealed class ImportedSubmodel : MonoBehaviour
    {
        public string classname;
        public int modelIndex;
        public string targetName;
        public string target;
        /// Quake "angle" (yaw; -1 = up, -2 = down) or NaN when absent.
        public float angle = float.NaN;
        /// Quake "angles" (pitch yaw roll) when present.
        public Vector3 angles;
        public bool hasAngles;
        public float speed = float.NaN;
        public float lip = float.NaN;
        public float wait = float.NaN;
        public float height = float.NaN;
        public float phase;
        public int spawnflags;
        public int dmg;
        /// Submodel bounds relative to this transform, Unity units.
        public Vector3 localMin;
        public Vector3 localMax;
    }
}
