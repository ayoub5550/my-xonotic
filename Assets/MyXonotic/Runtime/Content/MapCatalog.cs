using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyXonotic.Content
{
    /// <summary>
    /// Build-time generated list of every playable map scene in the player.
    /// Lives in Resources so the main menu can load it by name; data comes
    /// from each upstream <map>.mapinfo (title/author/description/gametypes/
    /// cdtrack) — parsed as plain text, never executed.
    /// </summary>
    public sealed class MapCatalog : ScriptableObject
    {
        public const string ResourceName = "MapCatalog";

        [Serializable]
        public sealed class Entry
        {
            public string mapName;      // upstream bsp base name, e.g. "boil"
            public string sceneName;    // Unity scene name in the build
            public string title;
            public string author;
            public string description;
            public string[] gametypes;
            public int cdtrack;
            public string musicName;
            public Texture2D preview;
            public int spawnPoints;
            public int warnings;
        }

        public List<Entry> maps = new List<Entry>();
        public string buildVersion;
        public string buildUtc;

        public static MapCatalog Load() => Resources.Load<MapCatalog>(ResourceName);
    }
}
