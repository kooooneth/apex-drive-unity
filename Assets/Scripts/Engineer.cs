using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// Your race engineer. He reports the gap and the tyres of the cars around you, tyre temperatures, fuel, wind and sun, and the stops
    /// rivals make. He talks in short bursts: chatter waits for a quiet moment, and only warnings interrupt. He also remembers whether
    /// you listen: ignore his calls and he repeats himself, then trusts you less and falls quiet.
    /// </summary>
    public class Engineer
    {
        float nextAllowed;                              // his own breathing space between messages
        int lastRank; float lastRankCall = -99f;
        float dirtyTime;
        string lastFlag = "";
        readonly HashSet<string> said = new HashSet<string>();
        readonly Dictionary<string, float> topic = new Dictionary<string, float>();
        float lastNow, behindClose, mateBehind;

        // how much you listen to him (0..100)
        public float trust = 60f;
        public int ignoredCalls, followedCalls;
        bool quietSaid, warmSaid;
        class Nag { public string key, text; public System.Func<bool> still; public float at; public int times; }
        readonly List<Nag> nags = new List<Nag>();

        // looking around
        float nextGap = 35f; Racer lastAhead; float lastAheadGap = -1f, lastGapAt;
        readonly Dictionary<Racer, bool> pitting = new Dictionary<Racer, bool>();
        int lastLap = -1; float nextTyre = 45f;
        bool drsSaid, windSaid;

        public string TrustText { get { return Mathf.RoundToInt(trust) + "%  (" + followedCalls + " calls followed, " + ignoredCalls + " ignored)"; } }

        /// <param name="prio">0 chatter, 1 information, 2 warning, 3 critical</param>
        bool Say(RaceManager rm, string key, string msg, float now, int prio = 1, bool once = true, string group = null, float groupCool = 0f)
        {
            if (prio < 3 && now < nextAllowed) return false;
            if (once && said.Contains(key)) return false;
            if (trust < 30f && prio < 2) return false;                         // an engineer who is ignored stops chatting
            if (group != null) { float t; if (topic.TryGetValue(group, out t) && now < t) return false; }
            said.Add(key);
            if (group != null) topic[group] = now + groupCool;
            rm.Radio(msg, prio);
            nextAllowed = now + (prio >= 2 ? 4f : 11f);
            return true;
        }

        void Followed() { followedCalls++; trust = Mathf.Min(100f, trust + 4f); }

        void Ignored(RaceManager rm, string key, System.Func<bool> still, string nagText)
        {
            ignoredCalls++; trust = Mathf.Max(0f, trust - 7f);
            if (still != null && nagText != null) nags.Add(new Nag { key = key, text = nagText, still = still, at = rm.Now });
        }

        /// <summary>Asks a question on the radio; remembers what you answered.</summary>
        void Ask(RaceManager rm, string key, string text, string yesLabel, string noLabel, System.Action yes, System.Action no, float ttl, System.Func<bool> still, string nag)
        {
            rm.calls.Ask(rm, key, text, yesLabel, noLabel,
                         () => { Followed(); yes(); },
                         () => { Ignored(rm, key, still, nag); no(); },
                         false, ttl,
                         () => Ignored(rm, key, still, nag));
        }

        void Nags(RaceManager rm, float now)
        {
            for (int i = nags.Count - 1; i >= 0; i--)
            {
                var n = nags[i];
                if (now - n.at < 32f) continue;
                if (!n.still()) { nags.RemoveAt(i); continue; }
                if (n.times == 0) { rm.Radio(n.text, 2); n.times = 1; n.at = now; nextAllowed = now + 5f; }
                else
                {
                    rm.Radio("I have asked you twice now. " + n.text, 2);
                    trust = Mathf.Max(0f, trust - 5f); nags.RemoveAt(i); nextAllowed = now + 5f;
                }
            }
            if (trust < 35f && !quietSaid) { quietSaid = true; rm.Radio("Right. I will keep the calls to what matters from now on.", 2); }
            if (trust > 80f && !warmSaid && followedCalls >= 3) { warmSaid = true; rm.Radio("Good, we are working well together. I will keep you posted on the cars around you.", 1); }
        }

        static string Corner(int i) { return i == 0 ? "front left" : i == 1 ? "front right" : i == 2 ? "rear left" : "rear right"; }

        public void Update(RaceManager rm, PlayerCar p, float now)
        {
            TrackModel tm = rm.track;
            int rank = rm.PlayerRank();
            if (lastRank == 0) lastRank = rank;

            // positions: one call, then a pause, even if the place changes hands several times
            if (rank != lastRank)
            {
                bool gained = rank < lastRank;
                if (now - lastRankCall > 25f && now > 12f && Say(rm, "pos" + now.ToString("0"), gained ? "P" + rank + ", good move, keep it clean." : "We lost a place, you are P" + rank + ".", now, 1, false))
                    lastRankCall = now;
                lastRank = rank;
            }

            // flags
            if (rm.flagBanner != lastFlag)
            {
                lastFlag = rm.flagBanner;
                if (lastFlag.StartsWith("YELLOW")) Say(rm, "y" + now.ToString("0"), "Yellow flag in your sector, no overtaking.", now, 2, false, "yellow", 25f);
                else if (lastFlag.StartsWith("BLUE")) Say(rm, "b" + now.ToString("0"), "Blue flag, let him through at the next corner.", now, 2, false, "blue", 25f);
            }

            Nags(rm, now);
            LookAround(rm, p, now);

            // the basics every engineer calls: worn tyres and heavy damage
            if (p.tireWear < 40f) Say(rm, "wear40", "Tyres are going off, plan the stop.", now, 1);
            if (p.tireWear < 22f)
            {
                float ahead = TrackModel.Mod(tm.PitZoneDist - p.dist, tm.roadLength);
                if (ahead < 700f) Say(rm, "box", "Box box, box box. Tyres are critical.", now, 2);
            }
            if (p.wingDamage > 35f) Say(rm, "wing", "Front wing damage. Manageable, but you have lost downforce.", now, 2);
            if (p.damage > 60f) Say(rm, "dmg", "Heavy damage, consider coming in.", now, 2);

            // a better engineer also watches the temperatures, the brakes, the battery, dirty air, DRS, the wind and the sun
            if (Staff.Engineer >= 1)
            {
                if (now > 8f && !rm.isRaining && rm.trackTemp > 42f) Say(rm, "hotTrack", "The track is very hot, the tyres will run hot. Look after them.", now, 0);
                else if (now > 8f && !rm.isRaining && rm.trackTemp < 20f) Say(rm, "coldTrack", "Cold track today, the tyres will take a while to come in.", now, 0);

                TyreTemps(rm, p, now);

                if (p.brakeTemp > 900f) Say(rm, "brk", "Brakes are very hot, brake earlier and let them cool.", now, 1, true);
                if (p.ers < 12f) Say(rm, "ersLow", "Battery is low, harvest through the corners.", now, 1);
                if (p.ers > 96f && now > 15f) Say(rm, "ersFull", "Battery is full, use the overtake button.", now, 0);

                if (p.dirtyAir > 0.5f) dirtyTime += 1f; else dirtyTime = 0f;
                if (dirtyTime > 3f) Say(rm, "dirty" + (now / 60f).ToString("0"), "You are in dirty air, give him some room.", now, 0, false, "dirty", 50f);

                if (p.drsAvailable && !drsSaid) { drsSaid = true; Say(rm, "drs", "DRS is enabled.", now, 0); }
                else if (p.drs != null && p.drs.lastGap >= 1f && p.drs.lastGap < 1.5f && !p.drsAvailable)
                    Say(rm, "drsmiss" + Mathf.FloorToInt(now / 90f), "Just missed DRS, he was " + p.drs.lastGap.ToString("0.0") + " ahead at the line.", now, 0, false, "drsmiss", 90f);

                WindAndSun(rm, p, now);
            }

            // race state (the race manager already calls safety cars, red flags and restarts)
            if (p.lap == rm.raceLaps - 1 && rm.raceLaps > 1) Say(rm, "final", "Final lap. Everything you have got.", now, 2);

            StrategyCalls(rm, p, now);
            lastNow = now;
        }

        // ---------------------------------------------------------------- the cars around you
        void LookAround(RaceManager rm, PlayerCar p, float now)
        {
            TrackModel tm = rm.track;
            float L = tm.roadLength;

            // rivals making a stop
            foreach (var c in rm.racers)
            {
                if (c == p || c.gone) continue;
                bool was; pitting.TryGetValue(c, out was);
                if (c.pitStopActive && !was)
                {
                    int diff = Mathf.Abs(RankOf(rm, c) - rm.PlayerRank());
                    if (diff <= 3 && now > 15f)
                        Say(rm, "rp" + RankOf(rm, c) + "_" + c.pitCount, rm.NameOf(c) + " is boxing" + (Staff.Engineer >= 1 ? ", he was on " + Tuning.CompoundNames[(int)c.compound] + " tyres " + Mathf.Max(0, c.lap - c.stintStartLap) + " laps old" : "") + ".", now, 1, false, "rivalPit", 15f);
                }
                pitting[c] = c.pitStopActive;
            }

            // the gaps, now and then, with names and tyres
            if (now >= nextGap && trust >= 40f)
            {
                Racer ahead = null, behind = null; float aMin = float.MaxValue, bMin = float.MaxValue;
                foreach (var c in rm.racers)
                {
                    if (c == p || c.gone || c.dnf) continue;
                    float a = TrackModel.Mod(c.dist - p.dist, L), b = L - a;
                    if (a < aMin) { aMin = a; ahead = c; }
                    if (b < bMin) { bMin = b; behind = c; }
                }
                float sp = Mathf.Max(12f, p.speed);
                float gAhead = aMin / sp, gBehind = bMin / sp;
                string msg = null;
                if (ahead != null && gAhead < 4f)
                {
                    msg = "Gap to " + rm.NameOf(ahead) + " ahead, " + gAhead.ToString("0.0") + " seconds";
                    if (Staff.Engineer >= 1) msg += ". He is on " + Tuning.CompoundNames[(int)ahead.compound] + ", " + Mathf.Max(0, ahead.lap - ahead.stintStartLap) + " laps old";
                    if (Staff.Engineer >= 2 && lastAhead == ahead && lastAheadGap > 0f && now - lastGapAt < 90f)
                    {
                        float d = lastAheadGap - gAhead;
                        if (d > 0.25f) msg += ". We are catching him";
                        else if (d < -0.25f) msg += ". He is pulling away";
                    }
                    lastAhead = ahead; lastAheadGap = gAhead; lastGapAt = now;
                }
                else if (behind != null && gBehind < 3f)
                    msg = rm.NameOf(behind) + " is " + gBehind.ToString("0.0") + " seconds behind you" + (Staff.Engineer >= 1 ? ", on " + Tuning.CompoundNames[(int)behind.compound] + " tyres" : "");
                if (msg != null) Say(rm, "gap" + now.ToString("0"), msg + ".", now, 0, false);
                nextGap = now + (trust > 75f ? 32f : 48f);
            }
        }

        static int RankOf(RaceManager rm, Racer r)
        {
            int rank = 1;
            foreach (var c in rm.racers) if (c != r && !c.dnf && c.dist > r.dist) rank++;
            return rank;
        }

        void TyreTemps(RaceManager rm, PlayerCar p, float now)
        {
            float lo = p.TempLowForCompound, hi = p.TempHighForCompound;
            if (p.tireTemp < lo - 8f && now > 6f && now < 40f) { Say(rm, "cold", "Tyres are cold, get some heat into them.", now, 1); return; }
            if (now < nextTyre) return;
            int hot = 0, cold = 0; float hotT = -1f, coldT = 999f;
            for (int i = 0; i < 4; i++)
            {
                float t = p.dyn.surf[i];
                if (t > hotT) { hotT = t; hot = i; }
                if (t < coldT) { coldT = t; cold = i; }
            }
            if (hotT > hi + 10f && Say(rm, "hotc" + now.ToString("0"), "The " + Corner(hot) + " is running hot, " + Mathf.RoundToInt(hotT) + " degrees. Ease the steering and look after it.", now, 1, false, "tyreTemp", 70f))
                nextTyre = now + 40f;
            else if (coldT < lo - 14f && now > 25f && Say(rm, "coldc" + now.ToString("0"), "The " + Corner(cold) + " is cold at " + Mathf.RoundToInt(coldT) + " degrees. Push a little more to keep temperature in it.", now, 1, false, "tyreTemp", 70f))
                nextTyre = now + 40f;
        }

        void WindAndSun(RaceManager rm, PlayerCar p, float now)
        {
            if (rm.wind != null && !windSaid && now > 5f && rm.wind.mean >= 5.5f)
            {
                windSaid = true;
                Say(rm, "windday", "It is windy out there, " + Mathf.RoundToInt(rm.wind.mean) + " metres a second" + (rm.wind.gustiness > 0.55f ? " and gusting" : "") + ". The car will move around on the straights.", now, 0);
            }
            if (Mathf.Abs(p.crossWind) > 7f && p.speed > 50f)
                Say(rm, "xwind" + Mathf.FloorToInt(now / 90f), "Strong crosswind on this straight, hold the car steady.", now, 1, false, "xwind", 90f);
            if (rm.GlareLevel > 0.55f)
                Say(rm, "glare" + Mathf.FloorToInt(now / 120f), "The sun is low and right in your eyes. Brake at the board and keep to the line.", now, 1, false, "glare", 120f);
        }

        // The pit wall asks you to make a call: Y / N on the keyboard (or the buttons). Ignore it and he remembers.
        void StrategyCalls(RaceManager rm, PlayerCar p, float now)
        {
            if (rm.calls.Busy || rm.redFlag || rm.rolling || p.pitStopActive || p.pitRequest) return;
            TrackModel tm = rm.track;
            float dt = Mathf.Clamp(now - lastNow, 0f, 0.1f);

            if (p.punctured)
            {
                Ask(rm, "punc", "Slow puncture! Box this lap?", "Box, box", "Try to stay out",
                    () => p.RequestPit(-1), () => rm.Radio("OK, stay out, but be careful with it.", 1), 12f,
                    () => p.punctured && !p.pitStopActive, "That puncture is only getting worse. Box this lap.");
                return;
            }
            if (p.wingDamage > 45f || p.floorDamage > 40f)
            {
                Ask(rm, "dmg", "Heavy aero damage - we are losing a lot of grip. Box for repairs?", "Box, box", "Stay out",
                    () => p.RequestPit(-1), () => rm.Radio("OK, we will manage it.", 1), 12f,
                    () => (p.wingDamage > 45f || p.floorDamage > 40f) && !p.pitStopActive, "That aero damage is costing us a second a lap. Box this lap.");
                return;
            }
            if ((rm.safetyCarActive || rm.vscActive) && p.tireWear < 75f)
            {
                Ask(rm, rm.safetyCarActive ? "sc" : "vsc",
                    (rm.safetyCarActive ? "Safety car! " : "Virtual safety car! ") + "A stop costs us far less right now. Box?", "Box, box", "Stay out",
                    () => p.RequestPit(-1), () => rm.Radio("Staying out.", 1), 10f, null, null);
                return;
            }

            // wet-weather lines: the dry line comes through first, and the water creeps across the road
            if (!rm.isRaining && rm.roadWetness > 0.18f && rm.roadWetness < 0.75f && now > 20f) Say(rm, "dryline", "The dry line is coming through. Stay on it for grip: off the line the road is still very wet.", now, 1);
            if (rm.isRaining && rm.puddles != null && rm.roadWetness > 0.6f && now > 40f) Say(rm, "puddles", "Standing water is building up and moving across the road. Look for a clear path round the puddles.", now, 1);

            // fuel in a sprint race: no refuelling, so the tank has to last
            if (rm.fuelOn && !rm.Refuels && p.lap != lastLap)
            {
                lastLap = p.lap;
                if (p.lap >= 1 && p.lap < rm.raceLaps)
                {
                    float m = p.FuelMarginLaps;
                    if (m < -0.25f)
                    {
                        Ask(rm, "fuelsave" + p.lap, "We are " + Mathf.Abs(m).ToString("0.0") + " laps short on fuel. Fuel save for two laps?", "Save fuel", "Keep pushing",
                            () => { p.SaveFor(70f, now); rm.Radio("Save mode on. Lift and coast into the slow corners.", 1); }, () => rm.Radio("OK, but watch the fuel.", 1), 12f,
                            () => p.FuelMarginLaps < -0.25f, "The fuel is still short. Save now or you will not reach the line.");
                        return;
                    }
                    if (m < 0f) Say(rm, "fuelshort" + p.lap, "Fuel is tight, " + Mathf.Abs(m).ToString("0.0") + " laps short at this rate. Lift and coast a little.", now, 1, false, "fuel", 40f);
                    else if (m > 0.55f && p.lap >= 2) Say(rm, "fuelok", "Fuel is fine, plus " + m.ToString("0.0") + " laps. You can use push mode.", now, 0);
                }
            }

            // the tyre strategy plan from the Race tab: on the planned laps the engineer asks you to box
            if (rm.IsRace && Strategy.ActiveFor(rm.raceLaps))
            {
                for (int s = 0; s < Strategy.plan.stops; s++)
                {
                    if (p.pitCount != s || p.lap != Strategy.plan.stopLap[s] - 1) continue;
                    int comp = Strategy.plan.compound[s + 1];
                    int sIdx = s;
                    Ask(rm, "plan" + s, "The plan says box this lap for " + Strategy.Names[comp] + " tyres. Box?", "Box, box", "Stay out",
                        () => p.RequestPit(comp), () => rm.Radio("OK, staying out. We will look at it again next lap.", 1), 16f,
                        () => p.pitCount == sIdx && !p.pitStopActive, "You are past the planned stop and the tyres are fading. Box this lap.");
                    return;
                }
            }

            // endurance: low fuel, a tired driver
            if (rm.Refuels && p.fuel < 18f)
            {
                Ask(rm, "fuel" + Mathf.FloorToInt(now / 150f), "Fuel is getting low. Box this lap?", "Box, box", "Stay out",
                    () => p.RequestPit(-1), () => rm.Radio("OK, we will lift and coast.", 1), 12f, () => p.fuel < 12f && !p.pitStopActive, "We are nearly out of fuel. Box now.");
                return;
            }
            if (rm.Refuels && p.fatigue > 0.65f && !p.swapPlanned)
            {
                Ask(rm, "swap" + Mathf.FloorToInt(now / 200f), "The driver is tiring. Swap to the relief driver at the next stop? It costs 4 seconds.", "Swap driver", "Carry on",
                    () => { p.swapPlanned = true; p.RequestPit(-1); }, () => rm.Radio("OK, hold on in there.", 1), 14f, null, null);
                return;
            }

            if (Staff.Engineer < 2) return;                                  // team orders, undercuts and pushes need a better engineer

            // team orders: your faster teammate has been stuck right behind you
            AICar mate = null;
            foreach (var c in rm.racers) { var a = c as AICar; if (a != null && a.id == 0 && !a.gone && !a.dnf) mate = a; }
            if (mate != null && mate.speed > p.speed + 0.5f && p.yieldTo == null)
            {
                float mb = TrackModel.Mod(p.dist - mate.dist, tm.roadLength);
                mateBehind = (mb > 1f && mb < 40f) ? mateBehind + dt : Mathf.Max(0f, mateBehind - dt * 2f);
                if (mateBehind > 6f)
                {
                    mateBehind = 0f;
                    rm.calls.Ask(rm, "orders" + Mathf.FloorToInt(now / 120f), "Team orders: " + mate.DriverName + " is quicker and right behind you. Let him through?", "Let him by", "Hold position",
                                 () => { Followed(); p.yieldTo = mate; p.yieldUntil = now + 25f; TeamCareer.rapport = Mathf.Min(100, TeamCareer.rapport + 6); TeamCareer.Save(); rm.Radio("Good, thanks. He is coming through.", 1); },
                                 () => { Ignored(rm, "orders", null, null); TeamCareer.rapport = Mathf.Max(0, TeamCareer.rapport - 8); TeamCareer.Save(); rm.Radio("Understood. You are racing him.", 1); }, false, 10f);
                    return;
                }
            }

            // undercut: a rival close to you has just pitted onto fresh tyres
            AICar boxing = null;
            foreach (var c in rm.racers)
            {
                var a = c as AICar;
                if (a != null && a.pitStopActive && Mathf.Abs(a.dist - p.dist) < 200f) boxing = a;
            }
            if (boxing != null && p.tireWear < 55f)
            {
                int stops = p.pitCount;
                Ask(rm, "uc" + Mathf.FloorToInt(now / 90f), boxing.label + " has pitted - undercut threat. Cover it and box now?", "Box, box", "Stay out",
                    () => p.RequestPit(-1), () => rm.Radio("OK, staying out. We will see how his tyres do.", 1), 10f,
                    () => p.pitCount == stops && p.tireWear < 45f && !p.pitStopActive, "He is catching you on fresh tyres. We should box.");
                return;
            }

            // a car glued to your gearbox: offer a push
            bool close = false; string who = "";
            foreach (var c in rm.racers)
            {
                if (c == p || c.gone) continue;
                float b = TrackModel.Mod(p.dist - c.dist, tm.roadLength);
                if (b > 1f && b < 40f) { close = true; who = c.label; }
            }
            behindClose = close ? behindClose + dt : Mathf.Max(0f, behindClose - dt * 2f);
            if (behindClose > 8f && p.engineMode != EngineMode.Push && p.ers > 20f)
            {
                behindClose = 0f;
                Ask(rm, "push" + Mathf.FloorToInt(now / 120f), who + " is right behind you. Push for 25 seconds?", "Push, push", "Hold the pace",
                    () => p.PushFor(25f, now), () => rm.Radio("OK, holding the pace.", 1), 10f, null, null);
            }
        }
    }
}
