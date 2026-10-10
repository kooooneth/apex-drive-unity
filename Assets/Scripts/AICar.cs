using UnityEngine;

namespace ApexDrive
{
    /// <summary>AI rival, ported from updateAI() in the HTML game.</summary>
    public class AICar : Racer
    {
        readonly RaceManager rm;
        TrackModel tm { get { return rm.track; } }
        WheelRig wheels;

        public int id;
        DrsTracker drs;
        public float speedRating, gridOffset, startDist, seed;
        public float pitThreshold;
        public Personality personality;
        float defend;
        public float lateral;                 // current sideways offset from the centre line
        public float slingshot;               // 0..1 burst of pace after pulling out of a tow
        float towCharge, slingLeft, slingPower, lastYaw, aiSteer;
        float blockedTime, passOffset, passSide, startDelay, shove, mismatchTime, yieldOffset;     // shove: sideways push from contact, fades away
        public bool pitPlanned;
        float aiPitch, aiRoll, lastSpeedAi;
        float yawExtra, rollExtra, hop;                      // body motion added by spins and crashes (degrees, degrees, metres)
        // things that go wrong
        public bool retired, removed;                        // stopped with a problem / taken away by the recovery truck
        public float failureDist = -1f;                      // the car breaks down once it has covered this much distance
        float retireSide = 1f, retireOffset, spinOffset, spinT, smokeUntil, hopV, spinRate, nextNote;
        bool spinning; int spinDir = 1, cautionSeen;
        ParticleSystem smoke;
        bool inLane; float laneBlend, workBlend; float sinceStop; int myBox = -1;
        public bool InLane { get { return inLane; } }
        public string DriverName { get { return Personalities.Drivers[id % Personalities.Drivers.Length]; } }           // AI cars drive through the real pit lane when they stop
        float mistakeUntil, mistakeDuration; int mistakeSide; bool wasInCorner;

        public AICar(RaceManager manager, GameObject visual, int index, float start, float rating, float offset, Personality pers = Personality.Balanced)
        {
            rm = manager; go = visual; wheels = visual.GetComponent<WheelRig>(); id = index; personality = pers; label = Personalities.Drivers[index % 5] + " (" + Personalities.Names[(int)pers] + ")";
            dist = startDist = start; speedRating = rating; gridOffset = offset;
            seed = Random.value * 10f;
            startDelay = 0.15f + Random.value * 0.35f;
            compound = rm.StartCompoundForAI();
            pitThreshold = pers == Personality.TireSaver ? 15f + Random.value * 10f : pers == Personality.Aggressive ? 35f + Random.value * 15f : 25f + Random.value * 20f;
            Place();
            lastYaw = go.transform.eulerAngles.y;
            smoke = Effects.MakeSmoke(go.transform, new Vector3(0f, 0.7f, -1.2f), new Color(0.38f, 0.38f, 0.4f, 0.6f));
        }

        /// <summary>A mechanical failure: the car slows to the edge of the track and stops, smoking.</summary>
        public void Retire()
        {
            if (retired) return;
            retired = true; pitPlanned = false;
            retireSide = lateral >= 0f ? 1f : -1f;
            rm.Radio(DriverName + " has stopped on track!", 1);
            rm.Commentate(DriverName + " is out of the race with a mechanical problem!", 2);
            rm.NoteIncident(dist);
            rm.AiRetired(this);
        }

        /// <summary>The recovery truck has taken the car away.</summary>
        public void Remove()
        {
            removed = true; gone = true;
            rm.pitBoxes.Release(this);
            go.SetActive(false);
        }

        void StartSpin()
        {
            if (spinning || retired) return;
            spinning = true; spinT = 0f; spinDir = Random.value < 0.5f ? -1 : 1;
            smokeUntil = rm.Now + 2.5f;
            rm.NoteIncident(dist);
            rm.Radio(DriverName + " has spun!", 1);
            rm.Commentate(DriverName + " has spun! That could cost him dearly.", 2);
        }

        /// <summary>Launched by a wheel-to-wheel touch: a hop and a roll.</summary>
        public void Launch(float vy, float spinDeg)
        {
            if (hop > 0.01f || hopV != 0f) return;
            hopV = vy; spinRate = spinDeg; speed *= 0.6f;
            rm.Commentate("Wheels touch and " + DriverName + " is launched into the air! What a moment!", 2);
            rm.NoteIncident(dist);
        }

