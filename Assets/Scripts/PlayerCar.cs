using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    public abstract class Racer
    {
        public GameObject go;
        public bool isPlayer;
        public string label;
        public float dist, speed;
        public float tireWear = 100f;
        public Compound compound = Compound.Medium;
        public bool pitStopActive;
        public float pitStopStart, pitStopEnd, pitStopStartWear;
        public int lap;
        public float finishTime = -1f;      // race clock when this car crossed the line after the last lap
        public int pitBox = -1;             // the pit box this car is stopped in (for the crew)
        public bool dnf, gone;              // retired from the race / taken away by the recovery truck
        public int pitCount, stintStartLap; // stops made, and the lap the current set of tyres went on
        public float drsOpen;               // 0 shut .. 1 open: the rear wing flap
    }

    public enum EngineMode { Balanced, Push, Save }

    /// <summary>Player physics, ported from updatePlayer() in the HTML game. Works in web space, see TrackModel.</summary>
    public class PlayerCar : Racer
    {
        readonly RaceManager rm;
        TrackModel tm { get { return rm.track; } }
        WheelRig wheels;
        Transform frontWing;

        // state in web space
        public Vector3 pos;
        public float heading;
        public float steerSmooth, pitchSmooth;
        public float damage;
        public float wallStunUntil, shakeUntil, hiccupUntil;
        public bool airborne; public float vY;
        public float offTrackSince, penaltySeconds; public int trackLimitCount;
        public EngineMode engineMode = EngineMode.Balanced;
        public bool offTrack, drsAvailable, drsActive, overCorner;
        public DrsTracker drs;
        public float headWind, crossWind;                   // m/s: wind against the car's direction, and sideways (positive to the right)
        float burnEma;                                      // fuel used per metre, smoothed
        public float draft;
        public float towCharge;           // 0..1: builds the longer you sit in a car's slipstream
        public float slingshot;           // 0..1: burst of pace after you pull out of a tow to pass
        public float launchBonus;         // -1..1: how good the getaway at the lights was (negative = wheelspin / bogged, fades over a few seconds)

        // damage model: front wing (wingDamage) + body (damage) + floor + engine + a puncture
        public float floorDamage;         // 0..100: lost downforce and top speed, wears the tyres
        public float engineDamage;        // 0..100: lost power - only a new car fixes this
        public bool punctured; public int punctureWheel;
        bool floorWarned, engineWarned;
        public int plannedCompound = -1;  // tyre chosen over the radio for the next stop
        public float pushReturnAt;        // a "push" radio call ends at this clock time
        public bool skipNextLapRecord;    // after a restart the first line-crossing is not a real lap
        public float launchRev = -1f;     // revs held at the lights (standing starts)
        bool unsafeRelease;
        float repairWing0, repairFloor0, repairEngine0, repairBody0;      // the damage the crew started with, so it can be repaired gradually during the stop
        public string pitWork = "";                                      // what the crew is doing, for the screen
        public float pitAlong;            // metres along the pit lane from the middle of the box row (negative = before it), from the real position
        float setupFit;                   // 0..1 how close the setup is to this circuit's ideal
        public float bodyPitch, bodyRoll, flipRoll;     // degrees: the body dives under braking, squats under power, rolls in corners (flipRoll: a crash)
        float lastSpeedPitch;
        public float aqua; float nextAquaFlash;         // 0..1 aquaplaning
        public float crashSpin;                         // deg/s of roll while launched by a wheel-to-wheel touch
        int crewStage;                                  // how far through its radio chatter the pit crew is
        public float pushTime, activeTime;              // seconds spent in push mode / on the move (engine wear after the race)
        public float wearPerLap, wearAtLapStart = 100f;  // tyre wear per lap, learnt as you drive (for the pit wall)
        public Racer yieldTo; public float yieldUntil;  // team orders: let this teammate through
        public int lostWheel = -1;                      // 0 front-left, 1 front-right, 2 rear-left, 3 rear-right
        public const int ASSIGNED_BOX = 2;          // your team's pit box (box 3): the pit assigns it to you
        public int targetBox = -1;                  // the box you are heading for
        public bool onKerb; public float kerbAmt;   // 0..1 how far onto the kerb / edge line
        public float rumble;              // 0..1 camera shake from kerbs
        public float bestCleanLap = -1f;  // the quickest lap without an incident (for the practice challenges)
        public CarHealth health;          // engine and gearbox temperatures, warnings and failures
        public float surfaceGrip = 1f;    // tarmac patches: old tarmac has a little less grip, new tarmac a little more, wet drain covers much less
        bool onBumpNow;
        public CarParts parts;            // mirrors, end plates and bargeboards that can be knocked off
        public bool setPbThisRace;        // a personal best lap was set in this race (for the sponsors)
        public CarDynamics dyn;           // tyre thermals, weight transfer, aero platform, gearbox, ERS maps, head motion, spotter

        // endurance races: fuel burns off with distance, the driver tires with time
        public float fuel = 100f, fuelTarget = 100f;     // fuel units 0..100, and how much to take at the next stop
        public float fatigue;                             // 0..1 driver tiredness
        public bool swapPlanned, outOfFuel;               // relief driver at the next stop
        // practice sessions: clean laps (no wall hits or track-limit offences)
        public int incidents, cleanLaps; int lapStartIncidents;

        public void CycleFuelTarget()
        {
            fuelTarget = fuelTarget > 85f ? 70f : fuelTarget > 55f ? 45f : 100f;
            rm.Radio("Fuel target: " + FuelTargetName, 3);
        }
        public string FuelTargetName { get { return fuelTarget > 85f ? "FULL" : fuelTarget > 55f ? "70%" : "45%"; } }

        // telemetry: one trace per completed lap
        public readonly List<LapTrace> traces = new List<LapTrace>();
        LapTrace curTrace = new LapTrace();
        float traceTimer;
        public bool inPitLaneNow;

        // realism state
        public float tireTemp = 70f;      // deg C - each compound has a window it works best in
        public float brakeTemp = 250f;    // deg C - hot brakes fade, cold brakes bite less
        public float brakeWearRace;       // % of brake wear used in this race (added to the garage's wear when it ends)
        public float ers = 60f;           // battery 0..100 (harvested under braking, spent on the overtake button)
        public bool ersDeploy;
        public float dirtyAir;            // 0..1 - following another car closely wrecks front-end grip
        public float wingDamage;          // 0..100 - broken front wing costs downforce
        public float slip;                // unused now (the car no longer pushes wide)
        int wingStage;

        // pit stop helpers
        public bool pitRequest, pitAssist;     // B = "box this lap": guides you into the lane and stops you in the box
        public float pitLift;                  // metres the car is jacked up
        public string pitGuide = "";
        bool pitDone;

        // lap + sector timing
        public float surfaceY;
        public float lapStart;
        List<Vector2> lapSamples = new List<Vector2>();
        float lastSampleAt;
        public readonly List<float> lapTimes = new List<float>();
        public float bestLapThisRace = -1f;
        int lastSector; float sectorStart;
        public readonly string[] sectorText = { "S1", "S2", "S3" };
        public readonly int[] sectorState = { 0, 0, 0 }; // 0 none, 1 best, 2 slower
        float[] bestSectors;

        // input (filled by RaceManager each frame)
        public bool inThrottle, inBrake, inLeft, inRight, inDrs, inDeploy;

        public PlayerCar(RaceManager manager, GameObject visual, float startDist, float gridOffset)
        {
            rm = manager; go = visual; isPlayer = true; label = RacerProfile.TagName;
            setupFit = Setup.Fit(manager.track.def.id);
            wheels = visual.GetComponent<WheelRig>();
            foreach (var t in visual.GetComponentsInChildren<Transform>()) if (t.name == "frontWing") frontWing = t;
            compound = GameSave.compound;
            dist = startDist;
            Vector2 p = tm.PointAt(startDist), t2 = tm.TangentAt(startDist), n = TrackModel.Normal(t2);
            Vector2 sp = p + n * gridOffset;
            pos = new Vector3(sp.x, tm.ElevationAt(startDist), sp.y);
            heading = Mathf.Atan2(t2.x, t2.y);
            bestSectors = GameSave.LoadBestSectors(tm.def.id);
            dyn = new CarDynamics(manager, this);
            health = new CarHealth(manager, this);
            parts = new CarParts(visual);
            ApplyTransform();
        }

        // ----- stats / setup -----
        float WingSpeed() { return 1.08f - (GameSave.wing / 100f) * 0.16f; }
        float WingGrip() { return 0.9f + (GameSave.wing / 100f) * 0.3f; }
        float GearAccel() { return 1.15f - (GameSave.gear / 100f) * 0.3f; }
        float GearSpeed() { return 0.92f + (GameSave.gear / 100f) * 0.16f; }
        float BrakeBias() { return 1f - Mathf.Abs(GameSave.brakeBias - 60f) / 100f * 0.25f; }
        float DamageMult() { return 1f - damage / 100f * 0.22f; }
        float EngineAccel() { return engineMode == EngineMode.Push ? 1.15f : engineMode == EngineMode.Save ? 0.85f : 1f; }

        float MaxSpeed()
        {
            float v = (45f + GameSave.power * 6.5f - GameSave.aero * 1.2f) * WingSpeed() * GearSpeed()
                      * (1f + draft + slingshot * 0.10f + (drsActive ? 0.09f : 0f) + 0.07f * dyn.ErsBoost * Regs.ErsMul) * DamageMult() * WindField.TopSpeedMul(headWind)
                      * (1f - 0.10f * floorDamage / 100f) * (1f - 0.08f * engineDamage / 100f) * (1f - 0.05f * dyn.FuelEffect)
                      * (1f + 0.012f * Setup.Norm(0)) * (0.985f + 0.03f * setupFit)        // ride height (drag) and how good the setup is
                      * (1f + 0.06f * TeamCareer.Perf) * (1f + 0.012f * TeamCareer.RdLevel(0))   // the team's car and its aero development
                      * (1f - 0.10f * GameSave.engineWear / 100f) * Regs.Power * (1f - 0.015f * GameSave.brakeDuct / 100f);        // big brake ducts cost a little drag                    // a tired engine, and the era's engine rules
            if (rm.fuelOn && fuel <= 0f) v = Mathf.Min(v, 22f);                // out of fuel: limp home
            if (lostWheel >= 0) v *= 0.5f;                                      // three wheels and a prayer
            if (yieldTo != null)                                                // team orders: let the teammate by
            {
                if (rm.Now > yieldUntil || yieldTo.dist > dist + 5f) yieldTo = null;
                else if (TrackModel.Mod(dist - yieldTo.dist, tm.roadLength) < 70f) v = Mathf.Min(v, yieldTo.speed * 0.95f);
            }
            if (engineDamage >= 99f) v = Mathf.Min(v, 14f);                      // a dead engine: limp to the pits
            if (health != null && health.gearStuck) v *= 0.75f;                  // stuck in gear
            if (rm.rolling) v = Mathf.Min(v, 31f);
            if (rm.vscActive) v = Mathf.Min(v, Tuning.VSC_SPEED_CAP);
            return rm.safetyCarActive ? Mathf.Min(v, rm.SafetyCarCapFor(this)) : v;
        }
        public float MaxSpeedNow { get { return MaxSpeed(); } }
        float Accel() { return (6f + GameSave.power * 1.1f) * GearAccel() * EngineAccel() * (1f + 0.25f * dyn.ErsBoost * Regs.ErsMul) * dyn.torqueMul * (health != null && health.gearStuck ? 0.6f : 1f) * (1f + slingshot * 0.6f) * (1f + launchBonus * 0.4f) * (1f - 0.25f * engineDamage / 100f) * (1f - 0.07f * dyn.FuelEffect) * (1f + 0.04f * Setup.Norm(2))
                                       * (1f + 0.10f * TeamCareer.Perf) * (1f + 0.02f * TeamCareer.RdLevel(1)) * (1f - 0.06f * GameSave.gearboxWear / 100f); }

        // tyre working window per compound (soft runs hottest-hungry, hard is happy cooler) - outside it grip drops up to 20%
        static readonly float[] TempLo = { 85f, 80f, 75f, 62f, 50f };
        static readonly float[] TempHi = { 105f, 100f, 95f, 82f, 68f };
        public float TempLowForCompound { get { return TempLo[(int)compound]; } }
        public float TempHighForCompound { get { return TempHi[(int)compound]; } }
        float TempGrip() { return dyn.tempGrip; }                     // the four tyres' own temperatures, see CarDynamics
        float TireFactor() { return surfaceGrip * (0.55f + 0.45f * (tireWear / 100f)) * Tuning.TireGrip[(int)compound] * Tuning.WetGrip(compound, rm.roadWetness) * TempGrip() * Regs.TyreGrip * (punctured ? 0.8f : 1f) * (1f + 0.02f * Setup.Norm(1)) * (1f - 0.025f * Setup.Norm(3)); }

        // brakes fade when overheated (over ~800C) and bite a little less when stone cold
        float BrakeFade()
        {
            if (brakeTemp > 800f) return 1f - 0.45f * Mathf.Clamp01((brakeTemp - 800f) / 500f);
            if (brakeTemp < 300f) return 0.88f + 0.12f * Mathf.Clamp01(brakeTemp / 300f);
            return 1f;
        }
        // worn discs and pads bite less, and past 85% wear they start to fade
        float BrakeWearFactor() { float w = Mathf.Clamp(GameSave.brakeWear + brakeWearRace, 0f, 100f); return 1f - 0.12f * w / 100f - (w > 85f ? 0.15f * (w - 85f) / 15f : 0f); }
        float BrakeDecel() { return (14f + GameSave.grip * 1.3f) * TireFactor() * BrakeBias() * BrakeFade() * BrakeWearFactor() * (1f - 0.1f * fatigue) * (1f - 0.6f * aqua) * (1f - 0.07f * dyn.FuelEffect); }
        float CornerLimit() { return (20f + GameSave.grip * 3.4f + GameSave.aero * 2.6f) * TireFactor() * WingGrip() * (1f - 0.22f * wingDamage / 100f) * (1f - 0.14f * dirtyAir)
                                    * dyn.AeroForLimit(speed) * (1f - 0.05f * dyn.FuelEffect) * WindField.GripMul(headWind) * dyn.LockPenalty * (1f + 0.01f * Staff.Aero) * (1f - 0.025f * parts.LostCount("barge")); }
        public float CornerLimitNow { get { return CornerLimit() * tm.CrestFactorAt(dist); } }
        public float TopSpeedDesign { get { return (45f + GameSave.power * 6.5f - GameSave.aero * 1.2f) * WingSpeed() * GearSpeed() * 1.08f; } }
        public void ScrapeDamage(float a) { AddFloorDamage(a); }
        public void AddEngineStress(float a) { GameSave.engineWear = Mathf.Min(100f, GameSave.engineWear + a); }
        public void LockUp(float wear) { tireWear = Mathf.Max(0f, tireWear - wear); }

        float DrsGapAhead()
        {
            float best = float.MaxValue;
            foreach (var c in rm.racers)
            {
                if (c == this || c.gone) continue;
                float ahead = TrackModel.Mod(c.dist - dist, tm.roadLength);
                if (ahead > 0.5f && ahead < best) best = ahead;
            }
            return best;
        }

        // ---- slipstream and the slingshot ----
        // Sitting in the air-hole of a car ahead (within ~22 m and roughly in its line) cuts your drag, and the
        // longer you stay there the more the tow is worth. Pull out to the side and that stored tow is released
        // as a slingshot: extra acceleration and top speed for a couple of seconds, enough to complete the pass.
        float slingLeft, slingPower;
        const float SLING_TIME = 2.6f;
        void UpdateTow(float dt)
        {
            Vector2 me = new Vector2(pos.x, pos.z);
            Vector2 right = new Vector2(Mathf.Cos(heading), -Mathf.Sin(heading));
            float strength = 0f; bool pullingOut = false;
            foreach (var c in rm.racers)
            {
                if (c == this || c.gone || c.pitStopActive) continue;
                float ahead = TrackModel.Mod(c.dist - dist, tm.roadLength);
                if (ahead < 2f || ahead > 26f) continue;
                Vector3 w = c.go.transform.position;
                float lat = Mathf.Abs(Vector2.Dot(new Vector2(w.x, -w.z) - me, right));
                if (lat <= 2.6f && ahead <= 22f) strength = Mathf.Max(strength, (1f - ahead / 22f) * (1f - 0.6f * lat / 2.6f));
                else if (lat > 1.8f && lat < 5f) pullingOut = true;
            }
            if (strength > 0.1f) towCharge = Mathf.Min(1f, towCharge + dt * 0.3f);
            else towCharge = Mathf.Max(0f, towCharge - dt * 0.5f);

            if (strength < 0.1f && pullingOut && towCharge > 0.5f && speed > 40f && slingLeft <= 0f)
            {
                slingPower = towCharge; slingLeft = SLING_TIME; towCharge = 0f;
                rm.Flash("SLINGSHOT!");
            }
            if (slingLeft > 0f) { slingLeft -= dt; slingshot = slingPower * Mathf.Clamp01(slingLeft / SLING_TIME); }
            else slingshot = 0f;
            draft = 0.14f * strength * (0.35f + 0.65f * towCharge);
        }

        // ---- getaway at the lights ----
        // Standing starts: hold the throttle during the countdown and release the clutch with the revs in the sweet
        // spot. Too few revs and the car bogs down; too many and it spins its wheels. With no rev meter (rolling
        // restarts, tutorial) it falls back to how quickly you get on the throttle.
        float launchStart = -1f, launchReact = -1f, launchLeft, launchBase;
        void UpdateLaunch(float dt, float now)
        {
            if (launchStart < 0f) return;
            if (launchReact < 0f)
            {
                if (launchRev >= 0f)
                {
                    launchReact = 0f;
                    if (launchRev > 0.97f) { launchBase = -0.75f; rm.Flash("WHEELSPIN"); }
                    else if (launchRev < 0.45f) { launchBase = -0.5f; rm.Flash("BOGGED DOWN"); }
                    else
                    {
                        float q = 1f - Mathf.Clamp01(Mathf.Abs(launchRev - 0.84f) / 0.2f);
                        launchBase = q;
                        rm.Flash(q > 0.85f ? "PERFECT LAUNCH" : q > 0.45f ? "GOOD LAUNCH" : "SLOW OFF THE LINE");
                    }
                    launchLeft = 3.5f;
                }
                else if (inThrottle || now - launchStart > 1.2f)
                {
                    launchReact = inThrottle ? now - launchStart : 99f;
                    launchBase = launchReact < 0.3f ? 1f : launchReact < 0.6f ? 0.5f : 0f;
                    launchLeft = 3.5f;
                    if (launchBase >= 1f) rm.Flash("PERFECT LAUNCH");
                    else if (launchBase > 0f) rm.Flash("GOOD LAUNCH");
                    else rm.Flash("SLOW OFF THE LINE");
                }
                else return;
            }
            if (launchLeft > 0f) { launchLeft -= dt; launchBonus = launchBase * Mathf.Clamp01(launchLeft / 3.5f + 0.35f); }
            else launchBonus = 0f;
        }

        float DraftBoost()
        {
            float best = DrsGapAhead();
            if (best > 14f) return 0f;
            return 0.12f * (1f - best / 14f);
        }

        // "dirty air": turbulence behind a car upsets your front wing. Only counts if you are actually behind
        // it (within ~30 m along the lap) and roughly in its wake sideways (within ~3 m).
        float DirtyAirTarget()
        {
            float best = 0f;
            Vector2 me = new Vector2(pos.x, pos.z);
            Vector2 right = new Vector2(Mathf.Cos(heading), -Mathf.Sin(heading));
            foreach (var c in rm.racers)
            {
                if (c == this || c.gone) continue;
                float ahead = TrackModel.Mod(c.dist - dist, tm.roadLength);
                if (ahead < 2f || ahead > 30f) continue;
                Vector3 w = c.go.transform.position;            // Unity world -> web space
                Vector2 them = new Vector2(w.x, -w.z);
                float lat = Mathf.Abs(Vector2.Dot(them - me, right));
                if (lat > 3f) continue;
                best = Mathf.Max(best, (1f - (ahead - 2f) / 28f) * (1f - 0.5f * lat / 3f));
            }
            return best;
        }

        /// <summary>Radio call or the B key: come in this lap, optionally with a chosen tyre (-1 = pick it yourself in the box).</summary>
        public void RequestPit(int compoundIdx)
        {
            plannedCompound = compoundIdx;
            if (!pitRequest && !pitStopActive) TogglePitRequest();
        }

        public void PushFor(float seconds, float now)
        {
            engineMode = EngineMode.Push; pushReturnAt = now + seconds;
        }

        /// <summary>Fuel saving: the engine runs lean for a while.</summary>
        public void SaveFor(float seconds, float now)
        {
            engineMode = EngineMode.Save; pushReturnAt = now + seconds;
        }

        /// <summary>How many laps of fuel you will have left at the flag at the current rate of use (negative: you will run dry before the end).</summary>
        public float FuelMarginLaps
        {
            get
            {
                if (!rm.fuelOn) return 0f;
                float remaining = Mathf.Max(0f, rm.raceLaps * tm.roadLength - dist);
                float rate = burnEma > 1e-7f ? burnEma : 100f / (tm.roadLength * rm.fuelLaps) * 0.8125f;       // fuel per metre
                return (fuel - remaining * rate) / (rate * tm.roadLength);
            }
        }

        /// <summary>Back on the grid after a red flag.</summary>
        public PlayerSnap Capture()
        {
            var s = new PlayerSnap
            {
                pos = pos, heading = heading, speed = speed, dist = dist, steer = steerSmooth, pitch = pitchSmooth, lapStart = lapStart, tireWear = tireWear, tireTemp = tireTemp,
                fuel = fuel, ers = ers, damage = damage, wing = wingDamage, floor = floorDamage, engine = engineDamage, lap = lap, lost = lostWheel, punctured = punctured, punctureWheel = punctureWheel
            };
            if (parts != null) s.parts = parts.LostKeys();
            return s;
        }

        /// <summary>A rewind: back to how the car was, damage included. The undone time does not count against the lap.</summary>
        public void Restore(PlayerSnap s, float elapsed)
        {
            pos = s.pos; heading = s.heading; speed = s.speed; dist = s.dist; steerSmooth = s.steer; pitchSmooth = s.pitch;
            lap = s.lap; lapStart = s.lapStart + elapsed;
            tireWear = s.tireWear; tireTemp = s.tireTemp; fuel = s.fuel; ers = s.ers;
            damage = s.damage; floorDamage = s.floor; engineDamage = s.engine; wingDamage = s.wing;
            wingStage = s.wing >= 70f ? 2 : s.wing >= 35f ? 1 : 0; AddWingDamage(0f);
            floorWarned = s.floor > 30f; engineWarned = s.engine > 25f;
            punctured = s.punctured; punctureWheel = s.punctureWheel;
            if (lostWheel >= 0 && s.lost < 0) { lostWheel = -1; if (wheels != null) wheels.LoseWheel(-1); }
            if (parts != null) { parts.SetLost(s.parts); rm.MirrorLost(-1, s.parts.Contains("mirrorL")); rm.MirrorLost(1, s.parts.Contains("mirrorR")); }
            airborne = false; vY = 0f; flipRoll = 0f; crashSpin = 0f; wallStunUntil = 0f; shakeUntil = 0f; hiccupUntil = 0f; offTrackSince = 0f; dyn.yawSlide = 0f;
            ApplyTransform();
        }

        public void ResetTo(float newDist, float offset)
        {
            Vector2 p = tm.PointAt(newDist), t2 = tm.TangentAt(newDist), n = TrackModel.Normal(t2);
            Vector2 sp = p + n * offset;
            pos = new Vector3(sp.x, tm.ElevationAt(newDist), sp.y);
            heading = Mathf.Atan2(t2.x, t2.y);
            dist = newDist; speed = 0f; lap = Mathf.FloorToInt(newDist / tm.roadLength);
            airborne = false; vY = 0f; steerSmooth = 0f;
            pitRequest = false; pitAssist = false; pitStopActive = false; pitLift = 0f;
            skipNextLapRecord = true;
            ApplyTransform();
        }

        /// <summary>Free tyres and repairs during a red flag (the engine stays damaged).</summary>
        public void RedFlagService()
        {
            tireWear = 100f; punctured = false; tireTemp = 70f; dyn.SetTyres(70f);
            floorDamage = 0f; floorWarned = false;
            wingDamage = 0f; wingStage = 0; AddWingDamage(0f); RestoreParts();
            if (wheels != null && wheels.sidewallMat != null) wheels.sidewallMat.mainTexture = Tex.Sidewall(compound);
        }

        /// <summary>His rear wheel against your front wheel: the car climbs it and flies. Big hits can tear a wheel off.</summary>
        void LaunchCrash(float closing, float side)
        {
            if (airborne) return;
            airborne = true;
            vY = Mathf.Min(9f, 3.5f + closing * 0.3f);
            crashSpin = side * (260f + closing * 12f);
            AddWingDamage(Mathf.Min(60f, closing * 3f));
            damage = Mathf.Min(100f, damage + closing * 0.9f);
            shakeUntil = rm.Now + 0.6f;
            rm.Radio("Wheel to wheel contact - the car is launched!", 3);
            rm.Commentate(rm.NameOf(this) + " is launched into the air! What a crash!", 2);
            rm.Flash("LAUNCHED!");
            rm.SpawnWingDebris(go.transform.position + go.transform.forward * 2f + Vector3.up * 0.4f, go.transform.right, 2);
            if (closing > 18f && Random.value < 0.6f) LoseWheel(side > 0f ? 1 : 0);
        }

        /// <summary>The wheel tether snaps: the wheel bounces away and the car limps.</summary>
        public void LoseWheel(int i)
        {
            if (lostWheel >= 0) return;
            lostWheel = i;
            if (wheels != null) wheels.LoseWheel(i);
            float sx = i % 2 == 0 ? -1f : 1f;
            Vector3 at = go.transform.position + go.transform.forward * (i < 2 ? 1.5f : -1.4f) + go.transform.right * sx + Vector3.up * 0.4f;
            rm.SpawnWheelDebris(at, go.transform.forward * Mathf.Max(8f, speed * 0.6f) + go.transform.right * sx * 5f + Vector3.up * 3f);
            AddFloorDamage(30f);
            rm.Radio("We have lost a wheel! Box, box - or R to recover.", 3);
            rm.Commentate(rm.NameOf(this) + " has lost a wheel!", 2);
            rm.Flash("WHEEL LOST");
        }

        void AddFloorDamage(float a)
        {
            floorDamage = Mathf.Min(100f, floorDamage + a);
            if (floorDamage > 30f && !floorWarned) { floorWarned = true; rm.Radio("Floor damage, we are losing downforce and top speed."); }
        }

        void AddEngineDamage(float a)
        {
            engineDamage = Mathf.Min(100f, engineDamage + a);
            if (engineDamage > 25f && !engineWarned) { engineWarned = true; rm.Radio("Engine damage, we have lost some power."); }
        }

        public void Puncture(int wheel)
        {
            if (punctured || pitStopActive) return;
            punctured = true; punctureWheel = wheel;
            rm.Radio("Puncture! " + (wheel % 2 == 0 ? "Front" : "Rear") + (wheel < 2 ? "-left" : "-right") + " tyre, box this lap.", 3);
            rm.Flash("PUNCTURE");
            rm.Commentate(rm.NameOf(this) + " has a puncture!", 2);
        }

        public void CycleEngineMode()
        {
            engineMode = engineMode == EngineMode.Balanced ? EngineMode.Push : engineMode == EngineMode.Push ? EngineMode.Save : EngineMode.Balanced;
        }

        public void ChooseCompound(Compound c)
        {
            if (compound == c) return;
            compound = c; GameSave.compound = c; GameSave.Save();
            rm.Radio(Tuning.CompoundNames[(int)c] + " tires going on", 1);
        }

        public void RecoverToCentre()
        {
            Vector2 near; int bi;
            tm.NearestCenter(new Vector2(pos.x, pos.z), out near, out bi);
            pos.x = near.x; pos.z = near.y;
            speed *= 0.3f;
            Vector2 t = tm.TangentAt((float)bi / TrackModel.N * tm.roadLength);
            heading = Mathf.Atan2(t.x, t.y);
        }

        // ----- main update -----
        public void Tick(float dt, float now)
        {
            if (rm.wind != null) rm.wind.Relative(new Vector2(Mathf.Sin(heading), Mathf.Cos(heading)), rm.Now, out headWind, out crossWind);
            UpdateTow(dt);
            UpdateLaunch(dt, now);
            if (speed > 5f) { activeTime += dt; if (engineMode == EngineMode.Push) pushTime += dt; }
            dirtyAir += (DirtyAirTarget() - dirtyAir) * Mathf.Min(1f, dt * 3f);

            // ERS: the overtake button spends the battery for extra pace; braking and coasting recharge it
            ersDeploy = inDeploy && ers > 2f && inThrottle && !pitStopActive;
            if (ersDeploy || dyn.ersAuto > 0f) ers -= 7f * Mathf.Max(ersDeploy ? 1f : 0f, dyn.ersAuto) * dt;
            if (!ersDeploy)
            {
                float charge = 0f;
                if (inBrake && speed > 5f) charge = 10f * Mathf.Clamp01(speed / 50f);
                else if (!inThrottle && speed > 5f) charge = 2.5f;
                charge *= (engineMode == EngineMode.Save ? 1.8f : engineMode == EngineMode.Push ? 0.6f : 1f) * dyn.ErsHarvest;
                ers += charge * dt;
            }
            ers = Mathf.Clamp(ers, 0f, 100f);

            float maxSpeed = MaxSpeed();
            float accel = Accel();
            float hiccup = now < hiccupUntil ? 0.25f : 1f;

            float pitchTarget = inThrottle ? 1f : inBrake ? -1f : 0f;
            pitchSmooth += (pitchTarget - pitchSmooth) * Mathf.Min(1f, dt * 4f);

            if (inThrottle)
            {
                float r = speed / maxSpeed;
                float drag = Mathf.Max(0.02f, 1f - r * r);
                speed += accel * hiccup * drag * dt;
            }
            else if (inBrake)
            {
                speed = Mathf.Max(0f, speed - BrakeDecel() * dt);
            }
            else
            {
                speed = Mathf.Max(0f, speed - (4f + dyn.engineBrake) * dt);
            }
            // gravity: climbing a hill costs speed, running downhill gives it back
            if (!airborne && !pitStopActive)
            {
                float slope = (tm.ElevationAt(dist + 1.5f) - tm.ElevationAt(dist - 1.5f)) / 3f;
                speed -= Tuning.GRAVITY * slope * 0.8f * dt;
            }
            // over the limit (the tow or a slingshot has just ended): ease back instead of snapping
            if (speed > maxSpeed) speed = Mathf.Max(maxSpeed, speed - (rm.safetyCarActive ? 25f : rm.vscActive ? 18f : rm.rolling ? 14f : 8f) * dt);
            speed = Mathf.Max(0f, speed);
            if (pitStopActive) speed = 0f;
            ApplyPitLimiterAndBoxBrake(dt);

            float steerInput = (inLeft ? 1f : 0f) - (inRight ? 1f : 0f);
            UpdatePitGuidance();
            steerInput = PitAssistSteer(steerInput);
            float speedFactor = Mathf.Clamp01(speed / 8f);
            bool stunned = now < wallStunUntil;
            // the chosen driver's stability aid, plus a bonus that grows with driver level (up to +15%)
            float assist = Drivers.All[Mathf.Clamp(GameSave.driver, 0, Drivers.All.Length - 1)].assist * Profile.TrainingBonus();
            float turnRate = 0.68f * assist * speedFactor * (stunned ? 0.15f : 1f) * (airborne ? 0.12f : 1f) * (1f - 0.05f * Setup.Norm(2)) * (1f - 0.8f * aqua) * dyn.turnMul * (1f - 0.04f * dyn.FuelEffect);
            if (punctured)
            {
                tireWear = Mathf.Max(0f, tireWear - 4f * dt);                                      // the tyre is losing air
                heading += (punctureWheel % 2 == 0 ? 1f : -1f) * 0.05f * speedFactor * dt;          // and the car pulls to one side
            }
            if (rm.fuelOn)
            {
                // fuel burns with distance (a lot less when you lift off), more in push mode; the car is also heavier when full
                float burn = (inThrottle ? 1f : 0.25f) * (engineMode == EngineMode.Push ? 1.25f : engineMode == EngineMode.Save ? 0.8f : 1f);
                float used = speed * dt * burn * 100f / (tm.roadLength * rm.fuelLaps);
                fuel = Mathf.Max(0f, fuel - used);
                if (speed > 8f && dt > 0f) { float perM = used / (speed * dt); burnEma = burnEma <= 0f ? perM : burnEma + (perM - burnEma) * Mathf.Min(1f, dt * 0.12f); }
                if (fuel <= 0f && !outOfFuel) { outOfFuel = true; rm.Radio(rm.Refuels ? "Out of fuel! Get it to the pits." : "Out of fuel! Lift and coast to the line.", 3); rm.Flash("OUT OF FUEL"); }
                if (fuel > 0f) outOfFuel = false;
                // the driver tires with time; kerbs, rain and pushing make it worse, fitness slows it down
                float est = Mathf.Max(60f, rm.raceLaps * tm.roadLength / 38f);
                float rate = (1f / (est * 1.15f)) * RacerProfile.FatigueMult * (1f + 0.25f * rm.roadWetness) * (1f + rumble) * (engineMode == EngineMode.Push ? 1.15f : 1f);
                fatigue = Mathf.Clamp01(fatigue + rate * dt);
                turnRate *= 1f - 0.2f * fatigue;                                                   // heavy steering when tired
                heading += Mathf.Sin(now * 23f) * Mathf.Sin(now * 9f + 0.7f) * 0.06f * fatigue * speedFactor * dt;
            }
            if (pushReturnAt > 0f && now > pushReturnAt) { pushReturnAt = 0f; engineMode = EngineMode.Balanced; rm.Radio("Back to balanced mode.", 1); }

            float limit = CornerLimit() * tm.CrestFactorAt(dist);
            overCorner = Mathf.Abs(speed) > limit;
            // The car always turns as far as you steer: no understeer (front pushing wide) and no oversteer.
            // Pushing past the tyre limit only costs tyre wear and heat.
            slip = 0f;
            steerSmooth += (steerInput - steerSmooth) * Mathf.Min(1f, dt * 5f * RacerProfile.SteerResponse);
            heading += steerSmooth * turnRate * dt + dyn.yawSlide * dt;
            heading = Mathf.Repeat(heading + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;

            // tyre and brake temperatures
            float heat = Mathf.Abs(steerSmooth) * speed * 0.6f + (inThrottle ? speed * 0.12f : 0f) + (inBrake ? speed * 0.35f : 0f) + (overCorner ? 20f : 0f);
            dyn.Update(dt, now);                                                                  // four tyres, weight transfer, aero platform, gearbox, ERS, head, spotter
            health.Update(dt, now);                                                               // engine and gearbox temperatures
            float brakeHeat = inBrake ? speed * 2.0f : 0f;
            float ductCool = 0.55f + 0.9f * GameSave.brakeDuct / 100f;                              // larger ducts cool the brakes better
            brakeTemp += (brakeHeat - (brakeTemp - 150f) * (0.03f + speed * 0.0012f) * ductCool) * dt;
            if (inBrake && speed > 5f) brakeWearRace += dt * 0.45f * (speed / 50f) * (brakeTemp > 750f ? 1.8f : 1f);       // braking wears the discs and pads
            brakeTemp = Mathf.Clamp(brakeTemp, 120f, 1400f);

            pos.x += Mathf.Sin(heading) * speed * dt;
            pos.z += Mathf.Cos(heading) * speed * dt;
            if (!airborne && speed > 5f)
            {
                // a crosswind shoves the car sideways (more at speed): you have to steer against it
                float shove = crossWind * 0.07f * Mathf.Clamp01(speed / 70f);
                pos.x += Mathf.Cos(heading) * shove * dt; pos.z -= Mathf.Sin(heading) * shove * dt;
            }

            // NOTE: there is deliberately no "magnet" pulling the car into the pit lane - the player
            // steers into it themselves (see inPitLaneNow below).

            // The road height comes from where the car physically IS along the track, not from the lap
            // odometer (dist keeps counting even when the car cuts across or runs wide). Reading the odometer
            // put the car at the wrong height on a slope, so it sank under the road surface on uphills.
            float along = tm.ProjectDist(new Vector2(pos.x, pos.z));
            float predicted = along + speed * dt;
            float groundY = tm.ElevationAt(predicted);
            surfaceY = groundY;
            if (!airborne)
            {
                float lookahead = Mathf.Max(10f, speed * 0.5f);
                float curSlope = (groundY - tm.ElevationAt(predicted - 2f)) / 2f;
                float projected = groundY + curSlope * lookahead;
                float actualAhead = tm.ElevationAt(predicted + lookahead);
                float gap = projected - actualAhead;
                if (gap > 1.6f && speed > Tuning.FLY_SPEED)
                {
                    airborne = true;
                    float peak = Mathf.Min(3.2f, Mathf.Max(0.5f, gap));
                    vY = curSlope * speed + Mathf.Sqrt(2f * Tuning.GRAVITY * peak);
                }
                else pos.y = groundY;
            }
            else
            {
                vY -= Tuning.GRAVITY * dt;
                pos.y += vY * dt;
                if (pos.y <= groundY)
                {
                    pos.y = groundY;
                    float impact = -vY;
                    vY = 0f; airborne = false;
                    if (impact > 3f) { shakeUntil = now + Mathf.Min(0.5f, impact * 0.04f); rm.Thud(); if (impact > 6f) AddFloorDamage(impact * 1.5f * (1f + Mathf.Max(0f, -Setup.Norm(0)))); }
                }
            }

            // off track: on the main road (to the curb) OR inside the pit lane counts as on track
            Vector2 nearest; int bestI;
            float lateral = tm.NearestCenter(new Vector2(pos.x, pos.z), out nearest, out bestI);
            // in the pit lane = physically close to the lane's centre line, and clearly off the racing surface
            // (the ends of the lane start and finish on the track itself)
            float laneDist = tm.DistToPitLane(new Vector2(pos.x, pos.z), out pitAlong);
            inPitLaneNow = laneDist <= TrackModel.PIT_LANE_HALFWIDTH + 1.5f && lateral >= 3.5f;

            // standing water: drive through a puddle fast on the wrong tyres and the car aquaplanes
            float aquaTarget = 0f;
            if (rm.puddles != null && rm.roadWetness > 0.5f && speed > 28f && !airborne && !inPitLaneNow)
            {
                float depth = rm.puddles.DepthAt(along, tm.SignedLateral(new Vector2(pos.x, pos.z)));
                float resist = compound == Compound.Wet ? 0.55f : compound == Compound.Inter ? 0.25f : 0f;
                aquaTarget = depth * Mathf.Clamp01((speed - 28f) / 35f) * (1f - resist);
            }
            aqua = Mathf.MoveTowards(aqua, aquaTarget, (aquaTarget > aqua ? 5f : 1.2f) * dt);
            if (aqua > 0.35f && now > nextAquaFlash) { nextAquaFlash = now + 3f; rm.Flash("AQUAPLANING!"); }
            if (aqua > 0.02f)
            {
                speed -= speed * 0.05f * aqua * dt;
                heading += Mathf.Sin(now * 17f) * Mathf.Sin(now * 7f + 2f) * 0.3f * aqua * dt;
            }

            // the road itself: patches of old and new tarmac, wet drain covers and bumps
            if (rm.road != null && !inPitLaneNow && !airborne)
            {
                float sLat = tm.SignedLateral(new Vector2(pos.x, pos.z));
                surfaceGrip = Mathf.MoveTowards(surfaceGrip, rm.road.GripAt(along, sLat, rm.roadWetness) * rm.LineGrip(along, sLat), dt * 6f);
                float bumpK = rm.road.BumpAt(along, sLat);
                bool onBump = bumpK > 0f && speed > 15f;
                if (onBump && !onBumpNow)
                {
                    shakeUntil = Mathf.Max(shakeUntil, now + 0.15f);
                    dyn.AddBump(bumpK * Mathf.Clamp01(speed / 80f) * 0.6f);
                    speed *= 1f - 0.004f * bumpK;
                    if (bumpK > 0.7f && speed > 70f) AddFloorDamage(0.6f);
                }
                onBumpNow = onBump;
                if (rm.roadWetness > 0.4f && speed > 30f && rm.road.OnDrain(along, sLat)) heading += (Random.value - 0.5f) * 0.07f;      // a wet drain cover twitches the car
            }
            else surfaceGrip = Mathf.MoveTowards(surfaceGrip, 1f, dt * 6f);

            // kerbs: the strip at the road edge shakes the car, scrubs speed and makes it twitchy - much worse in the wet
            float kerb = inPitLaneNow ? 0f : Mathf.Clamp01((lateral - 9.3f) / 1.4f);
            kerbAmt = kerb; onKerb = kerb > 0.05f;
            rumble = Mathf.MoveTowards(rumble, onKerb ? kerb * Mathf.Clamp01(speed / 40f) : 0f, dt * 8f);
            if (onKerb && speed > 5f && !airborne)
            {
                float wet = rm.roadWetness;
                speed -= speed * kerb * (0.03f + 0.07f * wet) * dt;
                float noise = Mathf.Sin(now * 53f) * Mathf.Sin(now * 31f + 1.3f);
                heading += noise * 0.35f * kerb * Mathf.Clamp01(speed / 50f) * (1f + 2.5f * wet) * RacerProfile.KerbMult * dt;
                if (kerb > 0.6f && speed > 60f && Mathf.Abs(steerSmooth) > 0.4f) AddFloorDamage(1.5f * dt * (1f + Mathf.Max(0f, -Setup.Norm(0)) * 1.5f));      // hammering the kerbs hurts the floor
            }
            // Off track only when 90% of the car is beyond the edge of the paved surface (road + kerb). The car is about
            // 2 m wide and 5.5 m long, so when it is angled to the track it sticks out sideways further.
            Vector2 trackTan = tm.TangentAt((float)bestI / TrackModel.N * tm.roadLength);
            float dAng = Mathf.Abs(Mathf.Repeat(heading - Mathf.Atan2(trackTan.x, trackTan.y) + Mathf.PI, Mathf.PI * 2f) - Mathf.PI);
            float halfExtent = Mathf.Abs(Mathf.Cos(dAng)) * 1.0f + Mathf.Abs(Mathf.Sin(dAng)) * 2.75f;
            float offLimit = TrackModel.CURB_OUTER + 0.8f * halfExtent;     // centre distance at which 90% of the car is outside
            offTrack = lateral > offLimit && !inPitLaneNow;
            if (offTrack)
            {
                speed = Mathf.Max(0f, speed - 10f * dt);
                if (offTrackSince <= 0f) offTrackSince = now;
            }
            else if (offTrackSince > 0f)
            {
                if (now - offTrackSince > 0.4f)
                {
                    trackLimitCount++; incidents++;
                    if (trackLimitCount % 3 == 0) { penaltySeconds += 2f; rm.Radio("Track limits - 2 second penalty added"); }
                    else rm.Radio("Track limits warning, " + (trackLimitCount % 3) + "/3", 1);
                }
                offTrackSince = 0f;
            }

            // guard rail: snap back to the centre line, take damage (and break the front wing)
            float wallDist = tm.WallOffsetAt((float)bestI / TrackModel.N * tm.roadLength);
            if (lateral > wallDist)
            {
                float hit = speed;
                incidents++;
                if (hit > 18f) KnockOff(hit, Vector2.Dot(new Vector2(pos.x, pos.z) - nearest, TrackModel.Normal(trackTan)) > 0f ? "R" : "L");      // the wall is on the right if the car is on the right side of the centre line
                pos.x = nearest.x; pos.z = nearest.y;
                damage = Mathf.Min(100f, damage + speed * 1.1f * TestData.ImpactMul);
                AddWingDamage(Mathf.Min(45f, speed * 0.9f) * TestData.ImpactMul);
                { string td = TestData.Add(2, hit / 25f); if (td != null) rm.Radio(td, 1); }
                speed *= 0.4f;
                wallStunUntil = now + 0.3f;
                shakeUntil = now + 0.22f;
                rm.Flash("WALL HIT");
                rm.Commentate(rm.NameOf(this) + " hits the wall!", 1);
                rm.Thud();
                rm.NoteIncident(dist);
                AddFloorDamage(Mathf.Min(30f, hit * 0.4f));
                if (hit > 40f) AddEngineDamage(hit * 0.25f);
                if (hit > 45f && Random.value < 0.3f) Puncture(Random.Range(0, 4));
                rm.HeavyImpact(hit, false);
            }

            // DRS
            if (drs == null) drs = new DrsTracker(rm, this);
            drs.Update(dt, dist, speed, inDrs, inBrake || speed < 45f, offTrack || inPitLaneNow || pitStopActive);
            drsAvailable = drs.available && !offTrack;
            drsActive = drs.active && !offTrack;

            if (!isFinite(pos.x) || !isFinite(pos.z) || Vector2.Distance(new Vector2(pos.x, pos.z), nearest) > 40f)
            {
                Vector2 rp = tm.PointAt(dist);
                pos.x = rp.x; pos.z = rp.y; speed = 0f;
            }

            PitLogic(dt, now);
            Sectors(now);
            if (now - lastSampleAt >= 0.05f)
            {
                lastSampleAt = now;
                lapSamples.Add(new Vector2(now - lapStart, TrackModel.Mod(dist, tm.roadLength)));
            }
            dist += speed * dt;
            // keep the lap odometer in step with where the car really is (weaving / cutting corners made it drift,
            // which broke the pit-lane checks on later laps)
            float lapLen = tm.roadLength;
            float drift = Mathf.Repeat(along - dist + lapLen * 0.5f, lapLen) - lapLen * 0.5f;
            dist += Mathf.Abs(drift) > 25f ? drift : drift * Mathf.Min(1f, dt * 6f);
            if (wheels != null) wheels.Animate(speed, steerSmooth * 0.45f, dt);
            traceTimer += dt;
            if (traceTimer >= 0.1f)
            {
                traceTimer = 0f;
                curTrace.Add(now - lapStart, TrackModel.Mod(dist, tm.roadLength) / tm.roadLength, speed, inThrottle, inBrake);
            }

            int newLap = Mathf.FloorToInt(dist / tm.roadLength);
            if (newLap > lap && skipNextLapRecord)
            {
                // first crossing after a red-flag restart: not a timed lap
                skipNextLapRecord = false;
                lapSamples = new List<Vector2>(); lapStart = now; lap = newLap; curTrace = new LapTrace();
                for (int i = 0; i < 3; i++) { sectorText[i] = "S" + (i + 1); sectorState[i] = 0; }
            }
            else if (newLap > lap)
            {
                float lapSec = now - lapStart;
                lapTimes.Add(lapSec);
                curTrace.lapTime = lapSec; curTrace.lapNumber = newLap;
                if (curTrace.Count > 5) traces.Add(curTrace);
                curTrace = new LapTrace();
                if (incidents == lapStartIncidents) { cleanLaps++; if (bestCleanLap < 0f || lapSec < bestCleanLap) bestCleanLap = lapSec; }
                float usedWear = wearAtLapStart - tireWear;
                if (usedWear > 0.5f) wearPerLap = wearPerLap <= 0f ? usedWear : Mathf.Lerp(wearPerLap, usedWear, 0.5f);
                wearAtLapStart = tireWear;
                lapStartIncidents = incidents;
                if (bestLapThisRace < 0f || lapSec < bestLapThisRace) bestLapThisRace = lapSec;
                float saved = GameSave.BestLap(tm.def.id);
                if (saved < 0f || lapSec < saved)
                {
                    GameSave.SaveBestLap(tm.def.id, lapSec);
                    Ghost.Save(tm.def.id, lapSec, lapSamples);
                    rm.Radio("New personal best lap!", 1); setPbThisRace = true;
                    rm.Commentate(rm.NameOf(this) + " sets a new personal best lap, " + Hud.FormatTime(lapSec) + ".", 1);
                }
                rm.Cheer();
                dyn.OnLap(newLap, lapSec);
                lapSamples = new List<Vector2>();
                lapStart = now;
                lap = newLap;
                float ew = GameSave.engineWear / 100f;
                if (Random.value < 0.14f * (1f - GameSave.reliability / 11f) * (engineMode == EngineMode.Push ? 1.6f : engineMode == EngineMode.Save ? 0.5f : 1f) + 0.25f * ew * ew)
                {
                    hiccupUntil = now + 1.4f;
                    rm.Flash("ENGINE GLITCH");
                }
                if (GameSave.engineWear > 92f && Random.value < 0.18f)            // a worn-out engine can let go
                {
                    AddEngineDamage(45f);
                    rm.Radio("Engine failure! We are losing power.", 3);
                    rm.Flash("ENGINE FAILURE");
                }
                for (int i = 0; i < 3; i++) { sectorText[i] = "S" + (i + 1); sectorState[i] = 0; }
            }
            // weight transfer: the nose dives when you brake, the tail squats when you accelerate, the body rolls in corners
            float accNow = dt > 0f ? (speed - lastSpeedPitch) / dt : 0f;
            lastSpeedPitch = speed;
            float bodyPitchTarget = Mathf.Clamp(-accNow * 0.10f, -2.4f, 3.2f) * Mathf.Clamp01(speed / 25f);
            float rollTarget = steerSmooth * Mathf.Clamp01(speed / 60f) * 2.4f;
            bodyPitch += (bodyPitchTarget - bodyPitch) * Mathf.Min(1f, dt * 7f);
            bodyRoll += (rollTarget - bodyRoll) * Mathf.Min(1f, dt * 6f);
            if (airborne) flipRoll += crashSpin * dt;                                                // tumbling after a launch
            else if (crashSpin != 0f)
            {
                float settle = Mathf.Round(flipRoll / 360f) * 360f;
                flipRoll = Mathf.Lerp(flipRoll, settle, Mathf.Min(1f, dt * 10f));
                if (Mathf.Abs(flipRoll - settle) < 1f) { flipRoll = 0f; crashSpin = 0f; }
            }
            ApplyTransform();
        }

        float nextContactFx;

        /// <summary>
        /// Car-to-car contact. Cars are treated as 5.2 x 2 m boxes in the player's frame: if one overlaps, the
        /// pair is pushed apart along the shallower axis. Rear-ending a car pins you to its speed, bumps cost
        /// front wing and body damage in proportion to the closing speed, and side-by-side rubbing shoves both
        /// cars apart and scrubs a little speed.
        /// </summary>
        public void ResolveCollisions(float now)
        {
            if (pitStopActive || airborne) return;
            Vector2 me = new Vector2(pos.x, pos.z);
            Vector2 fwd = new Vector2(Mathf.Sin(heading), Mathf.Cos(heading));
            Vector2 right = new Vector2(Mathf.Cos(heading), -Mathf.Sin(heading));
            float myLat = tm.SignedLateral(me);
            foreach (var c in rm.racers)
            {
                var nr = c as NetRacer;
                if (nr != null)
                {
                    // another person's car: both computers push their own car out of the overlap, so it works out as one bump
                    if (nr.gone || nr.dnf || nr.pitStopActive || nr.finishTime >= 0f || inPitLaneNow) continue;
                    Vector3 nw = nr.go.transform.position;
                    Vector2 nrel = new Vector2(nw.x, -nw.z) - me;
                    float nlf = Vector2.Dot(nrel, fwd), nlr = Vector2.Dot(nrel, right);
                    float nox = 5.2f - Mathf.Abs(nlf), noy = 2.0f - Mathf.Abs(nlr);
                    if (nox <= 0f || noy <= 0f) continue;
                    float nclose = Mathf.Abs(speed - nr.speed);
                    if (nox / 5.2f < noy / 2.0f)
                    {
                        float push = nox * 0.5f;
                        float dir = nlf > 0f ? -1f : 1f;
                        pos.x += fwd.x * push * dir; pos.z += fwd.y * push * dir;
                        if (nlf > 0f) { speed = Mathf.Min(speed, nr.speed * 0.97f); AddWingDamage(Mathf.Min(40f, nclose * 1.6f)); }
                        else speed += nclose * 0.15f;
                    }
                    else
                    {
                        float push = noy * 0.5f + 0.05f, nside = nlr >= 0f ? 1f : -1f;
                        pos.x -= right.x * nside * push; pos.z -= right.y * nside * push;
                    }
                    damage = Mathf.Min(100f, damage + nclose * 0.25f);
                    continue;
                }
                var ai = c as AICar;
                if (ai == null || ai.gone || ai.pitStopActive || (inPitLaneNow && ai.InLane)) continue;
                Vector3 w = ai.go.transform.position;                    // Unity world -> web space
                Vector2 rel = new Vector2(w.x, -w.z) - me;
                float lf = Vector2.Dot(rel, fwd), lr = Vector2.Dot(rel, right);
                float ox = 5.2f - Mathf.Abs(lf), oy = 2.0f - Mathf.Abs(lr);
                if (ox <= 0f || oy <= 0f) continue;

                float closing = Mathf.Abs(speed - ai.speed);
                bool rearEnd = ox / 5.2f < oy / 2.0f;                      // overlapping mostly front-to-back
                float side = lr >= 0f ? 1f : -1f;                          // which side of you they are on
                bool myFault;
                bool wheelContact = rearEnd && Mathf.Abs(lr) > 0.9f && closing > 7f;      // his wheel against mine: the faster car can be launched
                if (rearEnd)
                {
                    float push = ox * 0.5f;
                    if (lf > 0f)                                           // you ran into the back of them
                    {
                        pos.x -= fwd.x * push; pos.z -= fwd.y * push;
                        ai.Bump(0f, closing * 0.25f, push);
                        speed = Mathf.Min(speed, ai.speed * 0.97f);
                        AddWingDamage(Mathf.Min(40f, closing * 1.6f));
                        KnockOff(closing * 3f, "L"); KnockOff(closing * 3f, "R");
                    }
                    else                                                   // they ran into you
                    {
                        pos.x += fwd.x * push; pos.z += fwd.y * push;
                        ai.Bump(0f, -closing * 0.6f, -push);
                        speed += closing * 0.15f;
                    }
                    myFault = lf > 0f;
                    damage = Mathf.Min(100f, damage + closing * 0.5f);
                    AddFloorDamage(closing * 0.4f);
                }
                else
                {
                    float push = oy * 0.5f + 0.05f;
                    pos.x -= right.x * side * push; pos.z -= right.y * side * push;
                    float dLat = ai.lateral - myLat;
                    ai.Bump((dLat >= 0f ? 1f : -1f) * push, -speed * 0.01f, 0f);
                    speed *= 1f - Mathf.Min(0.05f, 0.01f + closing * 0.002f);
                    AddWingDamage(Mathf.Min(8f, 1f + closing * 0.3f));
                    damage = Mathf.Min(100f, damage + 1f + closing * 0.2f);
                    AddFloorDamage(1f + closing * 0.7f);
                    myFault = speed > 20f && steerSmooth * side > 0.25f;   // you were turning into them
                    KnockOff(14f + closing * 2f, side > 0f ? "L" : "R");     // the other car is on your left if side > 0
                }
                if (wheelContact && Mathf.Max(speed, ai.speed) > Tuning.FLY_SPEED)
                {
                    if (lf > 0f) LaunchCrash(closing, side);                                        // you ran up the back of him
                    else ai.Launch(Mathf.Min(8f, 3f + closing * 0.25f), -side * (240f + closing * 10f));
                }
                // the stewards: causing a collision is a warning, then time penalties; a big hit is five or ten seconds straight away
                if (myFault && closing > 3f) rm.stewards.Offend(rm, this, "contact", "causing a collision", closing > 12f ? 10f : 5f, closing > 12f);
                if (closing > 6f && Random.value < Mathf.Min(0.45f, closing * 0.03f)) Puncture(Random.Range(0, 4));
                if (closing > 22f) rm.HeavyImpact(closing, true);

                if (now >= nextContactFx)
                {
                    nextContactFx = now + 0.5f;
                    shakeUntil = now + 0.2f;
                    rm.Flash("CONTACT");
                    rm.Commentate("Contact between " + rm.NameOf(this) + " and " + ai.DriverName + "!", 1);
                    rm.Thud();
                    if (closing > 12f) { rm.NoteIncident(dist); rm.Radio("Contact with " + ai.label + ", check the car", 1); }
                }
            }
        }

        /// <summary>A knock: depending on how hard it was, a mirror, an end plate or a bargeboard on that side can come off.</summary>
        void KnockOff(float impact, string side)
        {
            if (impact > 18f && Random.value < 0.55f) BreakPart("mirror" + side);
            if (impact > 28f && Random.value < 0.5f) BreakPart("endplate" + side);
            if (impact > 38f && Random.value < 0.4f) BreakPart("barge" + side);
        }

        /// <summary>The engine has overheated and let go.</summary>
        public void BlowEngine()
        {
            AddEngineDamage(55f);
            rm.Radio("The engine has let go! We are losing a lot of power.", 3);
            rm.Flash("ENGINE BLOWN");
            rm.Commentate(rm.NameOf(this) + "'s engine has let go!", 2);
        }

        public void BreakPart(string key)
        {
            float sgn = key.EndsWith("L") ? -1f : 1f;
            if (parts == null || !parts.Break(key, rm, go.transform.position + go.transform.right * sgn * 0.8f + Vector3.up * 0.6f, go.transform.right * sgn)) return;
            rm.Flash("PART LOST");
            string eff = key.StartsWith("endplate") ? " We lose some front downforce." : key.StartsWith("barge") ? " The floor works less well." : " You cannot see that side in the mirror, look with Z or X.";
            rm.Radio("We have lost the " + CarParts.Describe(key) + "." + eff);
            if (key.StartsWith("endplate")) AddWingDamage(9f);
            else if (key.StartsWith("barge")) AddFloorDamage(10f);
            else rm.MirrorLost(sgn < 0f ? -1 : 1, true);
        }

        /// <summary>Everything that was knocked off is put back (a pit stop or a red flag).</summary>
        void RestoreParts() { if (parts == null) return; parts.RestoreAll(); rm.MirrorLost(-1, false); rm.MirrorLost(1, false); }

        // front-wing damage: costs downforce, visibly shortens the wing, and sheds debris as it breaks up
        void AddWingDamage(float amount)
        {
            wingDamage = Mathf.Min(100f, wingDamage + amount);
            int stage = wingDamage >= 70f ? 2 : wingDamage >= 35f ? 1 : 0;
            if (stage > wingStage)
            {
                wingStage = stage;
                rm.SpawnWingDebris(go.transform.position + go.transform.forward * 3f + Vector3.up * 0.3f, go.transform.right, stage);
                rm.Radio(stage == 1 ? "Front wing damage, we are losing downforce" : "Big front wing damage, be careful in the corners");
            }
            if (frontWing != null)
            {
                float k = 1f - 0.65f * Mathf.Clamp01(wingDamage / 100f);
                frontWing.localScale = new Vector3(1.9f * k, 0.05f, 0.45f);
                frontWing.localRotation = Quaternion.Euler(0f, 0f, wingDamage * 0.12f);
            }
        }

        static bool isFinite(float f) { return !float.IsNaN(f) && !float.IsInfinity(f); }

        // ---- pit stop: request, guidance, limiter, stopping in the box ----
        float RelPit(float d) { return TrackModel.Mod(d - tm.PitZoneDist + tm.roadLength / 2f, tm.roadLength) - tm.roadLength / 2f; }

        static float Sm(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / Mathf.Max(0.0001f, b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>B: box this lap. Turns on lane assist (steering + stopping in the box).</summary>
        public void TogglePitRequest()
        {
            if (pitStopActive) return;
            pitRequest = !pitRequest;
            pitAssist = pitRequest;
            pitDone = false;
            rm.Radio(pitRequest ? "Box box, box this lap." : "Stay out, stay out.", 3);
        }

        void UpdatePitGuidance()
        {
            float rel = RelPit(dist);
            pitGuide = "";
            if (pitAssist)
            {
                if (pitStopActive) pitGuide = "PIT STOP";
                else if (rel < TrackModel.PIT_ENTRY) pitGuide = "BOX BOX - PIT ENTRY IN " + Mathf.Max(0, Mathf.RoundToInt(TrackModel.PIT_ENTRY - rel)) + " M   -   YOUR BOX IS " + (ASSIGNED_BOX + 1);
                else if (!pitDone && inPitLaneNow) pitGuide = targetBox < 0 ? "PIT LANE - NO FREE BOX AHEAD" : targetBox == ASSIGNED_BOX ? "PIT LANE - YOUR BOX IS " + (targetBox + 1) : "PIT LANE - YOUR BOX WAS MISSED, STOPPING IN BOX " + (targetBox + 1);
                else if (rel <= TrackModel.PIT_EXIT) pitGuide = "PIT EXIT - LIMIT 80 KM/H";
            }
            else if (inPitLaneNow && rel >= TrackModel.PIT_ENTRY && rel <= TrackModel.PIT_EXIT) pitGuide = targetBox >= 0 && !pitDone ? "PIT LANE - YOUR BOX IS " + (targetBox + 1) : "PIT LANE - LIMIT 80 KM/H";
            if (rel > TrackModel.PIT_EXIT || rel < TrackModel.PIT_ENTRY - 10f) pitDone = false;      // ready for the next stop once we are away from the box
            if (pitAssist && rel > TrackModel.PIT_EXIT) { pitAssist = false; pitRequest = false; }
        }

        float PitAssistSteer(float steerInput)
        {
            if (!pitAssist || pitStopActive || airborne) return steerInput;
            float rel = RelPit(dist);
            if (rel < TrackModel.PIT_ENTRY - 20f) return steerInput;          // still on the straight: set up the entry yourself
            float look = dist + Mathf.Clamp(speed * 0.7f, 8f, 30f);
            Vector2 target; Vector3 lp;
            if (tm.PitLanePosAt(look, out lp))
            {
                // pass the other boxes on the track side of the lane, and move over to the garage side for your own
                float factor = tm.pitLaneOffset > 0.01f ? tm.PitLaneOffsetAt(look) / tm.pitLaneOffset : 0f;
                bool working = targetBox >= 0 && !pitDone && Mathf.Abs(tm.PitBoxAlong(targetBox) - pitAlong) < 16f;
                float shift = (working ? TrackModel.PIT_WORK_SHIFT : -TrackModel.PIT_WORK_SHIFT) * factor;
                target = new Vector2(lp.x, lp.z) + tm.PitLaneNormalAt(look) * shift;
            }
            else target = tm.PointAt(look);
            float want = Mathf.Atan2(target.x - pos.x, target.y - pos.z);
            float diff = Mathf.Repeat(want - heading + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;
            return Mathf.Clamp(diff * 2.5f + steerInput * 0.5f, -1f, 1f);
        }

        bool NeedsService { get { return pitRequest || tireWear < 99.5f || wingDamage > 1f || floorDamage > 1f || engineDamage > 1f || damage > 8f || punctured || lostWheel >= 0 || (parts != null && parts.TotalLost > 0) || (rm.fuelOn && fuel < fuelTarget - 5f); } }

        /// <summary>
        /// The pit crew can fix everything, and the bigger the job the longer it takes: a new front wing, the floor, the bodywork, the engine,
        /// a puncture or lost wheel, and every part that was knocked off (a mirror is quick, a bargeboard takes longer). Returns the jobs and the total seconds.
        /// </summary>
        public float RepairJobs(List<string> names)
        {
            float total = 0f;
            System.Action<string, float> job = (n, s) => { if (s > 0.05f) { total += s; if (names != null) names.Add(n + " +" + s.ToString("0.0") + "s"); } };
            float k = 1f - 0.07f * Staff.Mechanic;                                  // a better chief mechanic works faster
            job("front wing", wingDamage > 1f ? (0.6f + wingDamage * 0.03f) * k : 0f);
            job("floor", floorDamage > 1f ? (0.5f + floorDamage * 0.03f) * k : 0f);
            job("bodywork", damage > 8f ? (0.4f + damage * 0.02f) * k : 0f);
            job("engine", engineDamage > 1f ? (1.0f + engineDamage * 0.055f) * k : 0f);
            job("puncture", punctured ? 0.6f * k : 0f);
            job("lost wheel", lostWheel >= 0 ? 2.5f * k : 0f);
            if (parts != null)
                foreach (var key in CarParts.Keys)
                    if (parts.IsLost(key)) job(CarParts.Describe(key), (key.StartsWith("mirror") ? 0.4f : key.StartsWith("endplate") ? 1.0f : 1.5f) * k);
            return total;
        }

        public float RepairSeconds() { return RepairJobs(null); }

        void ApplyPitLimiterAndBoxBrake(float dt)
        {
            if (pitStopActive) return;
            if (targetBox >= 0 && !inPitLaneNow) { rm.pitBoxes.Release(this); targetBox = -1; }        // left the lane: give the box back
            float rel = RelPit(dist);
            // with the pit assist on, slow smoothly on the way in so you arrive at the 80 km/h limit
            if (pitAssist && !inPitLaneNow && rel < TrackModel.PIT_ENTRY && rel > TrackModel.PIT_ENTRY - 130f)
            {
                float entrySpeed = 22f + (TrackModel.PIT_ENTRY - rel) * 0.55f;
                if (speed > entrySpeed) speed = Mathf.Max(entrySpeed, speed - 32f * dt);
            }
            bool inZone = inPitLaneNow || (pitAssist && rel >= TrackModel.PIT_ENTRY && rel <= TrackModel.PIT_EXIT);
            if (!inZone) return;
            if (speed > 22f) speed = Mathf.Max(22f, speed - 40f * dt);                         // pit-lane speed limit, 80 km/h
            if (NeedsService && inPitLaneNow && !pitDone)
            {
                if (targetBox < 0) targetBox = rm.pitBoxes.Claim(this, ASSIGNED_BOX, i => tm.PitBoxAlong(i) - pitAlong, speed);
                if (targetBox >= 0)
                {
                    float ahead = tm.PitBoxAlong(targetBox) - pitAlong;                       // metres to the middle of our box (negative once past)
                    float target = Mathf.Clamp(Mathf.Max(ahead, 0f) * 0.55f, 0f, 22f);
                    if (speed > target) speed = Mathf.Max(target, speed - 16f * dt);
                }
            }
        }

        static float AngleLerp(float a, float b, float t)
        {
            float d = Mathf.Repeat(b - a + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;
            return a + d * t;
        }

        void PitLogic(float dt, float now)
        {
            // opt-in: you must be physically inside the pit lane near the box (and be slow) - never forced
            bool inPitZone = inPitLaneNow && targetBox >= 0 && Mathf.Abs(tm.PitBoxAlong(targetBox) - pitAlong) < 3.5f;
            bool needStop = NeedsService;

            if (pitStopActive)
            {
                float total = Mathf.Max(0.1f, pitStopEnd - pitStopStart);
                float frac = Mathf.Min(1f, (now - pitStopStart) / total);
                tireWear = Mathf.Min(100f, pitStopStartWear + (100f - pitStopStartWear) * frac);
                tireTemp = Mathf.Lerp(tireTemp, 78f, dt * 2f);       // fresh tyres come out of the blankets warm
                // the crew repairs everything while the tyres are changed: the damage melts away as the stop goes on
                float left = 1f - Mathf.Clamp01(frac * 1.1f);
                if (repairWing0 > 0f) { wingDamage = repairWing0 * left; AddWingDamage(0f); }
                if (repairFloor0 > 0f) floorDamage = repairFloor0 * left;
                if (repairEngine0 > 0f) engineDamage = repairEngine0 * left;
                if (repairBody0 > 0f) damage = repairBody0 * left;
                if (rm.Refuels) fuel = Mathf.Min(Mathf.Max(fuel, fuelTarget), fuel + 14f * dt);

                if (crewStage < 1 && frac > 0.05f) { crewStage = 1; rm.CrewSay("Car is in, jack up!"); }
                else if (crewStage < 2 && frac > 0.34f) { crewStage = 2; rm.CrewSay("Old wheels off!"); }
                else if (crewStage < 3 && frac > 0.52f) { crewStage = 3; rm.CrewSay("New tyres on, gun, gun, gun!"); }
                else if (crewStage < 4 && frac > 0.76f) { crewStage = 4; rm.CrewSay("Jack down, wait for it!"); }

                // settle the car exactly into the box, jack it up, and take the wheels off / put them on
                Vector2 bc; float by, bh; Vector2 bf, br; float bo;
                tm.PitBoxFrame(Mathf.Max(0, pitBox), out bc, out by, out bh, out bf, out br, out bo);
                float k = Mathf.Min(1f, dt * 8f);
                pos.x = Mathf.Lerp(pos.x, bc.x, k); pos.z = Mathf.Lerp(pos.z, bc.y, k);
                heading = AngleLerp(heading, bh, k);
                pitLift = 0.28f * (Sm(0.2f, 0.3f, frac) - Sm(0.72f, 0.8f, frac));
                if (wheels != null)
                {
                    wheels.SetWheelsVisible(frac < 0.34f || frac > 0.6f);
                    if (frac > 0.5f && wheels.sidewallMat != null) wheels.sidewallMat.mainTexture = Tex.Sidewall(compound);
                }
                if (now >= pitStopEnd)
                {
                    pitStopActive = false; tireWear = 100f; wingStage = 0; dyn.SetTyres(82f); RestoreParts();      // fresh tyres come out of the blankets warm
                    wingDamage = 0f; floorDamage = 0f; engineDamage = 0f; damage = 0f; engineWarned = false; AddWingDamage(0f); pitWork = ""; health.Reset();
                    repairWing0 = repairFloor0 = repairEngine0 = repairBody0 = 0f;
                    rm.pitBoxes.Release(this); targetBox = -1; pitBox = -1;
                    pitCount++; stintStartLap = lap; wearAtLapStart = 100f;
                    punctured = false; floorWarned = false;
                    if (lostWheel >= 0) { lostWheel = -1; if (wheels != null) wheels.LoseWheel(-1); }
                    if (rm.Refuels)
                    {
                        fuel = Mathf.Max(fuel, fuelTarget);
                        if (swapPlanned) { swapPlanned = false; fatigue = 0.15f; rm.Radio("Relief driver in - fresh legs. Go go go!", 2); }
                        else fatigue = Mathf.Max(0f, fatigue - 0.3f);                                         // a drink and a stretch
                    }
                    pitLift = 0f; pitRequest = false; pitDone = true;
                    if (unsafeRelease) { unsafeRelease = false; rm.stewards.Penalty(rm, this, "unsafe release in the pit lane", 5f); }
                    if (wheels != null) wheels.SetWheelsVisible(true);
                    rm.CrewSay("Go go go!");
                }
            }
            else if (inPitZone && speed < 4f && needStop && !pitDone)
            {
                pitStopActive = true; pitBox = targetBox; crewStage = 0;
                pitStopStart = now; pitStopEnd = now + (2.6f + Random.value * 0.4f) * (1f - 0.07f * Staff.Mechanic);      // a better chief mechanic: quicker stops
                pitStopStartWear = tireWear; speed = 0f;
                if (plannedCompound >= 0) { ChooseCompound((Compound)plannedCompound); plannedCompound = -1; }
                repairWing0 = wingDamage; repairFloor0 = floorDamage; repairEngine0 = engineDamage; repairBody0 = damage;
                var jobList = new List<string>();
                float repairTime = RepairJobs(jobList);                                                  // the bigger the job, the longer it takes
                pitStopEnd += repairTime;
                pitWork = jobList.Count > 0 ? "Repairs: " + string.Join(", ", jobList.ToArray()) : "";
                if (jobList.Count > 0) rm.Radio("Fixing everything: " + string.Join(", ", jobList.ToArray()) + ". About " + repairTime.ToString("0.0") + " seconds more.", 1);
                if (rm.Refuels) pitStopEnd += Mathf.Max(0f, fuelTarget - fuel) / 14f;                       // refuelling: 14 units a second
                if (swapPlanned) pitStopEnd += 4f;                                                         // changing driver
                unsafeRelease = false;
                string mistake = null;
                // the more there is to do, the more can go wrong; a better chief mechanic makes fewer mistakes
                float errChance = Mathf.Min(0.5f, (0.20f + 0.04f * jobList.Count) / (1f + 0.3f * Staff.Mechanic));
                float roll = Random.value, pick = Random.value;
                string[] wheelName = { "front left", "front right", "rear left", "rear right" };
                if (roll < errChance)
                {
                    if (pick < 0.28f) { float extra = 1.6f + Random.value * 1.8f; pitStopEnd += extra; mistake = "Wheel gun problem on the " + wheelName[Random.Range(0, 4)] + ", we lost " + extra.ToString("0.0") + " seconds."; }
                    else if (pick < 0.46f) { float extra = 2f + Random.value * 2f; pitStopEnd += extra; mistake = "The " + wheelName[Random.Range(0, 4)] + " wheel nut is cross-threaded, " + extra.ToString("0.0") + " seconds lost."; }
                    else if (pick < 0.62f) { float extra = 1.0f + Random.value * 1.2f; pitStopEnd += extra; mistake = "The jack was slow to drop, " + extra.ToString("0.0") + " seconds gone."; }
                    else if (pick < 0.74f) { float extra = 3f + Random.value * 1.5f; pitStopEnd += extra; mistake = "They fitted the wrong set of tyres and had to swap them, " + extra.ToString("0.0") + " seconds lost."; }
                    else if (pick < 0.84f && repairTime > 0.5f) { float extra = repairTime * (0.25f + Random.value * 0.3f); pitStopEnd += extra; mistake = "The new part would not fit first time, the repair took " + extra.ToString("0.0") + " seconds longer."; }
                    else if (pick < 0.92f) { float extra = 1.2f + Random.value * 1.3f; pitStopEnd += extra; mistake = "The engine would not fire on release, " + extra.ToString("0.0") + " seconds lost."; }
                    else { unsafeRelease = true; pitStopEnd -= 0.3f; mistake = "Released too early! That could be an unsafe release."; }
                }
                if (mistake != null) { rm.Radio(mistake, 3); rm.Flash("PIT CREW ERROR"); }
                else rm.Radio("Good box, changing tires", 1);
            }
            else
            {
                float hot = Mathf.Max(0f, tireTemp - TempHi[(int)compound]) / 40f;
                float wearRate = 0.015f * (offTrack ? 1.8f : 1f) * (overCorner ? 1.6f : 1f) * Tuning.TireWear[(int)compound] * Tuning.WetWear(compound, rm.roadWetness)
                                 * (engineMode == EngineMode.Push ? 1.2f : engineMode == EngineMode.Save ? 0.85f : 1f)
                                 * (1f + hot) * (1f + 0.5f * dirtyAir) * (1f + 0.4f * kerbAmt) * RacerProfile.WearMult * Regs.TyreWear * (1f + 0.8f * dyn.blister) * (1f + 0.15f * dyn.FuelEffect) * (1f + 0.15f * dyn.FuelEffect)
                                 * (1f + 0.10f * Setup.Norm(1)) * (1f - 0.06f * Setup.Norm(3)) * (1.06f - 0.12f * setupFit)
                                 * (1f - 0.12f * TeamCareer.Perf) * (1f - 0.04f * TeamCareer.RdLevel(2));
                tireWear = Mathf.Max(0f, tireWear - speed * dt * wearRate);
            }
        }

        void Sectors(float now)
        {
            float sectorWidth = tm.roadLength / 3f;
            int cur = Mathf.Min(2, Mathf.FloorToInt(TrackModel.Mod(dist, tm.roadLength) / sectorWidth));
            if (cur == lastSector) return;
            float elapsed = now - sectorStart;
            float prev = bestSectors[lastSector];
            bool isBest = prev < 0f || elapsed < prev;
            if (isBest) { bestSectors[lastSector] = elapsed; GameSave.SaveBestSector(tm.def.id, lastSector, elapsed); }
            string delta = "";
            if (prev >= 0f) { float dd = elapsed - prev; delta = " (" + (dd >= 0f ? "+" : "") + dd.ToString("0.00") + ")"; }
            sectorText[lastSector] = "S" + (lastSector + 1) + " " + elapsed.ToString("0.00") + delta;
            sectorState[lastSector] = isBest ? 1 : (prev >= 0f ? 2 : 0);
            sectorStart = now;
            lastSector = cur;
        }

        /// <param name="rev">revs held at the lights (0..1), -1 = no rev minigame, -2 = no launch at all (rolling start)</param>
        public void BeginRace(float now, float rev = -1f)
        {
            launchStart = rev <= -2f ? -1f : now; launchReact = -1f; launchBonus = 0f; launchBase = 0f; launchLeft = 0f; launchRev = rev;
            towCharge = 0f; slingshot = 0f; slingLeft = 0f;
            curTrace = new LapTrace(); traceTimer = 0f;
            lapStart = now; sectorStart = now; lastSector = Mathf.Min(2, Mathf.FloorToInt(TrackModel.Mod(dist, tm.roadLength) / (tm.roadLength / 3f)));
        }

        public void ApplyTransform()
        {
            go.transform.position = TrackModel.W(pos.x, pos.y + pitLift, pos.z);
            go.transform.rotation = Quaternion.Euler(bodyPitch, (Mathf.PI - heading) * Mathf.Rad2Deg, bodyRoll + flipRoll);
        }
    }
}
