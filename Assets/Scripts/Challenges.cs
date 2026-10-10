using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// Practice challenges with medals: a perfect lap (clean and quick), a braking test (stop on the line) and tyre saver (three quick laps on
    /// as little tyre wear as possible). The target times come from the lap simulation, so they fit your car. Medals are kept per circuit.
    /// </summary>
    public static class Challenges
    {
        public const int FIRST_DRILL = 4;                    // practice drills 4, 5 and 6 are the challenges
        public static readonly string[] Names = { "Perfect lap", "Braking test", "Tyre saver" };
        public static readonly string[] MedalNames = { "No medal", "Bronze", "Silver", "Gold" };
        public static readonly int[] Cash = { 0, 200, 400, 800 };
        public static readonly int[] Xp = { 0, 15, 30, 60 };

        public static Color MedalColor(int m)
        {
            return m == 3 ? new Color(1f, 0.82f, 0.2f) : m == 2 ? new Color(0.82f, 0.85f, 0.9f) : m == 1 ? new Color(0.85f, 0.55f, 0.28f) : new Color(0.45f, 0.48f, 0.55f);
        }

        static string Key(string id, int c) { return "ad_med_" + id + "_" + c; }
        public static int Best(string id, int c) { return PlayerPrefs.GetInt(Key(id, c), 0); }

        /// <summary>Saves the medal if it beats the old one. Returns true if it is a new best.</summary>
        public static bool Record(string id, int c, int medal)
        {
            if (medal <= Best(id, c)) return false;
            PlayerPrefs.SetInt(Key(id, c), medal); PlayerPrefs.Save();
            return true;
        }

        /// <summary>The simulated clean lap of this circuit's car on fresh medium tyres: the reference for the times.</summary>
        public static float RefLap(string id)
        {
            float wearOut;
            return SetupLab.LapOnCompound(id, 1, 100f, out wearOut);
        }

        // perfect lap and tyre saver: lap time against the reference
        public static float GoldLap(float r) { return r * 1.12f; }
        public static float SilverLap(float r) { return r * 1.22f; }
        public static float BronzeLap(float r) { return r * 1.38f; }

        public static int LapMedal(float lap, float r)
        {
            if (lap <= 0f) return 0;
            return lap <= GoldLap(r) ? 3 : lap <= SilverLap(r) ? 2 : lap <= BronzeLap(r) ? 1 : 0;
        }

        // braking test: metres from the line when stopped (before the line is "short", past it is "over")
        public static int BrakeMedal(float gap)
        {
            float score = gap > 0f ? gap * 2.5f : -gap;                    // going over the line is penalised more than stopping short
            return score <= 2.5f ? 3 : score <= 6f ? 2 : score <= 12f ? 1 : 0;
        }

        /// <summary>The tyre wear left after three simulated laps at the reference pace on medium tyres: the saver medals are measured against it.</summary>
        public static float SaverRef(string id)
        {
            float wear = 100f;
            for (int i = 0; i < 3; i++) { float w; SetupLab.LapOnCompound(id, 1, wear, out w); wear = w; }
            return wear;
        }

        // tyre saver: tyre wear left after three laps compared with the reference, every lap inside the time limit
        public static int SaverMedal(float wearLeft, float refLeft, bool timesOk)
        {
            if (!timesOk) return 0;
            float gain = wearLeft - refLeft;
            return gain >= 18f ? 3 : gain >= 10f ? 2 : gain >= 3f ? 1 : 0;
        }
    }
}
