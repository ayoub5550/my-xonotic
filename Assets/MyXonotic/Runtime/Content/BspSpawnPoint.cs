using UnityEngine;

namespace MyXonotic.Content
{
    /// <summary>
    /// Marker for one info_player_deathmatch/info_player_start entity found
    /// in the BSP entity lump. This is a spawn *marker*, not a player: the
    /// importer never instantiates a player prefab, it only records where
    /// and which way one could face if spawned here.
    ///
    /// The GameObject's position already carries the feet-on-floor origin
    /// converted via BspCoordinateSpace.QuakeToUnity (see that class for the
    /// full coordinate-convention writeup, including why no eye-height
    /// offset is applied here).
    /// </summary>
    public sealed class BspSpawnPoint : MonoBehaviour
    {
        [Tooltip("Facing angle in Unity's Y-axis Euler convention, converted from the entity's Quake 'angle'/'yaw' key.")]
        public float yaw;

        [Tooltip("Originating entity classname, e.g. info_player_deathmatch or info_player_start.")]
        public string sourceClass;
    }
}
