using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Fallback spawn marker created by ArenaBootstrap when no imported BSP spawn
    /// points (MyXonotic.Content.BspSpawnPoint) are available.
    /// </summary>
    public sealed class SpawnMarker : MonoBehaviour
    {
        public float Yaw;
    }
}
