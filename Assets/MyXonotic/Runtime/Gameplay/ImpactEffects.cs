using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Cheap procedural impact feedback: an expanding tinted sphere plus a short
    /// point light, and the weapon's original impact sound. Built from the same
    /// runtime primitives as the rest of the slice (no prefabs, no particle
    /// system assets) so it stays lightweight on phones.
    /// </summary>
    public sealed class ImpactEffects : MonoBehaviour
    {
        float _life;
        float _age;
        float _maxScale;
        Light _light;
        float _lightIntensity;

        public static void Spawn(Vector3 point, Vector3 normal, WeaponType weapon, bool hitActor, float splashRadius = 0f)
        {
            var def = WeaponController.GetDef(weapon);
            bool big = splashRadius > 0f;
            var go = new GameObject("Impact_" + weapon);
            go.transform.position = point + normal * 0.02f;

            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = ArenaPrimitives.SphereMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ArenaMaterials.Get(big ? Color.Lerp(def.Tint, Color.white, 0.4f) : def.Tint);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var fx = go.AddComponent<ImpactEffects>();
            fx._life = big ? 0.35f : 0.12f;
            fx._maxScale = big ? Mathf.Max(0.8f, splashRadius * 0.9f) : 0.25f;
            go.transform.localScale = Vector3.one * 0.05f;

            if (big)
            {
                fx._light = go.AddComponent<Light>();
                fx._light.type = LightType.Point;
                fx._light.color = Color.Lerp(def.Tint, Color.white, 0.3f);
                fx._light.range = splashRadius * 2.5f;
                fx._lightIntensity = 4f;
                fx._light.intensity = fx._lightIntensity;
                fx._light.shadows = LightShadows.None;
            }

            WeaponAudio.PlayAt(WeaponAudio.Impact(weapon), point, big ? 1f : 0.6f);
            if (hitActor) WeaponAudio.PlayAt(WeaponAudio.Misc("bodyimpact1"), point, 0.7f);
        }

        void Update()
        {
            if (ArenaBootstrap.IsPaused) return;
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _life);
            transform.localScale = Vector3.one * Mathf.Lerp(0.05f, _maxScale, Mathf.Sqrt(t));
            if (_light != null) _light.intensity = _lightIntensity * (1f - t);
            if (t >= 1f) Destroy(gameObject);
        }
    }
}
