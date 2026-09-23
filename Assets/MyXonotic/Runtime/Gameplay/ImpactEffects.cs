using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Cheap procedural combat feedback built from the runtime primitives (no
    /// prefabs, no particle-system assets) so it stays light on phones:
    /// <list type="bullet">
    /// <item>impact: expanding tinted sphere + short point light + original impact sound;</item>
    /// <item>dev.11 explosions: additionally a flattened shockwave ring, a burst of
    /// ballistic debris chips and a camera shake scaled by distance;</item>
    /// <item>dev.11 muzzle flash: a brief tinted sphere + light parented to the
    /// weapon rig's <c>shot</c> joint;</item>
    /// <item>beams (Arc, hook): a short-lived tinted LineRenderer.</item>
    /// </list>
    /// </summary>
    public sealed class ImpactEffects : MonoBehaviour
    {
        float _life;
        float _age;
        float _maxScale;
        Vector3 _scaleAxis = Vector3.one;
        Light _light;
        float _lightIntensity;

        /// <summary>Debris chip counts / shake budget (tunable, tests read them).</summary>
        public const int ExplosionDebrisCount = 10;
        public const float ExplosionShakeRadius = 14f;

        /// <summary>Live effect objects (impacts, debris, flashes) for tests.</summary>
        public static int LiveCount { get; private set; }

        /// <summary>Camera shake requested this frame (Player reads and decays it).</summary>
        public static float PendingShake { get; private set; }
        public static float ConsumeShake() { float s = PendingShake; PendingShake = 0f; return s; }

        /// <summary>Short-lived tinted beam (Arc, hook rope flash) from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static void Beam(Vector3 from, Vector3 to, Color tint, float life, bool hitActor)
        {
            var go = new GameObject("Beam");
            go.transform.position = from;
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.startWidth = 0.06f;
            lr.endWidth = 0.03f;
            lr.sharedMaterial = ArenaMaterials.Get(hitActor ? Color.Lerp(tint, Color.white, 0.5f) : tint);
            lr.startColor = tint;
            lr.endColor = tint;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = tint;
            light.range = 3f;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;
            go.transform.position = to;
            Destroy(go, Mathf.Max(0.05f, life));
        }

        /// <summary>Brief flash parented to the muzzle joint of the first-person rig.</summary>
        public static void MuzzleFlash(Transform shotJoint, Color tint)
        {
            if (shotJoint == null) return;
            var go = new GameObject("MuzzleFlash");
            go.transform.SetParent(shotJoint, false);
            go.transform.localPosition = Vector3.zero;
            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = ArenaPrimitives.SphereMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ArenaMaterials.Get(Color.Lerp(tint, Color.white, 0.6f));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var fx = go.AddComponent<ImpactEffects>();
            fx._life = 0.07f;
            fx._maxScale = 0.16f;
            fx._scaleAxis = new Vector3(1f, 1f, 1.6f); // stretched along the barrel
            go.transform.localScale = Vector3.one * 0.04f;
            fx._light = go.AddComponent<Light>();
            fx._light.type = LightType.Point;
            fx._light.color = Color.Lerp(tint, Color.white, 0.4f);
            fx._light.range = 4f;
            fx._lightIntensity = 2.5f;
            fx._light.intensity = fx._lightIntensity;
            fx._light.shadows = LightShadows.None;
        }

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
                Shockwave(point, normal, def.Tint, splashRadius);
                if (Application.isPlaying && GameSettings.Effects != EffectsLevel.Low) ParticleFx.Fireball(point, def.Tint, splashRadius);
                Debris(point, normal, def.Tint, ExplosionDebrisCount, splashRadius);
                RequestShake(point, splashRadius);
            }
            else if (!hitActor)
            {
                Debris(point, normal, Color.Lerp(def.Tint, Color.gray, 0.5f), 3, 0.6f);
            }

            WeaponAudio.PlayAt(WeaponAudio.Impact(weapon), point, big ? 1f : 0.6f);
            if (hitActor) WeaponAudio.PlayAt(WeaponAudio.Misc("bodyimpact1"), point, 0.7f);
        }

        /// <summary>Flattened expanding disc along the surface for explosions.</summary>
        static void Shockwave(Vector3 point, Vector3 normal, Color tint, float radius)
        {
            var go = new GameObject("Shockwave");
            go.transform.position = point + normal * 0.05f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal.sqrMagnitude > 0.01f ? normal : Vector3.up);
            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = ArenaPrimitives.SphereMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ArenaMaterials.Get(Color.Lerp(tint, Color.white, 0.7f));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var fx = go.AddComponent<ImpactEffects>();
            fx._life = 0.3f;
            fx._maxScale = Mathf.Max(1.2f, radius * 1.6f);
            fx._scaleAxis = new Vector3(1f, 0.08f, 1f);
            go.transform.localScale = new Vector3(0.1f, 0.01f, 0.1f);
        }

        /// <summary>Small ballistic chips thrown out of the impact point.</summary>
        static void Debris(Vector3 point, Vector3 normal, Color tint, int count, float speedScale)
        {
            var mat = ArenaMaterials.Get(tint);
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Debris");
                go.transform.position = point + normal * 0.05f;
                go.transform.rotation = Random.rotation;
                go.transform.localScale = Vector3.one * Random.Range(0.04f, 0.09f);
                var mf = go.AddComponent<MeshFilter>();
                mf.mesh = ArenaPrimitives.CubeMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                var chip = go.AddComponent<DebrisChip>();
                Vector3 dir = (normal + Random.insideUnitSphere * 0.9f).normalized;
                chip.Velocity = dir * Random.Range(3f, 7f) * Mathf.Clamp(speedScale, 0.5f, 2f);
                chip.Spin = Random.insideUnitSphere * 720f;
                chip.Life = Random.Range(0.35f, 0.7f);
            }
        }

        static void RequestShake(Vector3 point, float radius)
        {
            var cam = Camera.main;
            if (cam == null) return;
            float d = Vector3.Distance(cam.transform.position, point);
            float reach = Mathf.Max(ExplosionShakeRadius, radius * 3f);
            if (d > reach) return;
            float amount = Mathf.Lerp(0.35f, 0.02f, d / reach);
            PendingShake = Mathf.Max(PendingShake, amount);
        }

        void OnEnable() { LiveCount++; }
        void OnDisable() { LiveCount--; }

        void Update()
        {
            if (ArenaBootstrap.IsPaused) return;
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _life);
            float s = Mathf.Lerp(0.05f, _maxScale, Mathf.Sqrt(t));
            transform.localScale = new Vector3(s * _scaleAxis.x, s * _scaleAxis.y, s * _scaleAxis.z);
            if (_light != null) _light.intensity = _lightIntensity * (1f - t);
            if (t >= 1f) Destroy(gameObject);
        }
    }

    /// <summary>One explosion chip: gravity, bounce-free flight, shrink-out.</summary>
    public sealed class DebrisChip : MonoBehaviour
    {
        public Vector3 Velocity;
        public Vector3 Spin;
        public float Life;
        float _age;
        Vector3 _scale;

        void Start() { _scale = transform.localScale; }

        void Update()
        {
            if (ArenaBootstrap.IsPaused) return;
            float dt = Time.deltaTime;
            _age += dt;
            Velocity += Physics.gravity * dt;
            transform.position += Velocity * dt;
            transform.Rotate(Spin * dt, Space.Self);
            float k = Mathf.Clamp01(_age / Life);
            transform.localScale = _scale * (1f - k * k);
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
