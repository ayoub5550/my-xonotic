using System;
using System.IO;
using MyXonotic.Content.Mdl;
using MyXonotic.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// dev.18 (ROADMAP items from the owner's Poco F3 test + Test Lab dev.17):
    ///  1. projectiles: every Xonotic projectile model (incl. the MDL hagarmissile) imports; runtime table covers Blaster/Electro/Crylink/Hagar; original scales.
    ///  2. settings page: four tabs, sliders/toggles/choices with positive rects, immediate persistence through the settings classes.
    ///  3. graphics: glow layer / tcMod scroll / blend + additive shader selection; bloom shader compiles; effects levels.
    ///  4. bot difficulty: default 3/2/1, slider range, aim.qc skill formulas (fire tolerance, bad-aim magnitude, think cadence), low skill misses more.
    ///  5. bot survival: SafeStep rejects positions off the NavMesh / inside trigger_hurt; suicide causes are reported.
    /// </summary>
    public static class Dev18Tests
    {
        public static void Run(Action<bool, string> check)
        {
            Projectiles(check);
            Settings(check);
            Graphics(check);
            BotDifficulty(check);
            BotSurvival(check);
        }

        // ------------------------------------------------------------ 1 projectiles

        static void Projectiles(Action<bool, string> check)
        {
            check(ProjectileVisuals.ModelResource(WeaponType.Blaster, false) == "Weapons/BlasterProjectile", "blaster uses laser.mdl model");
            check(ProjectileVisuals.ModelResource(WeaponType.Electro, false) == "Weapons/ElectroProjectile" && ProjectileVisuals.ModelResource(WeaponType.Electro, true) == "Weapons/ElectroBallProjectile", "electro primary=elaser, secondary=ebomb");
            check(ProjectileVisuals.ModelResource(WeaponType.Crylink, false) == "Weapons/CrylinkProjectile", "crylink uses plasmatrail model");
            check(ProjectileVisuals.ModelResource(WeaponType.Hagar, false) == "Weapons/HagarProjectile", "hagar uses its own model");
            check(ProjectileVisuals.ModelResource(WeaponType.Vortex, false) == null && ProjectileVisuals.ModelResource(WeaponType.Fireball, false) == null, "hitscan / particle weapons have no model");
            check(Mathf.Approximately(ProjectileVisuals.ModelScale(WeaponType.Devastator), 2f) && Mathf.Approximately(ProjectileVisuals.ModelScale(WeaponType.Hagar), 0.75f) && Mathf.Approximately(ProjectileVisuals.ModelScale(WeaponType.Mortar), 1f), "original projectile.qc scales: rocket 2, hagar 0.75, others 1");
            foreach (var src in ProjectileModelImporter.Sources)
                check(!src.StandIn, "no stand-in projectile model left: " + src.Weapon);
            int expectedPrefabs = ProjectileModelImporter.Sources.Length;
            check(ProjectileVisuals.LoadedModelCount() == expectedPrefabs, "all " + expectedPrefabs + " projectile prefabs load from Resources (run weapons gate) — got " + ProjectileVisuals.LoadedModelCount());

            // MDL reader on the real pack file.
            var resolver = new XonoticContentResolver();
            string mdl = resolver.FindFile("models/hagarmissile.mdl");
            check(mdl != null, "hagarmissile.mdl present in content roots");
            if (mdl != null)
            {
                var bytes = File.ReadAllBytes(mdl);
                check(MdlReader.IsMdl(bytes) && !MyXonotic.Content.Md3.Md3Reader.IsMd3(bytes), "hagarmissile.mdl is IDPO (true Quake MDL)");
                var model = MdlReader.Read(bytes, mdl, "test");
                var surf = model.Model.Surfaces[0];
                check(surf.Triangles.Length == 680 * 3 && surf.Positions.Length >= 1120, "hagarmissile: 680 triangles, >= 1120 split vertices (" + surf.Positions.Length + ")");
                check(model.Skin != null && model.Skin.Width == 172 && model.Skin.Height == 84 && model.Skin.Rgba.Length == 172 * 84 * 4, "hagarmissile skin 172x84 expanded to RGBA");
                float maxAbs = 0f;
                foreach (var p in surf.Positions) maxAbs = Mathf.Max(maxAbs, Mathf.Abs(p.X), Mathf.Abs(p.Y), Mathf.Abs(p.Z));
                check(maxAbs > 0.05f && maxAbs < 1.5f, "hagarmissile fits a plausible metre box (max |coord| " + maxAbs.ToString("0.00") + " m)");
                bool uvOk = true;
                foreach (var uv in surf.TexCoords) if (uv.X < -0.01f || uv.X > 1.01f || uv.Y < -0.01f || uv.Y > 1.01f) uvOk = false;
                check(uvOk, "hagarmissile UVs within 0..1");
            }
            string elaser = resolver.FindFile("models/elaser.mdl");
            check(elaser != null && MyXonotic.Content.Md3.Md3Reader.IsMd3(File.ReadAllBytes(elaser)), "elaser.mdl is MD3 despite the extension (dispatch by magic)");
        }

        // --------------------------------------------------------------- 2 settings

        static void Settings(Action<bool, string> check)
        {
            GameSettings.OverrideForTest(EffectsLevel.Medium, true, 60, 1f, GameSettings.DefaultMusicVolume, 1f);
            check(GameSettings.BloomActive, "bloom active on MEDIUM with toggle on");
            GameSettings.OverrideForTest(EffectsLevel.Low, true, 60, 1f, 0.35f, 1f);
            check(!GameSettings.BloomActive, "LOW effects disables bloom regardless of the toggle");
            GameSettings.OverrideForTest(EffectsLevel.High, false, 30, 0.5f, 0.2f, 0.7f);
            check(!GameSettings.BloomActive && GameSettings.TargetFps == 30 && Mathf.Approximately(GameSettings.MasterVolume, 0.5f), "settings override round-trips fps/volumes");
            GameSettings.OverrideForTest(EffectsLevel.Medium, true, 60, 1f, GameSettings.DefaultMusicVolume, 1f);

            MatchSettings.OverrideForTest(GameMode.Deathmatch, false, 3);
            check(MatchSettings.BotSkill == MatchSettings.DefaultBotSkill && MatchSettings.DefaultBotSkill == 3, "default bot skill 3");
            check(MatchSettings.BotSkillFor(0) == 3 && MatchSettings.BotSkillFor(1) == 2 && MatchSettings.BotSkillFor(2) == 1 && MatchSettings.BotSkillFor(3) == 3, "bots get skill 3 / 2 / 1 (cycling)");
            MatchSettings.OverrideBotSkillForTest(1);
            check(MatchSettings.BotSkillFor(1) == 1 && MatchSettings.BotSkillFor(2) == 1, "skill never below 1");
            MatchSettings.OverrideBotSkillForTest(10);
            check(MatchSettings.BotSkillFor(0) == 10 && MatchSettings.BotSkillFor(2) == 8, "skill 10 → 10 / 9 / 8");
            MatchSettings.OverrideBotSkillForTest(MatchSettings.DefaultBotSkill);
            check(MatchSettings.BotSkillName(1) == "EASY" && MatchSettings.BotSkillName(5) == "MEDIUM" && MatchSettings.BotSkillName(8) == "HARD" && MatchSettings.BotSkillName(10) == "NIGHTMARE", "skill names");

            // The page itself: built headlessly, every tab laid out.
            var host = new GameObject("SettingsHost", typeof(RectTransform), typeof(Canvas));
            try
            {
                host.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var menuGo = new GameObject("MainMenu", typeof(MainMenu));
                var menu = menuGo.GetComponent<MainMenu>();
                typeof(MainMenu).GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(menu, null);
                menu.ShowScreen(MenuScreen.Settings);
                var page = menu.Settings;
                check(page != null, "settings page exists");
                if (page != null)
                {
                    foreach (SettingsTab tab in Enum.GetValues(typeof(SettingsTab)))
                    {
                        page.Show(tab);
                        Canvas.ForceUpdateCanvases();
                        foreach (var rt in menuGo.GetComponentsInChildren<RectTransform>(false)) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                        int graphics = 0, bad = 0;
                        foreach (var g in menuGo.GetComponentsInChildren<Graphic>(false)) { graphics++; var r = g.rectTransform.rect; if (r.width <= 0f || r.height <= 0f) bad++; }
                        check(graphics > 5 && bad == 0, "settings tab " + tab + " lays out " + graphics + " graphics with positive rects (" + bad + " bad)");
                        foreach (var b in menuGo.GetComponentsInChildren<Button>(false))
                            check(b.GetComponent<RectTransform>().rect.height >= 44f, "settings tab " + tab + " button '" + b.name + "' thumb-sized");
                        foreach (var s in menuGo.GetComponentsInChildren<Slider>(false))
                            check(s.handleRect != null && s.handleRect.rect.width >= 40f && s.fillRect != null, "settings tab " + tab + " slider '" + s.name + "' has a fat handle and a fill");
                    }
                    page.Show(SettingsTab.Video);
                    check(menuGo.transform.Find("MenuCanvas/SafeArea/Settings/SettingsPanel/Video/EffectsOpt0") != null, "VIDEO tab has the effects choice");
                    check(menuGo.transform.Find("MenuCanvas/SafeArea/Settings/SettingsPanel/Audio/MasterSlider") != null, "AUDIO tab has the master slider");
                    check(menuGo.transform.Find("MenuCanvas/SafeArea/Settings/SettingsPanel/Controls/Rows/SensitivitySlider") != null, "CONTROLS tab has the sensitivity slider");
                    check(menuGo.transform.Find("MenuCanvas/SafeArea/Settings/SettingsPanel/Controls/SensitivityPad") != null && menuGo.GetComponentInChildren<SensitivityPreview>(true) != null, "CONTROLS tab has the sensitivity preview pad");
                    check(menuGo.transform.Find("MenuCanvas/SafeArea/Settings/SettingsPanel/Game/BotSkillSlider") != null, "GAME tab has the bot difficulty slider");
                    check(menuGo.transform.Find("MenuCanvas/SafeArea/Settings/SettingsPanel/Game/DevPanel/ShareLog") != null, "GAME tab keeps SHARE LOG");
                    var skill = menuGo.transform.Find("MenuCanvas/SafeArea/Settings/SettingsPanel/Game/BotSkillSlider").GetComponent<Slider>();
                    check(Mathf.Approximately(skill.minValue, 1f) && Mathf.Approximately(skill.maxValue, 10f) && skill.wholeNumbers, "bot difficulty slider is 1..10 whole numbers");
                    skill.value = 7f;
                    check(MatchSettings.BotSkill == 7 && MatchSettings.BotSkillFor(2) == 5, "moving the slider writes MatchSettings immediately (7 → 7/6/5)");
                    MatchSettings.OverrideBotSkillForTest(MatchSettings.DefaultBotSkill);
                }
                UnityEngine.Object.DestroyImmediate(menuGo);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        // --------------------------------------------------------------- 3 graphics

        static void Graphics(Action<bool, string> check)
        {
            foreach (var name in new[] { "MyXonotic/Lightmapped", "MyXonotic/LightmappedBlend", "MyXonotic/LightmappedAdd", "MyXonotic/Bloom" })
            {
                var sh = Shader.Find(name);
                check(sh != null && sh.isSupported, "shader " + name + " present and supported");
            }
            var lm = Shader.Find("MyXonotic/Lightmapped");
            if (lm != null)
            {
                var m = new Material(lm);
                check(m.HasProperty("_GlowTex") && m.HasProperty("_HasGlow") && m.HasProperty("_Scroll"), "Lightmapped exposes glow + scroll properties");
                UnityEngine.Object.DestroyImmediate(m);
            }
            check(Shader.Find("MyXonotic/Bloom") != null && Shader.Find("MyXonotic/Bloom").passCount == 4, "bloom shader has 4 passes");

            // Shader-script → blend mode classification.
            var opaque = Script("textures/foo", ("textures/foo", null, null), ("$lightmap", "GL_DST_COLOR GL_ZERO", null));
            check(BspImportPipeline.ClassifyBlend(opaque) == BspImportPipeline.BlendKind.Opaque, "diffuse × lightmap → opaque");
            var cutout = Script("textures/grate", ("textures/grate", "GL_SRC_ALPHA GL_ONE_MINUS_SRC_ALPHA", "GE128"), ("$lightmap", "GL_DST_COLOR GL_ZERO", null));
            check(BspImportPipeline.ClassifyBlend(cutout) == BspImportPipeline.BlendKind.Cutout, "alphaFunc → cutout");
            var glass = Script("textures/glass", ("textures/glass", "BLEND", null));
            glass.SurfaceParms.Add("trans");
            check(BspImportPipeline.ClassifyBlend(glass) == BspImportPipeline.BlendKind.Blend, "blendfunc blend + trans → translucent");
            var beam = Script("textures/beam", ("textures/beam", "ADD", null));
            beam.SurfaceParms.Add("trans");
            check(BspImportPipeline.ClassifyBlend(beam) == BspImportPipeline.BlendKind.Additive, "blendfunc add → additive");
            var water = Script("textures/water", ("textures/water", null, null));
            water.WaterLike = true;
            check(BspImportPipeline.ClassifyBlend(water) == BspImportPipeline.BlendKind.Blend, "dp_water / dp_refract → translucent");
            var scrolled = Script("textures/conv", ("textures/conv", null, null), ("$lightmap", "GL_DST_COLOR GL_ZERO", null));
            scrolled.Stages[0].TcModScroll = new Vector2(0.5f, 0f);
            check(BspImportPipeline.ScrollOf(scrolled) == new Vector2(0.5f, 0f), "tcMod scroll read from the diffuse stage");
            check(BspImportPipeline.GlowPathFor("textures/exx/light01", "/root/textures/exx/light01.tga") == "textures/exx/light01_glow", "glow companion name = <diffuse>_glow");

            // Parser: tcMod scroll / dp_water.
            var parsed = XonoticContentResolver.ParseShaderScripts("textures/t1\n{\n\tsurfaceparm trans\n\tdp_water 0.1 0.9 1 1 1 1 1\n\t{\n\t\tmap textures/t1\n\t\ttcMod scroll 0.25 -0.1\n\t\tblendfunc blend\n\t}\n}\n");
            check(parsed.Count == 1 && parsed[0].WaterLike && parsed[0].Stages.Count == 1 && parsed[0].Stages[0].TcModScroll == new Vector2(0.25f, -0.1f), "shader parser reads dp_water and tcMod scroll");
        }

        static MaterialScript Script(string name, params (string map, string blend, string alpha)[] stages)
        {
            var s = new MaterialScript { Name = name };
            foreach (var st in stages) s.Stages.Add(new MaterialScript.StageInfo { Map = st.map, BlendFunc = st.blend, AlphaFunc = st.alpha });
            return s;
        }

        // ---------------------------------------------------------- 4 bot difficulty

        static void BotDifficulty(Action<bool, string> check)
        {
            // fire tolerance table from aim.qc (dist qu → max angle) at skill 10, accurate shot: 1000/(d-9)-0.35
            float d100 = BotAim.MaxFireDeviation(100f / 32f, 10, true);
            check(Mathf.Abs(d100 - 10.64f) < 0.05f, "max fire deviation at 100 qu, skill 10 = 10.6° (" + d100.ToString("0.00") + ")");
            check(BotAim.MaxFireDeviation(500f / 32f, 3) > BotAim.MaxFireDeviation(500f / 32f, 8), "low skill fires from a wider cone");
            check(BotAim.MaxFireDeviation(0.1f, 1) <= 90f, "deviation capped at 90°");

            // deterministic aim: fixed random → skill 1 keeps a much larger error than skill 10 after 1 s of tracking
            float err1 = TrackError(1), err10 = TrackError(10);
            check(err10 < 1.0f, "skill 10 converges onto a still target within 1 s (error " + err10.ToString("0.00") + "°)");
            check(err1 > err10 * 3f, "skill 1 stays far less accurate than skill 10 (" + err1.ToString("0.0") + "° vs " + err10.ToString("0.00") + "°)");

            // think cadence: aim think interval 0.5 - 0.05*skill
            var a3 = new BotAim(3) { Random01 = () => 0.5f, RandomVec = () => Vector3.zero };
            a3.Reset(Vector3.forward);
            a3.Update(Vector3.right, true, 0f, 0.05f);
            float turned3 = Vector2.Distance(BotAim.AnglesOf(Vector3.forward), a3.ViewAngles);
            var a10 = new BotAim(10) { Random01 = () => 0.5f, RandomVec = () => Vector3.zero };
            a10.Reset(Vector3.forward);
            a10.Update(Vector3.right, true, 0f, 0.05f);
            float turned10 = Vector2.Distance(BotAim.AnglesOf(Vector3.forward), a10.ViewAngles);
            check(turned10 > turned3 && turned3 > 0f, "skill 10 turns faster per frame than skill 3 (" + turned10.ToString("0.0") + "° vs " + turned3.ToString("0.0") + "°)");

            // fire window: opens only inside the cone
            var f = new BotAim(5) { Random01 = () => 0.5f, RandomVec = () => Vector3.zero };
            f.Reset(Vector3.forward);
            check(!f.UpdateFire(Vector3.right, 10f, 1f), "no fire while looking 90° away");
            check(f.UpdateFire(Vector3.forward, 10f, 1f), "fire allowed when aligned");
            check(f.UpdateFire(Vector3.right, 10f, 1.1f) && !f.UpdateFire(Vector3.right, 10f, 2f), "fire window lasts 0.5-0.05*skill s then closes");

            check(Mathf.Approximately(Vector3.Dot(BotAim.DirectionOf(BotAim.AnglesOf(new Vector3(0.3f, -0.5f, 0.8f).normalized)), new Vector3(0.3f, -0.5f, 0.8f).normalized), 1f), "angles ↔ direction round-trip");
        }

        /// Track a still target 60° to the right for 1 s at 50 Hz with a fixed pseudo-random stream.
        static float TrackError(int skill)
        {
            uint seed = 12345u;
            System.Func<float> rnd = () => { seed = seed * 1664525u + 1013904223u; return (seed >> 8) / 16777216f; };
            var aim = new BotAim(skill) { Random01 = rnd, RandomVec = () => new Vector3(rnd() * 2f - 1f, rnd() * 2f - 1f, rnd() * 2f - 1f) };
            aim.Reset(Vector3.forward);
            Vector3 target = Quaternion.Euler(0f, 60f, 0f) * Vector3.forward;
            Vector3 dir = Vector3.forward;
            for (int i = 1; i <= 50; i++) dir = aim.Update(target, true, i * 0.02f, 0.02f);
            return Vector3.Angle(dir, target);
        }

        // ---------------------------------------------------------- 5 bot survival

        static void BotSurvival(Action<bool, string> check)
        {
            check(Bot.SafeStep(Vector3.zero, Vector3.forward) == Vector3.forward, "SafeStep passes when there is no NavMesh (nothing to check against)");
            check(MobileBloom.Threshold >= 0.8f && MobileBloom.Intensity <= 0.5f, "bloom stays subtle (Test Lab run 1: light panels haloed into white slabs)");
            check(File.ReadAllText("Assets/MyXonotic/Resources/Lightmapped.shader").Contains("1 - (1 - saturate(c.rgb)) * (1 - saturate(g))"), "glow is screen-blended, not added");
            check(Bot.CombatFallback(0.2f, Vector3.forward, Vector3.back) == Vector3.forward, "vetoed strafe: follow the NavMesh path first");
            check(Bot.CombatFallback(0.2f, Vector3.zero, Vector3.back) == Vector3.zero, "vetoed strafe, no path: hold briefly");
            check(Bot.CombatFallback(1.5f, Vector3.zero, Vector3.back) == Vector3.back, "vetoed strafe > 1 s: guarded retreat");
            check(Bot.CombatFallback(1.5f, Vector3.zero, Vector3.zero) == Vector3.zero, "no safe option: hold (caller gives up at CombatHoldGiveUp)");
            check(Bot.CombatHoldGiveUp > 1f && Bot.CombatHoldGiveUp <= 5f, "combat hold give-up is a few seconds, not a freeze");
            var hurt = new GameObject("hurt", typeof(BoxCollider), typeof(MapTrigger));
            try
            {
                hurt.GetComponent<BoxCollider>().isTrigger = true;
                hurt.transform.position = new Vector3(0f, 0f, 3f);
                hurt.transform.localScale = new Vector3(2f, 2f, 2f);
                var t = hurt.GetComponent<MapTrigger>();
                t.TriggerKind = MapTrigger.Kind.Hurt;
                Physics.SyncTransforms();
                check(Bot.InsideLethalTrigger(new Vector3(0f, 0f, 3f)), "point inside a trigger_hurt volume is lethal");
                check(!Bot.InsideLethalTrigger(new Vector3(0f, 0f, -3f)), "point outside is safe");
                check(Bot.SafeStep(Vector3.zero, Vector3.forward * 3f) == Vector3.zero, "SafeStep refuses a step into trigger_hurt");
            }
            finally { UnityEngine.Object.DestroyImmediate(hurt); }

            var go = new GameObject("actor", typeof(CharacterController), typeof(Actor));
            try
            {
                var actor = go.GetComponent<Actor>();
                actor.TakeDamage(10000, Vector3.zero, null, Actor.CauseVoid);
                check(actor.Suicides == 1 && actor.SuicidesVoid == 1 && actor.SuicidesHurt == 0 && actor.SuicidesSelf == 0, "void death counted as void suicide");
                actor.ResetForSpawn();
                actor.TakeDamage(10000, Vector3.zero, null, Actor.CauseHurt);
                actor.ResetForSpawn();
                actor.TakeDamage(10000, Vector3.zero, actor);
                check(actor.Suicides == 3 && actor.SuicidesHurt == 1 && actor.SuicidesSelf == 1, "trigger_hurt and self-splash deaths classified");
                string report = GameLoop.BuildReport(10f, 600, 0.02f);
                check(report.Contains("projectile_models=" + ProjectileVisuals.LoadedModelCount() + "/" + ProjectileVisuals.AllModelResources.Length) && report.Contains("bot_skill=3/2/1") && report.Contains("bloom="), "game loop report carries dev.18 diagnostics (suicide causes are per-arena)");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
