using UnityEngine;

namespace MyXonotic
{
    public enum PickupType { Health, Armor, AmmoShells, AmmoBullets, AmmoRockets, AmmoCells, Weapon }

    /// <summary>
    /// World pickup with a respawn timer; grants health/armor/ammo on touch.
    ///
    /// Testable surface (no Play Mode / frame wait required):
    ///  - <see cref="TryCollect(Actor)"/>: the accept/reject + grant +
    ///    deactivate decision, callable directly instead of only through
    ///    OnTriggerEnter. Returns false, leaving the pickup unchanged, when:
    ///    inactive/respawning; arena paused; null/dead actor; a non-positive
    ///    <see cref="Amount"/>; already-full health/armor; or a missing/capped
    ///    ammo receiver.
    ///  - <see cref="Tick(float)"/>: the per-frame update (spin + respawn
    ///    countdown) as a plain method taking an explicit delta instead of
    ///    reading Time.deltaTime, so a test can force a respawn with one call.
    ///  - <see cref="IsAvailable"/> / <see cref="ForceActivate"/>: unchanged
    ///    (ArenaBootstrap.Restart() already calls ForceActivate() on every
    ///    pickup it owns).
    ///
    /// Both methods lazily populate the cached Renderer/Collider references if
    /// called before Awake() has run (e.g. a test that calls TryCollect/Tick
    /// right after AddComponent, in a context where Unity has not yet driven
    /// the component lifecycle).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class Pickup : MonoBehaviour
    {
        public PickupType Type;
        public int Amount = 25;
        public float RespawnTime = 12f;
        /// Which weapon a <see cref="PickupType.Weapon"/> pickup grants.
        public WeaponType Weapon = WeaponType.Shotgun;

        /// Raised after a successful grant: (pickup, collector).
        public static event System.Action<Pickup, Actor> AnyCollected;

        /// Human-readable label for HUD notifications.
        public string Label
        {
            get
            {
                switch (Type)
                {
                    case PickupType.Health: return Amount + " Health";
                    case PickupType.Armor: return Amount + " Armor";
                    case PickupType.AmmoShells: return Amount + " Shells";
                    case PickupType.AmmoBullets: return Amount + " Bullets";
                    case PickupType.AmmoRockets: return Amount + " Rockets";
                    case PickupType.AmmoCells: return Amount + " Cells";
                    case PickupType.Weapon: return WeaponController.GetDef(Weapon).Name;
                    default: return Type.ToString();
                }
            }
        }

        MeshRenderer _renderer;
        Collider _collider;
        bool _cached;
        float _respawnTimer;
        bool _active = true;
        public bool IsAvailable => _active;

        void Awake() => EnsureCached();

        void EnsureCached()
        {
            if (_cached) return;
            _renderer = GetComponent<MeshRenderer>();
            _collider = GetComponent<Collider>();
            if (_collider != null) _collider.isTrigger = true;
            _cached = true;
        }

        void Update()
        {
            if (ArenaBootstrap.IsPaused) return;
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Per-frame update: visual spin while available, respawn countdown
        /// while consumed. Rejects a paused arena and any non-finite or
        /// negative delta (no state change) so a direct caller cannot corrupt
        /// the respawn timer; Update() additionally skips calling this at all
        /// while paused, but the guard lives here too since this is public.
        /// </summary>
        public void Tick(float deltaTime)
        {
            EnsureCached();
            if (ArenaBootstrap.IsPaused) return;
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f) return;

            transform.Rotate(Vector3.up, 60f * deltaTime);
            if (_active) return;
            _respawnTimer -= deltaTime;
            if (_respawnTimer <= 0f) SetActive(true);
        }

        void OnTriggerEnter(Collider other)
        {
            TryCollect(other.GetComponentInParent<Actor>());
        }

        /// <summary>
        /// Attempts to grant this pickup to <paramref name="actor"/>. Returns
        /// true and deactivates the pickup (starting its respawn timer) only
        /// when something was actually granted; otherwise returns false and
        /// leaves the pickup fully available and unchanged. Never throws on a
        /// null actor.
        /// </summary>
        public bool TryCollect(Actor actor)
        {
            EnsureCached();
            if (!_active || ArenaBootstrap.IsPaused) return false;
            if (actor == null || actor.IsDead) return false;
            if (Amount <= 0 && Type != PickupType.Weapon) return false;

            switch (Type)
            {
                case PickupType.Health:
                    if (actor.Health >= Actor.MaxHealth) return false;
                    actor.AddHealth(Amount);
                    break;
                case PickupType.Armor:
                    if (actor.Armor >= Actor.MaxArmor) return false;
                    actor.AddArmor(Amount);
                    break;
                case PickupType.AmmoShells:
                    if (!TryGrantAmmo(actor, AmmoType.Shells)) return false;
                    break;
                case PickupType.AmmoBullets:
                    if (!TryGrantAmmo(actor, AmmoType.Bullets)) return false;
                    break;
                case PickupType.AmmoRockets:
                    if (!TryGrantAmmo(actor, AmmoType.Rockets)) return false;
                    break;
                case PickupType.AmmoCells:
                    if (!TryGrantAmmo(actor, AmmoType.Cells)) return false;
                    break;
                case PickupType.Weapon:
                {
                    var wc = actor.GetComponent<WeaponController>();
                    if (wc == null || !wc.GiveWeapon(Weapon)) return false;
                    break;
                }
                default:
                    return false;
            }

            SetActive(false);
            _respawnTimer = RespawnTime;
            AnyCollected?.Invoke(this, actor);
            return true;
        }

        /// <summary>
        /// Grants ammo only if there is a receiver and it is not already full;
        /// returns false (no state change on either side) otherwise so the
        /// caller does not consume the pickup for nothing.
        /// </summary>
        bool TryGrantAmmo(Actor actor, AmmoType type)
        {
            var wc = actor.GetComponent<WeaponController>();
            if (wc == null) return false;
            return wc.AddAmmo(type, Amount);
        }

        void SetActive(bool active)
        {
            _active = active;
            if (_renderer != null) _renderer.enabled = active;
            if (_collider != null) _collider.enabled = active;
        }

        /// Used by ArenaBootstrap.Restart() to reset all pickups immediately.
        public void ForceActivate()
        {
            EnsureCached();
            SetActive(true);
        }
    }
}
