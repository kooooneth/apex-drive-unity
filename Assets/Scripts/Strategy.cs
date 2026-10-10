using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The tyre strategy planner: a plan of up to two stops (which compound for each stint and after which lap to stop), with the race time
    /// predicted by the same lap simulation as the wind tunnel. The race engineer asks you to box on the planned laps.
    /// </summary>
    public static class Strategy
    {
        public const float PIT_LOSS = 9.5f;                  // seconds lost by a stop: the slow pit lane plus the time stationary
        public static readonly string[] Names = { "Soft", "Medium", "Hard" };
        public static readonly int[] Slicks = { 0, 1, 2 };

        public class Plan
        {
            public int stops;
            public int[] stopLap = { 1, 2 };                 // stop at the end of this lap (1-based), for stops 1 and 2
            public int[] compound = { 1, 1, 1 };             // the compound of each stint
        }

        public static Plan plan = new Plan();
        public static bool enabled;                          // the plan is used in the race (the engineer asks you to follow it)
        static string loadedFor = "";

        public class Result
        {
            public float total;
            public readonly List<float> stintAvg = new List<float>();
            public readonly List<float> stintEndWear = new List<float>();
            public readonly List<int> stintLaps = new List<int>();
        }

        // ---- saving, one plan per circuit
        static string Key(string id) { return "ad_strat_" + id; }

        public static void Load(string id)
        {
            if (loadedFor == id) return;
            loadedFor = id;
            plan = new Plan();
            string s = PlayerPrefs.GetString(Key(id), "");
            if (s.Length == 0) { enabled = false; return; }
            var p = s.Split(',');
            try
            {
                enabled = p[0] == "1"; plan.stops = int.Parse(p[1]);
                plan.stopLap[0] = int.Parse(p[2]); plan.stopLap[1] = int.Parse(p[3]);
                plan.compound[0] = int.Parse(p[4]); plan.compound[1] = int.Parse(p[5]); plan.compound[2] = int.Parse(p[6]);
            }
            catch (System.Exception) { plan = new Plan(); enabled = false; }
        }

        public static void Save(string id)
        {
            PlayerPrefs.SetString(Key(id), (enabled ? "1" : "0") + "," + plan.stops + "," + plan.stopLap[0] + "," + plan.stopLap[1] + "," + plan.compound[0] + "," + plan.compound[1] + "," + plan.compound[2]);
        }

        /// <summary>Makes sure the stop laps are in order and inside the race.</summary>
        public static void Tidy(int laps)
        {
            plan.stops = Mathf.Clamp(plan.stops, 0, Mathf.Min(2, Mathf.Max(0, laps - 1)));
            plan.stopLap[0] = Mathf.Clamp(plan.stopLap[0], 1, Mathf.Max(1, laps - 1 - (plan.stops == 2 ? 1 : 0)));
            plan.stopLap[1] = Mathf.Clamp(plan.stopLap[1], plan.stopLap[0] + 1, Mathf.Max(plan.stopLap[0] + 1, laps - 1));
        }

        /// <summary>The predicted race time of a plan on a circuit.</summary>
        public static Result Simulate(string id, int laps, Plan pl)
        {
            var r = new Result();
            float pitFrac = 0.7f;
            foreach (var d in Tracks.All) if (d.id == id) pitFrac = d.pitFrac;
            float wear = 100f; int stint = 0; int comp = pl.compound[0];
            float sum = 0f; float n = 0f;
            for (int lap = 1; lap <= laps; lap++)
            {
                float wearOut;
                float t = SetupLab.LapOnCompound(id, comp, wear, out wearOut);
                bool stopHere = stint < pl.stops && lap == pl.stopLap[stint];
                if (stopHere)
                {
                    // the stop is made part-way round the lap, where the pit entry is: the first part on the old tyres, the rest on the new set
                    int next = pl.compound[stint + 1];
                    float wearNew;
                    float tNew = SetupLab.LapOnCompound(id, next, 100f, out wearNew);
                    float lapT = pitFrac * t + (1f - pitFrac) * tNew;
                    r.total += lapT + PIT_LOSS; sum += pitFrac * t; n += pitFrac;
                    r.stintAvg.Add(sum / Mathf.Max(0.5f, n)); r.stintEndWear.Add(wear - (wear - wearOut) * pitFrac); r.stintLaps.Add(Mathf.RoundToInt(n));
                    stint++; comp = next;
                    wear = 100f - (100f - wearNew) * (1f - pitFrac);
                    sum = (1f - pitFrac) * tNew; n = 1f - pitFrac;
                    continue;
                }
                r.total += t; sum += t; n += 1f; wear = wearOut;
                if (lap == laps) { r.stintAvg.Add(sum / Mathf.Max(0.5f, n)); r.stintEndWear.Add(wear); r.stintLaps.Add(Mathf.RoundToInt(n)); }
            }
            return r;
        }

        /// <summary>Tries every sensible plan (up to two stops on soft, medium or hard) and returns the quickest.</summary>
        public static Plan Suggest(string id, int laps)
        {
            Plan best = null; float bestT = float.MaxValue;
            int maxStops = Mathf.Min(2, Mathf.Max(0, laps - 1));
            for (int stops = 0; stops <= maxStops; stops++)
            {
                int stints = stops + 1;
                int combos = 1; for (int i = 0; i < stints; i++) combos *= 3;
                for (int cmb = 0; cmb < combos; cmb++)
                {
                    int c0 = cmb % 3, c1 = (cmb / 3) % 3, c2 = (cmb / 9) % 3;
                    for (int l1 = 1; l1 <= Mathf.Max(1, laps - 1); l1++)
                    {
                        if (stops == 0 && l1 > 1) break;
                        int l2start = stops == 2 ? l1 + 1 : 0, l2end = stops == 2 ? laps - 1 : 0;
                        for (int l2 = l2start; l2 <= l2end; l2++)
                        {
                            var p = new Plan { stops = stops, compound = new[] { c0, c1, c2 }, stopLap = new[] { stops >= 1 ? l1 : 1, stops == 2 ? l2 : 2 } };
                            float t = Simulate(id, laps, p).total;
                            if (t < bestT) { bestT = t; best = p; }
                        }
                    }
                }
            }
            return best ?? new Plan();
        }

        /// <summary>Is this plan to be followed in this race? (The stops have to fall inside the race.)</summary>
        public static bool ActiveFor(int raceLaps)
        {
            return enabled && plan.stops > 0 && plan.stopLap[plan.stops - 1] < raceLaps;
        }
    }
}
