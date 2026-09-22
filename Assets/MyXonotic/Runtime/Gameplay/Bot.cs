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

        /// Current enemy (re-evaluated every decision tick: nearest visible enemy actor).
        public Transform Target;
        public WeaponController Weapons;
        /// Skeletal animator of the attached character body (null with the static/capsule body).
        public CharacterAnimator Animator;
        public Actor Actor { get; private set; }
        public Vector3 Velocity => _velocity;

        /// CTF objective this tick: enemy flag, own base (carrying) or own dropped flag.
        public Vector3? Objective { get; private set; }

        Mover _groundMover;
        float _groundMoverTime;
        float _retargetTimer;

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

        /// Bots respawn with one random extra core weapon (the player relies on map pickups).
        void OnRespawned(Actor actor)
        {
            if (ArenaBootstrap.TestMode || Weapons == null) return;
            Weapons.GiveWeapon((WeaponType)Random.Range((int)WeaponType.MachineGun, WeaponController.CoreWeaponCount));
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y < 0.5f) return;
            var mover = hit.collider.GetComponentInParent<Mover>();
            if (mover == null) return;
            _groundMover = mover;
            _groundMoverTime = Time.time;
        }

        /// Picks the nearest live enemy with line of sight (any actor, not only the player).
        void Retarget()
        {
            Actor best = null;
            float bestDist = float.MaxValue;
            Vector3 eye = transform.position + Vector3.up * 1.5f;
            foreach (var a in GameState.Actors)
            {
                if (a == null || a == Actor || a.IsDead || !Actor.IsEnemyOf(a)) continue;
                Vector3 aim = a.transform.position + Vector3.up * 1f;
                float d = Vector3.Distance(eye, aim);
                if (d > SightRange || d >= bestDist) continue;
                if (Physics.Raycast(eye, (aim - eye) / d, out RaycastHit hit, d, ~0, QueryTriggerInteraction.Ignore))
                {
                    var hitActor = hit.collider.GetComponentInParent<Actor>();
                    if (hitActor != a) continue;
                }
                best = a; bestDist = d;
            }
            if (best != null) Target = best.transform;
            else if (Target != null)
            {
                // Keep the last target only if it is still a live enemy.
                var t = Target.GetComponent<Actor>();
                if (t == null || t.IsDead || !Actor.IsEnemyOf(t)) Target = null;
            }
        }

        /// CTF: where this bot wants to go (null = no flag objective).
        Vector3? CtfObjective()
        {
            var arena = ArenaBootstrap.Instance;
            if (arena == null || arena.Mode != GameMode.CaptureTheFlag || Actor.Team == Team.None) return null;
            var carried = CtfFlag.CarriedBy(Actor);
            var own = CtfFlag.ForTeam(Actor.Team);
            var enemy = CtfFlag.ForTeam(MatchSettings.Opponent(Actor.Team));
            if (carried != null) return own != null ? own.Home : (Vector3?)null;               // bring it home
            if (own != null && own.State == CtfFlag.FlagState.Dropped) return own.transform.position; // return ours
            if (enemy != null && enemy.State != CtfFlag.FlagState.Carried) return enemy.transform.position; // go get theirs
            return null;
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
                _retargetTimer -= Time.deltaTime;
                if (_retargetTimer <= 0f) { _retargetTimer = 0.5f; Retarget(); }
                bool seesTarget = TargetVisible(out Vector3 toTarget, out float dist);
                _repickTimer -= Time.deltaTime;
                if (_repickTimer <= 0f)
                {
                    _repickTimer = Random.Range(1.2f, 3f);
                    _strafeDir = Random.value < 0.5f ? -1f : 1f;
                    _wantedPickup = FindWantedPickup();
                    Objective = CtfObjective();
                    if (_wantedPickup == null && !Objective.HasValue)
                    {
                        Vector2 rand = Random.insideUnitCircle * 12f;
                        _wanderTarget = transform.position + new Vector3(rand.x, 0f, rand.y);
                    }
                }

                if (Objective.HasValue && (CtfFlag.CarriedBy(Actor) != null || !seesTarget))
                    _wanderTarget = Objective.Value;
                else if (_wantedPickup != null && _wantedPickup.IsAvailable)
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

            Vector3 carry = Vector3.zero;
            if (_groundMover != null && Time.time - _groundMoverTime < 0.15f) carry = _groundMover.LastDelta;
            else _groundMover = null;

            _cc.Move((_velocity + _externalImpulse) * Time.deltaTime + carry);
            _externalImpulse = Vector3.Lerp(_externalImpulse, Vector3.zero, 6f * Time.deltaTime);
            if (transform.position.y < -200f) Actor.TakeDamage(10000, Vector3.zero, null);

            if (Animator != null) Animator.SetMotion(_velocity, transform.forward, grounded);
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
                    case PickupType.Weapon: want = Weapons.Has(p.Weapon) ? 0.2f : (p.Weapon == WeaponType.Hook ? 0.1f : 3f); break;
                    case PickupType.Health: want = Actor.Health < 70 ? 2.5f : 0.3f; break;
                    case PickupType.Armor: want = Actor.Armor < 60 ? 1.5f : 0.2f; break;
                    case PickupType.Strength:
                    case PickupType.Shield: want = 4f; break;
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
                case WeaponType.Arc: return 6f;
                case WeaponType.MachineGun: return 12f;
                case WeaponType.Vortex:
                case WeaponType.Rifle: return 22f;
                case WeaponType.Mortar:
                case WeaponType.Devastator:
                case WeaponType.Fireball: return 12f;
                default: return 9f;
            }
        }

        void ChooseWeapon(float dist)
        {
            if (Weapons == null) return;
            _weaponThinkTimer -= Time.deltaTime;
            if (_weaponThinkTimer > 0f) return;
            _weaponThinkTimer = 0.8f;

            // Bots never use the Hook; Minelayer only defensively (not in this table).
            WeaponType[] order;
            if (dist < 7f)
                order = new[] { WeaponType.Arc, WeaponType.Shotgun, WeaponType.MachineGun, WeaponType.Crylink, WeaponType.Electro, WeaponType.Vortex, WeaponType.Blaster };
            else if (dist < 18f)
                order = new[] { WeaponType.Fireball, WeaponType.Devastator, WeaponType.Hagar, WeaponType.Electro, WeaponType.Mortar, WeaponType.Crylink, WeaponType.MachineGun, WeaponType.Arc, WeaponType.Vortex, WeaponType.Shotgun, WeaponType.Blaster };
            else
                order = new[] { WeaponType.Vortex, WeaponType.Rifle, WeaponType.MachineGun, WeaponType.Devastator, WeaponType.Electro, WeaponType.Hagar, WeaponType.Mortar, WeaponType.Blaster, WeaponType.Shotgun };

            foreach (var w in order)
            {
                if (!Weapons.CanFire(w)) continue;
                if (w != Weapons.Current) Weapons.SwitchTo(w);
                return;
            }
        }

        void EngageTarget(Vector3 toTarget, float dist)
        {
            if (Weapons == null || Target == null) return;
            Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
            if (flat.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            if (dist > FireRange) return;

            ChooseWeapon(dist);
            var def = Weapons.CurrentDef;
            var fire = def.Primary;

            // Never fire splash weapons into our own face; never fire hold-style weapons blindly.
            if (fire.SplashRadius > 0f && dist < fire.SplashRadius + 1.5f) return;
            if (fire.Mode == FireMode.Hook || fire.Mode == FireMode.Mine) return;
            if (fire.Mode == FireMode.Beam && dist > fire.Speed) return;

            Vector3 origin = transform.position + Vector3.up * 1.5f;
            Vector3 aimPoint = Target.position + Vector3.up * 1f;

            // Lead moving targets with projectile weapons.
            if (fire.Mode != FireMode.Hitscan && fire.Speed > 0f)
            {
                var targetPlayer = Target.GetComponent<Player>();
                var targetBot = Target.GetComponent<Bot>();
                if (targetPlayer != null || targetBot != null)
                {
                    float flight = dist / fire.Speed;
                    aimPoint += (targetPlayer != null ? targetPlayer.Velocity : targetBot.Velocity) * flight;
                    // Compensate gravity drop for lobbed shots.
                    if (fire.GravityScale > 0f) aimPoint += Vector3.up * (0.5f * (800f / 32f) * fire.GravityScale * flight * flight);
                }
            }

            Vector3 dir = (aimPoint - origin).normalized;
            // Skill error.
            dir = Quaternion.Euler(Random.Range(-AimErrorDegrees, AimErrorDegrees), Random.Range(-AimErrorDegrees, AimErrorDegrees), 0f) * dir;
            if (Weapons.TryFire(origin, dir, false) && Animator != null && fire.Refire >= 0.5f) Animator.PlayOneShot("shoot");
        }
    }
}
