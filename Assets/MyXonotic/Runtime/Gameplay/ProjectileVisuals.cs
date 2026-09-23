using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// dev.17: projectile appearance. Until now every shot was a tinted sphere; this
    /// attaches the original Xonotic projectile model when the editor importer has
    /// generated one (Resources/Weapons/&lt;Weapon&gt;Projectile, see
    /// ProjectileModelImporter) and adds ParticleSystem smoke behind heavy shots.
    /// Falls back to the sphere when no model exists (Blaster/Electro/Crylink are
    /// glowing bolts in the original too).
    /// </summary>
    public static class ProjectileVisuals
    {
        /// Every projectile prefab the weapons gate generates (ProjectileModelImporter.Sources
        /// mirrors this list; Dev18Tests cross-checks). GameLoop reports how many load on device.
        public static readonly string[] AllModelResources =
        {
            "Weapons/DevastatorProjectile", "Weapons/MortarProjectile", "Weapons/MinelayerProjectile", "Weapons/HagarProjectile",
            "Weapons/BlasterProjectile", "Weapons/ElectroProjectile", "Weapons/ElectroBallProjectile", "Weapons/CrylinkProjectile",
        };

        /// Resource name of the imported model for a weapon / fire mode (null = keep the sphere).
        /// Table from Xonotic qcsrc/common/models/all.inc + client/weapons/projectile.qc:
        /// rocket.md3, grenademodel.md3, mine.md3, hagarmissile.mdl, laser.mdl (Blaster),
        /// elaser.mdl (Electro primary "beam"), ebomb.mdl (Electro ball), plasmatrail.mdl (Crylink).
        /// Fireball / Arc bolt have no model in the original (particles), Vortex/Rifle/Machinegun/Shotgun are hitscan.
        public static string ModelResource(WeaponType weapon, bool secondary = false)
        {
            switch (weapon)
            {
                case WeaponType.Devastator:
                case WeaponType.Mortar:
                case WeaponType.Minelayer:
                case WeaponType.Hagar:
                case WeaponType.Blaster:
                case WeaponType.Crylink:
                    return "Weapons/" + weapon + "Projectile";
                case WeaponType.Electro:
                    return secondary ? "Weapons/ElectroBallProjectile" : "Weapons/ElectroProjectile";
                default:
                    return null;
            }
        }

        /// projectile.qc `this.scale`: rocket 2, hagar 0.75, everything else 1.
        public static float ModelScale(WeaponType weapon) =>
            weapon == WeaponType.Devastator ? 2f : weapon == WeaponType.Hagar ? 0.75f : 1f;

        /// How many of the expected projectile prefabs are actually present (device evidence via GameLoop).
        public static int LoadedModelCount()
        {
            int n = 0;
            foreach (var r in AllModelResources) if (Resources.Load<GameObject>(r) != null) n++;
            return n;
        }

        /// Weapons whose projectile leaves a smoke trail in the original (rocket / hagar / mortar).
        public static bool HasSmoke(WeaponType weapon) =>
            weapon == WeaponType.Devastator || weapon == WeaponType.Hagar || weapon == WeaponType.Mortar;

        /// Instantiates the imported model under <paramref name="parent"/>. Model +X (Xonotic forward)
        /// is turned onto parent +Z so Projectile can simply LookRotation(velocity).
        public static bool TryAttachModel(Transform parent, WeaponType weapon, out GameObject model) => TryAttachModel(parent, weapon, false, out model);

        public static bool TryAttachModel(Transform parent, WeaponType weapon, bool secondary, out GameObject model)
        {
            model = null;
            string res = ModelResource(weapon, secondary);
            if (res == null) return false;
            var prefab = Resources.Load<GameObject>(res);
            if (prefab == null) return false;
            model = Object.Instantiate(prefab, parent);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            model.transform.localScale = Vector3.one * ModelScale(weapon);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return true;
        }

        /// World-space smoke puffs emitted per metre travelled; released (kept alive) on impact.
        public static ParticleSystem AddSmokeTrail(Transform parent, Color tint)
        {
            var go = new GameObject("Smoke");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                Color.Lerp(tint, new Color(0.75f, 0.75f, 0.75f), 0.6f), new Color(0.55f, 0.55f, 0.55f));
            main.maxParticles = 80;
            main.gravityModifier = -0.02f;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.rateOverDistance = 8f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.35f, 0.35f, 0.35f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f));
            ParticleFx.ConfigureRenderer(ps);
            ps.Play();
            return ps;
        }

        /// Detaches the smoke so it drifts after the projectile is destroyed, then self-destroys.
        public static void ReleaseSmoke(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.transform.SetParent(null, true);
            var em = ps.emission;
            em.enabled = false;
            var main = ps.main;
            main.stopAction = ParticleSystemStopAction.Destroy;
            ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            Object.Destroy(ps.gameObject, main.startLifetime.constantMax + 0.1f);
        }
    }

    /// <summary>Shared ParticleSystem helpers: sphere-mesh particles drawn with the shipped vertex-colour shader.</summary>
    public static class ParticleFx
    {
        static Material _material;

        /// VertexColor shader with _VertexWeight 1 so each particle's colour is used (ArenaMaterials pins it to 0).
        public static Material Material()
        {
            if (_material != null) return _material;
            var shader = Shader.Find("MyXonotic/VertexColor");
            if (shader == null) shader = Shader.Find("Standard");
            _material = new Material(shader) { name = "Particle vertex colour" };
            if (_material.HasProperty("_Color")) _material.color = Color.white;
            if (_material.HasProperty("_VertexWeight")) _material.SetFloat("_VertexWeight", 1f);
            return _material;
        }

        public static void ConfigureRenderer(ParticleSystem ps)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = ArenaPrimitives.SphereMesh;
            r.sharedMaterial = Material();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.alignment = ParticleSystemRenderSpace.World;
        }

        /// Expanding fireball + lingering smoke for an explosion of <paramref name="radius"/> metres.
        public static void Fireball(Vector3 point, Color tint, float radius)
        {
            var go = new GameObject("Fireball");
            go.transform.position = point;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // AddComponent auto-plays; duration is read-only while playing
            var main = ps.main;
            main.loop = false;
            main.duration = 0.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 1.5f, radius * 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.18f, radius * 0.35f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(tint, Color.white, 0.5f), Color.Lerp(tint, new Color(1f, 0.5f, 0.1f), 0.5f));
            main.gravityModifier = 0.15f;
            main.maxParticles = 48;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(Mathf.RoundToInt(radius * 8f), 12, 36)) });
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.05f, radius * 0.1f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.6f, 0.5f, 0.4f), 0.5f), new GradientColorKey(new Color(0.15f, 0.15f, 0.15f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));
            ConfigureRenderer(ps);
            ps.Play();
        }
    }
}
