using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// dev.18: light bloom for the arena camera (ROADMAP dev.18 item 3: "post-processing
    /// خفيف (bloom) مع وضع منخفض"). Quarter-resolution threshold → 5-tap separable blur →
    /// additive composite, all in Resources/Bloom.shader; roughly 1.5 full-screen passes
    /// of cost. Disabled entirely (no OnRenderImage work) when
    /// <see cref="GameSettings.BloomActive"/> is false (Low effects or the toggle off).
    /// Added by ArenaBootstrap to the player camera; the menu has none.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class MobileBloom : MonoBehaviour
    {
        public const string ShaderName = "MyXonotic/Bloom";
        public const float Threshold = 0.85f; // dev.18 run 1: 0.7 haloed every light panel into a blob
        public const float Intensity = 0.45f;
        public const int Downsample = 4;

        Material _mat;
        public bool Available => _mat != null;

        void Awake()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null || !shader.isSupported) { enabled = false; return; }
            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetFloat("_Threshold", Threshold);
            _mat.SetFloat("_Intensity", Intensity);
        }

        void OnDestroy() { if (_mat != null) Destroy(_mat); }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (_mat == null || !GameSettings.BloomActive) { Graphics.Blit(src, dst); return; }
            int w = Mathf.Max(1, src.width / Downsample), h = Mathf.Max(1, src.height / Downsample);
            var a = RenderTexture.GetTemporary(w, h, 0, src.format);
            var b = RenderTexture.GetTemporary(w, h, 0, src.format);
            a.filterMode = FilterMode.Bilinear; b.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, a, _mat, 0);
            Graphics.Blit(a, b, _mat, 1);
            Graphics.Blit(b, a, _mat, 2);
            _mat.SetTexture("_BloomTex", a);
            Graphics.Blit(src, dst, _mat, 3);
            RenderTexture.ReleaseTemporary(a);
            RenderTexture.ReleaseTemporary(b);
        }
    }
}
