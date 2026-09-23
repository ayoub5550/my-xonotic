using System;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Core damageable entity shared by the player and bots: health, armor, frags,
    /// death and respawn plumbing. Deliberately simple for a development slice
    /// (no ragdoll, no full item inventory).
    /// </summary>
    public sealed class Actor : MonoBehaviour
    {
        public const int MaxHealth = 200;
        public const int MaxArmor = 200;
        public const int StartHealth = 100;
        public const int StartArmor = 0;
        public const float ArmorAbsorbRatio = 0.6f;

        public string DisplayName = "Actor";
        public float RespawnDelay = 2.5f;

        /// Team in TDM/CTF; None in Deathmatch. Same-team damage is ignored.
        public Team Team = Team.None;

        /// Powerups (Xonotic item_strength / item_invincible): seconds remaining.
        public const float PowerupDuration = 30f;
        public const float StrengthDamageFactor = 3f;
        public const float ShieldDamageDivisor = 3f;
        public float StrengthRemaining { get; private set; }
        public float ShieldRemaining { get; private set; }
        public bool HasStrength => StrengthRemaining > 0f;
        public bool HasShield => ShieldRemaining > 0f;
        /// Fired when a powerup starts: (actor, isStrength).
        public static event Action<Actor, bool> PowerupStarted;
        /// Fired when a powerup expires: (actor, isStrength).
        public static event Action<Actor, bool> PowerupEnded;

        /// Assigned by ArenaBootstrap so this actor can find a spawn point on respawn.
        public ArenaBootstrap Arena;

        public int Health { get; private set; } = StartHealth;
        public int Armor { get; private set; } = StartArmor;
        public int Frags { get; private set; }
        public int Deaths { get; private set; }
        /// dev.17: deaths with no enemy killer (void, self-splash) — what Test Lab reports as bot_suicides.
        public int Suicides { get; private set; }
        /// dev.18: why the killer-less deaths happened (Test Lab dev.17: bot_suicides=9 without a cause).
        public int SuicidesVoid { get; private set; }
        public int SuicidesHurt { get; private set; }
        public int SuicidesSelf { get; private set; }
        public int SuicidesOther { get; private set; }
        public const string CauseVoid = "void", CauseHurt = "hurt";
        public bool IsDead { get; private set; }

        /// victim, killer (killer may be null for environmental/self death).
        public event Action<Actor, Actor> Died;
        public event Action<Actor> Respawned;

        /// Global damage feed: (victim, attacker, rawDamage). Used by the HUD for hit markers/flash.
        public static event Action<Actor, Actor, int> AnyDamage;

        float _respawnTimer;

        public void ResetForSpawn()
        {
            Health = StartHealth;
            Armor = StartArmor;
            IsDead = false;
            _sinceDamage = 1000f;
            _sinceSpawn = 0f;
            _healthAcc = _armorAcc = 0f;
            var controller = GetComponent<CharacterController>();
            if (controller != null) controller.enabled = true;
            var weapons = GetComponent<WeaponController>();
            if (weapons != null) weapons.ResetLoadout();
            StrengthRemaining = 0f;
            ShieldRemaining = 0f;
            foreach (var renderer in GetComponentsInChildren<Renderer>()) renderer.enabled = true;
            var animator = GetComponentInChildren<CharacterAnimator>();
            if (animator != null) animator.OnRespawn();
        }

        /// True when <paramref name="other"/> may be damaged by this actor (not self, not a teammate).
        public bool IsEnemyOf(Actor other)
        {
            if (other == null || other == this) return false;
            return Team == Team.None || other.Team == Team.None || other.Team != Team;
        }

        public void GiveStrength(float seconds = PowerupDuration)
        {
            bool started = StrengthRemaining <= 0f;
            StrengthRemaining = Mathf.Max(StrengthRemaining, seconds);
            if (started) PowerupStarted?.Invoke(this, true);
        }

        public void GiveShield(float seconds = PowerupDuration)
        {
            bool started = ShieldRemaining <= 0f;
            ShieldRemaining = Mathf.Max(ShieldRemaining, seconds);
            if (started) PowerupStarted?.Invoke(this, false);
        }

        // ---- dev.14: Xonotic health/armor regeneration and rot (balance-xonotic.cfg) ----
        public const float HealthRegen = 0.08f;        // g_balance_health_regen
        public const float HealthRegenLinear = 0.5f;   // g_balance_health_regenlinear
        public const float HealthRot = 0.02f;          // g_balance_health_rot
        public const float HealthRotLinear = 1f;       // g_balance_health_rotlinear
        public const float ArmorRot = 0.02f;           // g_balance_armor_rot
        public const float ArmorRotLinear = 1f;        // g_balance_armor_rotlinear
        public const int RegenStable = 100;            // g_balance_health_regenstable / rotstable
        public const float PauseRegen = 5f;            // g_balance_pause_health_regen (after damage)
        public const float PauseRot = 1f;              // g_balance_pause_health_rot (after damage)
        public const float PauseRotSpawn = 5f;         // g_balance_pause_health_rot_spawn
        float _sinceDamage = 1000f;
        float _sinceSpawn;
        float _healthAcc;
        float _armorAcc;

        /// Xonotic CalcRotRegen: health drifts towards 100 (slow regen below, rot
        /// above) and armor above 100 rots; regen pauses 5 s after taking damage.
        /// Public and frame-free so the Editor tests can drive it.
        public void TickRegen(float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt) || IsDead) return;
            _sinceDamage += dt;
            _sinceSpawn += dt;
            if (Health < RegenStable && _sinceDamage >= PauseRegen)
                _healthAcc += (HealthRegen * (RegenStable - Health) + HealthRegenLinear) * dt;
            else if (Health > RegenStable && _sinceDamage >= PauseRot && _sinceSpawn >= PauseRotSpawn)
                _healthAcc -= (HealthRot * (Health - RegenStable) + HealthRotLinear) * dt;
            else _healthAcc = 0f;
            if (_healthAcc >= 1f) { int n = (int)_healthAcc; _healthAcc -= n; Health = Mathf.Min(RegenStable, Health + n); }
            else if (_healthAcc <= -1f) { int n = (int)-_healthAcc; _healthAcc += n; Health = Mathf.Max(RegenStable, Health - n); }

            if (Armor > RegenStable && _sinceDamage >= PauseRot && _sinceSpawn >= PauseRotSpawn)
                _armorAcc -= (ArmorRot * (Armor - RegenStable) + ArmorRotLinear) * dt;
            else _armorAcc = 0f;
            if (_armorAcc <= -1f) { int n = (int)-_armorAcc; _armorAcc += n; Armor = Mathf.Max(RegenStable, Armor - n); }
        }

        /// Powerup countdown as a plain method (testable without frames).
        public void TickPowerups(float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            if (StrengthRemaining > 0f)
            {
                StrengthRemaining -= dt;
                if (StrengthRemaining <= 0f) { StrengthRemaining = 0f; PowerupEnded?.Invoke(this, true); }
            }
            if (ShieldRemaining > 0f)
            {
                ShieldRemaining -= dt;
                if (ShieldRemaining <= 0f) { ShieldRemaining = 0f; PowerupEnded?.Invoke(this, false); }
            }
        }

        public void AddHealth(int amount) => Health = Mathf.Clamp(Health + amount, 0, MaxHealth);

        public void AddArmor(int amount) => Armor = Mathf.Clamp(Armor + amount, 0, MaxArmor);

        /// Applies damage after armor absorption (ArenaMath.ApplyArmor is the pure,
        /// testable part) and routes knockback into whichever mover component exists.
        public void TakeDamage(int rawDamage, Vector3 knockback, Actor instigator, string cause = null)
        {
            if (IsDead || ArenaBootstrap.IsPaused || rawDamage <= 0) return;
            // Team modes: no friendly fire (self damage still applies).
            if (instigator != null && instigator != this && !instigator.IsEnemyOf(this)) return;
            if (instigator != null && instigator.HasStrength) rawDamage = Mathf.RoundToInt(rawDamage * StrengthDamageFactor);
            if (HasShield) rawDamage = Mathf.Max(1, Mathf.CeilToInt(rawDamage / ShieldDamageDivisor));

            int armor = Armor;
            int toHealth = ArenaMath.ApplyArmor(rawDamage, ref armor, ArmorAbsorbRatio);
            Armor = armor;
            Health -= toHealth;
            _sinceDamage = 0f;
            AnyDamage?.Invoke(this, instigator, rawDamage);

            if (knockback.sqrMagnitude > 0f)
            {
                var player = GetComponent<Player>();
                if (player != null) player.ApplyExternalImpulse(knockback);
                var bot = GetComponent<Bot>();
                if (bot != null) bot.ApplyExternalImpulse(knockback);
            }

            if (Health <= 0)
            {
                Die(instigator, cause);
            }
        }

        void Die(Actor killer, string cause = null)
        {
            IsDead = true;
            Health = 0;
            Deaths++;
            if (killer != null && killer != this) killer.Frags++;
            else
            {
                Frags--; Suicides++;
                if (killer == this) SuicidesSelf++;
                else if (cause == CauseVoid) SuicidesVoid++;
                else if (cause == CauseHurt) SuicidesHurt++;
                else SuicidesOther++;
            }
            StrengthRemaining = 0f;
            ShieldRemaining = 0f;
            var animator = GetComponentInChildren<CharacterAnimator>();
            if (animator != null)
            {
                // Animated body: play the death animation and leave the corpse
                // visible until respawn; hide only the non-animated pieces.
                foreach (var renderer in GetComponentsInChildren<MeshRenderer>()) renderer.enabled = false;
                animator.OnDeath();
            }
            else
            {
                foreach (var renderer in GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            }
            var controller = GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            Died?.Invoke(this, killer);
            _respawnTimer = RespawnDelay;
        }

        void Update()
        {
            if (ArenaBootstrap.IsPaused) return;
            if (!IsDead) { TickPowerups(Time.deltaTime); TickRegen(Time.deltaTime); return; }
            _respawnTimer -= Time.deltaTime;
            if (_respawnTimer <= 0f) Respawn();
        }

        void Respawn()
        {
            ResetForSpawn();
            float yaw = transform.eulerAngles.y;
            if (Arena != null) yaw = Arena.PlaceAtSpawn(transform);

            var player = GetComponent<Player>();
            if (player != null) player.ResetForRespawn(yaw);

            Respawned?.Invoke(this);
        }

        /// Used by ArenaBootstrap.Restart(); forces an immediate respawn regardless of timer.
        public void ForceRespawn()
        {
            _respawnTimer = 0f;
            Respawn();
        }

        public void ResetScore()
        {
            Frags = 0;
            Deaths = 0;
        }
    }
}
