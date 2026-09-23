using System;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// The nine core Xonotic weapons, in the classic selection order
    /// (impulse 1..9). Index = slot number shown in the HUD weapon bar.
    /// </summary>
    public enum WeaponType
    {
        Blaster = 0,
        Shotgun = 1,
        MachineGun = 2,
        Mortar = 3,
        Electro = 4,
        Crylink = 5,
        Vortex = 6,
        Hagar = 7,
        Devastator = 8,
        // Extra Xonotic weapons (not part of the default 1-9 bar; shown when owned).
        Rifle = 9,
        Minelayer = 10,
        Arc = 11,
        Fireball = 12,
        Hook = 13
    }

    /// <summary>Shared ammo pools (several weapons draw from the same pool).</summary>
    public enum AmmoType { None = 0, Shells = 1, Bullets = 2, Rockets = 3, Cells = 4 }

    /// <summary>How a shot is delivered.</summary>
    public enum FireMode
    {
        /// Instant ray with optional spread and pellet count.
        Hitscan,
        /// Straight-flying kinematic projectile.
        Projectile,
        /// Projectile affected by gravity (arcs), explodes on impact.
        Ballistic,
        /// Projectile affected by gravity that bounces and explodes on a timer or actor touch.
        Bouncing,
        /// Short-range melee swing (Shotgun secondary).
        Melee,
        /// Remote-detonates this owner's live projectiles (Devastator secondary).
        Detonate,
        /// No shot: hold to zoom (Vortex secondary).
        Zoom,
        /// Continuous short-range beam: one damage tick per Refire while held (Arc primary).
        Beam,
        /// Gravity projectile that sticks to the world, arms, and explodes near an enemy (Minelayer primary).
        Mine,
        /// Grappling hook: pulls the owner towards the hit point while held (Hook primary).
        Hook
    }

    /// <summary>One fire mode of one weapon.</summary>
    [Serializable]
    public struct FireDef
    {
        public FireMode Mode;
        public int Damage;
        /// Splash damage at the edge of <see cref="SplashRadius"/> (Xonotic edgedamage); Damage at the centre.
        public int EdgeDamage;
        /// Seconds between shots.
        public float Refire;
        public int Shots;
        /// Cone half-angle in degrees applied per pellet/projectile.
        public float SpreadDegrees;
        public float Speed;
        public float SplashRadius;
        public float Knockback;
        /// Ammo taken per trigger pull (not per pellet).
        public int AmmoCost;
        /// Seconds a bouncing/ballistic projectile lives before it detonates.
        public float FuseSeconds;
        /// Gravity scale for Ballistic/Bouncing projectiles (1 = full).
        public float GravityScale;
        /// Damage multiplier applied to the instigator's own splash damage.
        public float SelfDamageFactor;
    }

    /// <summary>
    /// Per-weapon tuning taken from Xonotic 0.8.6 <c>bal-wep-xonotic.cfg</c>
    /// (dev.14: numbers synced 1:1 — damage, edge damage, refire, projectile
    /// speed, splash radius, force, ammo, spread). Units: 32 qu = 1 m, so
    /// speeds/radii are the cfg value × Q; <see cref="FireDef.Knockback"/> is the
    /// cfg <c>force</c> × Q and is added straight to the victim's velocity
    /// (m/s), negative values pull (Crylink). Spread is the cfg ratio converted to
    /// a cone half-angle. Self splash damage uses g_balance_selfdamagepercent 0.65.
    /// </summary>
    [Serializable]
    public struct WeaponDef
    {
        public WeaponType Type;
        public string Name;
        public string ShortName;
        public AmmoType Ammo;
        public FireDef Primary;
        public FireDef Secondary;
        /// Ammo handed out together with the weapon pickup.
        public int PickupAmmo;
        public Color Tint;

        const float Q = 1f / 32f; // quake units -> metres
        public const float SelfDamagePercent = 0.65f; // g_balance_selfdamagepercent

        /// Xonotic spread is a ratio applied to a random unit vector orthogonal to the aim direction.
        static float SpreadDeg(float ratio) => Mathf.Atan(ratio) * Mathf.Rad2Deg;

        static FireDef Hitscan(int dmg, float refire, int shots, float spreadRatio, float force, int cost) => new FireDef
        {
            Mode = FireMode.Hitscan, Damage = dmg, Refire = refire, Shots = shots, SpreadDegrees = SpreadDeg(spreadRatio),
            Knockback = force * Q, AmmoCost = cost, SelfDamageFactor = 0f
        };

        static FireDef Proj(FireMode mode, int dmg, int edge, float refire, int shots, float spreadRatio, float speedQu,
            float radiusQu, float forceQu, int cost, float fuse = 0f, float gravity = 0f, float selfDmg = SelfDamagePercent) => new FireDef
        {
            Mode = mode, Damage = dmg, EdgeDamage = edge, Refire = refire, Shots = shots, SpreadDegrees = SpreadDeg(spreadRatio),
            Speed = speedQu * Q, SplashRadius = radiusQu * Q, Knockback = forceQu * Q, AmmoCost = cost, FuseSeconds = fuse,
            GravityScale = gravity, SelfDamageFactor = selfDmg
        };

        public static WeaponDef Blaster() => new WeaponDef
        {
            Type = WeaponType.Blaster, Name = "Blaster", ShortName = "BLS", Ammo = AmmoType.None, PickupAmmo = 0,
            Tint = new Color(1f, 0.55f, 0.1f),
            Primary = Proj(FireMode.Projectile, 20, 10, 0.7f, 1, 0f, 6000f, 60f, 375f, 0, fuse: 5f),
            Secondary = Proj(FireMode.Projectile, 25, 12, 0.7f, 1, 0f, 6000f, 70f, 360f, 0, fuse: 5f)
        };

        public static WeaponDef Shotgun() => new WeaponDef
        {
            Type = WeaponType.Shotgun, Name = "Shotgun", ShortName = "SG", Ammo = AmmoType.Shells, PickupAmmo = 15,
            Tint = new Color(0.85f, 0.8f, 0.6f),
            Primary = Hitscan(4, 0.75f, 12, 0.12f, 15f, 1),
            // Melee: g_balance_shotgun_secondary_melee_range 120 qu.
            Secondary = new FireDef { Mode = FireMode.Melee, Damage = 70, Refire = 1.25f, Shots = 1, Speed = 120f * Q, Knockback = 200f * Q, AmmoCost = 0 }
        };

        public static WeaponDef MachineGun() => new WeaponDef
        {
            Type = WeaponType.MachineGun, Name = "Machine Gun", ShortName = "MG", Ammo = AmmoType.Bullets, PickupAmmo = 60,
            Tint = new Color(0.9f, 0.9f, 0.3f),
            Primary = Hitscan(10, 0.1f, 1, 0.03f, 3f, 1),
            // Burst mode: 3 × first_damage 14, burst_refire2 0.45.
            Secondary = Hitscan(14, 0.45f, 3, 0.02f, 3f, 3)
        };

        public static WeaponDef Mortar() => new WeaponDef
        {
            Type = WeaponType.Mortar, Name = "Mortar", ShortName = "MRT", Ammo = AmmoType.Rockets, PickupAmmo = 15,
            Tint = new Color(0.4f, 0.8f, 0.4f),
            Primary = Proj(FireMode.Ballistic, 55, 25, 0.8f, 1, 0f, 1900f, 120f, 250f, 2, fuse: 20f, gravity: 1f),
            Secondary = Proj(FireMode.Bouncing, 55, 30, 0.7f, 1, 0f, 1400f, 120f, 250f, 2, fuse: 2.5f, gravity: 1f)
        };

        public static WeaponDef Electro() => new WeaponDef
        {
            Type = WeaponType.Electro, Name = "Electro", ShortName = "ELC", Ammo = AmmoType.Cells, PickupAmmo = 25,
            Tint = new Color(0.3f, 0.6f, 1f),
            Primary = Proj(FireMode.Projectile, 40, 20, 0.6f, 1, 0f, 2500f, 100f, 200f, 4, fuse: 5f),
            Secondary = Proj(FireMode.Bouncing, 30, 15, 1.2f, 1, 0f, 1000f, 150f, 50f, 2, fuse: 4f, gravity: 0.6f)
        };

        public static WeaponDef Crylink() => new WeaponDef
        {
            Type = WeaponType.Crylink, Name = "Crylink", ShortName = "CRY", Ammo = AmmoType.Cells, PickupAmmo = 25,
            Tint = new Color(0.85f, 0.3f, 0.9f),
            // Negative force: Crylink pulls its victims towards the shooter.
            Primary = Proj(FireMode.Projectile, 10, 5, 0.7f, 6, 0.08f, 2000f, 80f, -50f, 3, fuse: 5f),
            Secondary = Proj(FireMode.Projectile, 8, 4, 0.7f, 5, 0.01f, 3000f, 100f, -200f, 3, fuse: 5f)
        };

        public static WeaponDef Vortex() => new WeaponDef
        {
            Type = WeaponType.Vortex, Name = "Vortex", ShortName = "VOR", Ammo = AmmoType.Cells, PickupAmmo = 25,
            Tint = new Color(0.4f, 0.9f, 1f),
            Primary = Hitscan(80, 1.5f, 1, 0f, 200f, 6),
            Secondary = new FireDef { Mode = FireMode.Zoom, Refire = 0f }
        };

        public static WeaponDef Hagar() => new WeaponDef
        {
            Type = WeaponType.Hagar, Name = "Hagar", ShortName = "HAG", Ammo = AmmoType.Rockets, PickupAmmo = 25,
            Tint = new Color(1f, 0.5f, 0.3f),
            Primary = Proj(FireMode.Projectile, 25, 12, 0.16667f, 1, 0f, 2200f, 65f, 100f, 1, fuse: 5f),
            // Secondary: four-rocket volley (Xonotic loads up to 4 and fires them together).
            Secondary = Proj(FireMode.Projectile, 35, 17, 0.75f, 4, 0.05f, 2000f, 80f, 75f, 4, fuse: 5f)
        };

        public static WeaponDef Devastator() => new WeaponDef
        {
            Type = WeaponType.Devastator, Name = "Devastator", ShortName = "DEV", Ammo = AmmoType.Rockets, PickupAmmo = 15,
            Tint = new Color(1f, 0.25f, 0.15f),
            Primary = Proj(FireMode.Ballistic, 80, 40, 1.1f, 1, 0f, 1300f, 110f, 400f, 4, fuse: 10f, gravity: 0f),
            Secondary = new FireDef { Mode = FireMode.Detonate, Refire = 0.3f }
        };

        public static WeaponDef Rifle() => new WeaponDef
        {
            Type = WeaponType.Rifle, Name = "Rifle", ShortName = "RIF", Ammo = AmmoType.Bullets, PickupAmmo = 40,
            Tint = new Color(0.75f, 0.65f, 0.45f),
            Primary = Hitscan(80, 1.2f, 1, 0f, 100f, 10),
            Secondary = Hitscan(20, 0.9f, 4, 0.04f, 50f, 10)
        };

        public static WeaponDef Minelayer() => new WeaponDef
        {
            Type = WeaponType.Minelayer, Name = "Mine Layer", ShortName = "MIN", Ammo = AmmoType.Rockets, PickupAmmo = 20,
            Tint = new Color(0.6f, 0.75f, 0.35f),
            Primary = Proj(FireMode.Mine, 40, 20, 1.5f, 1, 0f, 1000f, 175f, 250f, 4, fuse: 60f, gravity: 1f),
            Secondary = new FireDef { Mode = FireMode.Detonate, Refire = 0.3f }
        };

        public static WeaponDef Arc() => new WeaponDef
        {
            Type = WeaponType.Arc, Name = "Arc", ShortName = "ARC", Ammo = AmmoType.Cells, PickupAmmo = 25,
            Tint = new Color(0.55f, 0.85f, 1f),
            // Beam: 100 dps / 600 force per second / 6 cells per second, ticked every 0.2 s; range 1000 qu (Speed = range).
            Primary = new FireDef { Mode = FireMode.Beam, Damage = 20, Refire = 0.2f, Shots = 1, Speed = 1000f * Q, Knockback = 120f * Q, AmmoCost = 1 },
            Secondary = Proj(FireMode.Projectile, 25, 12, 0.16667f, 1, 0f, 2300f, 65f, 120f, 1, fuse: 5f)
        };

        public static WeaponDef Fireball() => new WeaponDef
        {
            Type = WeaponType.Fireball, Name = "Fireball", ShortName = "FRB", Ammo = AmmoType.None, PickupAmmo = 0,
            Tint = new Color(1f, 0.6f, 0.1f),
            Primary = Proj(FireMode.Ballistic, 200, 50, 2f, 1, 0f, 1200f, 200f, 600f, 0, fuse: 15f, gravity: 0f),
            // Secondary: three bouncing fire mines (firemine: 40 damage, 900 qu/s, 7 s).
            Secondary = Proj(FireMode.Bouncing, 40, 20, 1.5f, 3, 0.1f, 900f, 60f, 100f, 0, fuse: 7f, gravity: 1f)
        };

        public static WeaponDef Hook() => new WeaponDef
        {
            Type = WeaponType.Hook, Name = "Grappling Hook", ShortName = "HOK", Ammo = AmmoType.None, PickupAmmo = 0,
            Tint = new Color(0.7f, 0.7f, 0.75f),
            Primary = new FireDef { Mode = FireMode.Hook, Refire = 0.2f, Speed = 2000f * Q },
            // Secondary: gravity bomb (25 dmg, radius 500, force −2000 = pulls everything in), 3 s refire.
            Secondary = Proj(FireMode.Bouncing, 25, 5, 3f, 1, 0f, 1000f, 500f, -2000f, 0, fuse: 3f, gravity: 1f)
        };

        public static WeaponDef For(WeaponType type)
        {
            switch (type)
            {
                case WeaponType.Rifle: return Rifle();
                case WeaponType.Minelayer: return Minelayer();
                case WeaponType.Arc: return Arc();
                case WeaponType.Fireball: return Fireball();
                case WeaponType.Hook: return Hook();
                case WeaponType.Blaster: return Blaster();
                case WeaponType.Shotgun: return Shotgun();
                case WeaponType.MachineGun: return MachineGun();
                case WeaponType.Mortar: return Mortar();
                case WeaponType.Electro: return Electro();
                case WeaponType.Crylink: return Crylink();
                case WeaponType.Vortex: return Vortex();
                case WeaponType.Hagar: return Hagar();
                case WeaponType.Devastator: return Devastator();
                default: return Blaster();
            }
        }
    }

    /// <summary>
    /// Owns the arsenal state of one actor: which weapons it carries, the
    /// shared ammo pools, the current weapon, fire cooldown and shot dispatch
    /// (hitscan/projectile/melee/detonate). Works for the player and bots.
    /// </summary>
    public sealed class WeaponController : MonoBehaviour
    {
        /// All weapons including the five extras (Rifle..Hook).
        public const int WeaponCount = 14;
        /// The nine classic weapons that always occupy bar slots 1-9.
        public const int CoreWeaponCount = 9;
        public const int AmmoTypeCount = 5;
        /// Live mines one actor may have at once (Minelayer).
        public const int MaxLiveMines = 3;

        /// Ammo pool caps (Xonotic g_pickup_*_max defaults).
        public static readonly int[] MaxAmmo = { 0, 60, 320, 160, 180 };
        /// Ammo every actor spawns with (Xonotic g_start_ammo_* defaults; shells for the starting Shotgun).
        public static readonly int[] StartAmmo = { 0, 15, 0, 0, 0 };
        /// Weapons every actor spawns with (Xonotic default: Blaster + Shotgun).
        public static readonly WeaponType[] StartWeapons = { WeaponType.Blaster, WeaponType.Shotgun };

        public Actor Owner;
        public WeaponView View;

        public WeaponType Current = WeaponType.Blaster;

        /// Fired after a successful shot: (weapon, alt).
        public event Action<WeaponType, bool> Fired;
        /// Fired when Current changes: (previous, current).
        public event Action<WeaponType, WeaponType> WeaponChanged;
        /// Fired when a weapon is newly acquired.
        public event Action<WeaponType> WeaponAcquired;

        static readonly WeaponDef[] Defs = BuildDefs();
        readonly int[] _ammo = new int[AmmoTypeCount];
        int _ownedMask;
        float _cooldownTimer;
        bool _zooming;

        static WeaponDef[] BuildDefs()
        {
            var defs = new WeaponDef[WeaponCount];
            for (int i = 0; i < WeaponCount; i++) defs[i] = WeaponDef.For((WeaponType)i);
            return defs;
        }

        public bool IsReady => _cooldownTimer <= 0f;
        public float CooldownRemaining => Mathf.Max(0f, _cooldownTimer);
        /// True while the Vortex secondary (zoom) is held.
        public bool IsZooming => _zooming;

        public static WeaponDef GetDef(WeaponType t) => Defs[(int)t];
        public WeaponDef CurrentDef => Defs[(int)Current];

        public int GetAmmo(AmmoType a) => _ammo[(int)a];
        /// Ammo available to weapon <paramref name="t"/> (its pool), or -1 for weapons without ammo.
        public int GetAmmo(WeaponType t)
        {
            var a = Defs[(int)t].Ammo;
            return a == AmmoType.None ? -1 : _ammo[(int)a];
        }

        public bool Has(WeaponType t) => (_ownedMask & (1 << (int)t)) != 0;
        public int OwnedCount { get { int n = 0; for (int i = 0; i < WeaponCount; i++) if (Has((WeaponType)i)) n++; return n; } }

        /// True when the weapon is owned and has ammo for at least one primary shot.
        public bool CanFire(WeaponType t)
        {
            if (!Has(t)) return false;
            var def = Defs[(int)t];
            return def.Ammo == AmmoType.None || _ammo[(int)def.Ammo] >= Mathf.Max(1, def.Primary.AmmoCost);
        }

        /// Test/driver hook: clears the fire cooldown immediately instead of yielding frames.
        public void ResetCooldownForTest() => _cooldownTimer = 0f;

        void Awake() => ResetLoadout();

        public void ResetLoadout()
        {
            _ownedMask = 0;
            for (int i = 0; i < AmmoTypeCount; i++) _ammo[i] = StartAmmo[i];
            foreach (var w in StartWeapons) _ownedMask |= 1 << (int)w;
            Current = WeaponType.Shotgun;
            _cooldownTimer = 0f;
            _zooming = false;
            if (Hook != null) Hook.Release();
            if (MatchSettings.AllWeapons && !ArenaBootstrap.TestMode) GiveAll();
        }

        /// Gives every weapon and full ammo ("all weapons" mutator / debug).
        public void GiveAll()
        {
            for (int i = 0; i < WeaponCount; i++) _ownedMask |= 1 << i;
            for (int i = 1; i < AmmoTypeCount; i++) _ammo[i] = MaxAmmo[i];
        }

        /// Bar slot i (0-based) -> weapon: slots 0-8 are the core weapons, further
        /// slots are the OWNED extra weapons in enum order. Returns false when empty.
        public bool SlotToWeapon(int slot, out WeaponType weapon)
        {
            weapon = WeaponType.Blaster;
            if (slot < 0) return false;
            if (slot < CoreWeaponCount) { weapon = (WeaponType)slot; return true; }
            int n = CoreWeaponCount;
            for (int i = CoreWeaponCount; i < WeaponCount; i++)
            {
                if (!Has((WeaponType)i)) continue;
                if (n == slot) { weapon = (WeaponType)i; return true; }
                n++;
            }
            return false;
        }

        /// Number of bar slots to show: 9 core + owned extras.
        public int VisibleSlotCount
        {
            get { int n = CoreWeaponCount; for (int i = CoreWeaponCount; i < WeaponCount; i++) if (Has((WeaponType)i)) n++; return n; }
        }

        /// Owner's grappling hook (player only); null for bots.
        public GrapplingHook Hook;

        /// Called each frame by the input owner so hold-style weapons (Hook) know the trigger state.
        public void SetPrimaryHeld(bool held)
        {
            if (Hook != null) Hook.Held = held && CurrentDef.Primary.Mode == FireMode.Hook;
        }

        void Update()
        {
            if (!ArenaBootstrap.IsPaused && _cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;
        }

        /// Adds ammo to a pool; returns false if the pool was already full (nothing granted).
        public bool AddAmmo(AmmoType a, int amount)
        {
            if (a == AmmoType.None || amount <= 0) return false;
            int i = (int)a;
            if (_ammo[i] >= MaxAmmo[i]) return false;
            _ammo[i] = Mathf.Clamp(_ammo[i] + amount, 0, MaxAmmo[i]);
            return true;
        }

        /// Legacy convenience: add ammo to the pool weapon <paramref name="t"/> uses.
        public bool AddAmmo(WeaponType t, int amount) => AddAmmo(Defs[(int)t].Ammo, amount);

        /// <summary>
        /// Grants a weapon (plus its pickup ammo). Returns true when anything
        /// changed (new weapon or ammo added). Auto-switches to the new weapon
        /// when it ranks higher than the current one and we are not mid-fight
        /// (cooldown idle), mirroring cl_autoswitch.
        /// </summary>
        public bool GiveWeapon(WeaponType t, bool autoSwitch = true)
        {
            bool isNew = !Has(t);
            _ownedMask |= 1 << (int)t;
            bool ammoAdded = AddAmmo(Defs[(int)t].Ammo, Defs[(int)t].PickupAmmo);
            if (isNew)
            {
                WeaponAcquired?.Invoke(t);
                if (autoSwitch && (int)t > (int)Current && (int)t < CoreWeaponCount && IsReady) SwitchTo(t);
            }
            return isNew || ammoAdded;
        }

        public bool SwitchTo(WeaponType t)
        {
            if ((int)t < 0 || (int)t >= WeaponCount || !Has(t)) return false;
            if (t == Current) return true; // already holding it: a no-op success
            var previous = Current;
            Current = t;
            _zooming = false;
            WeaponChanged?.Invoke(previous, t);
            return true;
        }

        /// Cycles to the next/previous OWNED weapon (wrapping).
        public bool SwitchCycle(int direction)
        {
            if (OwnedCount <= 1) return false;
            int step = direction >= 0 ? 1 : -1;
            int idx = (int)Current;
            for (int n = 0; n < WeaponCount; n++)
            {
                idx = (idx + step + WeaponCount) % WeaponCount;
                if (Has((WeaponType)idx)) return SwitchTo((WeaponType)idx);
            }
            return false;
        }

        /// Highest-ranked owned CORE weapon that can currently fire (falls back to Blaster).
        public WeaponType BestUsable()
        {
            for (int i = CoreWeaponCount - 1; i >= 0; i--)
                if (CanFire((WeaponType)i)) return (WeaponType)i;
            return WeaponType.Blaster;
        }

        /// Called each frame by the input owner so the Vortex zoom follows the held state.
        public void SetSecondaryHeld(bool held)
        {
            _zooming = held && CurrentDef.Secondary.Mode == FireMode.Zoom;
        }

        /// <summary>
        /// Attempts to fire the current weapon. Returns false if on cooldown,
        /// out of ammo, the owner is dead/missing, or the mode fires nothing.
        /// </summary>
        public bool TryFire(Vector3 origin, Vector3 direction, bool alt)
        {
            if (!IsReady || ArenaBootstrap.IsPaused || Owner == null || Owner.IsDead ||
                direction.sqrMagnitude < 0.001f) return false;
            direction.Normalize();
            WeaponDef def = Defs[(int)Current];
            FireDef fire = alt ? def.Secondary : def.Primary;
            if (fire.Mode == FireMode.Zoom) return false; // handled by SetSecondaryHeld

            if (fire.Mode == FireMode.Detonate)
            {
                int detonated = Projectile.DetonateOwnedBy(Owner);
                if (detonated == 0) return false;
                _cooldownTimer = fire.Refire;
                Fired?.Invoke(Current, alt);
                return true;
            }

            if (fire.Mode == FireMode.Hook)
            {
                if (Hook == null || Hook.IsActive) return false;
                if (!Hook.Fire(origin, direction, fire.Speed)) return false;
                _cooldownTimer = fire.Refire;
                if (View != null) { View.PlayFireAnimation(alt); View.PlayFireSound(alt); }
                Fired?.Invoke(Current, alt);
                return true;
            }

            if (fire.Mode == FireMode.Mine && Projectile.CountMinesOwnedBy(Owner) >= MaxLiveMines) return false;

            if (def.Ammo != AmmoType.None && fire.AmmoCost > 0 && _ammo[(int)def.Ammo] < fire.AmmoCost)
            {
                // Out of ammo: drop to the best usable weapon so the player is never stuck.
                var fallback = BestUsable();
                if (fallback != Current) SwitchTo(fallback);
                return false;
            }

            _cooldownTimer = fire.Refire;
            if (def.Ammo != AmmoType.None && fire.AmmoCost > 0) _ammo[(int)def.Ammo] -= fire.AmmoCost;

            int shots = Mathf.Max(1, fire.Shots);
            for (int s = 0; s < shots; s++)
            {
                Vector3 dir = Spread(direction, fire.SpreadDegrees, s, shots);
                switch (fire.Mode)
                {
                    case FireMode.Hitscan:
                        FireHitscan(origin, dir, fire);
                        break;
                    case FireMode.Melee:
                        FireMelee(origin, dir, fire);
                        break;
                    case FireMode.Beam:
                        FireBeam(origin, dir, fire);
                        break;
                    default:
                        Projectile.Spawn(origin, dir, fire, Owner, Current);
                        break;
                }
            }
            if (View != null) { View.Kick(alt); View.PlayFireSound(alt); }
            else WeaponAudio.PlayAt(WeaponAudio.Fire(Current, alt), origin, 0.8f); // bots: positional
            Fired?.Invoke(Current, alt);
            return true;
        }

        /// Deterministic-ish cone spread: pellets are spaced around the cone so a
        /// shotgun blast always covers the whole cone instead of clumping.
        static Vector3 Spread(Vector3 dir, float degrees, int index, int count)
        {
            if (degrees <= 0f) return dir;
            float angle = (index + 0.5f) / count * 360f + UnityEngine.Random.Range(-40f, 40f);
            float radius = degrees * Mathf.Sqrt(UnityEngine.Random.Range(0.15f, 1f));
            Quaternion cone = Quaternion.AngleAxis(angle, dir) * Quaternion.AngleAxis(radius, Vector3.Cross(dir, Vector3.up).sqrMagnitude > 0.001f ? Vector3.Cross(dir, Vector3.up) : Vector3.right);
            return (cone * dir).normalized;
        }

        void FireHitscan(Vector3 origin, Vector3 direction, FireDef fire)
        {
            // Ignore trigger volumes (pickups etc.) so the hitscan only stops on solid
            // geometry or actor bodies, never on an invisible pickup trigger.
            var hits = Physics.RaycastAll(origin, direction, 400f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var actor = hit.collider.GetComponentInParent<Actor>();
                if (actor == Owner) continue;
                if (actor != null)
                {
                    Vector3 kb = ArenaMath.KnockbackImpulse(direction, fire.Knockback);
                    actor.TakeDamage(fire.Damage, kb, Owner);
                }
                ImpactEffects.Spawn(hit.point, hit.normal, Current, actor != null);
                break;
            }
        }

        /// Arc beam: a range-limited hitscan tick with a visible beam. Splash-free.
        void FireBeam(Vector3 origin, Vector3 direction, FireDef fire)
        {
            float range = fire.Speed > 0f ? fire.Speed : 20f;
            Vector3 end = origin + direction * range;
            var hits = Physics.RaycastAll(origin, direction, range, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            bool hitActor = false;
            foreach (var hit in hits)
            {
                var actor = hit.collider.GetComponentInParent<Actor>();
                if (actor == Owner) continue;
                end = hit.point;
                if (actor != null)
                {
                    actor.TakeDamage(fire.Damage, ArenaMath.KnockbackImpulse(direction, fire.Knockback), Owner);
                    hitActor = true;
                }
                ImpactEffects.Spawn(hit.point, hit.normal, Current, actor != null);
                break;
            }
            ImpactEffects.Beam(origin, end, CurrentDef.Tint, fire.Refire, hitActor);
        }

        void FireMelee(Vector3 origin, Vector3 direction, FireDef fire)
        {
            float range = fire.Speed > 0f ? fire.Speed : 2f;
            var hits = Physics.SphereCastAll(origin, 0.5f, direction, range, ~0, QueryTriggerInteraction.Ignore);
            Actor best = null;
            float bestDist = float.MaxValue;
            foreach (var hit in hits)
            {
                var actor = hit.collider.GetComponentInParent<Actor>();
                if (actor == null || actor == Owner || actor.IsDead) continue;
                if (hit.distance < bestDist) { bestDist = hit.distance; best = actor; }
            }
            if (best != null)
                best.TakeDamage(fire.Damage, ArenaMath.KnockbackImpulse(direction, fire.Knockback), Owner);
        }
    }
}
