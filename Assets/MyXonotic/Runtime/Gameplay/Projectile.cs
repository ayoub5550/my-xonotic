using System.Collections.Generic;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Simple kinematic projectile used by the blaster (direct-hit, no splash) and the
    /// rocket (splash damage + self impulse). Moves by raycast each frame rather than
    /// relying on rigidbody physics, keeping the slice dependency-free.
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        public Vector3 Velocity;
        public int Damage;
        public float Knockback;
        public float SplashRadius;
        public Actor Instigator;
        public float LifeTime = 5f;

        /// Grace period right after spawning where a hit on the instigator's own
        /// body is ignored, so firing from the muzzle/camera position (which can sit
        /// inside the owner's own collider) does not self-detonate immediately.
        const float SelfIgnoreWindow = 0.06f;

        float _age;

        public static Projectile Spawn(Vector3 origin, Vector3 dir, float speed, int damage,
            float knockback, float splashRadius, Actor instigator, bool isRocket)
        {
            var go = new GameObject(isRocket ? "Projectile_Rocket" : "Projectile_Blaster");
            // Spawn exactly at the real muzzle/camera origin (no artificial forward
            // offset) so thin walls right in front of the muzzle cannot be tunnelled.
            go.transform.position = origin;
            go.transform.localScale = Vector3.one * (isRocket ? 0.35f : 0.18f);

            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = ArenaPrimitives.SphereMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ArenaMaterials.Get(isRocket ? new Color(0.9f, 0.15f, 0.1f) : new Color(1f, 0.65f, 0.1f));

            var p = go.AddComponent<Projectile>();
            p.Velocity = dir.normalized * speed;
            p.Damage = damage;
            p.Knockback = knockback;
            p.SplashRadius = splashRadius;
            p.Instigator = instigator;
            return p;
        }

        void Update()
        {
            if (ArenaBootstrap.IsPaused) return;
            _age += Time.deltaTime;
            if (_age > LifeTime) { Destroy(gameObject); return; }

            float step = Velocity.magnitude * Time.deltaTime;
            if (step <= 0f) return;
            Vector3 dir = Velocity.normalized;

            // Ignore trigger volumes (pickups) along the flight path; only solid
            // geometry and actor bodies should stop a projectile.
            var candidates = Physics.RaycastAll(transform.position, dir, step, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(candidates, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in candidates)
            {
                var hitActor = hit.collider.GetComponentInParent<Actor>();
                bool selfHitTooEarly = hitActor == Instigator && _age < SelfIgnoreWindow;
                if (!selfHitTooEarly)
                {
                    Explode(hit.point + hit.normal * 0.02f, hit.collider);
                    return;
                }
            }
            transform.position += Velocity * Time.deltaTime;
        }

        void Explode(Vector3 point, Collider hitCollider)
        {
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

                    float dmg = ArenaMath.SplashDamage(dist, SplashRadius, Damage);
                    if (dmg <= 0f) continue;

                    Vector3 toTarget = targetPoint - point;
                    Vector3 kb = ArenaMath.SplashKnockback(toTarget, dist, SplashRadius, Knockback);
                    actor.TakeDamage(Mathf.RoundToInt(dmg), kb, Instigator);
                }
            }
            else
            {
                var actor = hitCollider != null ? hitCollider.GetComponentInParent<Actor>() : null;
                if (actor != null && actor != Instigator)
                {
                    Vector3 kb = ArenaMath.KnockbackImpulse(Velocity.normalized, Knockback);
                    actor.TakeDamage(Damage, kb, Instigator);
                }
            }
            Destroy(gameObject);
        }
    }
}
