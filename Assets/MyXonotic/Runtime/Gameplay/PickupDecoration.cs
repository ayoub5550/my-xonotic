using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Visual-only original item/weapon model for pickup classes that have
    /// no gameplay yet (weapons, cells, power-ups). Spins like a Pickup but
    /// cannot be collected; replaced by real pickups as gameplay lands.
    /// </summary>
    public sealed class PickupDecoration : MonoBehaviour
    {
        void Update()
        {
            if (ArenaBootstrap.IsPaused) return;
            transform.Rotate(Vector3.up, 60f * Time.deltaTime);
        }
    }
}
