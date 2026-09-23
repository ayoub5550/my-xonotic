using System.Collections.Generic;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// dev.13: runtime access to the original Xonotic HUD/menu art baked by the
    /// Editor <c>HudArtImporter</c> into <c>Resources/Hud/&lt;name&gt;</c>
    /// (gfx/hud/luma icons with their _alpha companions merged, luminos menu
    /// background, gametype icons). Every accessor returns null when the asset
    /// was not generated so callers keep their text-only fallback — the HUD and
    /// menu must never depend on the art being present.
    /// </summary>
    public static class HudArt
    {
        public const string ResourcePrefix = "Hud/";

        static readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();

        /// Upstream luma icon file names per weapon (gfx/hud/luma/weapon*.jpg).
        public static readonly string[] WeaponIconNames =
        {
            "weaponlaser", "weaponshotgun", "weaponuzi", "weapongrenadelauncher", "weaponelectro",
            "weaponcrylink", "weaponnex", "weaponhagar", "weaponrocketlauncher",
            "weaponrifle", "weaponminelayer", "weaponarc", "weaponfireball", "weaponhook"
        };

        public static string WeaponIconName(WeaponType type)
        {
            int i = (int)type;
            return i >= 0 && i < WeaponIconNames.Length ? WeaponIconNames[i] : null;
        }

        public static string AmmoIconName(AmmoType ammo)
        {
            switch (ammo)
            {
                case AmmoType.Shells: return "ammo_shells";
                case AmmoType.Bullets: return "ammo_bullets";
                case AmmoType.Rockets: return "ammo_rockets";
                case AmmoType.Cells: return "ammo_cells";
                default: return null;
            }
        }

        public static string GametypeIconName(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.TeamDeathmatch: return "gametype_tdm";
                case GameMode.CaptureTheFlag: return "gametype_ctf";
                default: return "gametype_dm";
            }
        }

        public static Texture2D Texture(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Texture2D tex;
            if (_textures.TryGetValue(name, out tex)) return tex;
            tex = Resources.Load<Texture2D>(ResourcePrefix + name);
            _textures[name] = tex;
            return tex;
        }

        public static Sprite Sprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Sprite sprite;
            if (_sprites.TryGetValue(name, out sprite)) return sprite;
            var tex = Texture(name);
            sprite = tex != null ? UnityEngine.Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f) : null;
            _sprites[name] = sprite;
            return sprite;
        }

        public static bool Has(string name) => Texture(name) != null;

        /// Drop cached references (scene change / tests).
        public static void ClearCache()
        {
            _textures.Clear();
            _sprites.Clear();
        }
    }
}
