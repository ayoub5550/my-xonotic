using MyXonotic.Content;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Runtime behaviour for the brush entities the importer placed as
    /// <see cref="ImportedSubmodel"/> markers: func_door / func_door_secret
    /// (slide open when an actor comes near, close after <c>wait</c>),
    /// func_rotating (spin around one axis), func_bobbing (sinusoidal drift)
    /// and func_plat (rise when touched). Attached by <see cref="Attach"/> at
    /// arena start; the mesh + MeshCollider stay on the same object and move
    /// with it. Actors standing on a mover are carried by reading
    /// <see cref="LastDelta"/> (Player/Bot). Approximation of the Quake-family
    /// defaults, not a port of any engine code; triggered doors (targetname)
    /// are opened by proximity too because trigger/target chains are not
    /// modelled yet.
    /// </summary>
    public sealed class Mover : MonoBehaviour
    {
        public enum Kind { Door, Rotating, Bobbing, Plat }

        const float Q = 1f / 32f;
        public const float DoorTriggerMargin = 60f * Q;
        public const float DefaultDoorSpeed = 100f * Q;
        public const float DefaultDoorLip = 8f * Q;
        public const float DefaultDoorWait = 3f;
        public const float DefaultRotateSpeed = 100f;
        public const float DefaultBobHeight = 32f * Q;
        public const float DefaultBobPeriod = 4f;
        public const float DefaultPlatSpeed = 150f * Q;
        public const float DefaultPlatWait = 3f;

        public Kind MoverKind;
        public string Classname;
        /// World-space displacement applied this frame (for riders).
        public Vector3 LastDelta { get; private set; }
        /// Door/plat progress 0 (rest) .. 1 (fully travelled).
        public float Progress { get; private set; }
        public bool IsOpen => Progress >= 0.999f;
        public bool IsMoving => _state == State.Opening || _state == State.Closing;
        public Vector3 TravelVector => _travel;
        public Vector3 RotationAxis => _axis;
        public float RotationSpeed => _rotSpeed;

        enum State { Closed, Opening, Open, Closing }
        State _state = State.Closed;

        Vector3 _restPos;
        Quaternion _restRot;
        Vector3 _travel;      // door/plat: full travel vector (world)
        float _speed;         // m/s
        float _wait;          // seconds open (-1 = stay open)
        float _waitTimer;
        Vector3 _axis;        // rotating: Unity-space axis
        float _rotSpeed;      // deg/s (already sign-converted)
        float _angle;
        float _bobHeight, _bobPeriod, _bobPhase;
        Vector3 _bobAxis;
        Bounds _localBounds;
        float _time;
        AudioSource _audio;

        /// Attach a Mover to every animatable ImportedSubmodel under <paramref name="root"/>; returns how many.
        public static int Attach(Transform root)
        {
            int n = 0;
            foreach (var sub in root.GetComponentsInChildren<ImportedSubmodel>(true))
            {
                if (sub.GetComponent<Mover>() != null) continue;
                Kind kind;
                if (!KindFor(sub.classname, out kind)) continue;
                var m = sub.gameObject.AddComponent<Mover>();
                m.Configure(sub, kind);
                n++;
            }
            return n;
        }

        public static bool KindFor(string classname, out Kind kind)
        {
            kind = Kind.Door;
            switch (classname)
            {
                case "func_door":
                case "func_door_secret": kind = Kind.Door; return true;
                case "func_rotating": kind = Kind.Rotating; return true;
                case "func_bobbing": kind = Kind.Bobbing; return true;
                case "func_plat": kind = Kind.Plat; return true;
                default: return false;
            }
        }

        /// Quake yaw / -1 (up) / -2 (down) to a Unity-space unit direction.
        public static Vector3 DirectionFromAngle(float angle)
        {
            if (float.IsNaN(angle)) return Vector3.forward;
            if (Mathf.Approximately(angle, -1f)) return Vector3.up;
            if (Mathf.Approximately(angle, -2f)) return Vector3.down;
            float rad = angle * Mathf.Deg2Rad;
            // Quake (cos, sin, 0) -> Unity (x, z, y) swap.
            return new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)).normalized;
        }

        /// Door travel distance: bounds extent along the direction minus lip.
        public static float DoorTravel(Bounds local, Vector3 dir, float lip)
        {
            Vector3 size = local.size;
            float extent = Mathf.Abs(dir.x) * size.x + Mathf.Abs(dir.y) * size.y + Mathf.Abs(dir.z) * size.z;
            return Mathf.Max(0.05f, extent - lip);
        }

        public void Configure(ImportedSubmodel sub, Kind kind)
        {
            MoverKind = kind;
            Classname = sub.classname;
            _restPos = transform.position;
            _restRot = transform.rotation;
            _localBounds = new Bounds();
            _localBounds.SetMinMax(sub.localMin, sub.localMax);
            if (_localBounds.size.sqrMagnitude < 1e-6f)
            {
                var mf = GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) _localBounds = mf.sharedMesh.bounds;
            }

            // Kinematic body so the moving MeshCollider is treated as a moving obstacle.
            var rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;

            float speedQ = float.IsNaN(sub.speed) || sub.speed <= 0f ? float.NaN : sub.speed;
            switch (kind)
            {
                case Kind.Door:
                {
                    Vector3 dir = DirectionFromAngle(sub.angle);
                    float lip = float.IsNaN(sub.lip) ? DefaultDoorLip : sub.lip * Q;
                    _travel = dir * DoorTravel(_localBounds, dir, lip);
                    _speed = float.IsNaN(speedQ) ? DefaultDoorSpeed : speedQ * Q;
                    _wait = float.IsNaN(sub.wait) ? DefaultDoorWait : sub.wait;
                    if ((sub.spawnflags & 1) != 0)
                    {
                        // START_OPEN: the brush is placed at the open position; rest is the far end.
                        _restPos = transform.position + _travel;
                        _travel = -_travel;
                        transform.position = _restPos;
                    }
                    break;
                }
                case Kind.Plat:
                {
                    float h = float.IsNaN(sub.height) ? Mathf.Max(0.1f, _localBounds.size.y - 8f * Q) : sub.height * Q;
                    // The brush is placed at the TOP; the plat rests lowered by height.
                    _restPos = transform.position - Vector3.up * h;
                    transform.position = _restPos;
                    _travel = Vector3.up * h;
                    _speed = float.IsNaN(speedQ) ? DefaultPlatSpeed : speedQ * Q;
                    _wait = float.IsNaN(sub.wait) ? DefaultPlatWait : sub.wait;
                    break;
                }
                case Kind.Rotating:
                {
                    // spawnflags 4 = X axis, 8 = Y axis (Quake Y = Unity Z), else Z (Unity Y).
                    if ((sub.spawnflags & 4) != 0) _axis = Vector3.right;
                    else if ((sub.spawnflags & 8) != 0) _axis = Vector3.forward;
                    else _axis = Vector3.up;
                    // Right-handed Quake rotation -> left-handed Unity: negate.
                    _rotSpeed = -(float.IsNaN(speedQ) ? DefaultRotateSpeed : speedQ);
                    break;
                }
                case Kind.Bobbing:
                {
                    _bobHeight = float.IsNaN(sub.height) ? DefaultBobHeight : sub.height * Q;
                    _bobPeriod = float.IsNaN(speedQ) ? DefaultBobPeriod : speedQ;
                    _bobPhase = sub.phase;
                    if ((sub.spawnflags & 1) != 0) _bobAxis = Vector3.right;
                    else if ((sub.spawnflags & 2) != 0) _bobAxis = Vector3.forward;
                    else _bobAxis = Vector3.up;
                    break;
                }
            }
        }

        /// World-space trigger box for doors/plats: bounds expanded by the Quake 60-unit field.
        public Bounds TriggerBounds
        {
            get
            {
                var b = new Bounds(_restPos + _localBounds.center, _localBounds.size);
                b.Expand(new Vector3(DoorTriggerMargin * 2f, DoorTriggerMargin, DoorTriggerMargin * 2f));
                if (MoverKind == Kind.Plat) b.Encapsulate(_restPos + _travel + _localBounds.center);
                return b;
            }
        }

        bool ActorNearby()
        {
            var b = TriggerBounds;
            foreach (var a in GameState.Actors)
            {
                if (a == null || a.IsDead) continue;
                Vector3 p = a.transform.position + Vector3.up * 0.9f;
                if (b.Contains(p)) return true;
            }
            return false;
        }

        void Update()
        {
            if (ArenaBootstrap.IsPaused) { LastDelta = Vector3.zero; return; }
            Step(Time.deltaTime);
        }

        /// Frame step as a plain method (testable). Doors/plats poll actor proximity.
        public void Step(float dt)
        {
            if (dt <= 0f || float.IsNaN(dt)) return;
            Vector3 before = transform.position;
            _time += dt;
            switch (MoverKind)
            {
                case Kind.Rotating:
                    _angle = Mathf.Repeat(_angle + _rotSpeed * dt, 360f);
                    transform.rotation = Quaternion.AngleAxis(_angle, _axis) * _restRot;
                    break;
                case Kind.Bobbing:
                {
                    float t = (_time / Mathf.Max(0.1f, _bobPeriod) + _bobPhase) * Mathf.PI * 2f;
                    transform.position = _restPos + _bobAxis * (Mathf.Sin(t) * _bobHeight);
                    break;
                }
                case Kind.Door:
                case Kind.Plat:
                    StepDoor(dt, ActorNearby());
                    break;
            }
            LastDelta = transform.position - before;
        }

        /// Door/plat state machine driven by an explicit "someone is near" flag.
        public void StepDoor(float dt, bool actorNear)
        {
            float travelLen = _travel.magnitude;
            float rate = travelLen > 0f ? _speed / travelLen : 1f;
            // Transitions triggered by presence happen before the movement step, so a
            // door starts moving in the same tick it is triggered.
            if (_state == State.Closed && actorNear) { _state = State.Opening; PlayMoveSound(); }
            else if (_state == State.Closing && actorNear) _state = State.Opening; // blocked/re-triggered: reopen
            switch (_state)
            {
                case State.Closed:
                    break;
                case State.Opening:
                    Progress = Mathf.Min(1f, Progress + rate * dt);
                    if (Progress >= 1f) { _state = State.Open; _waitTimer = _wait; }
                    break;
                case State.Open:
                    if (_wait < 0f) break;
                    if (actorNear) { _waitTimer = _wait; break; }
                    _waitTimer -= dt;
                    if (_waitTimer <= 0f) { _state = State.Closing; PlayMoveSound(); }
                    break;
                case State.Closing:
                    Progress = Mathf.Max(0f, Progress - rate * dt);
                    if (Progress <= 0f) _state = State.Closed;
                    break;
            }
            transform.position = _restPos + _travel * Progress;
        }

        /// Test hook: force the door fully open/closed without waiting.
        public void ForceProgress(float progress)
        {
            Progress = Mathf.Clamp01(progress);
            _state = Progress >= 1f ? State.Open : Progress <= 0f ? State.Closed : State.Opening;
            transform.position = _restPos + _travel * Progress;
        }

        void PlayMoveSound()
        {
            var clip = WeaponAudio.Load("Weapons/Plats_medplat1");
            if (clip == null) return;
            if (_audio == null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.spatialBlend = 1f;
                _audio.minDistance = 3f;
                _audio.maxDistance = 40f;
                _audio.rolloffMode = AudioRolloffMode.Linear;
                _audio.playOnAwake = false;
            }
            _audio.PlayOneShot(clip, 0.6f);
        }
    }
}
