using System.Collections.Generic;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Kinematic projectile shared by every projectile weapon. Moves by raycast
    /// each frame (no rigidbody), supports gravity (Mortar/Electro ball),
    /// bouncing with a fuse, splash damage with occlusion, reduced self damage,
    /// and remote detonation (Devastator secondary).
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        public Vector3 Velocity;
        public int Damage;
        public float Knockback;
        public float SplashRadius;
        public int EdgeDamage;
        public float SelfDamageFactor = 0.5f;
        public Actor Instigator;
        public WeaponType Weapon;
        public float LifeTime = 8f;
        public float GravityScale;
        public bool Bounces;
        public float BounceDamping = 0.55f;
        /// Minelayer mine: sticks to the world on impact, arms after <see cref="MineArmDelay"/>,
        /// explodes when an enemy comes within <see cref="MineTriggerRadius"/> (or on remote detonation).
        public bool IsMine;
        public const float MineArmDelay = 1f;
        public const float MineTriggerRadius = 60f / 32f;
        public bool IsStuck { get; private set; }
        float _stuckTime;

        /// Grace period right after spawning where a hit on the instigator's own
        /// body is ignored, so firing from the muzzle/camera position (which can sit
        /// inside the owner's own collider) does not self-detonate immediately.
        const float SelfIgnoreWindow = 0.08f;
        const float Gravity = 800f / 32f;

        static readonly List<Projectile> Live = new List<Projectile>();
        public static int LiveCount => Live.Count;

        float _age;
        bool _exploded;
        TrailRenderer _trail;

        public static Projectile Spawn(Vector3 origin, Vector3 dir, FireDef fire, Actor instigator, WeaponType weapon)
        {
            var def = WeaponController.GetDef(weapon);
            bool heavy = fire.SplashRadius > 2f;
            var go = new GameObject("Projectile_" + weapon);
            go.transform.position = origin;
            go.transform.localScale = Vector3.one * (heavy ? 0.3f : 0.16f);

            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = ArenaPrimitives.SphereMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ArenaMaterials.Get(def.Tint);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = def.Tint;
            light.range = heavy ? 4f : 2.5f;
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = heavy ? 0.35f : 0.12f;
            trail.startWidth = heavy ? 0.18f : 0.08f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.05f;
            trail.sharedMaterial = ArenaMaterials.Get(def.Tint);
            trail.startColor = def.Tint;
            trail.endColor = new Color(def.Tint.r, def.Tint.g, def.Tint.b, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var p = go.AddComponent<Projectile>();
            p.Velocity = dir.normalized * fire.Speed;
            p.Damage = fire.Damage;
            p.Knockback = fire.Knockback;
            p.EdgeDamage = fire.EdgeDamage;
            p.SplashRadius = fire.SplashRadius;
            p.SelfDamageFactor = fire.SelfDamageFactor;
            p.Instigator = instigator;
            p.Weapon = weapon;
            p.GravityScale = fire.GravityScale;
            p.Bounces = fire.Mode == FireMode.Bouncing;
            p.IsMine = fire.Mode == FireMode.Mine;
            p.LifeTime = fire.FuseSeconds > 0f ? fire.FuseSeconds : 8f;
            p._trail = trail;
            return p;
        }

        /// Detonates every live projectile fired by <paramref name="owner"/>; returns how many.
        public static int DetonateOwnedBy(Actor owner)
        {
            int n = 0;
            // Iterate over a copy: Explode() removes from Live.
            var snapshot = Live.ToArray();
            foreach (var p in snapshot)
            {
                if (p == null || p.Instigator != owner || p._exploded) continue;
                p.Explode(p.transform.position, null);
                n++;
            }
            return n;
        }

        /// Live (stuck or flying) mines owned by <paramref name="owner"/>.
        public static int CountMinesOwnedBy(Actor owner)
        {
            int n = 0;
            foreach (var p in Live) if (p != null && p.IsMine && p.Instigator == owner && !p._exploded) n++;
            return n;
        }

        void OnEnable() => Live.Add(this);
        void OnDisable() => Live.Remove(this);

        void Update()
        {
            if (ArenaBootstrap.IsPaused) return;
            float dt = Time.deltaTime;
            _age += dt;
            if (_age > LifeTime)
            {
                // Fused projectiles (grenades, electro balls, mines) explode on timeout; others fizzle.
                if (SplashRadius > 0f && (Bounces || GravityScale > 0f || IsMine)) Explode(transform.position, null);
                else Destroy(gameObject);
                return;
            }

            if (IsStuck)
            {
                // Armed mine: proximity trigger on enemies only.
                if (_age - _stuckTime < MineArmDelay) return;
                foreach (var c in Physics.OverlapSphere(transform.position, MineTriggerRadius, ~0, QueryTriggerInteraction.Ignore))
                {
                    var a = c.GetComponentInParent<Actor>();
                    if (a == null || a.IsDead || a == Instigator) continue;
                    if (Instigator != null && !Instigator.IsEnemyOf(a)) continue;
                    Explode(transform.position, null);
                    return;
                }
                return;
            }

            if (GravityScale > 0f) Velocity += Vector3.down * (Gravity * GravityScale * dt);

            float step = Velocity.magnitude * dt;
            if (step <= 0f) return;
            Vector3 dir = Velocity / (step / dt);

            // Ignore trigger volumes (pickups) along the flight path; only solid
            // geometry and actor bodies should stop a projectile.
            var candidates = Physics.RaycastAll(transform.position, dir, step + 0.02f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(candidates, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in candidates)
            {
                var hitActor = hit.collider.GetComponentInParent<Actor>();
                if (hitActor == Instigator && _age < SelfIgnoreWindow) continue;

                if (IsMine && hitActor == null)
                {
                    // Stick to the surface and arm.
                    transform.position = hit.point + hit.normal * 0.05f;
                    Velocity = Vector3.zero;
                    GravityScale = 0f;
                    IsStuck = true;
                    _stuckTime = _age;
                    if (_trail != null) _trail.emitting = false;
                    WeaponAudio.PlayAt(WeaponAudio.Load(WeaponAudio.ResourceName(WeaponType.Minelayer, "mine_stick")), transform.position, 0.7f);
                    return;
                }
                if (Bounces && hitActor == null)
                {
                    // Reflect off world geometry, lose energy; explode once nearly at rest.
                    transform.position = hit.point + hit.normal * 0.03f;
                    Velocity = Vector3.Reflect(Velocity, hit.normal) * BounceDamping;
                    if (Velocity.magnitude < 1.5f) Velocity = Vector3.zero;
                    return;
                }
                Explode(hit.point + hit.normal * 0.02f, hit.collider);
                return;
            }
            transform.position += Velocity * dt;
        }

        void Explode(Vector3 point, Collider hitCollider)
        {
            if (_exploded) return;
            _exploded = true;
            bool hitActorDirect = false;

            if (SplashRadius > 0f)
            {
                Collider[] hits = Physics.OverlapSphere(point, SplashRadius, ~0, QueryTriggerInteraction.Ignore);
                var applied = new HashSet<Actor>();
                foreach (Collider h in hits)
                {
                    var actor = h.GetComponentInParent<Actor>();
                    if (actor == null || !applied.Add(actor)) continue; // dedupe multi-collider actors

                    Vector3 targetPoint = actor.transform.position + Vector3.up * 0.8f;
                    float dist = Vector3.Distance(point, targetPoint);

                    // Occlusion check: a wall between the blast and the target blocks
                    // splash damage/knockback, same as line-of-sight for the direct hit.
                    if (dist > 0.05f)
                    {
                        Vector3 toTargetDir = (targetPoint - point).normalized;
                        if (Physics.Raycast(point, toTargetDir, out RaycastHit occlusionHit, dist - 0.05f, ~0, QueryTriggerInteraction.Ignore))
                        {
                            var occluderActor = occlusionHit.collider.GetComponentInParent<Actor>();
                            if (occluderActor != actor) continue; // blocked by geometry (or another actor)
                        }
                    }

                    float dmg = ArenaMath.SplashDamage(dist, SplashRadius, Damage, EdgeDamage);
                    if (actor == Instigator) dmg *= SelfDamageFactor;
                    else hitActorDirect = true;
                    Vector3 toTarget = targetPoint - point;
                    Vector3 kb = ArenaMath.SplashKnockback(toTarget, dist, SplashRadius, Knockback);
                    // Rocket-jumping: the owner always receives the full push even when self damage is reduced.
                    if (dmg <= 0f && actor != Instigator) continue;
                    actor.TakeDamage(Mathf.Max(0, Mathf.RoundToInt(dmg)), kb, Instigator);
                }
            }
            else
            {
                var actor = hitCollider != null ? hitCollider.GetComponentInParent<Actor>() : null;
                if (actor != null && actor != Instigator)
                {
                    Vector3 kb = ArenaMath.KnockbackImpulse(Velocity.normalized, Knockback);
                    actor.TakeDamage(Damage, kb, Instigator);
                    hitActorDirect = true;
                }
            }
            ImpactEffects.Spawn(point, Vector3.up, Weapon, hitActorDirect, SplashRadius);
            Destroy(gameObject);
        }
    }
}
