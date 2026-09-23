using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MyXonotic
{
    /// <summary>
    /// dev.16: Xonotic-style bot (qcsrc/server/bot/default/havocbot + bot.qc),
    /// values from xonotic-server.cfg `bot_ai_*`:
    ///  - strategy tick every bot_ai_strategyinterval 7 s (5.5 s with a moving goal)
    ///    picks ONE goal: needed item (weighted by need, whole map) → enemy → roam spawn.
    ///  - NavMesh path following (BotNavigator) replaces the dev.13 straight-line hunt.
    ///  - enemy detection every 2 s (4 s while sticking to an enemy), radius 10000 qu.
    ///  - weapon choice every 0.5 s from bot_ai_custom_weapon_priority_{close,mid,far}
    ///    with the 300 / 850 qu distance split.
    ///  - bot_ai_ignoregoal_timeout 3 s: a goal that keeps us stuck is dropped for 10 s.
    ///  - skill 1..10 (server default `skill 8`): aim error, think cadence, bunnyhop
    ///    from bot_ai_bunnyhop_skilloffset 7.
    /// TestMode freezes decisions (deterministic tests) but keeps gravity/knockback.
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
        /// bot_ai_enemydetectionradius 10000 qu.
        public const float SightRange = 10000f / 32f;

        // xonotic-server.cfg
        public const float GoalReach = 1.5f;                    // goal considered reached (m)
        public const float StrategyInterval = 7f;              // bot_ai_strategyinterval
        public const float StrategyIntervalMoving = 5.5f;      // bot_ai_strategyinterval_movingtarget
        public const float EnemyDetectionInterval = 2f;        // bot_ai_enemydetectioninterval
        public const float EnemyDetectionSticking = 4f;        // bot_ai_enemydetectioninterval_stickingtoenemy
        public const float ChooseWeaponInterval = 0.5f;        // bot_ai_chooseweaponinterval
        public const float IgnoreGoalTimeout = 3f;             // bot_ai_ignoregoal_timeout
        public const float IgnoreGoalFor = 10f;
        public const float ProgressWindow = 2.5f;              // dev.17: seconds without net movement while holding a goal = stuck
        public const float ProgressMinDistance = 0.6f;         // metres the bot must have moved in that window
        public const float EdgeGuard = 0.9f;                   // dev.17: roam goals closer than this to a NavMesh edge are rejected                // how long a dropped goal stays blacklisted (ours)
        public const float FriendsAwarePickupRadius = 500f / 32f; // bot_ai_friends_aware_pickup_radius
        public const float CloseRange = 300f / 32f;            // bot_ai_custom_weapon_priority_distances
        public const float FarRange = 850f / 32f;
        public const float AimSkillOffset = 1.8f;              // bot_ai_aimskill_offset (degrees of induced error)
        public const int BunnyhopSkill = 7;                    // bot_ai_bunnyhop_skilloffset
        public const float ThinkInterval = 0.05f;              // bot_ai_thinkinterval (scaled by skill)
        public const int DefaultSkill = 8;                     // xonotic-server.cfg `skill 8`

        /// Kept for older callers; derived from Skill in SetSkill().
        public float AimErrorDegrees = 3.5f;
        public float StrafeBias = 0.6f;
        public int Skill { get; private set; } = DefaultSkill;

        public bool IsHunting { get; private set; }
        public Transform Target;
        public WeaponController Weapons;
        public CharacterAnimator Animator;
        public Actor Actor { get; private set; }
        public Vector3 Velocity => _velocity;
        public Vector3? Objective { get; private set; }
        /// Current strategy goal (diagnostics / tests).
        public Vector3? Goal => _nav.HasGoal ? _nav.Goal : (Vector3?)null;
        public string GoalKind { get; private set; } = "none";
        public BotNavigator Navigator => _nav;

        BotNavigator _navStore;
        BotNavigator _nav => _navStore ?? (_navStore = new BotNavigator());
        readonly Dictionary<Vector3, float> _ignoredGoals = new Dictionary<Vector3, float>();

        Mover _groundMover;
        float _groundMoverTime;
        CharacterController _cc;
        Vector3 _velocity;
        float _strategyTimer;
        float _retargetTimer;
        float _weaponThinkTimer;
        float _thinkTimer;
        float _strafeDir = 1f;
        float _jumpTimer;
        float _stuckTime;
        Vector3 _progressAnchor;
        float _progressTimer;
        Pickup _wantedPickup;
        bool _goalIsEnemy;
        Vector3 _aimDir;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            Actor = GetComponent<Actor>();
            SetSkill(Skill);
        }

        void OnEnable() { if (Actor != null) Actor.Respawned += OnRespawned; }
        void OnDisable() { if (Actor != null) Actor.Respawned -= OnRespawned; }

        /// Skill 1..10. Aim error = bot_ai_aimskill_offset scaled by (10 - skill) / 5:
        /// skill 10 → 0°, skill 5 → 1.8°, skill 1 → 3.24°. (Xonotic applies the offset
        /// through a 5-stage filter chain; this linear mapping is our approximation.)
        public void SetSkill(int skill)
        {
            Skill = Mathf.Clamp(skill, 1, 10);
            AimErrorDegrees = AimErrorFor(Skill);
        }

        public static float AimErrorFor(int skill) => AimSkillOffset * (10 - Mathf.Clamp(skill, 1, 10)) / 5f;
        /// bot_ai_thinkinterval 0.05 "scales by skill": low skill thinks less often.
        public static float ThinkIntervalFor(int skill) => ThinkInterval * Mathf.Max(1, 11 - Mathf.Clamp(skill, 1, 10));
        public static bool BunnyhopsAt(int skill) => skill >= BunnyhopSkill;

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

        // ------------------------------------------------------------ targeting

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
                var t = Target.GetComponent<Actor>();
                if (t == null || t.IsDead || !Actor.IsEnemyOf(t)) Target = null;
            }
        }

        Actor NearestEnemy()
        {
            Actor best = null;
            float bestDist = float.MaxValue;
            foreach (var a in GameState.Actors)
            {
                if (a == null || a == Actor || a.IsDead || !Actor.IsEnemyOf(a)) continue;
                float d = (a.transform.position - transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = a; }
            }
            return best;
        }

        Vector3? CtfObjective()
        {
            var arena = ArenaBootstrap.Instance;
            if (arena == null || arena.Mode != GameMode.CaptureTheFlag || Actor.Team == Team.None) return null;
            var carried = CtfFlag.CarriedBy(Actor);
            var own = CtfFlag.ForTeam(Actor.Team);
            var enemy = CtfFlag.ForTeam(MatchSettings.Opponent(Actor.Team));
            if (carried != null) return own != null ? own.Home : (Vector3?)null;
            if (own != null && own.State == CtfFlag.FlagState.Dropped) return own.transform.position;
            if (enemy != null && enemy.State != CtfFlag.FlagState.Carried) return enemy.transform.position;
            return null;
        }

        public void ApplyExternalImpulse(Vector3 impulse) => _velocity += impulse;
        public void Launch(Vector3 velocity) { _velocity = velocity; }

        public void ResetMotion()
        {
            _velocity = Vector3.zero;
            _strategyTimer = 0f;
            _wantedPickup = null;
            _stuckTime = 0f;
            _goalIsEnemy = false;
            IsHunting = false;
            _nav.Clear();
            GoalKind = "none";
        }

        // ------------------------------------------------------------- strategy

        /// havocbot_chooseweapon/havocbot_goalrating: one goal per strategy tick.
        void ChooseStrategy(bool seesTarget)
        {
            Objective = CtfObjective();
            _wantedPickup = FindWantedPickup();
            _goalIsEnemy = false;
            if (Objective.HasValue && (CtfFlag.CarriedBy(Actor) != null || _wantedPickup == null))
            {
                SetGoal(Objective.Value, "flag");
                return;
            }
            if (_wantedPickup != null)
            {
                SetGoal(_wantedPickup.transform.position, "item:" + _wantedPickup.Type);
                return;
            }
            Actor prey = seesTarget ? null : NearestEnemy();
            if (prey != null)
            {
                _goalIsEnemy = true;
                IsHunting = true;
                SetGoal(prey.transform.position, "enemy");
                return;
            }
            // bot_wander_enable 1: roam to a spawn point we are not standing on.
            var arena = ArenaBootstrap.Instance;
            if (arena != null && arena.SpawnCount > 0)
            {
                Vector3 pick = transform.position;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    Vector3 candidate = arena.SpawnPosition(Random.Range(0, arena.SpawnCount));
                    if ((candidate - transform.position).sqrMagnitude > 16f && !IsIgnored(candidate) && !NearNavMeshEdge(candidate)) { pick = candidate; break; }
                }
                SetGoal(pick, "roam");
                return;
            }
            Vector2 rand = Random.insideUnitCircle * 12f;
            SetGoal(transform.position + new Vector3(rand.x, 0f, rand.y), "wander");
        }

        void SetGoal(Vector3 goal, string kind)
        {
            _nav.SetGoal(goal);
            GoalKind = kind;
            _stuckTime = 0f;
            _progressAnchor = transform.position;
            _progressTimer = 0f;
        }

        /// dev.17: Test Lab dev.16 showed bots dying to the void (bot_frags=-10). Spawn points that sit
        /// on a ledge lip are legal goals but the CharacterController slides off; skip goals within
        /// EdgeGuard of the NavMesh boundary when a NavMesh exists.
        static bool NearNavMeshEdge(Vector3 point)
        {
            if (!MapNavMesh.Available) return false;
            if (!NavMesh.FindClosestEdge(point, out NavMeshHit hit, NavMesh.AllAreas)) return false;
            return hit.distance < EdgeGuard;
        }

        /// dev.17 pure helper (tested): stuck when a goal is held and the bot moved less than
        /// ProgressMinDistance over ProgressWindow seconds — catches the "pressing into a wall
        /// with zero wish direction" case that the velocity-based check misses.
        public static bool NoProgress(Vector3 anchor, Vector3 now, float elapsed) =>
            elapsed >= ProgressWindow && BotNavigator.FlatDistance(anchor, now) < ProgressMinDistance;

        /// dev.17 (bot_ai avoids self-damage): a splash weapon must not be fired when the shot would
        /// detonate within its blast radius of the shooter — i.e. world geometry sits closer than
        /// radius + margin along the aim line (the target itself does not count).
        public static bool SplashWouldHitSelf(Vector3 origin, Vector3 dir, float splashRadius, Transform target)
        {
            if (splashRadius <= 0f) return false;
            float danger = splashRadius + 1f;
            if (!Physics.Raycast(origin, dir, out RaycastHit hit, danger, ~0, QueryTriggerInteraction.Ignore)) return false;
            return target == null || (hit.transform != target && !hit.transform.IsChildOf(target));
        }

        bool IsIgnored(Vector3 goal)
        {
            float until;
            return _ignoredGoals.TryGetValue(Round(goal), out until) && Time.time < until;
        }

        static Vector3 Round(Vector3 v) => new Vector3(Mathf.Round(v.x), Mathf.Round(v.y), Mathf.Round(v.z));

        // ---------------------------------------------------------------- update

        void Update()
        {
            if (ArenaBootstrap.IsPaused) return;
            if (Actor != null && Actor.IsDead) return;

            bool testMode = ArenaBootstrap.TestMode;
            Vector3 wishDir = Vector3.zero;
            bool wantJump = false;
            bool grounded = _cc.isGrounded;

            if (!testMode)
            {
                _retargetTimer -= Time.deltaTime;
                if (_retargetTimer <= 0f)
                {
                    _retargetTimer = Target != null ? EnemyDetectionSticking : EnemyDetectionInterval;
                    Retarget();
                }
                bool seesTarget = TargetVisible(out Vector3 toTarget, out float dist);
                if (seesTarget) IsHunting = false;

                _strategyTimer -= Time.deltaTime;
                bool goalDone = _nav.HasGoal && BotNavigator.FlatDistance(_nav.Goal, transform.position) < GoalReach;
                bool itemGone = _wantedPickup != null && !_wantedPickup.IsAvailable;
                if (_strategyTimer <= 0f || !_nav.HasGoal || goalDone || itemGone || (_goalIsEnemy && seesTarget))
                {
                    _strategyTimer = _goalIsEnemy ? StrategyIntervalMoving : StrategyInterval;
                    _strafeDir = Random.value < 0.5f ? -1f : 1f;
                    ChooseStrategy(seesTarget);
                }
                // Moving goal: keep the enemy position fresh between strategy ticks.
                if (_goalIsEnemy && !seesTarget)
                {
                    var prey = NearestEnemy();
                    if (prey != null) _nav.SetGoal(prey.transform.position);
                }

                bool navJump;
                Vector3 navDir = _nav.Steer(transform.position, out navJump);

                if (seesTarget && !(Objective.HasValue && CtfFlag.CarriedBy(Actor) != null))
                {
                    // Combat: hold the weapon's preferred range and strafe; path in when far.
                    float preferred = PreferredRange();
                    Vector3 flat = BotNavigator.Flat(toTarget);
                    if (dist > preferred * 1.6f && navDir != Vector3.zero && _goalIsEnemy) wishDir = navDir;
                    else
                    {
                        Vector3 side = Vector3.Cross(Vector3.up, flat) * _strafeDir;
                        float closeFactor = Mathf.Clamp((dist - preferred) / preferred, -1f, 1f);
                        wishDir = BotNavigator.Flat(flat * closeFactor + side * StrafeBias);
                    }
                }
                else
                {
                    wishDir = navDir;
                    wantJump = navJump;
                }

                // Stuck handling (bot_ai_ignoregoal_timeout): jump first, then drop the goal.
                bool stuck = wishDir.sqrMagnitude > 0.01f && grounded &&
                             new Vector3(_velocity.x, 0f, _velocity.z).magnitude < 0.5f;
                _stuckTime = stuck ? _stuckTime + Time.deltaTime : Mathf.Max(0f, _stuckTime - Time.deltaTime * 0.5f);
                if (stuck && _stuckTime > 0.4f) wantJump = true;
                // dev.17: position-based progress check (the Game Loop pilot stood at a wall for 90 s in dev.16).
                bool noProgress = false;
                if (_nav.HasGoal)
                {
                    _progressTimer += Time.deltaTime;
                    if (_progressTimer >= ProgressWindow)
                    {
                        noProgress = NoProgress(_progressAnchor, transform.position, _progressTimer);
                        _progressAnchor = transform.position;
                        _progressTimer = 0f;
                        if (noProgress) wantJump = true;
                    }
                }
                else { _progressAnchor = transform.position; _progressTimer = 0f; }
                if (noProgress || _stuckTime > IgnoreGoalTimeout || (_nav.GoalUnreachable && !_goalIsEnemy && !seesTarget))
                {
                    if (_nav.HasGoal) _ignoredGoals[Round(_nav.Goal)] = Time.time + IgnoreGoalFor;
                    _stuckTime = 0f;
                    _strategyTimer = 0f;
                    _nav.Clear();
                }

                // Bunnyhop (skill >= 7): keep hopping while running roughly straight.
                _jumpTimer -= Time.deltaTime;
                if (grounded && wishDir.sqrMagnitude > 0.01f)
                {
                    Vector3 horizontal = new Vector3(_velocity.x, 0f, _velocity.z);
                    float turn = horizontal.sqrMagnitude > 0.1f ? Vector3.Angle(horizontal, wishDir) : 180f;
                    if (BunnyhopsAt(Skill) && turn <= 20f && !seesTarget) wantJump = true;   // bot_ai_bunnyhop_dir_deviation_max 20
                    else if (_jumpTimer <= 0f && seesTarget && Random.value < 0.15f) { wantJump = true; _jumpTimer = Random.Range(1.5f, 3.5f); }
                }

                if (seesTarget) EngageTarget(toTarget, dist);
                else if (wishDir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(wishDir, Vector3.up);
            }

            var horiz = new Vector3(_velocity.x, 0f, _velocity.z);
            if (grounded && wishDir.sqrMagnitude < 0.01f)
                horiz = ArenaMath.ApplyGroundFriction(horiz, Player.GroundFriction, Player.StopSpeed, Time.deltaTime);
            horiz = ArenaMath.Accelerate(horiz, wishDir, MoveSpeed, grounded ? Acceleration : Player.AirAcceleration, Time.deltaTime);
            _velocity.x = horiz.x;
            _velocity.z = horiz.z;

            if (grounded && _velocity.y < 0f) _velocity.y = -1f;
            _velocity.y += Gravity * Time.deltaTime;
            if (grounded && wantJump) _velocity.y = JumpSpeed;

            Vector3 carry = Vector3.zero;
            if (_groundMover != null && Time.time - _groundMoverTime < 0.15f) carry = _groundMover.LastDelta;
            else _groundMover = null;

            _cc.Move(_velocity * Time.deltaTime + carry);
            if (transform.position.y < ArenaBootstrap.VoidKillY) Actor.TakeDamage(10000, Vector3.zero, null);

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
            if (Physics.Raycast(eye, toTarget / dist, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                var hitActor = hit.collider.GetComponentInParent<Actor>();
                if (hitActor == null || hitActor.transform != Target) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ items

        /// havocbot item rating: need-weighted, whole map, discounted by distance; teammates
        /// within bot_ai_friends_aware_pickup_radius claim the item.
        Pickup FindWantedPickup()
        {
            var arena = ArenaBootstrap.Instance;
            if (arena == null || Weapons == null || Actor == null) return null;
            Pickup best = null;
            float bestScore = 0f;
            Vector3 here = transform.position;
            foreach (var p in arena.Pickups)
            {
                if (p == null || !p.IsAvailable || IsIgnored(p.transform.position)) continue;
                float d = Vector3.Distance(here, p.transform.position);
                if (!MapNavMesh.Available && (d > 25f || Mathf.Abs(p.transform.position.y - here.y) > 3f)) continue;
                if (TeammateNear(p.transform.position)) continue;
                float want = ItemWant(p.Type, p.Weapon, Weapons.Has(p.Weapon), Actor.Health, Actor.Armor,
                    Weapons.OwnedCount, Weapons.GetAmmo(Weapons.Current));
                float score = want / (1f + d * 0.05f);
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return bestScore > 0.6f ? best : null;
        }

        /// Pure rating (tested): how much a bot wants an item.
        public static float ItemWant(PickupType type, WeaponType weapon, bool hasWeapon, int health, int armor, int ownedWeapons, int currentAmmo)
        {
            switch (type)
            {
                case PickupType.Weapon: return hasWeapon ? 0.2f : (weapon == WeaponType.Hook ? 0.1f : 3f);
                case PickupType.Health: return health < 40 ? 5f : health < 70 ? 2.5f : 0.3f;
                case PickupType.Armor: return armor < 60 ? 1.5f : 0.2f;
                case PickupType.Strength:
                case PickupType.Shield: return 4f;
                default: return ownedWeapons > 2 && currentAmmo < 10 ? 1.8f : 0.5f;
            }
        }

        bool TeammateNear(Vector3 point)
        {
            if (Actor.Team == Team.None) return false;
            foreach (var a in GameState.Actors)
            {
                if (a == null || a == Actor || a.IsDead || a.Team != Actor.Team) continue;
                if ((a.transform.position - point).sqrMagnitude < FriendsAwarePickupRadius * FriendsAwarePickupRadius) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- weapons

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

        // bot_ai_custom_weapon_priority_* (xonotic-server.cfg), weapons we do not ship
        // (vaporizer, oknex, ok*, hlac, shockwave, tuba, seeker) removed, order kept.
        public static readonly WeaponType[] PriorityClose =
            { WeaponType.Vortex, WeaponType.Shotgun, WeaponType.MachineGun, WeaponType.Arc, WeaponType.Hagar, WeaponType.Crylink,
              WeaponType.Mortar, WeaponType.Electro, WeaponType.Devastator, WeaponType.Blaster, WeaponType.Fireball, WeaponType.Rifle };
        public static readonly WeaponType[] PriorityMid =
            { WeaponType.Devastator, WeaponType.Vortex, WeaponType.Fireball, WeaponType.Mortar, WeaponType.Electro, WeaponType.MachineGun,
              WeaponType.Arc, WeaponType.Crylink, WeaponType.Hagar, WeaponType.Shotgun, WeaponType.Blaster, WeaponType.Rifle };
        public static readonly WeaponType[] PriorityFar =
            { WeaponType.Vortex, WeaponType.Rifle, WeaponType.Electro, WeaponType.Devastator, WeaponType.Mortar, WeaponType.Hagar,
              WeaponType.Crylink, WeaponType.Blaster, WeaponType.MachineGun, WeaponType.Fireball, WeaponType.Shotgun };

        public static WeaponType[] PriorityFor(float distMeters) =>
            distMeters < CloseRange ? PriorityClose : distMeters < FarRange ? PriorityMid : PriorityFar;

        void ChooseWeapon(float dist)
        {
            if (Weapons == null) return;
            _weaponThinkTimer -= Time.deltaTime;
            if (_weaponThinkTimer > 0f) return;
            _weaponThinkTimer = ChooseWeaponInterval;
            foreach (var w in PriorityFor(dist))
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

            if (fire.SplashRadius > 0f && dist < fire.SplashRadius + 1.5f) return;
            if (fire.Mode == FireMode.Hook || fire.Mode == FireMode.Mine || fire.Mode == FireMode.Load) return;
            if (fire.Mode == FireMode.Beam && dist > fire.Speed) return;

            Vector3 origin = transform.position + Vector3.up * 1.5f;

            // Aim is re-evaluated at the skill think cadence (bot_ai_thinkinterval);
            // between ticks the bot keeps firing along its last aim like a slow hand.
            _thinkTimer -= Time.deltaTime;
            if (_thinkTimer <= 0f || _aimDir == Vector3.zero)
            {
                _thinkTimer = ThinkIntervalFor(Skill);
                Vector3 aimPoint = Target.position + Vector3.up * 1f;
                if (fire.Mode != FireMode.Hitscan && fire.Speed > 0f)
                {
                    var targetPlayer = Target.GetComponent<Player>();
                    var targetBot = Target.GetComponent<Bot>();
                    Vector3 vel = targetPlayer != null && targetPlayer.enabled ? targetPlayer.Velocity : targetBot != null ? targetBot.Velocity : Vector3.zero;
                    float flight = dist / fire.Speed;
                    aimPoint += vel * flight;
                    if (fire.GravityScale > 0f) aimPoint += Vector3.up * (0.5f * (800f / 32f) * fire.GravityScale * flight * flight);
                }
                Vector3 exact = (aimPoint - origin).normalized;
                _aimDir = Quaternion.Euler(Random.Range(-AimErrorDegrees, AimErrorDegrees), Random.Range(-AimErrorDegrees, AimErrorDegrees), 0f) * exact;
            }
            Vector3 dir = _aimDir;
            Weapons.UpdateAim(origin, dir);
            if (SplashWouldHitSelf(origin, dir, fire.SplashRadius, Target)) return;
            if (Weapons.TryFire(origin, dir, false) && Animator != null && fire.Refire >= 0.5f) Animator.PlayOneShot("shoot");
        }
    }
}
