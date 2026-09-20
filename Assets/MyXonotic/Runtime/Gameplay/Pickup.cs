using UnityEngine;

namespace MyXonotic
{
    public enum PickupType { Health, Armor, AmmoBlaster, AmmoRifle, AmmoRocket }

    /// <summary>World pickup with a respawn timer; grants health/armor/ammo on touch.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Pickup : MonoBehaviour
    {
        public PickupType Type;
        public int Amount = 25;
        public float RespawnTime = 12f;

        MeshRenderer _renderer;
        Collider _collider;
        float _respawnTimer;
        bool _active = true;
        public bool IsAvailable => _active;

        void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _collider = GetComponent<Collider>();
            _collider.isTrigger = true;
        }

        void Update()
        {
            if (ArenaBootstrap.IsPaused) return;
            transform.Rotate(Vector3.up, 60f * Time.deltaTime);
            if (_active) return;
            _respawnTimer -= Time.deltaTime;
            if (_respawnTimer <= 0f) SetActive(true);
        }

        void OnTriggerEnter(Collider other)
        {
            if (!_active || ArenaBootstrap.IsPaused) return;
            var actor = other.GetComponentInParent<Actor>();
            if (actor == null || actor.IsDead) return;

            switch (Type)
            {
                case PickupType.Health:
                    if (actor.Health >= Actor.MaxHealth) return;
                    actor.AddHealth(Amount); break;
                case PickupType.Armor:
                    if (actor.Armor >= Actor.MaxArmor) return;
                    actor.AddArmor(Amount); break;
                case PickupType.AmmoBlaster: GrantAmmo(actor, WeaponType.Blaster); break;
                case PickupType.AmmoRifle: GrantAmmo(actor, WeaponType.Rifle); break;
                case PickupType.AmmoRocket: GrantAmmo(actor, WeaponType.Rocket); break;
            }
            SetActive(false);
            _respawnTimer = RespawnTime;
        }

        void GrantAmmo(Actor actor, WeaponType type)
        {
            var wc = actor.GetComponent<WeaponController>();
            if (wc != null) wc.AddAmmo(type, Amount);
        }

        void SetActive(bool active)
        {
            _active = active;
            if (_renderer != null) _renderer.enabled = active;
            if (_collider != null) _collider.enabled = active;
        }

        /// Used by ArenaBootstrap.Restart() to reset all pickups immediately.
        public void ForceActivate() => SetActive(true);
    }
}
