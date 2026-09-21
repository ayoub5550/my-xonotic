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

        /// Assigned by ArenaBootstrap so this actor can find a spawn point on respawn.
        public ArenaBootstrap Arena;

        public int Health { get; private set; } = StartHealth;
        public int Armor { get; private set; } = StartArmor;
        public int Frags { get; private set; }
        public int Deaths { get; private set; }
        public bool IsDead { get; private set; }

        /// victim, killer (killer may be null for environmental/self death).
        public event Action<Actor, Actor> Died;
        public event Action<Actor> Respawned;

        float _respawnTimer;

        public void ResetForSpawn()
        {
            Health = StartHealth;
            Armor = StartArmor;
            IsDead = false;
            var controller = GetComponent<CharacterController>();
            if (controller != null) controller.enabled = true;
            var weapons = GetComponent<WeaponController>();
            if (weapons != null) weapons.ResetLoadout();
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>()) renderer.enabled = true;
        }

        public void AddHealth(int amount) => Health = Mathf.Clamp(Health + amount, 0, MaxHealth);

        public void AddArmor(int amount) => Armor = Mathf.Clamp(Armor + amount, 0, MaxArmor);

        /// Applies damage after armor absorption (ArenaMath.ApplyArmor is the pure,
        /// testable part) and routes knockback into whichever mover component exists.
        public void TakeDamage(int rawDamage, Vector3 knockback, Actor instigator)
        {
            if (IsDead || ArenaBootstrap.IsPaused || rawDamage <= 0) return;

            int armor = Armor;
            int toHealth = ArenaMath.ApplyArmor(rawDamage, ref armor, ArmorAbsorbRatio);
            Armor = armor;
            Health -= toHealth;

            if (knockback.sqrMagnitude > 0f)
            {
                var player = GetComponent<Player>();
                if (player != null) player.ApplyExternalImpulse(knockback);
                var bot = GetComponent<Bot>();
                if (bot != null) bot.ApplyExternalImpulse(knockback);
            }

            if (Health <= 0)
            {
                Die(instigator);
            }
        }

        void Die(Actor killer)
        {
            IsDead = true;
            Health = 0;
            Deaths++;
            if (killer != null && killer != this) killer.Frags++;
            else Frags--;
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>()) renderer.enabled = false;
            var controller = GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            Died?.Invoke(this, killer);
            _respawnTimer = RespawnDelay;
        }

        void Update()
        {
            if (!IsDead || ArenaBootstrap.IsPaused) return;
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
