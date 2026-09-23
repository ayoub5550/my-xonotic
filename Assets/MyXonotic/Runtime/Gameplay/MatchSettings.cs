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

        public const int MinBots = 1;
        public const int MaxBots = 7;
        public const int DefaultBots = 3;

        static bool _loaded;
        static GameMode _mode = GameMode.Deathmatch;
        static bool _allWeapons;
        static int _bots = DefaultBots;

        public static GameMode Mode
        {
            get { Load(); return _mode; }
            set { Load(); _mode = value; PlayerPrefs.SetInt(ModeKey, (int)value); }
        }

        /// "Weapon arena" mutator: every actor spawns with all weapons and full ammo.
        public static bool AllWeapons
        {
            get { Load(); return _allWeapons; }
            set { Load(); _allWeapons = value; PlayerPrefs.SetInt(AllWeaponsKey, value ? 1 : 0); }
        }

        public static int BotCount
        {
            get { Load(); return _bots; }
            set { Load(); _bots = Mathf.Clamp(value, MinBots, MaxBots); PlayerPrefs.SetInt(BotsKey, _bots); }
        }

        public static bool IsTeamMode => Mode != GameMode.Deathmatch;

        /// dev.16: bot skill 1..10 per bot index. Xonotic servers run one `skill` (default 8);
        /// we alternate 8 / 6 / 4 so a match has a strong, a medium and a weak bot.
        public static int BotSkillFor(int index)
        {
            switch (index % 3) { case 0: return 8; case 1: return 6; default: return 4; }
        }

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
