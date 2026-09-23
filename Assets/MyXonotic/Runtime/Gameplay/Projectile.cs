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

        // dev.15 mechanics
        public FireDef Fire;
        public bool Guided;
        float _maxSpeed, _accel;
        bool _bounced;
        /// Mine countdown after proximity trigger (g_balance_minelayer_lifetime_countdown); -1 = not triggered.
        float _mineCountdown = -1f;
        /// Set by Explode when triggered remotely (uses Fire.Remote* stats) or by an electro combo.
        bool _remote, _combo;
        /// Electro combo chain: a ball that got combo-triggered explodes after this delay (Xonotic uses 0.1 s intervals).
        const float ComboDelay = 0.1f;
        float _comboTimer = -1f;
        public bool IsElectroBall => Weapon == WeaponType.Electro && Bounces;

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
        ParticleSystem _smoke;
        bool _orientToVelocity;

        public static Projectile Spawn(Vector3 origin, Vector3 dir, FireDef fire, Actor instigator, WeaponType weapon) =>
            Spawn(origin, dir, fire, instigator, weapon, false);

        public static Projectile Spawn(Vector3 origin, Vector3 dir, FireDef fire, Actor instigator, WeaponType weapon, bool secondary)
        {
            var def = WeaponController.GetDef(weapon);
            bool heavy = fire.SplashRadius > 2f;
            var go = new GameObject("Projectile_" + weapon);
            go.transform.position = origin;
            go.transform.localScale = Vector3.one * (heavy ? 0.3f : 0.16f);

            // dev.17: original Xonotic projectile model when imported, tinted sphere otherwise.
            bool hasModel = Application.isPlaying && ProjectileVisuals.TryAttachModel(go.transform, weapon, secondary, out _);
            if (hasModel)
            {
                go.transform.localScale = Vector3.one;
                go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
            else
            {
                var mf = go.AddComponent<MeshFilter>();
                mf.mesh = ArenaPrimitives.SphereMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ArenaMaterials.Get(def.Tint);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

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
            p.Fire = fire;
            p.Velocity = dir.normalized * (fire.SpeedStart > 0f ? fire.SpeedStart : fire.Speed) + Vector3.up * fire.SpeedUp;
            p._maxSpeed = fire.Speed;
            p._accel = fire.SpeedAccel;
            p.Guided = fire.Guided;
            if (fire.BounceFactor > 0f) p.BounceDamping = fire.BounceFactor;
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
            p._orientToVelocity = hasModel;
            // dev.18: LOW effects preset skips the smoke particles.
            if (Application.isPlaying && ProjectileVisuals.HasSmoke(weapon) && GameSettings.Effects != EffectsLevel.Low) p._smoke = ProjectileVisuals.AddSmokeTrail(go.transform, def.Tint);
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
                p._remote = p.Fire.RemoteRadius > 0f;
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
                else { ProjectileVisuals.ReleaseSmoke(_smoke); Destroy(gameObject); }
                return;
            }

            if (_comboTimer >= 0f)
            {
                _comboTimer -= dt;
                if (_comboTimer <= 0f) { Explode(transform.position, null); return; }
            }

            if (IsStuck)
            {
                if (_mineCountdown >= 0f)
                {
                    _mineCountdown -= dt;
                    if (_mineCountdown <= 0f) Explode(transform.position, null);
                    return;
                }
                // Armed mine: proximity trigger on enemies only, then a short countdown.
                if (_age - _stuckTime < MineArmDelay) return;
                float trigger = IsMine ? WeaponDef.MineProximityRadius : MineTriggerRadius;
                foreach (var c in Physics.OverlapSphere(transform.position, trigger, ~0, QueryTriggerInteraction.Ignore))
                {
                    var a = c.GetComponentInParent<Actor>();
                    if (a == null || a.IsDead || a == Instigator) continue;
                    if (Instigator != null && !Instigator.IsEnemyOf(a)) continue;
                    _mineCountdown = WeaponDef.MineCountdown;
                    return;
                }
                return;
            }

            if (GravityScale > 0f) Velocity += Vector3.down * (Gravity * GravityScale * dt);
            if (_orientToVelocity && Velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(Velocity.normalized, Vector3.up);

            // Devastator: speed ramp and steering towards the owner's aim while the trigger is held.
            if (_accel > 0f && Velocity.sqrMagnitude > 0.0001f)
            {
                float sp = Velocity.magnitude;
                if (sp < _maxSpeed) Velocity *= Mathf.Min(_maxSpeed, sp + _accel * dt) / sp;
            }
            if (Guided && _age >= WeaponDef.GuideDelay && Instigator != null)
            {
                var wc = Instigator.GetComponent<WeaponController>();
                if (wc != null && wc.GuideActive)
                {
                    Vector3 goal = wc.AimOrigin + wc.AimDirection * WeaponDef.GuideGoal;
                    if (Physics.Raycast(wc.AimOrigin, wc.AimDirection, out RaycastHit aimHit, WeaponDef.GuideGoal, ~0, QueryTriggerInteraction.Ignore)) goal = aimHit.point;
                    Velocity = SteerTowards(Velocity, goal - transform.position, WeaponDef.GuideRateDeg * dt);
                }
            }

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
                    // Reflect off world geometry, lose energy (bouncefactor); come to rest below bouncestop × gravity.
                    transform.position = hit.point + hit.normal * 0.03f;
                    Velocity = Vector3.Reflect(Velocity, hit.normal) * BounceDamping;
                    float stop = Fire.BounceStop > 0f ? Fire.BounceStop * Gravity : 1.5f;
                    if (Velocity.magnitude < stop) Velocity = Vector3.zero;
                    if (!_bounced)
                    {
                        _bounced = true;
                        if (Fire.LifetimeAfterBounce > 0f) LifeTime = Mathf.Min(LifeTime, _age + Fire.LifetimeAfterBounce);
                    }
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

            int damage = Damage, edge = EdgeDamage;
            float radius = SplashRadius, force = Knockback;
            if (_remote && Fire.RemoteRadius > 0f) { damage = Fire.RemoteDamage; edge = Fire.RemoteEdgeDamage; radius = Fire.RemoteRadius; force = Fire.RemoteKnockback; }
            if (_combo) { damage = WeaponDef.ElectroComboDamage; edge = WeaponDef.ElectroComboEdgeDamage; radius = WeaponDef.ElectroComboBlastRadius; force = WeaponDef.ElectroComboKnockback; }

            // Electro combo: a primary bolt exploding near live electro balls sets them off (chain through the balls themselves).
            if (Weapon == WeaponType.Electro && (!Bounces || _combo))
            {
                foreach (var other in Live)
                {
                    if (other == null || other == this || !other.IsElectroBall || other._exploded || other._comboTimer >= 0f) continue;
                    if ((other.transform.position - point).sqrMagnitude > WeaponDef.ElectroComboRadius * WeaponDef.ElectroComboRadius) continue;
                    other._combo = true;
                    other._comboTimer = ComboDelay;
                }
            }

            if (radius > 0f)
            {
                Collider[] hits = Physics.OverlapSphere(point, radius, ~0, QueryTriggerInteraction.Ignore);
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

                    float dmg = ArenaMath.SplashDamage(dist, radius, damage, edge);
                    if (actor == Instigator) dmg *= SelfDamageFactor;
                    else hitActorDirect = true;
                    Vector3 toTarget = targetPoint - point;
                    Vector3 kb = ArenaMath.SplashKnockback(toTarget, dist, radius, force);
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
            ImpactEffects.Spawn(point, Vector3.up, Weapon, hitActorDirect, radius);
            ProjectileVisuals.ReleaseSmoke(_smoke);
            Destroy(gameObject);
        }

        /// Rotates <paramref name="velocity"/> towards <paramref name="desired"/> by at most <paramref name="maxDegrees"/>, keeping its speed.
        public static Vector3 SteerTowards(Vector3 velocity, Vector3 desired, float maxDegrees)
        {
            float speed = velocity.magnitude;
            if (speed < 0.0001f || desired.sqrMagnitude < 0.0001f) return velocity;
            Vector3 dir = Vector3.RotateTowards(velocity / speed, desired.normalized, maxDegrees * Mathf.Deg2Rad, 0f);
            return dir * speed;
        }

        /// Test hook: marks this projectile as remotely detonated on its next Explode.
        public void MarkRemoteForTest() => _remote = Fire.RemoteRadius > 0f;
        /// Test hook: effective blast stats for the pending explosion (damage, edge, radius, force).
        public (int damage, int edge, float radius, float force) EffectiveBlast()
        {
            if (_combo) return (WeaponDef.ElectroComboDamage, WeaponDef.ElectroComboEdgeDamage, WeaponDef.ElectroComboBlastRadius, WeaponDef.ElectroComboKnockback);
            if (_remote && Fire.RemoteRadius > 0f) return (Fire.RemoteDamage, Fire.RemoteEdgeDamage, Fire.RemoteRadius, Fire.RemoteKnockback);
            return (Damage, EdgeDamage, SplashRadius, Knockback);
        }
        public bool ComboPending => _comboTimer >= 0f;
    }
}
