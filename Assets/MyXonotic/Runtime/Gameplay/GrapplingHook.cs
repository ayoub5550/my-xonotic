using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Grappling hook (Hook weapon primary). Fires a fast kinematic hook; when
    /// it lands on world geometry the owner is pulled towards the anchor while
    /// the trigger stays held. Releasing the trigger (or an 8 s timeout, or
    /// death) retracts it. Player-only: bots never fire the Hook.
    /// </summary>
    public sealed class GrapplingHook : MonoBehaviour
    {
        public const float PullSpeed = 800f / 32f;
        public const float MaxFlightSeconds = 1.2f;
        public const float MaxHoldSeconds = 8f;
        public const float ReleaseDistance = 1.2f;

        public Player Owner;
        /// Set every frame by the input owner (via WeaponController.SetPrimaryHeld).
        public bool Held;

        public bool IsActive => _state != State.Idle;
        public bool IsAnchored => _state == State.Anchored;
        public Vector3 Anchor => _anchor;

        enum State { Idle, Flying, Anchored }
        State _state;
        Vector3 _pos, _velocity, _anchor;
        float _age;
        LineRenderer _rope;
        GameObject _head;

        void Awake()
        {
            if (Owner == null) Owner = GetComponent<Player>();
            _rope = gameObject.AddComponent<LineRenderer>();
            _rope.useWorldSpace = true;
            _rope.positionCount = 2;
            _rope.startWidth = 0.04f;
            _rope.endWidth = 0.04f;
            _rope.sharedMaterial = ArenaMaterials.Get(new Color(0.75f, 0.75f, 0.8f));
            _rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _rope.enabled = false;
            _head = new GameObject("HookHead");
            _head.transform.SetParent(transform, false);
            _head.AddComponent<MeshFilter>().mesh = ArenaPrimitives.SphereMesh;
            var mr = _head.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ArenaMaterials.Get(new Color(0.6f, 0.6f, 0.65f));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _head.transform.localScale = Vector3.one * 0.15f;
            _head.SetActive(false);
        }

        /// Launches the hook. Returns false when one is already out.
        public bool Fire(Vector3 origin, Vector3 direction, float speed)
        {
            if (_state != State.Idle) return false;
            _state = State.Flying;
            _pos = origin;
            _velocity = direction.normalized * (speed > 0f ? speed : 60f);
            _age = 0f;
            Held = true;
            _rope.enabled = true;
            _head.SetActive(true);
            return true;
        }

        public void Release()
        {
            if (_state == State.Idle) return;
            _state = State.Idle;
            Held = false;
            if (_rope != null) _rope.enabled = false;
            if (_head != null) _head.SetActive(false);
            if (Owner != null) Owner.HookAnchor = null;
        }

        void OnDisable() => Release();

        void Update()
        {
            if (_state == State.Idle)
            {
                // Actor.ResetForSpawn re-enables every child renderer; keep the rope hidden while idle.
                if (_rope != null && _rope.enabled) _rope.enabled = false;
                return;
            }
            if (ArenaBootstrap.IsPaused) return;
            var actor = Owner != null ? Owner.Actor : null;
            if (actor == null || actor.IsDead || !Held) { Release(); return; }
            float dt = Time.deltaTime;
            _age += dt;

            if (_state == State.Flying)
            {
                if (_age > MaxFlightSeconds) { Release(); return; }
                float step = _velocity.magnitude * dt;
                var hits = Physics.RaycastAll(_pos, _velocity.normalized, step, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var hit in hits)
                {
                    var hitActor = hit.collider.GetComponentInParent<Actor>();
                    if (hitActor == actor) continue;
                    if (hitActor != null) { Release(); return; } // hooking players is not modelled
                    _anchor = hit.point + hit.normal * 0.05f;
                    _pos = _anchor;
                    _state = State.Anchored;
                    _age = 0f;
                    WeaponAudio.PlayAt(WeaponAudio.Load(WeaponAudio.ResourceName(WeaponType.Hook, "hook_impact")), _anchor, 0.7f);
                    break;
                }
                if (_state == State.Flying) _pos += _velocity * dt;
            }
            else
            {
                if (_age > MaxHoldSeconds) { Release(); return; }
                Vector3 eye = Owner.transform.position + Vector3.up * 1.2f;
                if (Vector3.Distance(eye, _anchor) < ReleaseDistance) { Release(); return; }
                Owner.HookAnchor = _anchor;
            }

            Vector3 from = Owner.transform.position + Vector3.up * 1.2f + Owner.transform.right * 0.3f;
            _rope.SetPosition(0, from);
            _rope.SetPosition(1, _pos);
            _head.transform.position = _pos;
        }
    }
}
