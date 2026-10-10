using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The wind tunnel and simulator: an actual lap simulation of your car on a circuit, built from the same formulas the car physics uses
    /// (top speed, acceleration, braking, the steering rate that limits how fast a corner can be taken, the aero platform, tyre wear and
    /// how grip falls with wear). It drives a four-lap stint over the circuit's real corner radii and searches for the setup that gives
    /// the quickest stint, so the "ideal" values it reports are the true optimum of the game's own model for your car.
    /// It reads the car's stats (engine, aero and tyre levels, the team's car, R&amp;D, the regulation era), so the ideals change when the car does.
    /// </summary>
    public static class SetupLab
    {
        public const int COUNT = 6;
        public static readonly string[] Names = { "Ride height", "Camber", "Differential", "Tyre pressure", "Wing angle", "Gear ratio" };
        const int STINT = 4, SAMPLES = 240;

        public struct S { public float ride, camber, diff, pressure, wing, gear, brake; }

        class Prof { public float length, cornerShare, slowShare, rain; public float[] radius; }
        static readonly Dictionary<string, Prof> profs = new Dictionary<string, Prof>();
        static readonly Dictionary<string, float[]> cache = new Dictionary<string, float[]>();

        static Prof ProfileOf(string id)
        {
            Prof p;
            if (profs.TryGetValue(id, out p)) return p;
            TrackDef def = null;
            foreach (var d in Tracks.All) if (d.id == id) def = d;
            if (def == null) def = Tracks.All[0];
            var tm = new TrackModel(def);
            p = new Prof { length = tm.roadLength, radius = new float[SAMPLES], rain = def.rainChance };
            int corner = 0, slow = 0;
            for (int i = 0; i < SAMPLES; i++)
            {
                float d = tm.roadLength * i / SAMPLES;
                float ang = Mathf.Max(0.015f, tm.CurvatureAt(d));              // the angle the road turns in 12 m
                p.radius[i] = 12f / ang;
                if (p.radius[i] < 140f) corner++;
                if (p.radius[i] < 45f) slow++;
            }
            p.cornerShare = (float)corner / SAMPLES;                           // how much of the lap is spent in corners
            p.slowShare = (float)slow / SAMPLES;                               // ...and in slow, traction-limited ones
            profs[id] = p;
            return p;
        }

        // ---------------------------------------------------------------- the car, as the physics sees it
        static float N(float v) { return (v - 50f) / 50f; }

        struct Car
        {
            public float vmax, a0, brake, clBase, turnRate, wearBase, wingGrip, tf0, ride, camber, pressure;
            public const float LAT = 0.75f;          // lateral acceleration a car can hold, per unit of the corner limit (m/s^2)
        }

        static Car MakeCar(S s, float fit)
        {
            float P = GameSave.power, A = GameSave.aero, G = GameSave.grip, perf = TeamCareer.Perf;
            float n0 = N(s.ride), n1 = N(s.camber), n2 = N(s.diff), n3 = N(s.pressure);
            var c = new Car();
            float wingSpeed = 1.08f - s.wing / 100f * 0.16f, wingGrip = 0.9f + s.wing / 100f * 0.3f;
            float gearAccel = 1.15f - s.gear / 100f * 0.3f, gearSpeed = 0.92f + s.gear / 100f * 0.16f;
            c.vmax = (45f + P * 6.5f - A * 1.2f) * wingSpeed * gearSpeed * (1f + 0.012f * n0) * (0.985f + 0.03f * fit)
                     * (1f + 0.06f * perf) * (1f + 0.012f * TeamCareer.RdLevel(0)) * (1f - 0.10f * GameSave.engineWear / 100f) * Regs.Power;
            c.a0 = (6f + P * 1.1f) * gearAccel * (1f + 0.04f * n2) * (1f + 0.10f * perf) * (1f + 0.02f * TeamCareer.RdLevel(1)) * (1f - 0.06f * GameSave.gearboxWear / 100f);
            float biasK = 1f - Mathf.Abs(s.brake - 60f) / 100f * 0.25f;
            c.tf0 = Tuning.TireGrip[(int)Compound.Medium] * Regs.TyreGrip * (1f + 0.02f * n1) * (1f - 0.025f * n3);
            c.brake = (14f + G * 1.3f) * biasK;
            c.clBase = (20f + G * 3.4f + A * 2.6f) * wingGrip * (1f + 0.01f * Staff.Aero);
            c.turnRate = 0.68f * (1f - 0.05f * n2) * ApexDrive.Profile.TrainingBonus();
            c.wearBase = 0.015f * Tuning.TireWear[(int)Compound.Medium] * (1f + 0.10f * n1) * (1f - 0.06f * n3) * (1.06f - 0.12f * fit)
                         * (1f - 0.12f * perf) * (1f - 0.04f * TeamCareer.RdLevel(2)) * Regs.TyreWear;
            c.ride = s.ride; c.camber = s.camber; c.pressure = s.pressure; c.wingGrip = wingGrip;
            return c;
        }

        /// <summary>How much of the downforce the car keeps at this speed with this ride height (1 = the platform's sweet spot).</summary>
        static float AeroKeep(float rideSetting, float v)
        {
            float baseRide = 0.55f + 0.30f * N(rideSetting);
            float comp = 0.30f * Mathf.Pow(Mathf.Clamp01(v / 80f), 2f);
            float avg = baseRide - 0.04f - comp * 1.075f;                             // half a tank of fuel
            float sens = Regs.AeroSens, peak = 1f + 0.08f * sens;
            float eff = Mathf.Clamp(1f + 0.08f * sens * (1f - Mathf.Abs(avg - 0.28f) / 0.2f), 1f - 0.16f * sens, peak);
            if (avg < 0.12f) eff *= 0.88f;
            return Mathf.Lerp(1f, eff / peak, Mathf.Clamp01(v / 70f));
        }

        /// <summary>One lap on tyres with this much wear left: the lap time, and how much wear it uses.</summary>
        static float OneLap(Prof p, Car c, float wear, out float wearUsed)
        {
            int M = SAMPLES; float ds = p.length / M;
            var v = new float[M]; var lim = new float[M];
            float tf = (0.55f + 0.45f * Mathf.Clamp01((wear - 4f) / 100f)) * c.tf0;       // grip falls as the tyres wear
            float B = c.brake * tf;
            for (int i = 0; i < M; i++)
            {
                float vTurn = Mathf.Min(c.vmax, Mathf.Max(10f, c.turnRate * p.radius[i] * 0.95f));       // the steering rate limit
                float cl = c.clBase * tf * AeroKeep(c.ride, vTurn);
                float vGrip = Mathf.Sqrt(Car.LAT * cl * p.radius[i]);                                      // the tyres' limit: v = sqrt(a * R)
                lim[i] = Mathf.Max(10f, Mathf.Min(vTurn, vGrip));
            }
            // backward pass (braking), then forward pass (acceleration), twice round so the lap closes up
            for (int i = 0; i < M; i++) v[i] = lim[i];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int k = M * 2 - 1; k >= 0; k--)
                {
                    int i = k % M, j = (i + 1) % M;
                    float allowed = Mathf.Sqrt(v[j] * v[j] + 2f * B * ds);
                    if (v[i] > allowed) v[i] = allowed;
                }
                for (int k = 0; k < M * 2; k++)
                {
                    int i = k % M, j = (i + 1) % M;
                    float a = c.a0 * Mathf.Max(0.02f, 1f - (v[i] / c.vmax) * (v[i] / c.vmax));
                    float allowed = Mathf.Sqrt(v[i] * v[i] + 2f * a * ds);
                    if (v[j] > allowed) v[j] = allowed;
                }
            }
            float t = 0f; wearUsed = 0f;
            for (int i = 0; i < M; i++)
            {
                int j = (i + 1) % M;
                float vm = Mathf.Max(5f, 0.5f * (v[i] + v[j]));
                t += ds / vm;
                float cl = c.clBase * tf * AeroKeep(c.ride, vm);
                wearUsed += c.wearBase * ds * (vm > cl ? 1.6f : 1f);
            }
            return t;
        }

        /// <summary>A stint over the circuit: total time in seconds, the time of the first lap and the tyre wear left at the end.</summary>
        static void Stint(Prof p, Car c, out float total, out float firstLap, out float wearLeft)
        {
            total = 0f; firstLap = 0f; float wear = 100f;
            for (int lap = 0; lap < STINT; lap++)
            {
                float used;
                float t = OneLap(p, c, wear, out used);
                if (lap == 0) firstLap = t;
                total += t; wear = Mathf.Max(0f, wear - used);
            }
            wearLeft = wear;
        }

        // ---------------------------------------------------------------- laps on a given compound (for the strategy planner)
        static float FitFor(string id, S s)
        {
            float dev = Mathf.Abs(s.ride - Ideal(id, 0)) + Mathf.Abs(s.camber - Ideal(id, 1)) + Mathf.Abs(s.diff - Ideal(id, 2)) + Mathf.Abs(s.pressure - Ideal(id, 3));
            return Mathf.Clamp01(1f - dev / 4f / 50f);
        }

        /// <summary>The simulated time of one lap on a compound (0 soft, 1 medium, 2 hard) with this wear left, with the setup of the car for this circuit.</summary>
        public static float LapOnCompound(string id, int compound, float wearIn, out float wearOut)
        {
            var p = ProfileOf(id);
            var s = CarSetups.SetupOf(id);
            var c = MakeCar(s, FitFor(id, s));
            c.tf0 = Tuning.TireGrip[compound] * Regs.TyreGrip * (1f + 0.02f * N(s.camber)) * (1f - 0.025f * N(s.pressure));
            c.wearBase = c.wearBase / Tuning.TireWear[(int)Compound.Medium] * Tuning.TireWear[compound];
            float used;
            float t = OneLap(p, c, wearIn, out used);
            wearOut = Mathf.Max(0f, wearIn - used);
            return t;
        }

        /// <summary>The cost of a setup: the time of the four-lap stint (grip falls as the tyres wear, so a setup that is kind to the tyres is rewarded).</summary>
        static float Cost(Prof p, S s, float fit)
        {
            float total, first, left;
            Stint(p, MakeCar(s, fit), out total, out first, out left);
            return total;
        }

        // ---------------------------------------------------------------- the search
        static float Get(S s, int k) { return k == 0 ? s.ride : k == 1 ? s.camber : k == 2 ? s.diff : k == 3 ? s.pressure : k == 4 ? s.wing : s.gear; }
        static S Set(S s, int k, float v)
        {
            if (k == 0) s.ride = v; else if (k == 1) s.camber = v; else if (k == 2) s.diff = v; else if (k == 3) s.pressure = v; else if (k == 4) s.wing = v; else s.gear = v;
            return s;
        }

        static string Signature()
        {
            return GameSave.power + "_" + GameSave.aero + "_" + GameSave.grip + "_" + Mathf.RoundToInt(TeamCareer.Perf * 100f) + "_"
                 + TeamCareer.RdLevel(0) + TeamCareer.RdLevel(1) + TeamCareer.RdLevel(2) + "_" + Regs.EraIndex + "_" + Staff.Aero + "_"
                 + Mathf.RoundToInt(GameSave.engineWear / 10f) + "_" + Mathf.RoundToInt(GameSave.gearboxWear / 10f);
        }

        /// <summary>A short code for the car as it is now: wind-tunnel readings are only valid for the car they were made on.</summary>
        public static string CarCode { get { return Signature(); } }

        /// <summary>
        /// The best setup for this circuit and this car (ride, camber, diff, pressure, wing, gear), 0..100 each. Ride height, wing and gear
        /// come from the lap simulation (the quickest stint, searched until it settles). Camber, differential and pressure are set by the
        /// character of the circuit: more of the lap in corners wants more camber, more slow corners want a more locked differential,
        /// hot or wet conditions want lower pressure. The car's own setup rating (Setup.Fit) is centred on exactly these values.
        /// </summary>
        public static float[] Optimum(string id)
        {
            string key = id + "|" + Signature();
            float[] r;
            if (cache.TryGetValue(key, out r)) return r;
            var p = ProfileOf(id);
            float camber = Mathf.Clamp(22f + 70f * p.cornerShare, 15f, 90f);
            float diff = Mathf.Clamp(30f + 55f * p.slowShare + 25f * p.cornerShare, 20f, 85f);
            float pressure = Mathf.Clamp(62f - 30f * p.rain - 14f * p.cornerShare, 25f, 75f);
            var s = new S { ride = 50f, camber = camber, diff = diff, pressure = pressure, wing = 50f, gear = 50f, brake = 60f };
            int[] free = { 0, 4, 5 };                                                // ride height, wing, gear
            for (int round = 0; round < 4; round++)
            {
                foreach (int k in free)
                {
                    float bestV = Get(s, k), bestC = float.MaxValue;
                    for (int x = 0; x <= 100; x += 5) { float c = Cost(p, Set(s, k, x), 1f); if (c < bestC - 1e-4f) { bestC = c; bestV = x; } }
                    float lo = Mathf.Max(0f, bestV - 5f), hi = Mathf.Min(100f, bestV + 5f);
                    for (float x = lo; x <= hi + 0.01f; x += 1f) { float c = Cost(p, Set(s, k, x), 1f); if (c < bestC - 1e-4f) { bestC = c; bestV = x; } }
                    s = Set(s, k, bestV);
                }
            }
            r = new[] { s.ride, s.camber, s.diff, s.pressure, s.wing, s.gear };
            cache[key] = r;
            return r;
        }

        public static float Ideal(string id, int k) { return Optimum(id)[Mathf.Clamp(k, 0, COUNT - 1)]; }

        static S CurrentSetup()
        {
            return new S { ride = GameSave.rideHeight, camber = GameSave.camber, diff = GameSave.diff, pressure = GameSave.pressure, wing = GameSave.wing, gear = GameSave.gear, brake = GameSave.brakeBias };
        }

        /// <summary>Seconds per lap (over the stint) you would gain by setting just this one value to its ideal, with everything else as it is now.</summary>
        public static float GainIfSet(string id, int k, float fit)
        {
            var p = ProfileOf(id);
            var cur = CurrentSetup();
            float now = Cost(p, cur, fit);
            float best = Cost(p, Set(cur, k, Ideal(id, k)), fit);
            return Mathf.Max(0f, (now - best) / STINT);
        }

        /// <summary>The simulated lap time (first lap, fresh tyres) of the current setup, and of the best possible setup.</summary>
        public static void Estimate(string id, float fit, out float lapNow, out float lapBest, out float gainPerLap)
        {
            var p = ProfileOf(id);
            float total, left;
            Stint(p, MakeCar(CurrentSetup(), fit), out total, out lapNow, out left);
            float costNow = Cost(p, CurrentSetup(), fit);
            var o = Optimum(id);
            var best = new S { ride = o[0], camber = o[1], diff = o[2], pressure = o[3], wing = o[4], gear = o[5], brake = 60f };
            float t2, l2; Stint(p, MakeCar(best, 1f), out t2, out lapBest, out l2);
            float costBest = Cost(p, best, 1f);
            gainPerLap = Mathf.Max(0f, (costNow - costBest) / STINT);
        }
    }
}
