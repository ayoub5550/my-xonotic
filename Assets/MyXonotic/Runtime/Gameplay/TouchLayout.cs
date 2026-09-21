using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Single screen-pixel layout shared by touch input (Player.cs) and the visible
    /// HUD (Hud.cs). Style/colors deliberately match the owner's LibreQuake touch
    /// reference (Assets/LQ/Scripts/UI/TouchControls.cs in my-librequake): a
    /// dynamic left joystick that appears where the thumb lands, a large
    /// translucent red FIRE, a smaller blue JUMP below-left of it, pale WPN-/WPN+
    /// stacked above the fire/jump cluster, a small plain ALT utility circle
    /// (kept for Xonotic secondary-fire — no LibreQuake equivalent, so it is
    /// deliberately understated), and PAUSE (colored brown by Hud, per brand)
    /// in the top-right corner. Every size/position derives from the safe area
    /// so notches/cutouts and different aspect ratios never eat a control zone.
    /// </summary>
    public static class TouchLayout
    {
        public static Rect Safe
        {
            get
            {
                var safe = Screen.safeArea;
                return safe.width > 0 && safe.height > 0
                    ? safe : new Rect(0, 0, Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
            }
        }

        /// One scale unit in pixels, derived from the safe-area height so every
        /// control keeps the same relative size across phones/tablets/orientations.
        public static float Unit => Safe.height * 0.11f;

        /// Left half of the safe area: a touch that begins here spawns the dynamic
        /// joystick at that exact point (it is not pinned to a fixed base image).
        public static Rect MoveZone
        {
            get { var s = Safe; return new Rect(s.x, s.y, s.width * 0.5f, s.height); }
        }

        // Joystick feel: a small radial dead zone kills thumb jitter/drift near the
        // touch-down point, then the analog value ramps linearly out to MaxRadius
        // (matching the knob's visual travel limit, so what you see is what you get).
        public static float JoystickDeadZone => Unit * 0.16f;
        public static float JoystickMaxRadius => Unit * 1.1f;
        public static float JoystickBaseRadius => Unit * 1.1f;
        public static float JoystickKnobRadius => Unit * 0.55f;

        // Center offset (from the bottom-right safe corner) and diameter, each as a
        // fraction of safe.height. These are the owner's exact LibreQuake reference
        // pixel values (my-librequake TouchControls.cs, 1280x720 reference canvas)
        // divided by 720, so FIRE/JUMP/WPN- /WPN+ reproduce that screenshot's
        // proportions exactly rather than an approximated scale.
        public static Rect Fire => Circle(170f / 720f, 170f / 720f, 190f / 720f);
        public static Rect Jump => Circle(340f / 720f, 70f / 720f, 110f / 720f);
        public static Rect WpnPlus => Circle(70f / 720f, 345f / 720f, 100f / 720f);
        public static Rect WpnMinus => Circle(70f / 720f, 455f / 720f, 100f / 720f);

        /// Small utility circle for alt-fire — kept (Xonotic secondary-fire modes
        /// are load-bearing gameplay, not decoration), deliberately smaller/plainer
        /// than FIRE, and placed clear of the FIRE/JUMP/WPN cluster above JUMP so it
        /// does not compete with the LibreQuake-reference layout.
        public static Rect Alt => Circle(340f / 720f, 185f / 720f, 90f / 720f);

        public static Rect Pause
        {
            get
            {
                var s = Safe;
                return new Rect(s.xMax - s.height * 0.2f, s.yMax - s.height * 0.12f,
                    s.height * 0.18f, s.height * 0.09f);
            }
        }

        public static Rect Restart
        {
            get
            {
                var s = Safe;
                return new Rect(s.center.x - s.height * 0.25f, s.center.y - s.height * 0.18f,
                    s.height * 0.5f, s.height * 0.12f);
            }
        }

        /// <summary>
        /// Bounding box of a round button, given as (left, bottom) offsets — each a
        /// fraction of safe.height — from the bottom-right safe-area corner to the
        /// button CENTER, and a diameter (also a fraction of safe.height). Using
        /// height as the common unit for both axes keeps circles circular and
        /// proportions stable across aspect ratios. Rect.Contains is used for hit
        /// testing (a generous square hit box under a round visual is intentional —
        /// more forgiving for a thumb than an exact circle test) and for drawing.
        /// </summary>
        static Rect Circle(float leftFrac, float bottomFrac, float diameterFrac)
        {
            var s = Safe;
            float d = diameterFrac * s.height;
            float cx = s.xMax - leftFrac * s.height;
            float cy = s.y + bottomFrac * s.height;
            return new Rect(cx - d * 0.5f, cy - d * 0.5f, d, d);
        }
    }
}
