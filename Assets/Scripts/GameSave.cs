using UnityEngine;

namespace ApexDrive
{
    public enum Compound { Soft = 0, Medium = 1, Hard = 2, Inter = 3, Wet = 4 }

    public static class Tuning
    {
        public static readonly float[] TireGrip = { 1.12f, 1.0f, 0.9f, 0.97f, 0.92f };
        public static readonly float[] TireWear = { 1.6f, 1.0f, 0.6f, 1.1f, 1.2f };
        public static readonly string[] CompoundNames = { "Soft", "Medium", "Hard", "Inter", "Wet" };

        /// <summary>How well a compound grips on a road with the given wetness (0 dry .. 1 soaked). Slicks hate water, intermediates
        /// work on a damp road, full wets only really work when it is properly wet.</summary>
        public static float WetGrip(Compound c, float w)
        {
            w = Mathf.Clamp01(w);
            if (c == Compound.Inter) return w < 0.55f ? Mathf.Lerp(0.88f, 1.06f, w / 0.55f) : Mathf.Lerp(1.06f, 1.0f, (w - 0.55f) / 0.45f);
            if (c == Compound.Wet) return Mathf.Lerp(0.74f, 1.04f, w);
            return 1f - 0.42f * w;
        }

        /// <summary>Wet-weather tyres shred themselves on a dry road.</summary>
        public static float WetWear(Compound c, float w)
        {
            w = Mathf.Clamp01(w);
            if (c == Compound.Inter) return Mathf.Lerp(2.4f, 1f, Mathf.Clamp01(w / 0.5f));
            if (c == Compound.Wet) return Mathf.Lerp(3.2f, 1.1f, w);
            return 1f;
        }
        public static readonly float[] DifficultyMult = { 0.86f, 1.0f, 1.12f };
        public static readonly string[] DifficultyNames = { "Easy", "Medium", "Hard" };
        public const float GRAVITY = 26f;
        public const float FLY_SPEED = 350f / 3.6f;         // cars only leave the ground (over a crest or after a wheel-to-wheel launch) above 350 km/h
        public const float DRS_START = 0.02f, DRS_END = 0.24f;
        public const float SAFETY_CAR_CHANCE = 0.3f, SAFETY_CAR_SPEED_CAP = 24f;
        public const float VSC_SPEED_CAP = 38f;             // virtual safety car: about 137 km/h
    }

    /// <summary>Everything the garage persists between runs (money, upgrades, setup, livery).</summary>
    public static class GameSave
    {
        public static int money = 3000;
        public static int power = 1, aero = 1, grip = 1, reliability = 1;
        public static Compound compound = Compound.Medium;
        public static float wing = 50f, gear = 50f, brakeBias = 60f;
        public static float rideHeight = 50f, camber = 50f, diff = 50f, pressure = 50f;   // setup sheet, 0..100
        public static int trackIndex = 0;
        public static int laps = 3;
        public static int driver = 1;
        public static int bodyColor = 0, accentColor = 7, pattern = 0;
        public static int difficulty = 1;
        public static float volume = 0.5f;
        public static int enduranceLaps = 0;    // index into RaceManager.ENDURANCE_LAPS
        public static float engineWear, gearboxWear;     // 0..100, build up race by race
        public static float brakeWear;                   // 0..100: brake discs and pads, worn by braking (hot brakes wear faster)
        public static float brakeDuct = 50f;             // 0 small ducts (hot brakes, less drag) .. 100 large ducts (cool brakes, more drag)
        public static int partsUsed, gridPenalty;        // replacements this season, and places lost on the next grid
        public static bool voices = true;      // spoken radio and replay commentary (Windows voices)
        public static int raceTime;            // 0 day, 1 dusk, 2 night (floodlit)
        public static bool rewinds = true;     // three rewinds per race to undo a mistake (Backspace)
        public static bool advHandling = true; // weight transfer: understeer, oversteer and lift-off slides
        public static bool manualGears;        // shift the gearbox yourself (. up, , down)
        public static bool spotter = true;     // 'car left / car right' calls for cars in the blind spot
        public static int startType = 0;        // 0 = standing start, 1 = rolling start

