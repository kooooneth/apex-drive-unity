using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// Test-track data: every overheating, gearbox failure and heavy impact teaches the engineers something. Enough data unlocks a fix (three
    /// levels for each), so a failure that keeps happening gets less and less likely. Saved between races.
    /// </summary>
    public static class TestData
    {
        public static readonly string[] Names = { "Engine cooling", "Gearbox cooling", "Impact structure" };
        public static readonly string[] FixText =
        {
            "Each level cuts the engine's heat build-up by 10%.",
            "Each level cuts the gearbox's heat build-up by 10%.",
            "Each level makes the car take 12% less damage from hitting the wall."
        };
        public static readonly float[] Thresholds = { 8f, 20f, 40f };
        public static readonly float[] points = new float[3];

        public static void Load() { for (int i = 0; i < 3; i++) points[i] = PlayerPrefs.GetFloat("ad_td" + i, 0f); }
        public static void Save() { for (int i = 0; i < 3; i++) PlayerPrefs.SetFloat("ad_td" + i, points[i]); PlayerPrefs.Save(); }

        public static int Level(int i) { int l = 0; for (int k = 0; k < 3; k++) if (points[i] >= Thresholds[k]) l = k + 1; return l; }

        public static float EngineHeatMul { get { return 1f - 0.10f * Level(0); } }
        public static float GearboxHeatMul { get { return 1f - 0.10f * Level(1); } }
        public static float ImpactMul { get { return 1f - 0.12f * Level(2); } }

        /// <summary>Adds data; returns a message if that unlocked a new level of a fix.</summary>
        public static string Add(int kind, float amount)
        {
            int before = Level(kind);
            points[kind] += amount;
            int after = Level(kind);
            Save();
            return after > before ? "The test data gave the engineers a fix: " + Names[kind] + " level " + after + "." : null;
        }
    }

    /// <summary>
    /// The car's temperatures. The engine heats up under power (push mode, a hot track and a worn engine make it worse) and cools when you lift
    /// and coast; the gearbox heats with every gearshift. Warnings come first; ignore them and the engine blows or the gearbox sticks.
    /// </summary>
    public class CarHealth
    {
        readonly PlayerCar p; readonly RaceManager rm;
        public float engine = 40f, gearbox = 35f;          // temperatures (deg C scaled): comfortable below 80, critical above 100
        public bool gearStuck;
        float engineOver, gearOver, nextWarnE, nextWarnG; int lastShifts;

        public CarHealth(RaceManager manager, PlayerCar car) { rm = manager; p = car; }

        public void Reset() { engine = 45f; gearbox = 40f; gearStuck = false; engineOver = gearOver = 0f; }

        void Learn(int kind, float amount)
        {
            string msg = TestData.Add(kind, amount);
            if (msg != null) rm.Radio(msg, 1);
        }

        public void Update(float dt, float now)
        {
            if (p.pitStopActive) { engine = Mathf.MoveTowards(engine, 45f, dt * 8f); gearbox = Mathf.MoveTowards(gearbox, 40f, dt * 8f); return; }
            float speed = p.speed;

            // ---- engine ----
            float inK = p.inThrottle
                ? 1f + (p.engineMode == EngineMode.Push ? 0.45f : p.engineMode == EngineMode.Save ? -0.25f : 0f) + 0.5f * GameSave.engineWear / 100f + 0.012f * (rm.trackTemp - 30f) + (p.ersDeploy ? 0.15f : 0f)
                : 0.15f;
            float cool = (engine - 35f) * 0.045f * (0.5f + speed / 80f);
            engine = Mathf.Clamp(engine + (2.2f * inK * TestData.EngineHeatMul - cool) * dt, 30f, 130f);

            if (engine > 82f && now > nextWarnE)
            {
                nextWarnE = now + 22f;
                rm.Radio(engine > 93f ? "Engine overheating! Lift off now, or the engine will blow." : "The engine temperature is high. Lift and coast through the next corner, or switch to save mode.", engine > 93f ? 3 : 2);
                rm.Flash(engine > 93f ? "ENGINE CRITICAL" : "ENGINE HOT");
                Learn(0, 1f);
            }
            if (engine > 100f) engineOver += dt; else engineOver = Mathf.Max(0f, engineOver - dt * 0.5f);
            if (engineOver > 3.5f)
            {
                engineOver = 0f; engine = 85f;
                p.BlowEngine();
                Learn(0, 4f);
            }

            // ---- gearbox: every shift and every second at full power heats it ----
            int shifts = p.dyn.shiftCount;
            float shiftHeat = (shifts - lastShifts) * 1.6f; lastShifts = shifts;
            float inG = (p.inThrottle ? 0.9f : 0.1f) + 0.9f * GameSave.gearboxWear / 100f;
            gearbox = Mathf.Clamp(gearbox + (inG * TestData.GearboxHeatMul - (gearbox - 30f) * 0.04f * (0.6f + speed / 100f)) * dt + shiftHeat * TestData.GearboxHeatMul, 25f, 130f);
            if (gearbox > 85f && now > nextWarnG && !gearStuck)
            {
                nextWarnG = now + 25f;
                rm.Radio("The gearbox temperature is high. Fewer gearshifts, or ease off for a few seconds.");
                rm.Flash("GEARBOX HOT");
                Learn(1, 1f);
            }
            if (gearbox > 100f && !gearStuck) gearOver += dt; else gearOver = Mathf.Max(0f, gearOver - dt * 0.5f);
            if (gearOver > 3f && !gearStuck)
            {
                gearStuck = true; gearbox = 90f;
                rm.Radio("Gearbox failure! We are stuck in gear, nurse it back to the pits.", 3);
                rm.Flash("GEARBOX FAILURE");
                Learn(1, 4f);
            }
        }
    }
}
