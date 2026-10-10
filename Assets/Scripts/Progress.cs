using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ApexDrive
{
    public class DriverDef { public string name, sub; public float assist; }

    public static class Drivers
    {
        public static readonly DriverDef[] All =
        {
            new DriverDef { name = "Mika \"Rookie\" Solano", sub = "Raw physics - every slide is on you.", assist = 0.85f },
            new DriverDef { name = "Dana Reyes", sub = "Balanced feel, light stability aid.", assist = 1.0f },
            new DriverDef { name = "Kenji \"Ace\" Muto", sub = "Smoothed inputs - easiest to push hard.", assist = 1.18f },
        };
    }

    public static class Livery
    {
        public static readonly string[] ColorNames = { "Red", "Teal", "Amber", "Green", "Purple", "Pink", "White", "Black" };
        public static readonly Color[] Colors =
        {
            new Color(1f, 0.29f, 0.18f), new Color(0.09f, 0.85f, 0.77f), new Color(1f, 0.69f, 0.13f), new Color(0.24f, 0.86f, 0.52f),
            new Color(0.54f, 0.36f, 1f), new Color(1f, 0.37f, 0.63f), new Color(0.93f, 0.94f, 0.97f), new Color(0.07f, 0.07f, 0.09f)
        };
        public static readonly string[] PatternNames = { "Solid", "Stripe", "Checkered" };
    }

    /// <summary>Driver level / XP, achievements, volume - persisted in PlayerPrefs.</summary>
    public static class Profile
    {
        public static int level = 1, xp = 0;
        public static readonly string[] AchIds = { "firstWin", "podium", "cleanRace", "survivor", "wetMaster", "seasonChampion", "tournamentChampion", "pole", "endurance", "customChampion", "trainer" };
        public static readonly string[] AchNames = { "First Win", "On The Podium", "Clean Race", "Survivor", "Wet Master", "Season Champion", "Tournament Champion", "Pole Position", "Endurance Finisher", "Season Architect", "Fully Trained" };
        public static readonly string[] AchDesc =
        {
            "Win any race", "Finish in the top 3", "Finish with no damage and no penalties",
            "Finish a race with 60%+ damage", "Win a race in the rain", "Win the 3-round career season", "Win the tournament", "Take pole position in qualifying",
            "Finish an endurance race", "Win a season you built yourself", "Get any racer stat to level 5"
        };
        static readonly HashSet<string> unlocked = new HashSet<string>();

        public const int TOURNAMENT_UNLOCK_LEVEL = 5;
        public const int TOURNAMENT_BONUS = 5000;

        public static int XpForNext(int lvl) { return lvl * 150; }
        public static float TrainingBonus() { return 1f + Mathf.Min(0.15f, (level - 1) * 0.0125f); }

        public static void Load()
        {
            level = PlayerPrefs.GetInt("ad_level", 1);
            xp = PlayerPrefs.GetInt("ad_xp", 0);
            unlocked.Clear();
            foreach (string s in PlayerPrefs.GetString("ad_ach", "").Split(','))
                if (s.Length > 0) unlocked.Add(s);
        }

        public static void Save()
        {
            PlayerPrefs.SetInt("ad_level", level);
            PlayerPrefs.SetInt("ad_xp", xp);
            PlayerPrefs.SetString("ad_ach", string.Join(",", new List<string>(unlocked).ToArray()));
            PlayerPrefs.Save();
        }

        public static bool Has(string id) { return unlocked.Contains(id); }
        public static int UnlockedCount { get { return unlocked.Count; } }

        /// <summary>Returns true only the first time an achievement is earned.</summary>
        public static bool Unlock(string id)
        {
            if (unlocked.Contains(id)) return false;
            unlocked.Add(id); Save();
            return true;
        }

        public static string AchName(string id) { return AchNames[System.Array.IndexOf(AchIds, id)]; }

        /// <summary>Adds XP and returns how many levels were gained.</summary>
        public static int AddXp(int amount)
        {
            int gained = 0;
            xp += amount;
            while (xp >= XpForNext(level)) { xp -= XpForNext(level); level++; gained++; }
            Save();
            return gained;
        }
    }

    /// <summary>A multi-round championship (the career season or the tougher tournament).</summary>
    public class Run
    {
        public static readonly string[] Calendar = { "harborline", "desert", "sprint" };
        public static readonly int[] Points = { 25, 18, 15, 12, 10, 8 };

        readonly string key;
        public readonly bool isCustom;
        public readonly List<string> calendar = new List<string>();     // the circuits, in order
        // custom season setup (saved separately from the season's progress)
        public string customName = "My Season";
        public int customLaps = 3, customWeather = 1, customDiff = 1;   // laps per round, 0 dry / 1 mixed / 2 wet, AI level
        public bool active;
        public int round, playerPts;
        public int[] aiPts = new int[5];

        public Run(string k, bool custom = false)
        {
            key = k; isCustom = custom;
            if (custom) LoadConfig(); else calendar.AddRange(Calendar);
            Load();
        }

        static bool TrackExists(string id)
        {
            foreach (var t in Tracks.All) if (t.id == id) return true;
            return false;
        }

        public void LoadConfig()
        {
            customName = PlayerPrefs.GetString("ad_custom_name", "My Season");
            customLaps = Mathf.Clamp(PlayerPrefs.GetInt("ad_custom_laps", 3), 1, 5);
            customWeather = Mathf.Clamp(PlayerPrefs.GetInt("ad_custom_wx", 1), 0, 2);
            customDiff = Mathf.Clamp(PlayerPrefs.GetInt("ad_custom_diff", 1), 0, 2);
            calendar.Clear();
            foreach (string s in PlayerPrefs.GetString("ad_custom_cal", "harborline,desert,sprint").Split(','))
                if (s.Length > 0 && TrackExists(s) && calendar.Count < 6) calendar.Add(s);
            if (calendar.Count == 0) calendar.Add(Tracks.All[0].id);
        }

        public void SaveConfig()
        {
            PlayerPrefs.SetString("ad_custom_name", customName);
            PlayerPrefs.SetInt("ad_custom_laps", customLaps);
            PlayerPrefs.SetInt("ad_custom_wx", customWeather);
            PlayerPrefs.SetInt("ad_custom_diff", customDiff);
            PlayerPrefs.SetString("ad_custom_cal", string.Join(",", calendar.ToArray()));
            PlayerPrefs.Save();
        }

        public static readonly Run Career = new Run("career");
        public static readonly Run Tournament = new Run("tourn");
        public static readonly Run Custom = new Run("custom", true);

        public void Load()
        {
            active = PlayerPrefs.GetInt("ad_" + key + "_active", 0) == 1;
            round = PlayerPrefs.GetInt("ad_" + key + "_round", 0);
            playerPts = PlayerPrefs.GetInt("ad_" + key + "_pp", 0);
            for (int i = 0; i < 5; i++) aiPts[i] = PlayerPrefs.GetInt("ad_" + key + "_ai" + i, 0);
        }

        public void Save()
        {
            PlayerPrefs.SetInt("ad_" + key + "_active", active ? 1 : 0);
            PlayerPrefs.SetInt("ad_" + key + "_round", round);
            PlayerPrefs.SetInt("ad_" + key + "_pp", playerPts);
            for (int i = 0; i < 5; i++) PlayerPrefs.SetInt("ad_" + key + "_ai" + i, aiPts[i]);
            PlayerPrefs.Save();
        }

        public void Begin() { active = true; round = 0; playerPts = 0; for (int i = 0; i < 5; i++) aiPts[i] = 0; Save(); }
        public string CurrentTrackId { get { return calendar[Mathf.Clamp(round, 0, calendar.Count - 1)]; } }
        public bool Finished { get { return round >= calendar.Count; } }

        /// <summary>Records one round. Returns true when this was the final round.</summary>
        public bool AddRound(List<Racer> ranked)
        {
            for (int i = 0; i < ranked.Count; i++)
            {
                int pts = i < Points.Length ? Points[i] : 0;
                if (ranked[i].isPlayer) playerPts += pts;
                else { var ai = ranked[i] as AICar; if (ai != null) aiPts[ai.id] += pts; }
            }
            round++;
            Save();
            return round >= calendar.Count;
        }

        public bool PlayerWon()
        {
            foreach (int a in aiPts) if (a >= playerPts) return false;
            return true;
        }

        public void End() { active = false; Save(); }

        public List<KeyValuePair<string, int>> Standings()
        {
            var l = new List<KeyValuePair<string, int>>();
            l.Add(new KeyValuePair<string, int>(RacerProfile.DisplayName, playerPts));
            for (int i = 0; i < 5; i++) l.Add(new KeyValuePair<string, int>(Personalities.Drivers[i % Personalities.Drivers.Length] + (i == 0 ? " (teammate)" : ""), aiPts[i]));
            l.Sort((a, b) => b.Value.CompareTo(a.Value));
            return l;
        }
    }

    /// <summary>Best-lap ghost: (time, distance) samples stored per circuit.</summary>
    public static class Ghost
    {
        public static void Save(string trackId, float duration, List<Vector2> samples)
        {
            var sb = new StringBuilder();
            sb.Append(duration.ToString("0.000", CultureInfo.InvariantCulture));
            foreach (var s in samples)
            {
                sb.Append(';');
                sb.Append(s.x.ToString("0.00", CultureInfo.InvariantCulture)); sb.Append(',');
                sb.Append(s.y.ToString("0.0", CultureInfo.InvariantCulture));
            }
            PlayerPrefs.SetString("ad_ghost_" + trackId, sb.ToString());
        }

        public static Vector2[] Load(string trackId, out float duration)
        {
            duration = 0f;
            string raw = PlayerPrefs.GetString("ad_ghost_" + trackId, "");
            if (raw.Length == 0) return null;
            string[] parts = raw.Split(';');
            if (parts.Length < 3) return null;
            float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
            var r = new Vector2[parts.Length - 1];
            for (int i = 1; i < parts.Length; i++)
            {
                string[] xy = parts[i].Split(',');
                float t = 0, d = 0;
                if (xy.Length == 2)
                {
                    float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out t);
                    float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out d);
                }
                r[i - 1] = new Vector2(t, d);
            }
            return r;
        }
    }
}
