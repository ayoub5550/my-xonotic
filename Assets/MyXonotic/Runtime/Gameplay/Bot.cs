using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Minimal grounded bot: wanders randomly, faces and fires at a target
    /// (the player) when within range. Disabled by ArenaBootstrap.TestMode so
    /// automated tests get deterministic, stationary bots.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Actor))]
    public sealed class Bot : MonoBehaviour
    {
        public const float MoveSpeed = 5f;
        public const float Acceleration = 10f;
        public const float Gravity = -18f;
        public const float FireRange = 30f;

        public Transform Target;
        public WeaponController Weapons;
        public Actor Actor { get; private set; }

        CharacterController _cc;
        Vector3 _velocity;
        Vector3 _externalImpulse;
        Vector3 _wanderTarget;
        float _repickTimer;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            Actor = GetComponent<Actor>();
            _wanderTarget = transform.position;
        }

        public void ApplyExternalImpulse(Vector3 impulse) => _externalImpulse += impulse;

        public void ResetMotion()
        {
            _velocity = Vector3.zero;
            _externalImpulse = Vector3.zero;
            _wanderTarget = transform.position;
            _repickTimer = 0f;
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

            if (!testMode)
            {
                _repickTimer -= Time.deltaTime;
                if (_repickTimer <= 0f)
                {
                    _repickTimer = Random.Range(2f, 5f);
                    Vector2 rand = Random.insideUnitCircle * 10f;
                    _wanderTarget = transform.position + new Vector3(rand.x, 0f, rand.y);
                }

                Vector3 toTarget = _wanderTarget - transform.position;
                toTarget.y = 0f;
                wishDir = toTarget.sqrMagnitude > 0.25f ? toTarget.normalized : Vector3.zero;
            }

            bool grounded = _cc.isGrounded;
            var horizontal = new Vector3(_velocity.x, 0f, _velocity.z);
            horizontal = ArenaMath.Accelerate(horizontal, wishDir, MoveSpeed, Acceleration, Time.deltaTime);
            _velocity.x = horizontal.x;
            _velocity.z = horizontal.z;

            if (grounded && _velocity.y < 0f) _velocity.y = -1f;
            _velocity.y += Gravity * Time.deltaTime;

            if (!testMode && wishDir.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(wishDir, Vector3.up);
            }

            _cc.Move((_velocity + _externalImpulse) * Time.deltaTime);
            _externalImpulse = Vector3.Lerp(_externalImpulse, Vector3.zero, 6f * Time.deltaTime);
            if (transform.position.y < -200f) Actor.TakeDamage(10000, Vector3.zero, null);

            if (!testMode) TryEngageTarget();
        }

        void TryEngageTarget()
        {
            if (Target == null || Weapons == null) return;
            Vector3 toPlayer = Target.position - transform.position;
            float dist = toPlayer.magnitude;
            if (dist > FireRange) return;

            Vector3 flat = new Vector3(toPlayer.x, 0f, toPlayer.z);
            if (flat.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);

            Vector3 origin = transform.position + Vector3.up * 1.2f;
            Vector3 dir = (Target.position + Vector3.up * 1f - origin).normalized;
            Weapons.TryFire(origin, dir, false);
        }
    }
}