        /// <summary>Contact with another car: a sideways shove (metres, decays), a speed change and a nudge along the track.</summary>
        public void Bump(float lateralShove, float speedDelta, float distDelta)
        {
            shove = Mathf.Clamp(shove + lateralShove, -3f, 3f);
            speed = Mathf.Max(0f, speed + speedDelta);
            dist += distDelta;
        }

        /// <summary>A rewind: put the car back where it was a few seconds ago.</summary>
        public void RewindTo(float newDist, float newSpeed, float newLateral, float newTire, int newLap)
        {
            dist = newDist; speed = newSpeed; lateral = newLateral; tireWear = newTire; lap = newLap;
            shove = 0f; blockedTime = 0f; defend = 0f;
            Place();
        }

        /// <summary>Back on the grid after a red flag.</summary>
        public void ResetTo(float newDist, float offset)
        {
            dist = startDist = newDist; gridOffset = offset; speed = 0f;
            blockedTime = passOffset = shove = yieldOffset = towCharge = slingLeft = slingshot = defend = 0f;
            lap = Mathf.FloorToInt(newDist / tm.roadLength);
            rm.pitBoxes.Release(this); myBox = -1; pitBox = -1; workBlend = 0f;
            pitStopActive = false; pitPlanned = false; inLane = false; laneBlend = 0f;
            Place();
            lastYaw = go.transform.eulerAngles.y;
        }

