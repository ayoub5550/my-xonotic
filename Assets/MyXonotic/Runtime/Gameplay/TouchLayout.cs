using UnityEngine;

namespace MyXonotic
{
    /// <summary>Single screen-pixel layout shared by touch input and the visible HUD.</summary>
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

        public static Rect Fire => Button(1.8f, 0f, 1.8f);
        public static Rect Alt => Button(3.8f, 0f, 1.6f);
        public static Rect Jump => Button(1.8f, 2f, 1.6f);
        public static Rect Next => Button(3.8f, 2f, 1.6f);
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

        static Rect Button(float left, float bottom, float size)
        {
            var s = Safe;
            float unit = s.height * 0.11f;
            return new Rect(s.xMax - unit * left, s.y + s.height * 0.03f + unit * bottom,
                unit * size, unit * size);
        }
    }
}
