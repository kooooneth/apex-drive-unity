using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// Your own racer: a name and three trainable stats. Train them with gym exercises (they cost energy) and
    /// practice sessions on the circuits. The stats change how the car and driver behave in races.
    /// </summary>
    public static class RacerProfile
    {
        public const int MAX_LEVEL = 10, MAX_ENERGY = 6, MAX_NAME = 14;
        public const int FITNESS = 0, FOCUS = 1, REFLEXES = 2;

        public static readonly string[] StatNames = { "Fitness", "Focus", "Reflexes" };
        public static readonly string[] StatDesc =
        {
            "Stamina: your driver tires more slowly in long races.",
            "Concentration: smoother driving - less tyre wear and steadier over the kerbs.",
            "Reaction speed: the car answers your steering faster."
        };

        public static string name = "";
        public static readonly int[] level = { 1, 1, 1 };
        public static readonly int[] xp = { 0, 0, 0 };
        public static int energy = MAX_ENERGY;

        public static string DisplayName { get { return name.Length > 0 ? name : "You"; } }

        // ---- appearance: race number, helmet colour, nationality ----
        public static int number = 7, helmet = 0, nation = 0;
        public static readonly string[] NationCodes = { "GBR", "USA", "GER", "FRA", "ITA", "JPN", "BRA", "AUS" };
        // each flag as three stripes
        public static readonly Color[][] NationColors =
        {
            new[] { new Color(0.1f, 0.2f, 0.6f), new Color(0.95f, 0.95f, 0.97f), new Color(0.8f, 0.1f, 0.15f) },
            new[] { new Color(0.8f, 0.1f, 0.15f), new Color(0.95f, 0.95f, 0.97f), new Color(0.1f, 0.2f, 0.6f) },
            new[] { new Color(0.05f, 0.05f, 0.06f), new Color(0.85f, 0.1f, 0.1f), new Color(1f, 0.8f, 0.1f) },
            new[] { new Color(0.1f, 0.2f, 0.6f), new Color(0.95f, 0.95f, 0.97f), new Color(0.85f, 0.1f, 0.15f) },
            new[] { new Color(0.1f, 0.55f, 0.25f), new Color(0.95f, 0.95f, 0.97f), new Color(0.8f, 0.1f, 0.15f) },
            new[] { new Color(0.95f, 0.95f, 0.97f), new Color(0.85f, 0.1f, 0.2f), new Color(0.95f, 0.95f, 0.97f) },
            new[] { new Color(0.1f, 0.55f, 0.25f), new Color(1f, 0.85f, 0.1f), new Color(0.1f, 0.55f, 0.25f) },
            new[] { new Color(0.05f, 0.15f, 0.5f), new Color(0.95f, 0.95f, 0.97f), new Color(0.85f, 0.1f, 0.15f) }
        };
        public static Color HelmetColor { get { return Livery.Colors[((helmet % Livery.Colors.Length) + Livery.Colors.Length) % Livery.Colors.Length]; } }
        public static string NationCode { get { return NationCodes[((nation % NationCodes.Length) + NationCodes.Length) % NationCodes.Length]; } }
        /// <summary>"#7 NAME (GBR)": how you appear in the results.</summary>
        public static string TagName { get { return "#" + number + " " + DisplayName + " (" + NationCode + ")"; } }

        // ---- training calendar: races take a week; stop training and your fitness fades ----
        public static int day = 1, lastTrainDay = 1;
        public static int DaysSinceTraining { get { return day - lastTrainDay; } }

        public static void Rest() { day++; energy = Mathf.Clamp(energy + 1, 0, MAX_ENERGY); Save(); }

        public static void Detrain(int amount)
        {
            xp[FITNESS] -= amount;
            while (xp[FITNESS] < 0 && level[FITNESS] > 1) { level[FITNESS]--; xp[FITNESS] += XpForNext(level[FITNESS]); }
            if (xp[FITNESS] < 0) xp[FITNESS] = 0;
            Save();
        }

        /// <summary>After a race week: not having trained for more than 10 days costs Fitness. Returns a note for the results, or null.</summary>
        public static string ApplyDetraining()
        {
            int idle = DaysSinceTraining;
            if (idle <= 10) return null;
            int loss = 15 * ((idle - 10) / 7 + 1);
            Detrain(loss);
            return "Detraining: Fitness lost " + loss + " XP - you have not trained for " + idle + " days.";
        }
        public static int XpForNext(int lvl) { return 60 + 40 * lvl; }

        /// <summary>1 (fresh) .. 28 (everything maxed).</summary>
        public static int RacerLevel { get { return level[0] + level[1] + level[2] - 2; } }

        public static string Rank
        {
            get
            {
                int l = RacerLevel;
                return l < 4 ? "Rookie" : l < 9 ? "Club racer" : l < 15 ? "Semi-pro" : l < 21 ? "Professional" : l < 28 ? "Champion" : "Legend";
            }
        }

        // ---- what the stats do ----
        public static float SteerResponse { get { return 1f + 0.03f * (level[REFLEXES] - 1); } }       // up to +27% steering response
        public static float WearMult { get { return 1f - 0.015f * (level[FOCUS] - 1); } }              // up to -13.5% tyre wear
        public static float KerbMult { get { return 1f - 0.04f * (level[FOCUS] - 1); } }               // up to -36% kerb twitch
        public static float FatigueMult { get { return 1f - 0.06f * (level[FITNESS] - 1); } }          // up to -54% fatigue

        public static void SetName(string n)
        {
            if (n == null) n = "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in n)
            {
                if (c == '\n' || c == '\r' || c == '\t') continue;
                sb.Append(c);
                if (sb.Length >= MAX_NAME) break;
            }
            name = sb.ToString();
        }

        /// <summary>Adds training XP to a stat; returns how many levels it gained.</summary>
        public static int AddXp(int stat, int amount)
        {
            if (level[stat] >= MAX_LEVEL) return 0;
            int gained = 0;
            xp[stat] += Mathf.Max(0, amount);
            while (level[stat] < MAX_LEVEL && xp[stat] >= XpForNext(level[stat])) { xp[stat] -= XpForNext(level[stat]); level[stat]++; gained++; }
            if (level[stat] >= MAX_LEVEL) xp[stat] = 0;
            Save();
            return gained;
        }

        public static bool SpendEnergy()
        {
            if (energy <= 0) return false;
            energy--; Save(); return true;
        }

        public static void AddEnergy(int n) { energy = Mathf.Clamp(energy + n, 0, MAX_ENERGY); Save(); }

        public static void Load()
        {
            SetName(PlayerPrefs.GetString("ad_rname", ""));
            for (int i = 0; i < 3; i++)
            {
                level[i] = Mathf.Clamp(PlayerPrefs.GetInt("ad_rlvl" + i, 1), 1, MAX_LEVEL);
                xp[i] = Mathf.Max(0, PlayerPrefs.GetInt("ad_rxp" + i, 0));
            }
            energy = Mathf.Clamp(PlayerPrefs.GetInt("ad_renergy", MAX_ENERGY), 0, MAX_ENERGY);
            number = Mathf.Clamp(PlayerPrefs.GetInt("ad_rnum", 7), 1, 99);
            helmet = PlayerPrefs.GetInt("ad_rhelm", 0); nation = PlayerPrefs.GetInt("ad_rnat", 0);
            day = Mathf.Max(1, PlayerPrefs.GetInt("ad_rday", 1)); lastTrainDay = PlayerPrefs.GetInt("ad_rtrain", 1);
        }

        public static void Save()
        {
            PlayerPrefs.SetString("ad_rname", name);
            for (int i = 0; i < 3; i++) { PlayerPrefs.SetInt("ad_rlvl" + i, level[i]); PlayerPrefs.SetInt("ad_rxp" + i, xp[i]); }
            PlayerPrefs.SetInt("ad_renergy", energy);
            PlayerPrefs.SetInt("ad_rnum", number); PlayerPrefs.SetInt("ad_rhelm", helmet); PlayerPrefs.SetInt("ad_rnat", nation);
            PlayerPrefs.SetInt("ad_rday", day); PlayerPrefs.SetInt("ad_rtrain", lastTrainDay);
            PlayerPrefs.Save();
        }
    }
}
