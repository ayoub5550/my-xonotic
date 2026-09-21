using UnityEngine;

namespace MyXonotic
{
    public enum WeaponType { Blaster = 0, Rifle = 1, Rocket = 2 }

    /// <summary>
    /// Original, independent weapon tuning data. Not copied from any existing game.
    /// </summary>
    [System.Serializable]
    public struct WeaponDef
    {
        public WeaponType Type;
        public string Name;
        public int Damage;
        public float PrimaryCooldown;
        public float AltCooldown;
        public float ProjectileSpeed;
        public float SplashRadius;
        public float KnockbackForce;
        public bool Hitscan;
        public int AmmoPerShot;
        public int StartAmmo;
        public int MaxAmmo;

        public static WeaponDef Blaster() => new WeaponDef
        {
            Type = WeaponType.Blaster, Name = "Blaster", Damage = 18,
            PrimaryCooldown = 0.35f, AltCooldown = 0.6f, ProjectileSpeed = 24f,
            SplashRadius = 0f, KnockbackForce = 6f, Hitscan = false,
            AmmoPerShot = 0, StartAmmo = 999, MaxAmmo = 999
        };

        public static WeaponDef Rifle() => new WeaponDef
        {
            Type = WeaponType.Rifle, Name = "Practice Rifle", Damage = 34,
            PrimaryCooldown = 0.15f, AltCooldown = 0.9f, ProjectileSpeed = 0f,
            SplashRadius = 0f, KnockbackForce = 2f, Hitscan = true,
            AmmoPerShot = 1, StartAmmo = 40, MaxAmmo = 80
        };

        public static WeaponDef Rocket() => new WeaponDef
        {
            Type = WeaponType.Rocket, Name = "Rocket Launcher", Damage = 90,
            PrimaryCooldown = 0.9f, AltCooldown = 1.4f, ProjectileSpeed = 16f,
            SplashRadius = 5f, KnockbackForce = 14f, Hitscan = false,
            AmmoPerShot = 1, StartAmmo = 10, MaxAmmo = 20
        };
    }

    /// <summary>
    /// Fires weapons for either the player or a bot. Alt-fire is a transparent,
    /// simplified approximation of a secondary mode rather than a fully distinct
    /// mechanic (documented per weapon below) since this is a development slice:
    ///  - Blaster alt: same projectile, doubled knockback (charged-shot approximation).
    ///  - Rifle alt: identical hitscan shot with a longer cooldown; no zoom implemented.
    ///  - Rocket alt: larger splash radius with a longer cooldown; no proximity trigger.
    /// </summary>
    public sealed class WeaponController : MonoBehaviour
    {
        public Actor Owner;
        public WeaponView View;

        public WeaponType Current = WeaponType.Blaster;

        readonly WeaponDef[] _defs = { WeaponDef.Blaster(), WeaponDef.Rifle(), WeaponDef.Rocket() };
        readonly int[] _ammo = new int[3];
        float _cooldownTimer;

        public bool IsReady => _cooldownTimer <= 0f;
        public int GetAmmo(WeaponType t) => _ammo[(int)t];
        public WeaponDef GetDef(WeaponType t) => _defs[(int)t];

        /// Test/driver hook: clears the fire cooldown immediately instead of yielding frames.
        public void ResetCooldownForTest() => _cooldownTimer = 0f;

        void Awake() => ResetLoadout();

        public void ResetLoadout()
        {
            for (int i = 0; i < _defs.Length; i++) _ammo[i] = _defs[i].StartAmmo;
            Current = WeaponType.Blaster;
            _cooldownTimer = 0f;
        }

        void Update()
        {
            if (!ArenaBootstrap.IsPaused && _cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;
        }

        public void AddAmmo(WeaponType t, int amount) =>
            _ammo[(int)t] = Mathf.Clamp(_ammo[(int)t] + amount, 0, _defs[(int)t].MaxAmmo);

        public void SwitchTo(WeaponType t)
        {
            if ((int)t >= 0 && (int)t < _defs.Length) Current = t;
        }

        /// Attempts to fire the currently selected weapon. Returns false if on cooldown,
        /// out of ammo, or the owner is dead/missing.
        public bool TryFire(Vector3 origin, Vector3 direction, bool alt)
        {
            if (!IsReady || ArenaBootstrap.IsPaused || Owner == null || Owner.IsDead ||
                direction.sqrMagnitude < 0.001f) return false;
            direction.Normalize();
            WeaponDef def = _defs[(int)Current];
            if (def.AmmoPerShot > 0 && _ammo[(int)Current] < def.AmmoPerShot) return false;

            _cooldownTimer = alt ? def.AltCooldown : def.PrimaryCooldown;
            if (def.AmmoPerShot > 0) _ammo[(int)Current] -= def.AmmoPerShot;

            switch (def.Type)
            {
                case WeaponType.Blaster:
                    Projectile.Spawn(origin, direction, def.ProjectileSpeed, def.Damage,
                        alt ? def.KnockbackForce * 2f : def.KnockbackForce, 0f, Owner, false);
                    break;
                case WeaponType.Rifle:
                    FireHitscan(origin, direction, def);
                    break;
                case WeaponType.Rocket:
                    float radius = alt ? def.SplashRadius * 1.5f : def.SplashRadius;
                    Projectile.Spawn(origin, direction, def.ProjectileSpeed, def.Damage,
                        def.KnockbackForce, radius, Owner, true);
                    break;
            }
            if (View != null) { View.Kick(); View.PlayFireSound(); }
            return true;
        }

        void FireHitscan(Vector3 origin, Vector3 direction, WeaponDef def)
        {
            // Ignore trigger volumes (pickups etc.) so the hitscan only stops on solid
            // geometry or actor bodies, never on an invisible pickup trigger.
            var hits = Physics.RaycastAll(origin, direction, 200f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var actor = hit.collider.GetComponentInParent<Actor>();
                if (actor == Owner) continue;
                if (actor != null)
                {
                    Vector3 kb = ArenaMath.KnockbackImpulse(direction, def.KnockbackForce);
                    actor.TakeDamage(def.Damage, kb, Owner);
                }
                Debug.DrawLine(origin, hit.point, Color.yellow, 0.15f);
                break;
            }
        }
    }
}