        public void Tick(float dt, float now)
        {
            if (removed) return;
            shove *= Mathf.Exp(-dt * 1.6f);
            float lookahead = Mathf.Min(75f, Mathf.Max(18f, speed * 1.15f));
            float curv = tm.CurvatureAt(dist);
            for (int s = 1; s <= 5; s++) curv = Mathf.Max(curv, tm.CurvatureAt(dist + lookahead * s / 5f));
            float tireGrip = Tuning.TireGrip[(int)compound] * Regs.TyreGrip * Tuning.WetGrip(compound, rm.roadWetness) * rm.AiTempFactor(compound) * (0.6f + 0.4f * (tireWear / 100f));

            // wrong tyres for the conditions: after a reaction delay the driver comes in for the right ones
            bool onWetTyres = compound >= Compound.Inter;
            bool wrong = (rm.roadWetness > 0.45f && !onWetTyres) || (rm.roadWetness < 0.12f && !rm.isRaining && onWetTyres);
            mismatchTime = wrong ? mismatchTime + dt : 0f;
            if (mismatchTime > 5f + seed * 0.8f) pitPlanned = true;
            float speedCap = (14f + speedRating * 7f - curv * 26f) * tireGrip * rm.aiDifficulty * Personalities.Pace(personality, rm.isRaining);
            float wobble = Mathf.Sin(now * 0.6f + seed) * 1.2f;
            float target = Mathf.Max(10f, speedCap + wobble);
            if (drs == null) drs = new DrsTracker(rm, this);
            if (drs.Update(dt, dist, speed, true, target < speed - 4f, retired || spinning || inLane || pitStopActive)) target *= 1.045f;       // DRS on a straight
            if (rm.wind != null)
            {
                float hw, cw; rm.wind.Relative(tm.TangentAt(dist), rm.Now, out hw, out cw);
                target *= WindField.TopSpeedMul(hw * 0.6f);
            }
            if (rm.puddles != null && rm.roadWetness > 0.5f)
            {
                float pd = rm.puddles.DepthAt(dist, lateral);                     // standing water slows everyone, slicks most
                if (pd > 0.15f) target *= 1f - 0.12f * pd * (compound >= Compound.Inter ? 0.4f : 1f);
                if (pd > 0.5f && !spinning && !retired && compound < Compound.Inter && speed > 40f && rm.roadWetness > 0.6f && Random.value < 0.004f) StartSpin();
            }
            if (rm.safetyCarActive) target = Mathf.Min(target, rm.SafetyCarCapFor(this));
            if (rm.vscActive) target = Mathf.Min(target, Tuning.VSC_SPEED_CAP);
            if (rm.rolling) target = Mathf.Min(target, 30f);
            if (rm.redFlag) target = 0f;

            bool inCorner = tm.CurvatureAt(dist) > 0.3f;
            if (inCorner && !wasInCorner && Random.value < 0.05f * rm.aiMistakeMult * Personalities.MistakeMult(personality, rm.isRaining))
            {
                mistakeDuration = 0.7f + Random.value * 0.7f;
                mistakeUntil = now + mistakeDuration;
                mistakeSide = Random.value < 0.5f ? -1 : 1;
                // a mistake can be a lock-up (smoke, a flat spot) or, more often in the wet, a spin
                if (speed > 30f && Random.value < 0.06f + 0.3f * rm.roadWetness * (compound >= Compound.Inter ? 0.3f : 1f)) StartSpin();
                else { tireWear = Mathf.Max(0f, tireWear - 3f - Random.value * 4f); smokeUntil = now + 0.9f; }
            }
            wasInCorner = inCorner;
            bool inMistake = now < mistakeUntil;
            if (inMistake) target *= 0.6f;

            if (!retired && failureDist > 0f && dist >= failureDist && !pitStopActive && !inLane && !rm.redFlag) Retire();
            if (retired)
            {
                target = 0f;
                retireOffset += (retireSide * 7.5f - retireOffset) * Mathf.Min(1f, dt * 0.8f);       // roll over to the side of the track
                if (speed < 3f && now > nextNote) { nextNote = now + 6f; rm.NoteIncident(dist); }       // keep the sector under yellow
            }
            if (spinning)
            {
                spinT += dt; target = 0f;
                float ys = spinT < 1.5f ? 540f * (1f - (1f - spinT / 1.5f) * (1f - spinT / 1.5f)) : spinT < 2.6f ? 540f : 540f + 180f * Mathf.SmoothStep(0f, 1f, spinT - 2.6f);
                yawExtra = spinDir * ys;
                spinOffset += (spinDir * 3.5f - spinOffset) * Mathf.Min(1f, dt * 1.5f);
                if (spinT > 3.6f) { spinning = false; yawExtra = 0f; }
            }
            else spinOffset += (0f - spinOffset) * Mathf.Min(1f, dt * 1.2f);
            if (smoke != null)
            {
                var sm = smoke.emission;
                sm.rateOverTime = (retired ? 28f : 0f) + (now < smokeUntil ? 70f : 0f) + (spinning ? 60f : 0f);
            }

            // ---- racing with other cars: hold a safe gap, and swing out to pass when stuck behind someone
            if (!rm.rolling && now < rm.raceStartClock + startDelay) target = 0f;       // each driver reacts to the lights a touch differently
            target *= 1f + slingshot * 0.10f;                            // a slingshot is worth up to +10% top speed
            bool inTow = false;

            // blue flag: a car a lap ahead is closing from behind - move off the racing line and let it through
            float yieldTarget = 0f; bool yielding = false;
            foreach (var c in rm.racers)
            {
                if (c == this || c.gone) continue;
                float behindGap = TrackModel.Mod(dist - c.dist, tm.roadLength);
                if (behindGap > 3f && behindGap < 90f && c.dist > dist + tm.roadLength * 0.5f)
                {
                    float cLat = c is AICar ? ((AICar)c).lateral : (rm.player != null ? tm.SignedLateral(new Vector2(rm.player.pos.x, rm.player.pos.z)) : 0f);
                    yieldTarget = (lateral >= cLat ? 1f : -1f) * 3.8f;
                    if (Mathf.Abs(lateral) > 5.5f) yieldTarget = lateral > 0f ? 3.8f : -3.8f;
                    yielding = true;
                    break;
                }
            }
            yieldOffset += (yieldTarget - yieldOffset) * Mathf.Min(1f, dt * 1.4f);
            if (yielding) target *= 0.93f;
            Racer front = null; float gapMin = 1e9f;
            foreach (var c in rm.racers)
            {
                if (c == this || c.gone) continue;
                float ahead = TrackModel.Mod(c.dist - dist, tm.roadLength);
                if (ahead > tm.roadLength * 0.5f || ahead < 0.5f) continue;
                if (ahead < gapMin) { gapMin = ahead; front = c; }
            }
            bool wantPass = false;
            if (front != null && gapMin < 28f)
            {
                float frontLat = front is AICar ? ((AICar)front).lateral
                               : (rm.player != null ? tm.SignedLateral(new Vector2(rm.player.pos.x, rm.player.pos.z)) : 0f);
                float dLat = Mathf.Abs(frontLat - lateral);
                if (dLat < 2.4f)
                {
                    float allowed = front.speed + Mathf.Max(0f, gapMin - 7f) * 0.6f;   // close up slowly, never ram the car ahead
                    if (slingLeft <= 0f || Mathf.Abs(passOffset) < 1.2f) target = Mathf.Min(target, allowed);
                    inTow = gapMin < 22f;
                    blockedTime += dt;
                    // sit in the tow until it is charged (or we have waited long enough), then pull out
                    if (blockedTime > 1.0f && gapMin < 22f && (towCharge > 0.5f || blockedTime > 2.5f))
                    {
                        wantPass = true;
                        passSide = frontLat >= lateral ? -1f : 1f;
                        if (Mathf.Abs(lateral) > 5.5f) passSide = lateral > 0f ? -1f : 1f;   // already near the edge: go the other way
                    }
                }
                else blockedTime = Mathf.Max(0f, blockedTime - dt * 2f);
            }
            else blockedTime = 0f;
            passOffset += ((wantPass ? passSide * 3.2f : 0f) - passOffset) * Mathf.Min(1f, dt * 1.6f);

            // slipstream charge builds behind a car; pulling out to pass releases it as a slingshot
            if (inTow) towCharge = Mathf.Min(1f, towCharge + dt * 0.3f);
            else towCharge = Mathf.Max(0f, towCharge - dt * 0.5f);
            if (wantPass && slingLeft <= 0f && towCharge > 0.4f && speed > 35f) { slingPower = towCharge; slingLeft = 2.6f; towCharge = 0f; }
            if (slingLeft > 0f) { slingLeft -= dt; slingshot = slingPower * Mathf.Clamp01(slingLeft / 2.6f); }
            else slingshot = 0f;

            float lapProgress = TrackModel.Mod(dist, tm.roadLength);
            float pz = tm.PitZoneDist;
            float raw = Mathf.Abs(lapProgress - pz);
            float distFromPit = Mathf.Min(raw, tm.roadLength - raw);
            bool inPitZone = distFromPit < 14f;
            float aheadToPit = pz - lapProgress;
            if (aheadToPit < 0f) aheadToPit += tm.roadLength;

            if (!pitPlanned && tireWear < pitThreshold) pitPlanned = true;
            if ((rm.safetyCarActive || rm.vscActive) && cautionSeen != rm.cautionCount && !retired)
            {
                cautionSeen = rm.cautionCount;
                if (!pitPlanned && tireWear < 70f && Random.value < 0.55f) pitPlanned = true;           // a cheap stop under the caution
            }
            if (retired) pitPlanned = false;
            if (rm.fuelOn)
            {
                sinceStop += speed * dt;                                           // endurance: the tank runs dry too
                if (!pitPlanned && sinceStop > rm.FuelRangeMeters * (0.86f + 0.1f * Mathf.Repeat(seed, 1f))) pitPlanned = true;
            }

            // a stop means using the pit lane: from its entry until a little past its exit, at the 80 km/h limit. The car
            // claims one of the six boxes on the way in, passes the others on the track side of the lane, and moves
            // over to the garage side to stop at the middle of its own box.
            float relPit = TrackModel.Mod(dist - pz + tm.roadLength * 0.5f, tm.roadLength) - tm.roadLength * 0.5f;
            bool wasInLane = inLane;
            inLane = (pitPlanned || inLane) && relPit >= TrackModel.PIT_ENTRY && relPit <= TrackModel.PIT_EXIT + 8f;
            if (inLane && !wasInLane && pitPlanned)
                myBox = rm.pitBoxes.Claim(this, RaceManager.AI_HOME_BOX[id % RaceManager.AI_HOME_BOX.Length], b => TrackModel.PitBoxD(b) - relPit, speed);
            if (!inLane && wasInLane) { rm.pitBoxes.Release(this); myBox = -1; pitBox = -1; }
            float workTarget = 0f;
            if (inLane)
            {
                float boxAhead = myBox >= 0 ? TrackModel.PitBoxD(myBox) - relPit : 1e9f;
                inPitZone = myBox >= 0 && Mathf.Abs(boxAhead) < 3f;
                target = Mathf.Min(target, 22f);
                if (pitPlanned && !pitStopActive && myBox >= 0) target = Mathf.Min(target, 1.2f + Mathf.Max(0f, boxAhead) * 0.45f);
                if (myBox >= 0 && (pitStopActive || (boxAhead < 14f && boxAhead > -8f))) workTarget = 1f;
            }
            workBlend = Mathf.MoveTowards(workBlend, workTarget, dt * 1.2f);

            if (pitStopActive)
            {
                float frac = Mathf.Min(1f, (now - pitStopStart) / (pitStopEnd - pitStopStart));
                tireWear = Mathf.Min(100f, pitStopStartWear + (100f - pitStopStartWear) * frac);
                target = 0f; speed = 0f;
                if (myBox >= 0) dist += (TrackModel.PitBoxD(myBox) - relPit) * Mathf.Min(1f, dt * 3f);      // settle into the middle of the box
                if (wheels != null)
                {
                    wheels.SetWheelsVisible(frac < 0.34f || frac > 0.6f);
                    if (frac > 0.5f && wheels.sidewallMat != null) wheels.sidewallMat.mainTexture = Tex.Sidewall(compound);
                }
                if (now >= pitStopEnd)
                {
                    if (wheels != null) wheels.SetWheelsVisible(true);
                    pitStopActive = false; pitPlanned = false; tireWear = 100f; mismatchTime = 0f; sinceStop = 0f;
                    pitCount++; stintStartLap = lap;
                    compound = rm.PitCompoundForAI(compound);
                    if (wheels != null && wheels.sidewallMat != null) wheels.sidewallMat.mainTexture = Tex.Sidewall(compound);
                }
            }
            else
            {
                bool inBrakeZone = aheadToPit < 90f || inPitZone;
                if (pitPlanned && inBrakeZone && !inLane)          // in the lane the brake target comes from our own box
                {
                    float brakeDist = inPitZone ? 0f : aheadToPit;
                    target = Mathf.Min(target, 2f + brakeDist * 0.42f);
                }
                if (pitPlanned && inPitZone && speed < 5f)
                {
                    pitStopActive = true; pitBox = myBox; pitStopStart = now; pitStopEnd = now + 2.5f + Random.value * 0.5f;
                    if (Random.value < 0.1f) pitStopEnd += 1.5f + Random.value * 2.5f;           // the odd slow stop
                    if (rm.fuelOn) pitStopEnd += 3f + Random.value * 3f;                          // refuelling
                    pitStopStartWear = tireWear; target = 0f; speed = 0f;
                }
                else tireWear = Mathf.Max(0f, tireWear - speed * dt * 0.014f * Tuning.TireWear[(int)compound] * Tuning.WetWear(compound, rm.roadWetness) * Personalities.WearMult(personality) * Regs.TyreWear);
            }

            // Acceleration tapers with speed (drag), like the player's car: strong off the line (about 11-14 m/s^2,
            // roughly 0-100 km/h in 2.5-3 s) and fading to almost nothing near top speed. Grip limits the launch.
            float aiTop = 82f;
            float accel0 = 7f + speedRating * 0.9f;
            float ratio = Mathf.Clamp01(speed / aiTop);
            float accel = accel0 * Mathf.Max(0.03f, 1f - ratio * ratio);
            if (speed < 25f) accel = Mathf.Min(accel, 12.5f);
            accel *= 1f + slingshot * 0.6f;
            bool brakingForPit = pitPlanned && (aheadToPit < 90f || inPitZone || inLane);
            bool brakingForCorner = target < speed - 3f;
            if (speed < target) speed = Mathf.Min(target, speed + accel * dt);
            else
            {
                // slowing down: ease toward the target but never harder than a real F1 can brake (about 4g)
                float rate = brakingForPit ? 3.2f : brakingForCorner ? 2.4f : 1.5f;
                float maxDecel = brakingForPit ? 30f : spinning ? 35f : retired ? 14f : rm.redFlag ? 12f : 40f;
                float dec = Mathf.Min((speed - target) * rate, maxDecel);
                speed = Mathf.Max(target, speed - dec * dt);
            }

            dist += speed * dt;
            if (wheels != null) wheels.Animate(speed, aiSteer * 0.45f, dt);
            int newLap = Mathf.FloorToInt(dist / tm.roadLength);
            if (newLap > lap) lap = newLap;

            // aggressive drivers defend: with a car right behind them they drift across to its side of the road
            float defendTarget = 0f;
            if (personality == Personality.Aggressive && rm.player != null && !pitStopActive)
            {
                float gap = dist - rm.player.dist;
                if (gap > 3f && gap < 14f)
                    defendTarget = Mathf.Clamp(tm.SignedLateral(new Vector2(rm.player.pos.x, rm.player.pos.z)) * 0.9f, -2.2f, 2.2f);
            }
            defend += (defendTarget - defend) * Mathf.Min(1f, dt * 2f);
            float mistakeOffset = 0f;
            if (inMistake)
            {
                float f = Mathf.Clamp01(1f - (mistakeUntil - now) / mistakeDuration);
                mistakeOffset = Mathf.Sin(f * Mathf.PI) * 3.2f * mistakeSide;
            }
            if (hopV != 0f || hop > 0f)
            {
                hop += hopV * dt; hopV -= 26f * dt; rollExtra += spinRate * dt;                      // launched: up, rolling, and back down
                if (hop <= 0f) { hop = 0f; hopV = 0f; spinRate = 0f; rollExtra = 0f; }
            }
            float accAi = dt > 0f ? (speed - lastSpeedAi) / dt : 0f;
            lastSpeedAi = speed;
            aiPitch += (Mathf.Clamp(-accAi * 0.10f, -2.4f, 3.2f) * Mathf.Clamp01(speed / 25f) - aiPitch) * Mathf.Min(1f, dt * 7f);
            aiRoll += (aiSteer * Mathf.Clamp01(speed / 60f) * 2.4f - aiRoll) * Mathf.Min(1f, dt * 6f);
            Place(mistakeOffset + defend + passOffset + shove + yieldOffset + retireOffset + spinOffset, tm.LineOffsetAt(dist) * 0.8f, dt);

            // steer the front wheels to match how fast the car is actually turning (yaw = 180 deg - heading)
            float yaw = go.transform.eulerAngles.y;
            float turnRate = dt > 0f ? -Mathf.DeltaAngle(lastYaw, yaw) * Mathf.Deg2Rad / dt : 0f;
            lastYaw = yaw;
            aiSteer += (Mathf.Clamp(turnRate / 0.5f, -1f, 1f) - aiSteer) * Mathf.Min(1f, dt * 6f);
        }

