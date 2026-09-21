using System.Collections.Generic;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Single source of truth for which original Xonotic sound files belong to
    /// which weapon event. The Editor importer (IqmWeaponImporter) copies every
    /// listed file into Resources/Weapons/&lt;Weapon&gt;_&lt;file&gt;.ogg; the runtime
    /// loads them back through <see cref="Load"/>. Missing clips are simply
    /// null (silent), never substituted by another weapon's sound.
    /// </summary>
    public static class WeaponAudio
    {
        public sealed class Set
        {
            public string[] Fire;
            public string[] AltFire;
            public string[] Impact;
        }

        /// Content-relative source files (under sound/weapons) per weapon.
        public static readonly Dictionary<WeaponType, Set> Sources = new Dictionary<WeaponType, Set>
        {
            [WeaponType.Blaster] = new Set { Fire = new[] { "lasergun_fire" }, AltFire = new[] { "lasergun_fire" }, Impact = new[] { "laserimpact" } },
            [WeaponType.Shotgun] = new Set { Fire = new[] { "shotgun_fire" }, AltFire = new[] { "shotgun_melee" }, Impact = new[] { "ric1", "ric2", "ric3" } },
            [WeaponType.MachineGun] = new Set { Fire = new[] { "uzi_fire" }, AltFire = new[] { "uzi_fire" }, Impact = new[] { "ric1", "ric2", "ric3" } },
            [WeaponType.Mortar] = new Set { Fire = new[] { "grenade_fire" }, AltFire = new[] { "grenade_fire" }, Impact = new[] { "grenade_impact" } },
            [WeaponType.Electro] = new Set { Fire = new[] { "electro_fire" }, AltFire = new[] { "electro_fire2" }, Impact = new[] { "electro_impact" } },
            [WeaponType.Crylink] = new Set { Fire = new[] { "crylink_fire" }, AltFire = new[] { "crylink_fire2" }, Impact = new[] { "crylink_impact", "crylink_impact2" } },
            [WeaponType.Vortex] = new Set { Fire = new[] { "nexfire" }, AltFire = new string[0], Impact = new[] { "neximpact" } },
            [WeaponType.Hagar] = new Set { Fire = new[] { "hagar_fire" }, AltFire = new[] { "hagar_fire" }, Impact = new[] { "hagexp1", "hagexp2", "hagexp3" } },
            [WeaponType.Devastator] = new Set { Fire = new[] { "rocket_fire" }, AltFire = new[] { "rocket_det" }, Impact = new[] { "rocket_impact" } },
        };

        /// Weapon-independent UI/gameplay sounds copied under Resources/Weapons/Common_*.
        public static readonly string[] CommonSources = { "weapon_switch", "weaponpickup", "dryfire" };
        /// Misc sounds (sound/misc) copied under Resources/Weapons/Misc_*.
        public static readonly string[] MiscSources = { "itempickup", "hit", "kill", "armorimpact", "bodyimpact1", "mediumhealth", "megahealth" };

        static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();

        /// Resources-relative name for a weapon clip, e.g. Weapons/Hagar_hagexp1.
        public static string ResourceName(WeaponType w, string file) => "Weapons/" + w + "_" + file;

        public static AudioClip Load(string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName)) return null;
            if (Cache.TryGetValue(resourceName, out var clip)) return clip;
            clip = Resources.Load<AudioClip>(resourceName);
            Cache[resourceName] = clip; // cache misses too
            return clip;
        }

        public static AudioClip Fire(WeaponType w, bool alt)
        {
            if (!Sources.TryGetValue(w, out var set)) return null;
            var list = alt && set.AltFire != null && set.AltFire.Length > 0 ? set.AltFire : set.Fire;
            return list == null || list.Length == 0 ? null : Load(ResourceName(w, list[0]));
        }

        public static AudioClip Impact(WeaponType w)
        {
            if (!Sources.TryGetValue(w, out var set) || set.Impact == null || set.Impact.Length == 0) return null;
            return Load(ResourceName(w, set.Impact[Random.Range(0, set.Impact.Length)]));
        }

        public static AudioClip Common(string file) => Load("Weapons/Common_" + file);
        public static AudioClip Misc(string file) => Load("Weapons/Misc_" + file);

        /// Plays a clip at a world position (3D) or flat (2D) on a throwaway AudioSource.
        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, bool spatial = true)
        {
            if (clip == null) return;
            var go = new GameObject("OneShotAudio");
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.spatialBlend = spatial ? 1f : 0f;
            src.minDistance = 3f;
            src.maxDistance = 60f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }
    }
}
