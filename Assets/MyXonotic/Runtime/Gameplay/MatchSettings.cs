using UnityEngine;

namespace MyXonotic
{
    /// <summary>Game modes offered by the main menu (offline, bots only).</summary>
    public enum GameMode { Deathmatch = 0, TeamDeathmatch = 1, CaptureTheFlag = 2 }

    /// <summary>Team of an actor. None in Deathmatch.</summary>
    public enum Team { None = 0, Red = 1, Blue = 2 }

    /// <summary>
    /// Match options chosen in the main menu and read by ArenaBootstrap when a
    /// map scene starts. Persisted with PlayerPrefs so the choice survives a
    /// return to the menu. Tests/drivers may set the static fields directly
    /// before the arena starts (nothing is read from PlayerPrefs after that).
    /// </summary>
    public static class MatchSettings
    {
        const string ModeKey = "mx_mode";
        const string AllWeaponsKey = "mx_allweapons";
        const string BotsKey = "mx_bots";
        const string BotSkillKey = "mx_botskill";

        public const int MinBots = 1;
        public const int MaxBots = 7;
        public const int DefaultBots = 3;
        /// dev.18: Xonotic `skill` 1..10. The server default 8 (dev.16) was "insanely hard" on touch
        /// (owner, Poco F3, dev.17); default 3 → bots 3 / 2 / 1. Slider on SETTINGS → GAME.
        public const int MinBotSkill = 1, MaxBotSkill = 10, DefaultBotSkill = 3;

        static bool _loaded;
        static GameMode _mode = GameMode.Deathmatch;
        static bool _allWeapons;
        static int _bots = DefaultBots;
        static int _botSkill = DefaultBotSkill;

        public static GameMode Mode
        {
            get { Load(); return _mode; }
            set { Load(); _mode = value; PlayerPrefs.SetInt(ModeKey, (int)value); Save(); }
        }

        /// "Weapon arena" mutator: every actor spawns with all weapons and full ammo.
        public static bool AllWeapons
        {
            get { Load(); return _allWeapons; }
            set { Load(); _allWeapons = value; PlayerPrefs.SetInt(AllWeaponsKey, value ? 1 : 0); Save(); }
        }

        public static int BotCount
        {
            get { Load(); return _bots; }
            set { Load(); _bots = Mathf.Clamp(value, MinBots, MaxBots); PlayerPrefs.SetInt(BotsKey, _bots); Save(); }
        }

        /// Base bot skill (1..10) chosen on the settings page; saved immediately.
        public static int BotSkill
        {
            get { Load(); return _botSkill; }
            set { Load(); _botSkill = Mathf.Clamp(value, MinBotSkill, MaxBotSkill); PlayerPrefs.SetInt(BotSkillKey, _botSkill); Save(); }
        }

        /// Xonotic-style difficulty label for the slider.
        public static string BotSkillName(int skill) => skill <= 3 ? "EASY" : skill <= 6 ? "MEDIUM" : skill <= 8 ? "HARD" : "NIGHTMARE";

        static void Save() { if (Application.isPlaying) PlayerPrefs.Save(); }

        public static bool IsTeamMode => Mode != GameMode.Deathmatch;

        /// Bot skill 1..10 per bot index: BotSkill, BotSkill-1, BotSkill-2 (cycling, never below 1)
        /// so a match has a strong, a medium and a weak bot around the chosen difficulty.
        /// dev.16 used a fixed 8 / 6 / 4; dev.18 default 3 → 3 / 2 / 1.
        public static int BotSkillFor(int index) => Mathf.Clamp(BotSkill - (index % 3), MinBotSkill, MaxBotSkill);

        /// Capture limit for CTF (Xonotic default 10).
        public const int CaptureLimit = 10;

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            if (!Application.isPlaying && Application.isBatchMode) return;
            _mode = (GameMode)Mathf.Clamp(PlayerPrefs.GetInt(ModeKey, 0), 0, 2);
            _allWeapons = PlayerPrefs.GetInt(AllWeaponsKey, 0) != 0;
            _bots = Mathf.Clamp(PlayerPrefs.GetInt(BotsKey, DefaultBots), MinBots, MaxBots);
            _botSkill = Mathf.Clamp(PlayerPrefs.GetInt(BotSkillKey, DefaultBotSkill), MinBotSkill, MaxBotSkill);
        }

        /// Test hook: set the base bot skill without touching PlayerPrefs.
        public static void OverrideBotSkillForTest(int skill)
        {
            _loaded = true;
            _botSkill = Mathf.Clamp(skill, MinBotSkill, MaxBotSkill);
        }

        /// Test hook: forget any loaded/persisted values and use the given ones (no PlayerPrefs write).
        public static void OverrideForTest(GameMode mode, bool allWeapons, int bots)
        {
            _loaded = true;
            _mode = mode;
            _allWeapons = allWeapons;
            _bots = Mathf.Clamp(bots, MinBots, MaxBots);
        }

        public static string ModeName(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.TeamDeathmatch: return "TEAM DEATHMATCH";
                case GameMode.CaptureTheFlag: return "CAPTURE THE FLAG";
                default: return "DEATHMATCH";
            }
        }

        public static string ModeShort(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.TeamDeathmatch: return "TDM";
                case GameMode.CaptureTheFlag: return "CTF";
                default: return "DM";
            }
        }

        public static Color TeamColor(Team team)
        {
            switch (team)
            {
                case Team.Red: return new Color(1f, 0.25f, 0.2f);
                case Team.Blue: return new Color(0.25f, 0.5f, 1f);
                default: return Color.white;
            }
        }

        public static Team Opponent(Team team) => team == Team.Red ? Team.Blue : team == Team.Blue ? Team.Red : Team.None;
    }
}
