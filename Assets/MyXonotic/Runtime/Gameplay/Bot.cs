using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Grounded arena bot: wanders, hunts nearby pickups it needs, picks the
    /// best owned weapon for the current range, leads moving targets with
    /// projectile weapons, keeps a spread of inaccuracy, and avoids firing
    /// explosives point-blank. Disabled by ArenaBootstrap.TestMode so
    /// automated tests get deterministic, stationary bots.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Actor))]
    public sealed class Bot : MonoBehaviour
    {
        public const float MoveSpeed = 6.5f;
        public const float Acceleration = 10f;
        public const float Gravity = -800f / 32f;
        public const float JumpSpeed = 260f / 32f;
        public const float FireRange = 45f;
        public const float SightRange = 60f;

        /// Aim error in degrees (0 = perfect). Lower on higher skill.
        public float AimErrorDegrees = 3.5f;
        /// Chance per decision tick to strafe instead of closing in.
        public float StrafeBias = 0.6f;

        public Transform Target;
        public WeaponController Weapons;
        public Actor Actor { get; private set; }

        CharacterController _cc;
        Vector3 _velocity;
        Vector3 _externalImpulse;
        Vector3 _wanderTarget;
        float _repickTimer;
        float _weaponThinkTimer;
        float _strafeDir = 1f;
        float _jumpTimer;
        Pickup _wantedPickup;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            Actor = GetComponent<Actor>();
            _wanderTarget = transform.position;
        }

        void OnEnable() { if (Actor != null) Actor.Respawned += OnRespawned; }
        void OnDisable() { if (Actor != null) Actor.Respawned -= OnRespawned; }

        /// Bots respawn with one random extra weapon (the player relies on map pickups).
        void OnRespawned(Actor actor)
        {
            if (ArenaBootstrap.TestMode || Weapons == null) return;
            Weapons.GiveWeapon((WeaponType)Random.Range((int)WeaponType.MachineGun, WeaponController.WeaponCount));
        }

        public void ApplyExternalImpulse(Vector3 impulse) => _externalImpulse += impulse;
        public void Launch(Vector3 velocity)
        {
            _velocity = velocity;
            _externalImpulse = Vector3.zero;
        }

        public void ResetMotion()
        {
            _velocity = Vector3.zero;
            _externalImpulse = Vector3.zero;
            _wanderTarget = transform.position;
            _repickTimer = 0f;
            _wantedPickup = null;
        }

        void Update()
        {
            if (ArenaBootstrap.IsPaused) return;
            if (Actor != null && Actor.IsDead) return;

            // In TestMode wander/attack decision-making is frozen for deterministic
            // tests, but gravity/grounding keeps running so bots stay on the floor
            // and still react to external impulses (splash knockback etc.).
            bool testMode = ArenaBootstrap.TestMode;
            Vector3 wishDir = Vector3.zero;
            bool wantJump = false;
            bool grounded = _cc.isGrounded;

            if (!testMode)
            {
                bool seesTarget = TargetVisible(out Vector3 toTarget, out float dist);
                _repickTimer -= Time.deltaTime;
                if (_repickTimer <= 0f)
                {
                    _repickTimer = Random.Range(1.2f, 3f);
                    _strafeDir = Random.value < 0.5f ? -1f : 1f;
                    _wantedPickup = FindWantedPickup();
                    if (_wantedPickup == null)
                    {
                        Vector2 rand = Random.insideUnitCircle * 12f;
                        _wanderTarget = transform.position + new Vector3(rand.x, 0f, rand.y);
                    }
                }

                if (_wantedPickup != null && _wantedPickup.IsAvailable)
                    _wanderTarget = _wantedPickup.transform.position;
                else if (seesTarget)
                {
                    // Keep a preferred distance for the weapon in hand and strafe around the target.
                    float preferred = PreferredRange();
                    Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z).normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, flat) * _strafeDir;
                    float closeFactor = Mathf.Clamp((dist - preferred) / preferred, -1f, 1f);
                    _wanderTarget = transform.position + (flat * closeFactor + side * StrafeBias) * 6f;
                }

                Vector3 toWander = _wanderTarget - transform.position;
                toWander.y = 0f;
                wishDir = toWander.sqrMagnitude > 0.5f ? toWander.normalized : Vector3.zero;

                // Occasional hop while fighting; also hop when stuck against geometry.
                _jumpTimer -= Time.deltaTime;
                bool stuck = wishDir.sqrMagnitude > 0.01f && new Vector3(_velocity.x, 0f, _velocity.z).magnitude < 0.5f && grounded;
                if (grounded && (_jumpTimer <= 0f && seesTarget && Random.value < 0.15f || stuck))
                {
                    wantJump = true;
                    _jumpTimer = Random.Range(1.5f, 3.5f);
                }

                if (seesTarget) EngageTarget(toTarget, dist);
                else if (wishDir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(wishDir, Vector3.up);
            }

            var horizontal = new Vector3(_velocity.x, 0f, _velocity.z);
            if (grounded && wishDir.sqrMagnitude < 0.01f)
                horizontal = ArenaMath.ApplyGroundFriction(horizontal, Player.GroundFriction, Player.StopSpeed, Time.deltaTime);
            horizontal = ArenaMath.Accelerate(horizontal, wishDir, MoveSpeed, grounded ? Acceleration : Player.AirAcceleration, Time.deltaTime);
            _velocity.x = horizontal.x;
            _velocity.z = horizontal.z;

            if (grounded && _velocity.y < 0f) _velocity.y = -1f;
            _velocity.y += Gravity * Time.deltaTime;
            if (grounded && wantJump) _velocity.y = JumpSpeed;

            _cc.Move((_velocity + _externalImpulse) * Time.deltaTime);
            _externalImpulse = Vector3.Lerp(_externalImpulse, Vector3.zero, 6f * Time.deltaTime);
            if (transform.position.y < -200f) Actor.TakeDamage(10000, Vector3.zero, null);
        }

        bool TargetVisible(out Vector3 toTarget, out float dist)
        {
            toTarget = Vector3.zero;
            dist = 0f;
            if (Target == null) return false;
            var targetActor = Target.GetComponent<Actor>();
            if (targetActor != null && targetActor.IsDead) return false;
            Vector3 eye = transform.position + Vector3.up * 1.5f;
            Vector3 aim = Target.position + Vector3.up * 1f;
            toTarget = aim - eye;
            dist = toTarget.magnitude;
            if (dist > SightRange) return false;
            // Line of sight: solid geometry blocks; the target's own colliders do not.
            if (Physics.Raycast(eye, toTarget / dist, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                var hitActor = hit.collider.GetComponentInParent<Actor>();
                if (hitActor == null || hitActor.transform != Target) return false;
            }
            return true;
        }

        Pickup FindWantedPickup()
        {
            var arena = ArenaBootstrap.Instance;
            if (arena == null || Weapons == null || Actor == null) return null;
            Pickup best = null;
            float bestScore = 0f;
            Vector3 here = transform.position;
            foreach (var p in arena.Pickups)
            {
                if (p == null || !p.IsAvailable) continue;
                float d = Vector3.Distance(here, p.transform.position);
                if (d > 25f || Mathf.Abs(p.transform.position.y - here.y) > 3f) continue;
                float want = 0f;
                switch (p.Type)
                {
                    case PickupType.Weapon: want = Weapons.Has(p.Weapon) ? 0.2f : 3f; break;
                    case PickupType.Health: want = Actor.Health < 70 ? 2.5f : 0.3f; break;
                    case PickupType.Armor: want = Actor.Armor < 60 ? 1.5f : 0.2f; break;
                    default: want = Weapons.OwnedCount > 2 && Weapons.GetAmmo(Weapons.Current) < 10 ? 1.8f : 0.5f; break;
                }
                float score = want / (1f + d * 0.15f);
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return bestScore > 0.6f ? best : null;
        }

        float PreferredRange()
        {
            if (Weapons == null) return 12f;
            switch (Weapons.Current)
            {
                case WeaponType.Shotgun: return 5f;
                case WeaponType.MachineGun: return 12f;
                case WeaponType.Vortex: return 22f;
                case WeaponType.Mortar:
                case WeaponType.Devastator: return 12f;
                default: return 9f;
            }
        }

        void ChooseWeapon(float dist)
        {
            if (Weapons == null) return;
            _weaponThinkTimer -= Time.deltaTime;
            if (_weaponThinkTimer > 0f) return;
            _weaponThinkTimer = 0.8f;

            WeaponType[] order;
            if (dist < 7f)
                order = new[] { WeaponType.Shotgun, WeaponType.MachineGun, WeaponType.Crylink, WeaponType.Electro, WeaponType.Vortex, WeaponType.Blaster };
            else if (dist < 18f)
                order = new[] { WeaponType.Devastator, WeaponType.Hagar, WeaponType.Electro, WeaponType.Mortar, WeaponType.Crylink, WeaponType.MachineGun, WeaponType.Vortex, WeaponType.Shotgun, WeaponType.Blaster };
            else
                order = new[] { WeaponType.Vortex, WeaponType.MachineGun, WeaponType.Devastator, WeaponType.Electro, WeaponType.Hagar, WeaponType.Mortar, WeaponType.Blaster, WeaponType.Shotgun };

            foreach (var w in order)
            {
                if (!Weapons.CanFire(w)) continue;
                if (w != Weapons.Current) Weapons.SwitchTo(w);
                return;
            }
        }

        void EngageTarget(Vector3 toTarget, float dist)
        {
            if (Weapons == null) return;
            Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
            if (flat.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            if (dist > FireRange) return;

            ChooseWeapon(dist);
            var def = Weapons.CurrentDef;
            var fire = def.Primary;

            // Never fire splash weapons into our own face.
            if (fire.SplashRadius > 0f && dist < fire.SplashRadius + 1.5f) return;

            Vector3 origin = transform.position + Vector3.up * 1.5f;
            Vector3 aimPoint = Target.position + Vector3.up * 1f;

            // Lead moving targets with projectile weapons.
            if (fire.Mode != FireMode.Hitscan && fire.Speed > 0f)
            {
                var targetPlayer = Target.GetComponent<Player>();
                if (targetPlayer != null)
                {
                    float flight = dist / fire.Speed;
                    aimPoint += targetPlayer.Velocity * flight;
                    // Compensate gravity drop for lobbed shots.
                    if (fire.GravityScale > 0f) aimPoint += Vector3.up * (0.5f * (800f / 32f) * fire.GravityScale * flight * flight);
                }
            }

            Vector3 dir = (aimPoint - origin).normalized;
            // Skill error.
            dir = Quaternion.Euler(Random.Range(-AimErrorDegrees, AimErrorDegrees), Random.Range(-AimErrorDegrees, AimErrorDegrees), 0f) * dir;
            Weapons.TryFire(origin, dir, false);
        }
    }
}
