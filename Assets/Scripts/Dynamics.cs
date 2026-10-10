using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The deeper car model behind PlayerCar: four tyres with surface and core temperatures and their own loads, weight
    /// transfer (understeer and oversteer), the aero platform (ride height, bottoming, porpoising), fuel weight, the ERS
    /// maps, an eight-speed gearbox with a rev limiter, the driver's head moving under the g-forces and the spotter.
    /// PlayerCar calls Update() once per frame and reads the multipliers it produces.
    /// </summary>
    public class CarDynamics
    {
        const int FL = 0, FR = 1, RL = 2, RR = 3;
        readonly RaceManager rm;
        readonly PlayerCar p;
        TrackModel tm { get { return rm.track; } }

        // ---------------------------------------------------------------- tyres
        public readonly float[] surf = { 70f, 70f, 70f, 70f };      // surface temperature of FL, FR, RL, RR
        public readonly float[] core = { 70f, 70f, 70f, 70f };
        public readonly float[] load = { 0.25f, 0.25f, 0.25f, 0.25f };
        public float tempGrip = 1f;                // grip multiplier from the tyre temperatures
        public float blister;                      // 0..1 an overheated surface: more wear, less grip
        static readonly float[] CompoundHeat = { 1.15f, 1.0f, 0.85f, 0.9f, 0.8f };

        public float AvgSurface { get { return (surf[0] + surf[1] + surf[2] + surf[3]) * 0.25f; } }
        public float AvgCore { get { return (core[0] + core[1] + core[2] + core[3]) * 0.25f; } }

        public void SetTyres(float temp) { for (int i = 0; i < 4; i++) { surf[i] = temp; core[i] = temp; grainC[i] = 0f; blisC[i] = 0f; } blister = 0f; grain = 0f; }
        // graining: a cold tyre sliding tears rubber off in little balls; blistering: a cooked tyre bubbles. Each compound prefers one.
        public readonly float[] grainC = new float[4], blisC = new float[4];
        public float grain;
        static readonly float[] GrainProne = { 1.0f, 0.9f, 1.4f, 0.5f, 0.4f };       // soft, medium, hard, inter, wet
        static readonly float[] BlisterProne = { 1.5f, 1.0f, 0.55f, 1.6f, 1.6f };

        // ---------------------------------------------------------------- forces and balance
        public float longG, latG;                  // smoothed accelerations in g
        public float frontShare = 0.45f;           // share of the weight on the front axle
        public float turnMul = 1f;                 // steering authority (understeer takes some away)
        public float yawSlide;                     // rad/s of extra rotation (a slide)
        public float balance;                      // -1 understeer .. +1 oversteer (for the engineer)
        float lastSpeed, lastHeading; bool haveLast;
        float lastSteerSign = 1f;
        float understeerTime;

        // ---------------------------------------------------------------- aero platform
        public float rideF = 0.4f, rideR = 0.4f;   // 0 = on the ground, 1 = high
        public float aeroEff = 1f;                 // 1 = the platform is in its sweet spot
        public float porpoise;                     // 0..1
        public bool scraping; float scrapeUntil, bump;
        public float aeroFront = 0.5f;             // share of the downforce on the front axle

        // ---------------------------------------------------------------- fuel
        public float FuelMass
        {
            get
            {
                if (rm.fuelOn) return Mathf.Clamp01(p.fuel / 100f);
                if (rm.mode == RaceManager.Mode.Qualifying) return 0.2f;
                if (rm.mode == RaceManager.Mode.Practice) return 0.5f;
                float laps = Mathf.Max(1, rm.raceLaps);
                return 1f - 0.92f * Mathf.Clamp01(p.dist / (tm.roadLength * laps));
            }
        }
        /// <summary>How much the fuel weighs on performance: full in endurance races; in sprint races it is centred so the average lap is unchanged.</summary>
        public float FuelEffect { get { return (rm.Refuels ? FuelMass : 0.9f * (FuelMass - 0.5f)) * Regs.FuelMul; } }

        // ---------------------------------------------------------------- ERS
        public int ersMap = 1;                     // 0 harvest, 1 balanced, 2 attack
        public float ersAuto;                      // 0..1 automatic deployment on straights
        public static readonly string[] ErsNames = { "HARVEST", "BALANCED", "ATTACK" };
        public string ErsMapName { get { return ErsNames[ersMap]; } }
        public float ErsBoost { get { return Mathf.Max(p.ersDeploy ? 1f : 0f, ersAuto); } }
        public float ErsHarvest { get { return ersMap == 0 ? 1.6f : ersMap == 2 ? 0.7f : 1f; } }

        public void CycleErsMap()
        {
            ersMap = (ersMap + 1) % 3;
            rm.Radio(ersMap == 0 ? "Harvest mode: we are charging the battery." : ersMap == 1 ? "Balanced mode: we deploy a little on the straights."
                                                                                  : "Attack mode: full deployment on the straights, the battery will drain.", 3);
        }

        // ---------------------------------------------------------------- gearbox
        public int gear = 1; public float rpm = 4200f; public int shiftCount;
        public float torqueMul = 1f, engineBrake;
        public bool limiter;
        public float squeal;                       // 0..1 locking brakes / wheel lock-up chirp
        static readonly float[] GearTop = { 0.16f, 0.27f, 0.39f, 0.51f, 0.63f, 0.74f, 0.86f, 1.00f };
        float shiftCutUntil, lastShiftAt, lockUntil; int shiftRequest; float lowRevSince; bool wrongGearSaid;

        public void RequestShift(int dir) { shiftRequest = dir; }

        // ---------------------------------------------------------------- the driver's head
        public float headX, headY, headZ, headPitch, headRoll;
        float headVx, headVy, headVz;
        Vector3 headTarget; float pitchTarget, rollTarget;

        // ---------------------------------------------------------------- spotter
        bool occL, occR; float occLSince, occRSince, nextSpot;

        // radio calls with a cool-down per topic
        readonly Dictionary<string, float> said = new Dictionary<string, float>();
        bool Say(string key, string text, float now, float cooldown)
        {
            if (Staff.Engineer < 1) return false;                      // detailed advice needs a better race engineer
            float last;
            if (said.TryGetValue(key, out last) && now - last < cooldown) return false;
            said[key] = now;
            rm.Radio(text, 1);
            return true;
        }

        public CarDynamics(RaceManager manager, PlayerCar car)
        {
            rm = manager; p = car;
            SetTyres(car.tireTemp);
            ersMap = 1;
        }

        /// <summary>A bump in the road squashes the platform for a moment.</summary>
        public void AddBump(float amount) { bump = Mathf.Max(bump, amount); }

        public float LockPenalty { get { return Time.time < lockUntil ? 0.82f : 1f; } }

        // ================================================================ per frame
        public void Update(float dt, float now)
        {
            if (dt <= 0f) return;
            float v = p.speed;
            float ambient = 22f + rm.trackTemp * 0.55f;

            // ---- accelerations from what the car actually did
            float yawRate = 0f;
            if (haveLast)
            {
                float dh = Mathf.Repeat(p.heading - lastHeading + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;
                yawRate = dh / dt;
                float la = (v - lastSpeed) / dt / 9.81f;
                longG += (Mathf.Clamp(la, -6f, 3f) - longG) * Mathf.Min(1f, dt * 7f);
            }
            haveLast = true; lastSpeed = v; lastHeading = p.heading;
            float latNow = Mathf.Clamp(v * Mathf.Abs(yawRate) / 9.81f, 0f, 6f);
            latG += (latNow - latG) * Mathf.Min(1f, dt * 8f);
            if (Mathf.Abs(p.steerSmooth) > 0.1f) lastSteerSign = Mathf.Sign(p.steerSmooth);
            float steerSign = Mathf.Abs(p.steerSmooth) > 0.05f ? Mathf.Sign(p.steerSmooth) : lastSteerSign;
            if (p.airborne) { longG *= 0.9f; latG *= 0.9f; }

            // ---- where the weight is: braking loads the front, power the rear, corners load the outside tyres
            frontShare = Mathf.Clamp(0.45f + (-longG) * 0.045f + (aeroFront - 0.5f) * 0.04f, 0.32f, 0.62f);
            float lt = Mathf.Clamp(latG * 0.045f, 0f, 0.12f);            // lateral transfer, per axle
            bool leftTurn = steerSign > 0f;                              // left turn: the right-hand tyres are the outside ones
            float fl = frontShare * 0.5f + (leftTurn ? -lt : lt), fr = frontShare * 0.5f + (leftTurn ? lt : -lt);
            float rl = (1f - frontShare) * 0.5f + (leftTurn ? -lt : lt), rr = (1f - frontShare) * 0.5f + (leftTurn ? lt : -lt);
            load[FL] = Mathf.Max(0.04f, fl); load[FR] = Mathf.Max(0.04f, fr); load[RL] = Mathf.Max(0.04f, rl); load[RR] = Mathf.Max(0.04f, rr);

            TyreThermals(dt, v, ambient);
            AeroPlatform(dt, now, v);
            Balance(dt, now, v);
            Gearbox(dt, now, v);
            Ers(dt);
            Head(dt, v);
            Spotter(now, v);
            Chatter(now);
        }

        // ---------------------------------------------------------------- tyres
        void TyreThermals(float dt, float v, float ambient)
        {
            float ch = CompoundHeat[Mathf.Clamp((int)p.compound, 0, 4)];
            float brake = Mathf.Max(0f, -longG), power = Mathf.Max(0f, longG);
            float wet = rm.roadWetness;
            float lo = p.TempLowForCompound, hi = p.TempHighForCompound;
            float gripSum = 0f, wSum = 0f;
            int ci = Mathf.Clamp((int)p.compound, 0, 4);
            float grainSum = 0f;
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2;
                float l4 = load[i] * 4f;
                float slide = (p.overCorner ? 0.6f : 0f) + 0.4f * p.kerbAmt;
                float heat = l4 * (5.5f * latG + 5f * brake * (front ? 1.15f : 0.85f) + 4f * power * (front ? 0.6f : 1.3f) + 4f * slide + (v > 5f ? 1.2f : 0f)) * ch;
                float cool = (surf[i] - ambient) * (0.10f + 0.0030f * v + 0.30f * wet);
                surf[i] += (heat - cool - (surf[i] - core[i]) * 0.35f) * dt;
                core[i] += ((surf[i] - core[i]) * 0.22f - (core[i] - ambient) * 0.012f) * dt;
                surf[i] = Mathf.Clamp(surf[i], ambient - 4f, 175f); core[i] = Mathf.Clamp(core[i], ambient - 4f, 160f);

                float teff = 0.65f * surf[i] + 0.35f * core[i];
                float d = teff < lo ? (lo - teff) / 25f : teff > hi ? (teff - hi) / 25f : 0f;
                float g = 1f - 0.20f * Mathf.Clamp01(d);
                // graining: the surface is too cold while the tyre is worked hard; it clears once the tyre is warm again
                float coldGap = lo - surf[i];
                bool working = latG > 1.0f || brake > 0.7f || p.overCorner;
                if (coldGap > 5f && working && v > 20f) grainC[i] = Mathf.Min(1f, grainC[i] + dt * (0.015f + 0.05f * Mathf.Clamp01(coldGap / 25f)) * GrainProne[ci] * (0.5f + l4 * 0.5f));
                else if (surf[i] > lo + 3f) grainC[i] = Mathf.Max(0f, grainC[i] - dt * 0.03f);
                // blistering: the surface is cooked; it heals very slowly
                if (surf[i] > hi + 12f) blisC[i] = Mathf.Min(1f, blisC[i] + dt * (0.02f + 0.06f * Mathf.Clamp01((surf[i] - hi - 12f) / 30f)) * BlisterProne[ci]);
                else blisC[i] = Mathf.Max(0f, blisC[i] - dt * 0.01f);
                g *= 1f - 0.10f * blisC[i] - (front ? 0.10f : 0.07f) * grainC[i];
                gripSum += g * load[i]; wSum += load[i];
                grainSum += grainC[i] * load[i];
            }
            blister = Mathf.Max(Mathf.Max(blisC[0], blisC[1]), Mathf.Max(blisC[2], blisC[3]));
            grain = grainSum / Mathf.Max(0.01f, wSum);
            tempGrip = gripSum / Mathf.Max(0.01f, wSum);
            p.tireTemp = AvgSurface;
        }

        // ---------------------------------------------------------------- aero platform
        void AeroPlatform(float dt, float now, float v)
        {
            float baseRide = 0.55f + 0.30f * Setup.Norm(0);
            float sag = 0.08f * FuelMass;
            float comp = 0.30f * Mathf.Pow(Mathf.Clamp01(v / 80f), 2f);
            float dive = 0.10f * Mathf.Clamp01(-longG / 3f), squat = 0.05f * Mathf.Clamp01(longG);
            // bumps: kerbs and landings squash the platform for a moment
            float hit = p.kerbAmt * 0.35f * Mathf.Clamp01(v / 80f);
            bump = Mathf.Max(bump - dt * 6f, hit);
            float osc = Mathf.Sin(now * 38f) * 0.03f * porpoise;
            rideF = baseRide - sag - comp - dive - bump + osc;
            rideR = baseRide - sag - comp * 1.15f - squat - bump - osc;
            float avg = (rideF + rideR) * 0.5f, low = Mathf.Min(rideF, rideR);

            // too low at speed and the floor stalls: the car bounces on its own air (porpoising)
            if (avg < 0.10f && v > 60f) porpoise = Mathf.Min(1f, porpoise + dt * 1.2f); else porpoise = Mathf.Max(0f, porpoise - dt * 2f);
            // the floor touches the road
            if (low < 0.06f && v > 30f && !p.airborne && now > scrapeUntil)
            {
                scrapeUntil = now + 0.35f;
                p.ScrapeDamage(0.8f * (0.06f - low) * 100f * 0.1f + 0.3f);
                Say("scrape", "We are bottoming out, the floor is hitting the road. Raise the ride height.", now, 40f);
            }
            scraping = now < scrapeUntil;

            // the best downforce is a car running close to the ground: too high and too low both lose it
            float sens = Regs.AeroSens;                                              // ground-effect rules make the platform matter more
            float peak = 1f + 0.08f * sens;
            float eff = 1f + 0.08f * sens * (1f - Mathf.Abs(avg - 0.28f) / 0.2f);
            eff = Mathf.Clamp(eff, 1f - 0.16f * sens, peak);
            if (avg < 0.12f) eff *= 0.88f;
            if (scraping) eff *= 0.85f;
            eff *= 1f - 0.05f * porpoise * (0.5f + 0.5f * Mathf.Sin(now * 38f));
            aeroEff = eff / peak;                                                     // 1 = the sweet spot
            aeroFront = Mathf.Clamp(0.5f + (rideR - rideF) * 0.9f - 0.15f * (Mathf.Max(0f, -longG) < 0.1f && v > 50f && latG > 1.2f ? 1f : 0f), 0.25f, 0.75f);
        }

        /// <summary>The aero part of the cornering limit: matters at speed, none at a crawl.</summary>
        public float AeroForLimit(float speed) { return Mathf.Lerp(1f, aeroEff, Mathf.Clamp01(speed / 70f)); }

        // ---------------------------------------------------------------- weight transfer: understeer and oversteer
        void Balance(float dt, float now, float v)
        {
            turnMul = 1f; balance = 0f;
            if (!GameSave.advHandling || p.airborne || p.pitStopActive) { yawSlide = Mathf.MoveTowards(yawSlide, 0f, dt * 4f); return; }
            float fgrip = 0.5f * (GripOf(FL) + GripOf(FR)), rgrip = 0.5f * (GripOf(RL) + GripOf(RR));
            float frontCap = fgrip * (1f + 1.1f * (frontShare - 0.5f)) * (1f + 0.5f * (aeroFront - 0.5f)) * (1f - 0.35f * p.dirtyAir);
            float rearCap = rgrip * (1f - 1.1f * (frontShare - 0.5f)) * (1f - 0.5f * (aeroFront - 0.5f));
            if (p.inThrottle && v < 70f) rearCap *= 1f - 0.12f * (1f - v / 70f);          // wheelspin out of slow corners
            if (p.aqua > 0.1f) { frontCap *= 1f - 0.3f * p.aqua; rearCap *= 1f - 0.3f * p.aqua; }
            float bal = frontCap - rearCap;                                              // negative = the front gives up first
            // what the tyres can hold depends on the corner: v = sqrt(a * R), with a = 0.75 x the corner limit (the same rule the lap simulation uses)
            float radius = 12f / Mathf.Max(0.02f, tm.CurvatureAt(p.dist + 6f));
            float limit = Mathf.Max(5f, Mathf.Min(p.CornerLimitNow, Mathf.Sqrt(0.75f * p.CornerLimitNow * radius)));
            float ratio = v / limit;
            float excess = Mathf.Clamp01((ratio - 0.92f) / 0.35f);
            float steerMag = Mathf.Abs(p.steerSmooth);

            if (excess > 0f && steerMag > 0.15f)
            {
                if (bal < 0f)
                {
                    turnMul = 1f - 0.28f * excess * Mathf.Clamp01(0.4f - bal * 4f);
                    balance = -excess;
                    understeerTime += dt;
                }
                else
                {
                    float push = excess * Mathf.Clamp01(0.3f + bal * 4f);
                    yawSlide += Mathf.Sign(p.steerSmooth) * push * 1.1f * dt * 1.6f;
                    balance = excess;
                }
            }
            else understeerTime = Mathf.Max(0f, understeerTime - dt);
            // lifting or braking hard in a quick corner shifts the weight forward and lightens the rear
            if (!p.inThrottle && latG > 1.8f && v > 45f && steerMag > 0.15f)
            {
                float lift = Mathf.Clamp01((latG - 1.8f) / 2.5f) * (p.inBrake ? 1f : 0.6f) * (1f - aeroFront + 0.5f) * 0.5f;
                yawSlide += Mathf.Sign(p.steerSmooth) * lift * 1.1f * dt;
                balance = Mathf.Max(balance, lift);
            }
            // the driver catches it: steering against the slide takes it out, and it dies away by itself
            float against = (Mathf.Sign(yawSlide) != Mathf.Sign(p.steerSmooth) && steerMag > 0.2f) ? steerMag : 0f;
            yawSlide -= yawSlide * Mathf.Min(1f, dt * (1.0f + 3.2f * against));
            yawSlide = Mathf.Clamp(yawSlide, -0.45f, 0.45f);
            if (Mathf.Abs(yawSlide) < 0.01f) yawSlide = 0f;
            if (Mathf.Abs(yawSlide) > 0.25f) Say("snap", "Oversteer, be gentle with the throttle.", now, 25f);
            if (understeerTime > 1.8f) { if (Say("under", "We are understeering, the front is pushing wide. Brake earlier and turn in more gently.", now, 40f)) understeerTime = 0f; }
        }

        float GripOf(int i)
        {
            float lo = p.TempLowForCompound, hi = p.TempHighForCompound;
            float teff = 0.65f * surf[i] + 0.35f * core[i];
            float d = teff < lo ? (lo - teff) / 25f : teff > hi ? (teff - hi) / 25f : 0f;
            return 1f - 0.20f * Mathf.Clamp01(d);
        }

        // ---------------------------------------------------------------- gearbox
        void Gearbox(float dt, float now, float v)
        {
            float top = Mathf.Max(30f, p.TopSpeedDesign);
            int g = gear - 1;
            bool manual = GameSave.manualGears;
            torqueMul = 1f; limiter = false; squeal = Mathf.MoveTowards(squeal, 0f, dt * 3f);

            if (v < 2.5f && (p.inBrake || !p.inThrottle)) { if (gear != 1) { gear = 1; g = 0; } }
            else if (!manual)
            {
                if (now - lastShiftAt > 0.28f)
                {
                    int want = 7;
                    for (int k = 0; k < 8; k++) if (v <= GearTop[k] * top * 0.965f) { want = k; break; }
                    if (want > g) { gear++; g++; Shifted(now, true); }
                    else if (g > 0 && v < GearTop[g - 1] * top * 0.86f) { gear--; g--; Shifted(now, false); }
                }
            }
            else
            {
                if (shiftRequest != 0 && now - lastShiftAt > 0.12f)
                {
                    if (shiftRequest > 0 && g < 7) { gear++; g++; Shifted(now, true); }
                    else if (shiftRequest < 0 && g > 0)
                    {
                        float newRpm = 4200f + 8600f * v / Mathf.Max(1f, GearTop[g - 1] * top);
                        gear--; g--; Shifted(now, false);
                        if (newRpm > 13800f)
                        {
                            // a downshift into too low a gear: the engine is over-revved and the rear wheels lock up for a moment
                            p.AddEngineStress(1.4f);
                            p.LockUp(2.5f);
                            squeal = 1f; lockUntil = Time.time + 0.5f;
                            rm.Flash("OVER-REV");
                            Say("overrev", "Over-rev! Downshift later, we cannot afford to stress the engine.", now, 20f);
                        }
                    }
                }
                if (v > 8f && !p.inBrake && p.inThrottle && wrongGearSaid == false && 4200f + 8600f * v / Mathf.Max(1f, GearTop[g] * top) < 5500f)
                {
                    lowRevSince += dt;
                    if (lowRevSince > 1.2f) { wrongGearSaid = true; rm.Flash("WRONG GEAR"); }
                }
                else lowRevSince = 0f;
                if (lowRevSince == 0f && wrongGearSaid && v < 8f) wrongGearSaid = false;
            }
            shiftRequest = 0;

            float tg = Mathf.Max(1f, GearTop[g] * top);
            float rawRpm = 4200f + 8600f * Mathf.Clamp(v / tg, 0f, 1.25f);
            if (v < 6f) rawRpm = Mathf.Max(rawRpm, 4200f + (p.inThrottle ? 5200f : 0f) * (1f - v / 6f));
            rpm += (rawRpm - rpm) * Mathf.Min(1f, dt * 16f);

            // lugging in too high a gear
            if (manual && rawRpm < 5600f && p.inThrottle && v > 8f) torqueMul *= 0.45f;
            // the limiter: at the top of the last gear (or any gear held in manual) the power is cut in rapid pulses
            bool atTop = g == 7 && v > 0.985f * p.MaxSpeedNow;
            if (rawRpm > 12750f || (atTop && p.inThrottle))
            {
                limiter = true;
                float pulse = Mathf.Sin(now * 90f);
                torqueMul *= pulse > 0f ? 0.92f : 0.55f;
                rpm = 12400f + 450f * pulse;
            }
            if (now < shiftCutUntil) torqueMul *= 0.05f;                                  // torque is cut for a moment at every shift
            float cs = Mathf.Clamp01((rpm - 4200f) / 8600f);
            engineBrake = (!p.inThrottle && !p.inBrake) ? 4.2f * cs * (1f - 0.06f * gear) : 0f;
            // hard braking from speed with the front axle overloaded: a chirp from the front tyres
            if (p.inBrake && -longG > 3.4f && v > 25f) squeal = Mathf.Max(squeal, Mathf.Clamp01((-longG - 3.4f) / 1.2f));
        }

        void Shifted(float now, bool up)
        {
            lastShiftAt = now; shiftCount++;
            shiftCutUntil = now + (up ? 0.07f : 0.045f);
        }

        // ---------------------------------------------------------------- ERS maps
        void Ers(float dt)
        {
            ersAuto = 0f;
            if (p.pitStopActive || !p.inThrottle || p.ers < 8f || p.speed < 45f || rm.safetyCarActive || rm.vscActive) return;
            if (ersMap == 0) return;
            float curv = tm.CurvatureAt(p.dist + 12f);
            bool straight = curv < 0.06f && Mathf.Abs(p.steerSmooth) < 0.25f;
            if (!straight) return;
            ersAuto = ersMap == 2 ? 0.8f : 0.35f;
        }

        // ---------------------------------------------------------------- the driver's head
        void Head(float dt, float v)
        {
            float sp = Mathf.Clamp01(v / 90f);
            float side = Mathf.Sign(p.steerSmooth);
            // the head is thrown to the outside of a corner (the left of the car when turning right) and forward under braking
            headTarget.x = -side * Mathf.Min(latG, 4.5f) * 0.014f * (Mathf.Abs(p.steerSmooth) > 0.05f ? 1f : 0f);
            headTarget.z = -Mathf.Clamp(longG, -4f, 2f) * 0.010f;
            headTarget.y = -Mathf.Clamp(-longG, 0f, 4f) * 0.004f;
            pitchTarget = -Mathf.Clamp(longG, -4f, 2f) * 0.35f;
            rollTarget = side * Mathf.Min(latG, 4.5f) * 0.45f * (Mathf.Abs(p.steerSmooth) > 0.05f ? 1f : 0f);
            // a spring so the head lags the forces a little
            headX = Spring(headX, ref headVx, headTarget.x, dt);
            headZ = Spring(headZ, ref headVz, headTarget.z, dt);
            float vib = sp * sp * 0.0016f + p.rumble * 0.003f;
            headY = Spring(headY, ref headVy, headTarget.y, dt) + Mathf.Sin(Time.time * 61f) * Mathf.Sin(Time.time * 17f) * vib + porpoise * Mathf.Sin(Time.time * 38f) * 0.006f;
            headPitch += (pitchTarget - headPitch) * Mathf.Min(1f, dt * 6f) + porpoise * Mathf.Sin(Time.time * 38f) * 0.04f;
            headRoll += (rollTarget - headRoll) * Mathf.Min(1f, dt * 6f);
        }

        static float Spring(float x, ref float vel, float target, float dt)
        {
            const float k = 90f, c = 16f;
            vel += ((target - x) * k - vel * c) * dt;
            return x + vel * dt;
        }

        /// <summary>Moves the camera the way the driver's head moves.</summary>
        public void ApplyHead(Transform cam, bool cockpit)
        {
            float k = cockpit ? 1f : 0.3f;
            cam.position += (cam.right * headX + cam.forward * headZ + cam.up * headY) * k;
            cam.Rotate(headPitch * k, 0f, headRoll * k, Space.Self);
        }

        // ---------------------------------------------------------------- spotter: cars alongside, in the blind spot
        void Spotter(float now, float v)
        {
            bool l = false, r = false;
            if (GameSave.spotter && v > 12f && !p.pitStopActive)
            {
                Vector2 me = new Vector2(p.pos.x, p.pos.z);
                Vector2 fwd = new Vector2(Mathf.Sin(p.heading), Mathf.Cos(p.heading)), left = new Vector2(Mathf.Cos(p.heading), -Mathf.Sin(p.heading));
                foreach (var c in rm.racers)
                {
                    if (c == p || c.gone || c.pitStopActive || c.dnf) continue;
                    Vector3 w = c.go.transform.position;
                    Vector2 d = new Vector2(w.x, -w.z) - me;
                    float lon = Vector2.Dot(d, fwd), lat = Vector2.Dot(d, left);
                    if (Mathf.Abs(lon) > 4.3f) continue;
                    float al = Mathf.Abs(lat);
                    if (al < 1.3f || al > 6.8f) continue;
                    if (lat > 0f) l = true; else r = true;
                }
            }
            bool announced = false;
            if (now >= nextSpot)
            {
                if (l && r && !(occL && occR)) { rm.Radio("Three wide! Car left and car right.", 3); announced = true; nextSpot = now + 3f; }
                else if (l && !occL) { rm.Radio("Car left!", 3); announced = true; nextSpot = now + 4.5f; }
                else if (r && !occR) { rm.Radio("Car right!", 3); announced = true; nextSpot = now + 4.5f; }
                else if (!l && occL && now - occLSince > 1.2f) { rm.Radio("Clear left.", 0); announced = true; nextSpot = now + 1.6f; }
                else if (!r && occR && now - occRSince > 1.2f) { rm.Radio("Clear right.", 0); announced = true; nextSpot = now + 1.6f; }
                if (announced)
                {
                    if (l && !occL) occLSince = now;
                    if (r && !occR) occRSince = now;
                    occL = l; occR = r;
                }
            }
            // a car that was only there for a moment is forgotten without a call
            if (!announced)
            {
                if (!l && occL && now - occLSince <= 1.2f) occL = false;
                if (!r && occR && now - occRSince <= 1.2f) occR = false;
            }
        }

        // ---------------------------------------------------------------- engineer chatter about the new systems
        void Chatter(float now)
        {
            if (grain > 0.3f) Say("grain", "The tyres are graining: the surface is too cold and tearing. Brake a little harder and weave to get heat into them.", now, 60f);
            if (blister > 0.35f) Say("blister", "The surface of the tyres is blistering. Lift and ease the steering to let them cool.", now, 45f);
            if (Mathf.Min(rideF, rideR) < 0.12f && p.speed > 60f && porpoise > 0.5f) Say("porp", "The car is porpoising. Raise the ride height a little.", now, 45f);
        }

        /// <summary>Called when a lap is completed: the engineer reports what the falling fuel load is worth.</summary>
        public void OnLap(int lapNumber, float lapSec)
        {
            if (rm.IsRace) rm.bcast.Lap(lapNumber, lapSec);
            if (Staff.Engineer >= 3 && rm.IsRace && lapNumber >= 1)
            {
                float bestLap = p.bestLapThisRace;
                float delta = lapSec - bestLap;
                rm.Radio("Lap " + lapNumber + ", " + Hud.FormatTime(lapSec) + (delta < 0.05f ? ", that is your best of the race." : ", " + delta.ToString("0.0") + " off your best."), 0);
            }
        }
    }
}
