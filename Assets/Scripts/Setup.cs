using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The setup sheet: ride height, camber, differential and tyre pressure (plus the wing / gear / brake-bias sliders
    /// already in the garage). Every circuit has its own ideal values, hidden from you; practice sessions give
    /// engineer feedback ("the floor is bottoming out - raise the ride height") so you can tune toward them.
    /// </summary>
    public static class Setup
    {
        public static readonly string[] Names = SetupLab.Names;          // ride height, camber, differential, tyre pressure, wing angle, gear ratio
        static string lastTrack = "", lastFeedback = "";

        public static float Value(int k)
        {
            return k == 0 ? GameSave.rideHeight : k == 1 ? GameSave.camber : k == 2 ? GameSave.diff : k == 3 ? GameSave.pressure : k == 4 ? GameSave.wing : GameSave.gear;
        }

        /// <summary>The best value of a setting on this circuit (25..75).</summary>
        public static float Ideal(string trackId, int k)
        {
            return SetupLab.Ideal(trackId, k);            // from the lap simulation and the circuit's character, for the car as it is now
        }

        /// <summary>0 (way off) .. 1 (spot on) over the four setup values.</summary>
        public static float Fit(string trackId)
        {
            float dev = 0f;
            for (int k = 0; k < 4; k++) dev += Mathf.Abs(Value(k) - Ideal(trackId, k));
            return Mathf.Clamp01(1f - dev / 4f / 50f);
        }

        /// <summary>Normalised -1..1 around the middle, used by the physics.</summary>
        public static float Norm(int k) { return (Value(k) - 50f) / 50f; }

        public static List<string> Feedback(string trackId)
        {
            var l = new List<string>();
            string[] up =
            {
                "The floor is bottoming out over the kerbs - raise the ride height.",
                "The front tyres are overheating on the inside edge - take some camber out.",
                "The car pushes on corner entry - open the differential a little.",
                "The tyres feel hard and skittish - lower the pressure."
            };
            string[] down =
            {
                "We are not getting enough downforce - lower the ride height.",
                "The front end is not biting in the slow corners - add camber.",
                "We are spinning the rears on exit - lock the differential more.",
                "The tyres are overheating and squirming - raise the pressure."
            };
            for (int k = 0; k < 4; k++)
            {
                float dev = Value(k) - Ideal(trackId, k);
                if (Mathf.Abs(dev) < 10f) continue;
                // "too high" for ride height means too little downforce, so the messages are written the other way round
                if (k == 0) l.Add(dev > 0f ? down[0] : up[0]);
                else l.Add(dev > 0f ? up[k] : down[k]);
            }
            if (l.Count == 0) l.Add("The car feels great - the setup is right for this circuit.");
            return l;
        }

        public static void Remember(string trackId, List<string> lines)
        {
            lastTrack = trackId; lastFeedback = string.Join("  ", lines.ToArray());
        }

        public static string simResult = "";

        static string RevKey(string id) { return "ad_rev_" + id + "_" + SetupLab.CarCode; }       // a reading is only valid for the car it was made on: upgrades, a new era or a new team make it out of date
        public static bool Revealed(string id, int k) { return (PlayerPrefs.GetInt(RevKey(id), 0) & (1 << k)) != 0; }
        public static void Reveal(string id, int k) { PlayerPrefs.SetInt(RevKey(id), PlayerPrefs.GetInt(RevKey(id), 0) | (1 << k)); PlayerPrefs.Save(); }
        public static int NextUnrevealed(string id) { for (int k = 0; k < SetupLab.COUNT; k++) if (!Revealed(id, k)) return k; return -1; }
        /// <summary>" (ideal 41)" once the wind tunnel has measured that setting for this circuit.</summary>
        public static string Hint(string id, int k) { return Revealed(id, k) ? "  (ideal " + Mathf.RoundToInt(Ideal(id, k)) + ")" : ""; }

        public static string LastFeedbackFor(string trackId) { return lastTrack == trackId ? lastFeedback : ""; }
    }

    /// <summary>A race weekend: three practice sessions, qualifying, then the race. The setup is locked from qualifying on (parc ferme).</summary>
    public class Weekend
    {
        public static readonly string[] Names = { "Practice 1", "Practice 2", "Practice 3", "Qualifying", "Race" };
        public int stage;                // 0..2 practice, 3 qualifying, 4 race
        public int trackIndex;
        public bool locked;              // parc ferme: set when qualifying starts
        public string StageName { get { return Names[Mathf.Clamp(stage, 0, Names.Length - 1)]; } }
    }
}