        public static void Load()
        {
            money = PlayerPrefs.GetInt("ad_money", 3000);
            power = PlayerPrefs.GetInt("ad_power", 1);
            aero = PlayerPrefs.GetInt("ad_aero", 1);
            grip = PlayerPrefs.GetInt("ad_grip", 1);
            reliability = PlayerPrefs.GetInt("ad_rel", 1);
            compound = (Compound)PlayerPrefs.GetInt("ad_compound", 1);
            wing = PlayerPrefs.GetFloat("ad_wing", 50f);
            gear = PlayerPrefs.GetFloat("ad_gear", 50f);
            brakeBias = PlayerPrefs.GetFloat("ad_brake", 60f);
            rideHeight = PlayerPrefs.GetFloat("ad_rh", 50f); camber = PlayerPrefs.GetFloat("ad_cam", 50f);
            diff = PlayerPrefs.GetFloat("ad_diff2", 50f); pressure = PlayerPrefs.GetFloat("ad_prs", 50f);
            trackIndex = Mathf.Clamp(PlayerPrefs.GetInt("ad_track", 0), 0, Tracks.All.Length - 1);
            laps = PlayerPrefs.GetInt("ad_laps", 3);
            driver = PlayerPrefs.GetInt("ad_driver", 1);
            bodyColor = PlayerPrefs.GetInt("ad_body", 0);
            accentColor = PlayerPrefs.GetInt("ad_accent", 7);
            pattern = PlayerPrefs.GetInt("ad_pattern", 0);
            difficulty = PlayerPrefs.GetInt("ad_diff", 1);
            volume = PlayerPrefs.GetFloat("ad_vol", 0.5f);
            startType = PlayerPrefs.GetInt("ad_start", 0);
            voices = PlayerPrefs.GetInt("ad_voices", 1) == 1;
            advHandling = PlayerPrefs.GetInt("ad_adv", 1) == 1;
            rewinds = PlayerPrefs.GetInt("ad_rew", 1) == 1;
            raceTime = Mathf.Clamp(PlayerPrefs.GetInt("ad_tod", 0), 0, 2);
            manualGears = PlayerPrefs.GetInt("ad_manual", 0) == 1;
            spotter = PlayerPrefs.GetInt("ad_spot", 1) == 1;
            engineWear = PlayerPrefs.GetFloat("ad_ew", 0f); gearboxWear = PlayerPrefs.GetFloat("ad_gw", 0f);
            brakeWear = PlayerPrefs.GetFloat("ad_bw", 0f); brakeDuct = PlayerPrefs.GetFloat("ad_duct", 50f);
            partsUsed = PlayerPrefs.GetInt("ad_pu", 0); gridPenalty = PlayerPrefs.GetInt("ad_gp", 0);
            enduranceLaps = Mathf.Clamp(PlayerPrefs.GetInt("ad_endlaps", 0), 0, 2);
            Profile.Load();
            RacerProfile.Load();
            TeamCareer.Load();
            Staff.Load(); Sponsors.Load(); Facilities.Load(); TestData.Load();
        }

        public static void Save()
        {
            CarSetups.StoreCurrent();                       // the tuning of the car that is in the garage
            PlayerPrefs.SetInt("ad_money", money);
            PlayerPrefs.SetInt("ad_power", power);
            PlayerPrefs.SetInt("ad_aero", aero);
            PlayerPrefs.SetInt("ad_grip", grip);
            PlayerPrefs.SetInt("ad_rel", reliability);
            PlayerPrefs.SetInt("ad_compound", (int)compound);
            PlayerPrefs.SetFloat("ad_wing", wing);
            PlayerPrefs.SetFloat("ad_gear", gear);
            PlayerPrefs.SetFloat("ad_brake", brakeBias);
            PlayerPrefs.SetFloat("ad_rh", rideHeight); PlayerPrefs.SetFloat("ad_cam", camber);
            PlayerPrefs.SetFloat("ad_diff2", diff); PlayerPrefs.SetFloat("ad_prs", pressure);
            PlayerPrefs.SetInt("ad_track", trackIndex);
            PlayerPrefs.SetInt("ad_laps", laps);
            PlayerPrefs.SetInt("ad_driver", driver);
            PlayerPrefs.SetInt("ad_body", bodyColor);
            PlayerPrefs.SetInt("ad_accent", accentColor);
            PlayerPrefs.SetInt("ad_pattern", pattern);
            PlayerPrefs.SetInt("ad_diff", difficulty);
            PlayerPrefs.SetFloat("ad_vol", volume);
            PlayerPrefs.SetInt("ad_start", startType);
            PlayerPrefs.SetInt("ad_voices", voices ? 1 : 0);
            PlayerPrefs.SetInt("ad_rew", rewinds ? 1 : 0); PlayerPrefs.SetInt("ad_tod", raceTime); PlayerPrefs.SetInt("ad_adv", advHandling ? 1 : 0); PlayerPrefs.SetInt("ad_manual", manualGears ? 1 : 0); PlayerPrefs.SetInt("ad_spot", spotter ? 1 : 0);
            PlayerPrefs.SetFloat("ad_ew", engineWear); PlayerPrefs.SetFloat("ad_gw", gearboxWear);
            PlayerPrefs.SetFloat("ad_bw", brakeWear); PlayerPrefs.SetFloat("ad_duct", brakeDuct);
            PlayerPrefs.SetInt("ad_pu", partsUsed); PlayerPrefs.SetInt("ad_gp", gridPenalty);
            PlayerPrefs.SetInt("ad_endlaps", enduranceLaps);
            PlayerPrefs.Save();
        }

        public static float[] LoadBestSectors(string trackId)
        {
            var r = new float[3];
            for (int i = 0; i < 3; i++) r[i] = PlayerPrefs.GetFloat("ad_bs_" + trackId + "_" + i, -1f);
            return r;
        }

        public static void SaveBestSector(string trackId, int sector, float seconds)
        {
            PlayerPrefs.SetFloat("ad_bs_" + trackId + "_" + sector, seconds);
        }

        public static bool TutorialDone
        {
            get { return PlayerPrefs.GetInt("ad_tut", 0) == 1; }
            set { PlayerPrefs.SetInt("ad_tut", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static float BestLap(string trackId) { return PlayerPrefs.GetFloat("ad_bl_" + trackId, -1f); }
        public static void SaveBestLap(string trackId, float s) { PlayerPrefs.SetFloat("ad_bl_" + trackId, s); }

        // qualifying result per circuit: grid slot 0 (pole) .. 5, or -1 if not qualified
        public static int GridSlot(string trackId) { return PlayerPrefs.GetInt("ad_grid_" + trackId, -1); }
        public static void SaveGridSlot(string trackId, int slot) { PlayerPrefs.SetInt("ad_grid_" + trackId, slot); }
    }
}
