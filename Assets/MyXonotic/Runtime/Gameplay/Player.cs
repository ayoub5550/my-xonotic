using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Human-controlled actor: CharacterController based arena movement plus
    /// keyboard/mouse and independent multitouch input. Touch input tracks a
    /// separate finger id per logical role (move / look / fire / alt-fire / jump /
    /// weapon switch) so several can act at once (e.g. move + look + fire together).
    /// The FIRE (and ALT) finger can also aim: if no dedicated look finger is down,
    /// dragging the finger that is holding FIRE turns the camera, matching the
    /// owner's LibreQuake touch reference. Basic numeric movement targets reference
    /// Xonotic physicsX.cfg; advanced air control, ramp behavior and step sliding
    /// are NOT a faithful reimplementation yet (movement constants unchanged).
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

        public const float DefaultFieldOfView = 85f;
        public const float ZoomFieldOfView = 30f;
        public const float ZoomLookScale = 0.4f;

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

        // ---- Touch visualization state, read-only for Hud.cs -------------------
        // Hud never owns input: it only mirrors these flags/positions to draw the
        // dynamic joystick and button highlight state. All hit-testing/finger
        // tracking happens here.
        public bool TouchJoystickActive => _moveFingerId != -1;
        public Vector2 TouchJoystickOrigin => _moveTouchStart;
        /// Knob offset from the joystick origin, already clamped to the visual max
        /// radius (TouchLayout.JoystickMaxRadius) — draw the knob at Origin+this.
        public Vector2 TouchJoystickKnobOffset => _moveKnobOffset;
        public bool TouchFireHeld => _fireFingerId != -1;
        public bool TouchAltHeld => _altFingerId != -1;
        public bool TouchJumpHeld => _jumpFingerId != -1;
        public bool TouchWpnPlusHeld => _wpnPlusFingerId != -1;
        public bool TouchWpnMinusHeld => _wpnMinusFingerId != -1;
        /// True while the FIRE finger is also driving the look/aim delta because no
        /// dedicated look finger is currently down (used by Hud only for optional
        /// diagnostics — the requirement is "no visible AIM disc" so Hud must not
        /// render anything extra for this, it is exposed for completeness/tests).
        public bool TouchFireIsAiming => _fireFingerId != -1 && _lookFingerId == -1;

        CharacterController _cc;
        Vector3 _velocity;
        Vector3 _externalImpulse;
        float _pitch;
        float _yaw;

        // Multitouch: each logical role owns its own finger id so move/look/fire/
        // jump/weapon-switch can all be driven by different fingers simultaneously.
        // The FIRE finger is additionally allowed to double as the look finger
        // (see ReadTouch) so "aim while firing" works with a single thumb too.
        int _moveFingerId = -1;
        int _lookFingerId = -1;
        int _fireFingerId = -1;
        int _altFingerId = -1;
        int _jumpFingerId = -1;
        int _wpnPlusFingerId = -1;
        int _wpnMinusFingerId = -1;
        Vector2 _moveTouchStart;
        Vector2 _moveKnobOffset;

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

        /// Clears all tracked input roles (finger IDs, held flags, joystick visual
        /// state) so a stuck touch or a lost focus/pause event can never leave an
        /// input stuck "on" or a joystick knob frozen off-center. Does NOT zero
        /// physical velocity by itself; velocity is only reset on death/respawn
        /// (see ResetForRespawn) so a focus flicker mid-jump does not feel like a snap.
        public void ResetInputState()
        {
            _moveFingerId = -1;
            _lookFingerId = -1;
            _fireFingerId = -1;
            _altFingerId = -1;
            _jumpFingerId = -1;
            _wpnPlusFingerId = -1;
            _wpnMinusFingerId = -1;
            _moveKnobOffset = Vector2.zero;
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
                out bool firePrimary, out bool fireAlt, out bool switchNext, out bool switchPrev);
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            moveInput = Vector2.ClampMagnitude(moveInput, 1f);

            if (Weapons != null && Weapons.IsZooming) lookDelta *= ZoomLookScale;
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
                Weapons.SetSecondaryHeld(fireAlt);
                if (firePrimary) Weapons.TryFire(origin, dir, false);
                else if (fireAlt) Weapons.TryFire(origin, dir, true);

                // Vortex zoom: narrow the FOV while the secondary is held.
                float targetFov = Weapons.IsZooming ? ZoomFieldOfView : DefaultFieldOfView;
                ViewCamera.fieldOfView = Mathf.Lerp(ViewCamera.fieldOfView, targetFov, 1f - Mathf.Exp(-14f * dt));
            }

            if (Weapons != null)
            {
                if (switchNext || Input.GetKeyDown(KeyCode.Q) || Input.mouseScrollDelta.y < 0f) Weapons.SwitchCycle(+1);
                if (switchPrev || Input.GetKeyDown(KeyCode.E) || Input.mouseScrollDelta.y > 0f) Weapons.SwitchCycle(-1);
                if (_tappedWeaponSlot >= 0)
                {
                    Weapons.SwitchTo((WeaponType)_tappedWeaponSlot);
                    _tappedWeaponSlot = -1;
                }
                for (int i = 0; i < WeaponController.WeaponCount; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Weapons.SwitchTo((WeaponType)i);
            }
        }

        int _tappedWeaponSlot = -1;

        public void ApplyExternalImpulse(Vector3 impulse) => _externalImpulse += impulse;

        /// Trigger hook (e.g. jump pads): sets velocity directly — NOT additive
        /// with current velocity — and clears any residual external impulse, so
        /// the launch is authoritative and never blends with leftover knockback
        /// drift or a stale in-flight push. Movement constants are untouched;
        /// this only writes the physics state the trigger wants next frame.
        public void Launch(Vector3 velocity)
        {
            _velocity = velocity;
            _externalImpulse = Vector3.zero;
        }

        void ReadInput(out Vector2 move, out Vector2 look, out bool jump,
            out bool firePrimary, out bool fireAlt, out bool switchNext, out bool switchPrev)
        {
            move = Vector2.zero;
            look = Vector2.zero;
            jump = false;
            firePrimary = false;
            fireAlt = false;
            switchNext = false;
            switchPrev = false;

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
                ReadTouch(out move, out look, out jump, out firePrimary, out fireAlt, out switchNext, out switchPrev);
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

        // Touch layout (normalized against Screen.safeArea via TouchLayout, shared
        // with Hud.cs, so notches/cutouts never eat a control zone):
        //  - Left half of the safe area: a dynamic joystick appears wherever the
        //    thumb first lands (TouchLayout.MoveZone) and drives move; a radial
        //    dead zone (TouchLayout.JoystickDeadZone) absorbs jitter right at the
        //    touch-down point, ramping linearly to full magnitude at MaxRadius.
        //  - Right half (not otherwise claimed): drag-to-look, no visible disc.
        //  - FIRE / JUMP / WPN- / WPN+: small dedicated buttons, each its own
        //    finger id. FIRE additionally doubles as a look finger when no
        //    dedicated look finger is down, so a single thumb can fire-and-aim.
        // All roles are tracked by finger id independently, so move + look + fire
        // (+ jump, + weapon switch) can all be active from different fingers at once.
        void ReadTouch(out Vector2 move, out Vector2 look, out bool jump,
            out bool firePrimary, out bool fireAlt, out bool switchNext, out bool switchPrev)
        {
            move = Vector2.zero;
            look = Vector2.zero;
            jump = false;
            firePrimary = false;
            fireAlt = false;
            switchNext = false;
            switchPrev = false;

            if (Input.touchCount == 0) { ResetInputState(); return; }

            Rect safe = TouchLayout.Safe;
            Rect moveZone = TouchLayout.MoveZone;
            Rect fireRect = TouchLayout.Fire;
            Rect altRect = TouchLayout.Alt;
            Rect jumpRect = TouchLayout.Jump;
            Rect wpnPlusRect = TouchLayout.WpnPlus;
            Rect wpnMinusRect = TouchLayout.WpnMinus;
            Rect pauseRect = TouchLayout.Pause;

            // Pass 1: claim a role for every finger that just went down.
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.phase != TouchPhase.Began) continue;
                Vector2 pos = t.position;
                if (!safe.Contains(pos) || pauseRect.Contains(pos)) continue;

                int slot = TouchLayout.WeaponSlotAt(pos);
                if (slot >= 0) { _tappedWeaponSlot = slot; continue; }
                if (TouchLayout.WeaponBar.Contains(pos)) continue;

                if (fireRect.Contains(pos) && _fireFingerId == -1) { _fireFingerId = t.fingerId; continue; }
                if (altRect.Contains(pos) && _altFingerId == -1) { _altFingerId = t.fingerId; continue; }
                if (jumpRect.Contains(pos) && _jumpFingerId == -1) { _jumpFingerId = t.fingerId; continue; }
                if (wpnPlusRect.Contains(pos) && _wpnPlusFingerId == -1)
                {
                    _wpnPlusFingerId = t.fingerId;
                    switchNext = true;
                    continue;
                }
                if (wpnMinusRect.Contains(pos) && _wpnMinusFingerId == -1)
                {
                    _wpnMinusFingerId = t.fingerId;
                    switchPrev = true;
                    continue;
                }

                if (moveZone.Contains(pos) && _moveFingerId == -1)
                {
                    _moveFingerId = t.fingerId;
                    _moveTouchStart = pos;
                    _moveKnobOffset = Vector2.zero;
                }
                // A second finger inside the move zone (while it is already
                // claimed) must NOT fall through to look: look is only claimable
                // outside the move zone, otherwise a stray/extra left-side finger
                // would start turning the camera instead of being ignored.
                else if (!moveZone.Contains(pos) && _lookFingerId == -1)
                {
                    _lookFingerId = t.fingerId;
                }
            }

            float deadZone = TouchLayout.JoystickDeadZone;
            float maxRadius = TouchLayout.JoystickMaxRadius;
            float lookScale = (LookSensitivityTouch * 0.02f) * (720f / Mathf.Max(1f, safe.height));

            // Pass 2: drive the frame's output from whichever fingers are still down.
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch t = Input.GetTouch(i);
                bool released = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;

                if (t.fingerId == _moveFingerId)
                {
                    if (released) { _moveFingerId = -1; _moveKnobOffset = Vector2.zero; }
                    else
                    {
                        Vector2 raw = t.position - _moveTouchStart;
                        _moveKnobOffset = Vector2.ClampMagnitude(raw, maxRadius);
                        float dist = raw.magnitude;
                        if (dist > deadZone)
                        {
                            float scaled = Mathf.Clamp01((dist - deadZone) / Mathf.Max(1f, maxRadius - deadZone));
                            move = raw.normalized * scaled;
                        }
                    }
                }

                if (t.fingerId == _lookFingerId)
                {
                    if (released) _lookFingerId = -1;
                    else look += new Vector2(t.deltaPosition.x, t.deltaPosition.y) * lookScale;
                }

                if (t.fingerId == _fireFingerId)
                {
                    if (released) { _fireFingerId = -1; }
                    else
                    {
                        firePrimary = true;
                        // Aim with the FIRE finger only while no dedicated look
                        // finger is active (checked fresh each frame, so a second
                        // finger landing on the right side seamlessly takes over).
                        if (_lookFingerId == -1)
                            look += new Vector2(t.deltaPosition.x, t.deltaPosition.y) * lookScale;
                    }
                }

                if (t.fingerId == _altFingerId)
                {
                    if (released) { _altFingerId = -1; }
                    else
                    {
                        fireAlt = true;
                        if (_lookFingerId == -1)
                            look += new Vector2(t.deltaPosition.x, t.deltaPosition.y) * lookScale;
                    }
                }

                if (t.fingerId == _jumpFingerId)
                {
                    if (released) _jumpFingerId = -1;
                    else jump = true;
                }

                if (released && t.fingerId == _wpnPlusFingerId) _wpnPlusFingerId = -1;
                if (released && t.fingerId == _wpnMinusFingerId) _wpnMinusFingerId = -1;
            }
        }
    }
}
