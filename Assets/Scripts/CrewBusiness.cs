using UnityEngine;

namespace ApexDrive
{
    /// <summary>The people you can hire: a better race engineer, chief mechanic and aerodynamicist. Levels 0 to 3, bought with money.</summary>
    public static class Staff
    {
        public static readonly string[] Names = { "Race engineer", "Chief mechanic", "Aerodynamicist" };
        public static readonly string[] Effects =
        {
            "L1 detailed driving advice (understeer, bottoming, blisters, porpoising).  L2 strategy and team calls (undercuts, pushes, orders).  L3 lap-time deltas after every lap.",
            "Each level makes pit stops 7% quicker and the crew 30% less likely to make a mistake.",
            "Each level gives +1% cornering grip and +1 R&D point after every race."
        };
        public static readonly int[] Cost = { 1500, 3500, 7000 };
        public static readonly int[] level = new int[3];

        public static int Engineer { get { return level[0]; } }
        public static int Mechanic { get { return level[1]; } }
        public static int Aero { get { return level[2]; } }

        public static void Load() { for (int i = 0; i < 3; i++) level[i] = Mathf.Clamp(PlayerPrefs.GetInt("ad_staff" + i, 0), 0, 3); }
        public static void Save() { for (int i = 0; i < 3; i++) PlayerPrefs.SetInt("ad_staff" + i, level[i]); PlayerPrefs.Save(); }

        public static bool CanBuy(int i) { return level[i] < 3 && GameSave.money >= Cost[level[i]]; }

        public static void Buy(int i)
        {
            if (!CanBuy(i)) return;
            GameSave.money -= Cost[level[i]];
            level[i]++;
            Save(); GameSave.Save();
        }
    }

    /// <summary>The team's buildings, built up over the seasons: a factory, a wind tunnel and a simulator, levels 0 to 3.</summary>
    public static class Facilities
    {
        public static readonly string[] Names = { "Factory", "Wind tunnel facility", "Simulator facility" };
        public static readonly string[] Effects =
        {
            "Each level gives +1 R&D point after every race and makes the parts shop 4% cheaper.",
            "A wind tunnel run measures more settings at once: 2 at level 1, 2 at level 2, 3 at level 3.",
            "A simulator run costs less ($120, $90, $60). From level 2 it also lists what each setting is worth."
        };
        public static readonly int[] Cost = { 4000, 9000, 16000 };
        public static readonly int[] level = new int[3];

        public static void Load() { for (int i = 0; i < 3; i++) level[i] = Mathf.Clamp(PlayerPrefs.GetInt("ad_fac" + i, 0), 0, 3); }
        public static void Save() { for (int i = 0; i < 3; i++) PlayerPrefs.SetInt("ad_fac" + i, level[i]); PlayerPrefs.Save(); }
        public static bool CanBuy(int i) { return level[i] < 3 && GameSave.money >= Cost[level[i]]; }
        public static void Buy(int i) { if (!CanBuy(i)) return; GameSave.money -= Cost[level[i]]; level[i]++; Save(); GameSave.Save(); }

        public static float PartsDiscount { get { return 0.04f * level[0]; } }
        public static int RdBonus { get { return level[0]; } }
        public static int TunnelReveals { get { return level[1] == 0 ? 1 : level[1] == 3 ? 3 : 2; } }
        public static int SimCost { get { return 150 - 30 * level[2]; } }
        public static bool SimShowsAll { get { return level[2] >= 2; } }
    }

    /// <summary>
    /// Sponsors: pick one, and when you meet its goal in a race it pays out. Every few successes the deal grows (more money), and the
    /// sponsors' parts deals make the parts shop cheaper.
    /// </summary>
    public static class Sponsors
    {
        public class Sponsor { public string name, goal; public int pay; }

        public static readonly Sponsor[] All =
        {
            new Sponsor { name = "Podium Bank", goal = "Finish on the podium (top 3)", pay = 1800 },
            new Sponsor { name = "CleanDrive Insurance", goal = "Finish with no penalty seconds and under 10% damage", pay = 1500 },
            new Sponsor { name = "Overtake Energy", goal = "Finish at least 2 places better than you started", pay = 1700 },
            new Sponsor { name = "Speedline Tyres", goal = "Set a personal best lap during the race", pay = 1400 },
        };

        public static int chosen = -1;
        public static readonly int[] successes = new int[4];

        public static void Load()
        {
            chosen = PlayerPrefs.GetInt("ad_sp_chosen", -1);
            for (int i = 0; i < All.Length; i++) successes[i] = PlayerPrefs.GetInt("ad_sp_ok" + i, 0);
        }

        public static void Save()
        {
            PlayerPrefs.SetInt("ad_sp_chosen", chosen);
            for (int i = 0; i < All.Length; i++) PlayerPrefs.SetInt("ad_sp_ok" + i, successes[i]);
            PlayerPrefs.Save();
        }

        /// <summary>0..3: how big the deal with this sponsor has grown (one level every two successes).</summary>
        public static int Level(int i) { return Mathf.Min(3, successes[i] / 2); }
        public static int Pay(int i) { return Mathf.RoundToInt(All[i].pay * (1f + 0.25f * Level(i))); }

        /// <summary>The parts shop discount the sponsors' deals bring (3% for every level of every deal).</summary>
        public static float PartsDiscount
        {
            get { int l = 0; for (int i = 0; i < All.Length; i++) l += Level(i); return Mathf.Min(0.36f, 0.03f * l); }
        }

        /// <summary>A race has finished: did you meet the active sponsor's goal? Returns the line for the results screen, or null.</summary>
        public static string Evaluate(int rank, int startRank, float penaltySeconds, float damage, bool personalBest)
        {
            if (chosen < 0 || chosen >= All.Length) return null;
            bool met = chosen == 0 ? rank <= 3
                     : chosen == 1 ? penaltySeconds <= 0f && damage < 10f
                     : chosen == 2 ? startRank - rank >= 2
                     : personalBest;
            var s = All[chosen];
            if (!met) return "Sponsor " + s.name + ": goal missed (" + s.goal + ").";
            int pay = Pay(chosen);
            GameSave.money += pay;
            int before = Level(chosen);
            successes[chosen]++;
            Save();
            return "SPONSOR " + s.name + ": goal met!  +$" + pay + (Level(chosen) > before ? "   The deal grows to level " + Level(chosen) + "." : "");
        }
    }
}
