using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Human-controlled actor: CharacterController based arena movement plus
    /// keyboard/mouse and independent multitouch input. Touch input tracks
    /// separate finger IDs for move / look / jump / fire / alt-fire / weapon-switch
    /// so several can act at once (e.g. move + look + fire together).
    /// Basic numeric targets reference Xonotic physicsX.cfg; advanced air control,
    /// ramp behavior and step sliding are NOT a faithful reimplementation yet.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Actor))]
    public sealed class Player : MonoBehaviour
    {
        public const float WalkSpeed = 360f / 32f;
        public const float Acceleration = 15f;
        public const float AirAcceleration = 2f;
        public const float GroundFriction = 6f;
        public const float StopSpeed = 100f / 32f;
        public const float JumpSpeed = 260f / 32f;
        public const float Gravity = -800f / 32f;
        public const float LookSensitivityMouse = 3.5f;
        public const float LookSensitivityTouch = 3.2f;

        public Camera ViewCamera;
        public WeaponController Weapons;
        public Actor Actor { get; private set; }
        public bool IsGrounded { get; private set; }

        /// Current horizontal+vertical velocity, exposed read-only for test drivers/HUD.
        public Vector3 Velocity => _velocity;

        /// Test-driver injection hook: when true, ReadInput uses TestMove/TestLook/Test*
        /// instead of touch/keyboard, so an Editor playtest or automated test can drive
        /// movement deterministically without simulating real input devices.
        public bool UseTestInput;
        public Vector2 TestMove;
        public Vector2 TestLook;
        public bool TestJump;
        public bool TestFirePrimary;
        public bool TestFireAlt;

        CharacterController _cc;
        Vector3 _velocity;
        Vector3 _externalImpulse;
        float _pitch;
        float _yaw;

        // Multitouch: each logical role owns its own finger id so move/look/jump/fire/
        // alt-fire/switch can all be driven by different fingers simultaneously.
        int _moveFingerId = -1;
        int _lookFingerId = -1;
        int _fireFingerId = -1;
        int _altFingerId = -1;
        int _jumpFingerId = -1;
        int _switchFingerId = -1;
        Vector2 _moveTouchStart;
        Vector2 _lookTouchLast;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            Actor = GetComponent<Actor>();
            _yaw = transform.eulerAngles.y;
        }

        void Start()
        {
            if (!ArenaBootstrap.TestMode && !Application.isMobilePlatform)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                ResetInputState();
            }
            else if (!ArenaBootstrap.TestMode && !Application.isMobilePlatform && !ArenaBootstrap.IsPaused)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) ResetInputState();
        }

        /// Clears all tracked input roles (finger IDs, held flags) so a stuck touch
        /// or a lost focus event can never leave an input stuck "on". Does NOT zero
        /// physical velocity by itself; velocity is only reset on death/respawn
        /// (see ResetForRespawn) so a focus flicker mid-jump does not feel like a snap.
        public void ResetInputState()
        {
            _moveFingerId = -1;
            _lookFingerId = -1;
            _fireFingerId = -1;
            _altFingerId = -1;
            _jumpFingerId = -1;
            _switchFingerId = -1;
        }

        /// Called by ArenaBootstrap/Actor on respawn: re-centers velocity and view yaw
        /// so the player does not inherit stale rotation/velocity at the new spawn.
        public void ResetForRespawn(float yaw)
        {
            _velocity = Vector3.zero;
            _externalImpulse = Vector3.zero;
            _pitch = 0f;
            ResetInputState();
            SetViewYaw(yaw);
        }

        public void SetViewYaw(float yaw)
        {
            _yaw = yaw;
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        void Update()
        {
            if (ArenaBootstrap.IsPaused) return;
            if (Actor != null && Actor.IsDead) return;

            ReadInput(out Vector2 moveInput, out Vector2 lookDelta, out bool jump,
                out bool firePrimary, out bool fireAlt, out bool switchNext);
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            moveInput = Vector2.ClampMagnitude(moveInput, 1f);

            _yaw += lookDelta.x;
            _pitch = Mathf.Clamp(_pitch - lookDelta.y, -85f, 85f);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (ViewCamera != null) ViewCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            IsGrounded = _cc.isGrounded;
            Vector3 wishDir = transform.forward * moveInput.y + transform.right * moveInput.x;
            wishDir.y = 0f;
            float wishSpeed = WalkSpeed * Mathf.Min(1f, wishDir.magnitude);
            if (wishDir.sqrMagnitude > 0f) wishDir.Normalize();

            var horizontal = new Vector3(_velocity.x, 0f, _velocity.z);
            if (IsGrounded)
            {
                if (!jump) horizontal = ArenaMath.ApplyGroundFriction(horizontal, GroundFriction, StopSpeed, dt);
                horizontal = ArenaMath.Accelerate(horizontal, wishDir, wishSpeed, Acceleration, dt);
            }
            else
            {
                horizontal = ArenaMath.Accelerate(horizontal, wishDir, wishSpeed, AirAcceleration, dt);
            }
            _velocity.x = horizontal.x;
            _velocity.z = horizontal.z;

            if (IsGrounded && _velocity.y < 0f) _velocity.y = -1f;
            _velocity.y += Gravity * dt;

            if (IsGrounded && jump) _velocity.y = JumpSpeed;

            var flags = _cc.Move((_velocity + _externalImpulse) * dt);
            if ((flags & CollisionFlags.Above) != 0 && _velocity.y > 0) _velocity.y = 0;
            IsGrounded = (flags & CollisionFlags.Below) != 0 || _cc.isGrounded;
            _externalImpulse = Vector3.Lerp(_externalImpulse, Vector3.zero, 6f * dt);
            if (transform.position.y < -200f) Actor.TakeDamage(10000, Vector3.zero, null);

            if (Weapons != null && ViewCamera != null)
            {
                Vector3 origin = ViewCamera.transform.position;
                Vector3 dir = ViewCamera.transform.forward;
                if (firePrimary) Weapons.TryFire(origin, dir, false);
                else if (fireAlt) Weapons.TryFire(origin, dir, true);
            }

            if (Weapons != null)
            {
                if (switchNext || Input.GetKeyDown(KeyCode.Q))
                {
                    int next = ((int)Weapons.Current + 1) % 3;
                    Weapons.SwitchTo((WeaponType)next);
                }
                if (Input.GetKeyDown(KeyCode.Alpha1)) Weapons.SwitchTo(WeaponType.Blaster);
                if (Input.GetKeyDown(KeyCode.Alpha2)) Weapons.SwitchTo(WeaponType.Rifle);
                if (Input.GetKeyDown(KeyCode.Alpha3)) Weapons.SwitchTo(WeaponType.Rocket);
            }
        }

        public void ApplyExternalImpulse(Vector3 impulse) => _externalImpulse += impulse;

        void ReadInput(out Vector2 move, out Vector2 look, out bool jump,
            out bool firePrimary, out bool fireAlt, out bool switchNext)
        {
            move = Vector2.zero;
            look = Vector2.zero;
            jump = false;
            firePrimary = false;
            fireAlt = false;
            switchNext = false;

            if (UseTestInput)
            {
                move = TestMove;
                look = TestLook;
                jump = TestJump;
                firePrimary = TestFirePrimary;
                fireAlt = TestFireAlt;
                return;
            }

            if (Application.isMobilePlatform || Input.touchCount > 0)
            {
                ReadTouch(out move, out look, out jump, out firePrimary, out fireAlt, out switchNext);
                return;
            }

            move = new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            if (Cursor.lockState == CursorLockMode.Locked)
                look = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * LookSensitivityMouse;
            jump = Input.GetKey(KeyCode.Space);
            firePrimary = Input.GetMouseButton(0) && Cursor.lockState == CursorLockMode.Locked;
            fireAlt = Input.GetMouseButton(1) && Cursor.lockState == CursorLockMode.Locked;
        }

        // Touch layout (normalized against Screen.safeArea so notches/cutouts do not
        // eat control zones): left half = move stick, right half = look; small
        // dedicated buttons bottom-right for fire/alt/jump/switch, each its own finger.
        void ReadTouch(out Vector2 move, out Vector2 look, out bool jump,
            out bool firePrimary, out bool fireAlt, out bool switchNext)
        {
            move = Vector2.zero;
            look = Vector2.zero;
            jump = false;
            firePrimary = false;
            fireAlt = false;
            switchNext = false;

            if (Input.touchCount == 0) { ResetInputState(); return; }
            Rect safe = TouchLayout.Safe;
            float halfW = safe.x + safe.width * 0.5f;
            Rect fireRect = TouchLayout.Fire;
            Rect altRect = TouchLayout.Alt;
            Rect jumpRect = TouchLayout.Jump;
            Rect switchRect = TouchLayout.Next;

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.phase != TouchPhase.Began) continue;
                Vector2 pos = t.position;
                if (!safe.Contains(pos) || TouchLayout.Pause.Contains(pos)) continue;

                if (fireRect.Contains(pos) && _fireFingerId == -1) { _fireFingerId = t.fingerId; continue; }
                if (altRect.Contains(pos) && _altFingerId == -1) { _altFingerId = t.fingerId; continue; }
                if (jumpRect.Contains(pos) && _jumpFingerId == -1) { _jumpFingerId = t.fingerId; continue; }
                if (switchRect.Contains(pos) && _switchFingerId == -1) { _switchFingerId = t.fingerId; switchNext = true; continue; }

                if (pos.x < halfW && _moveFingerId == -1)
                {
                    _moveFingerId = t.fingerId;
                    _moveTouchStart = pos;
                }
                else if (pos.x >= halfW && _lookFingerId == -1)
                {
                    _lookFingerId = t.fingerId;
                    _lookTouchLast = pos;
                }
            }

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                bool released = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;

                if (t.fingerId == _moveFingerId)
                {
                    if (released) { _moveFingerId = -1; continue; }
                    Vector2 d = t.position - _moveTouchStart;
                    move = Vector2.ClampMagnitude(d / Mathf.Max(1f, safe.height * 0.12f), 1f);
                }
                else if (t.fingerId == _lookFingerId)
                {
                    if (released) { _lookFingerId = -1; continue; }
                    Vector2 d = t.position - _lookTouchLast;
                    _lookTouchLast = t.position;
                    look = d * (LookSensitivityTouch * 0.02f) * (720f / Mathf.Max(1f, safe.height));
                }
                else if (t.fingerId == _fireFingerId)
                {
                    if (released) { _fireFingerId = -1; continue; }
                    firePrimary = true;
                }
                else if (t.fingerId == _altFingerId)
                {
                    if (released) { _altFingerId = -1; continue; }
                    fireAlt = true;
                }
                else if (t.fingerId == _jumpFingerId)
                {
                    if (released) { _jumpFingerId = -1; continue; }
                    jump = true;
                }
                else if (t.fingerId == _switchFingerId && released)
                {
                    _switchFingerId = -1;
                }
            }
        }
    }
}