        void Place(float extra = 0f, float lineOff = 0f, float dt = 0f)
        {
            Vector2 p = tm.PointAt(dist), t = tm.TangentAt(dist), n = TrackModel.Normal(t);
            float offsetFactor = Mathf.Max(0f, 1f - (dist - startDist) / 40f);
            lateral = Mathf.Clamp(gridOffset * offsetFactor + (extra + lineOff) * (1f - offsetFactor), -8.4f, 8.4f);
            Vector2 pos = p + n * lateral;
            if (dt > 0f) laneBlend = Mathf.MoveTowards(laneBlend, inLane ? 1f : 0f, dt * 1.6f);
            if (laneBlend > 0.001f)
            {
                Vector2 lanePos = p, laneTan = t; Vector3 lp;
                if (tm.PitLanePosAt(dist, out lp))
                {
                    Vector2 laneBase = new Vector2(lp.x, lp.z);
                    float factor = tm.pitLaneOffset > 0.01f ? tm.PitLaneOffsetAt(dist) / tm.pitLaneOffset : 0f;
                    lanePos = laneBase + tm.PitLaneNormalAt(dist) * (Mathf.Lerp(-TrackModel.PIT_WORK_SHIFT, TrackModel.PIT_WORK_SHIFT, workBlend) * factor);
                    Vector3 lp2;
                    if (tm.PitLanePosAt(dist + 1.5f, out lp2)) laneTan = (new Vector2(lp2.x, lp2.z) - laneBase).normalized;
                }
                pos = Vector2.Lerp(pos, lanePos, laneBlend);
                Vector2 blended = Vector2.Lerp(t, laneTan, laneBlend);
                if (blended.sqrMagnitude > 0.0001f) t = blended.normalized;
            }
            go.transform.position = TrackModel.W(pos, tm.ElevationAt(dist) + hop);
            go.transform.rotation = Quaternion.Euler(aiPitch, (Mathf.PI - Mathf.Atan2(t.x, t.y)) * Mathf.Rad2Deg + yawExtra, aiRoll + rollExtra);
        }
    }
}
