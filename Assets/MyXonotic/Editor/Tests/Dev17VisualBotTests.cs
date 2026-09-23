using System;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// dev.17: projectile visuals + bot survival fixes from the dev.16 Test Lab run.
    ///  - ProjectileVisuals model/smoke tables
    ///  - ParticleFx material honours vertex colour
    ///  - Bot.NoProgress (position-based stuck) and Bot.SplashWouldHitSelf (wall raycast) are pure
    ///  - Actor.Suicides counts killer-less deaths and mirrors Frags--
    /// </summary>
    public static class Dev17VisualBotTests
    {
        public static void Run(Action<bool, string> check)
        {
            // ---------------- visuals ----------------
            check(ProjectileVisuals.ModelResource(WeaponType.Devastator) == "Weapons/DevastatorProjectile", "rocket model resource name");
            check(ProjectileVisuals.ModelResource(WeaponType.Blaster) == null && ProjectileVisuals.ModelResource(WeaponType.Electro) == null, "bolt weapons keep the glowing sphere");
            check(ProjectileVisuals.HasSmoke(WeaponType.Devastator) && ProjectileVisuals.HasSmoke(WeaponType.Hagar) && !ProjectileVisuals.HasSmoke(WeaponType.Crylink), "smoke only for rocket-class shots");
            foreach (var src in ProjectileModelImporter.Sources)
                check(ProjectileVisuals.ModelResource(src.Weapon) == "Weapons/" + src.Weapon + "Projectile", "importer source matches runtime lookup: " + src.Weapon);
            var mat = ParticleFx.Material();
            check(mat != null && (!mat.HasProperty("_VertexWeight") || Mathf.Approximately(mat.GetFloat("_VertexWeight"), 1f)), "particle material uses per-particle colour");
            check(Resources.Load<GameObject>("Weapons/DevastatorProjectile") != null, "rocket.md3 imported as Resources/Weapons/DevastatorProjectile (run weapons gate)");

            var host = new GameObject("smokeHost");
            try
            {
                var ps = ProjectileVisuals.AddSmokeTrail(host.transform, Color.red);
                check(ps != null && ps.emission.rateOverDistance.constant > 0f && ps.main.simulationSpace == ParticleSystemSimulationSpace.World, "smoke trail emits per distance in world space");
                var r = ps.GetComponent<ParticleSystemRenderer>();
                check(r.renderMode == ParticleSystemRenderMode.Mesh && r.mesh == ArenaPrimitives.SphereMesh, "particles are sphere meshes (no billboard shader dependency)");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
            bool fireballOk = true;
            try { ParticleFx.Fireball(Vector3.zero, Color.red, 2f); } catch (Exception) { fireballOk = false; }
            check(fireballOk, "fireball configures a fresh ParticleSystem without 'duration while playing' error");
            foreach (var fb in UnityEngine.Object.FindObjectsOfType<ParticleSystem>()) if (fb.name == "Fireball") UnityEngine.Object.DestroyImmediate(fb.gameObject);

            // ---------------- bot survival ----------------
            check(!Bot.NoProgress(Vector3.zero, Vector3.zero, Bot.ProgressWindow * 0.5f), "no verdict before the window elapses");
            check(Bot.NoProgress(Vector3.zero, new Vector3(0.2f, 3f, 0.1f), Bot.ProgressWindow), "vertical bobbing at a wall is no progress");
            check(!Bot.NoProgress(Vector3.zero, new Vector3(2f, 0f, 0f), Bot.ProgressWindow), "moving 2 m is progress");

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var target = new GameObject("target");
            try
            {
                wall.transform.position = new Vector3(0f, 0f, 2f);
                wall.transform.localScale = new Vector3(4f, 4f, 0.2f);
                Physics.SyncTransforms();
                check(Bot.SplashWouldHitSelf(Vector3.zero, Vector3.forward, 3f, target.transform), "wall 2 m ahead blocks a 3 m splash weapon");
                check(!Bot.SplashWouldHitSelf(Vector3.zero, Vector3.forward, 0.5f, target.transform), "wall 2 m ahead is safe for a 0.5 m splash");
                check(!Bot.SplashWouldHitSelf(Vector3.zero, Vector3.forward, 0f, target.transform), "hitscan never blocked");
                check(!Bot.SplashWouldHitSelf(Vector3.zero, Vector3.back, 3f, target.transform), "open air is safe");
                var wallCol = wall.GetComponent<Collider>();
                check(!Bot.SplashWouldHitSelf(Vector3.zero, Vector3.forward, 3f, wall.transform), "the target itself does not count as a wall");
                check(wallCol != null, "cube collider present");
            }
            finally { UnityEngine.Object.DestroyImmediate(wall); UnityEngine.Object.DestroyImmediate(target); }

            var victimGo = new GameObject("victim");
            try
            {
                var victim = victimGo.AddComponent<Actor>();
                int fragsBefore = victim.Frags;
                victim.TakeDamage(10000, Vector3.zero, null);
                check(victim.IsDead && victim.Suicides == 1 && victim.Frags == fragsBefore - 1, "killer-less death counts as suicide and costs a frag");
            }
            finally { UnityEngine.Object.DestroyImmediate(victimGo); }
        }
    }
}
