using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// Owns the whole game: garage menu, building the world, grid + countdown, race loop, weather,
    /// safety car, ghost, career / tournament, camera, audio and results. Created automatically at
    /// startup by Bootstrap - no scene setup needed.
    /// </summary>
    public class RaceManager : MonoBehaviour
    {
        public enum State { Menu, Countdown, Racing, Paused, Finished, Replay }
        public enum Mode { Quick, Qualifying, Career, Tournament, Tutorial, Custom, Endurance, Practice, Multiplayer }
        enum Tab { Garage, Race, Racer, Team, Crew, Profile }

        public State state = State.Menu;
        public Mode mode = Mode.Quick;
        public TrackModel track;
        public readonly List<Racer> racers = new List<Racer>();
        public PlayerCar player;
        public int raceLaps = 3;
        public float aiDifficulty = 1f, aiMistakeMult = 1f;

        // weather
        public bool isRaining, isFoggy;
        public float roadWetness, rainOpacity;

        // safety car
        public bool safetyCarActive;
        public bool scRestart;                        // the safety car has come in: the leader sets the pace until the control line
        float scDist, restartLine, slowRestart;
        float cautionEndAt;
        GameObject scGo;

        // shared HUD messaging
        public string radioText = "", warnText = "", countdownText = "";
        public float radioUntil, warnUntil;

        float clock;                 // race clock in seconds, frozen while paused
        public float Now { get { return clock; } }

        Hud hud;
        PitCrew[] pitCrews;
        public readonly PitBoxes pitBoxes = new PitBoxes();
        public static readonly int[] AI_NUMBERS = { 11, 14, 16, 23, 31 };
        public static readonly int[] AI_HOME_BOX = { 0, 1, 3, 4, 5 };      // which box each AI team uses (the player's team has box 3)
        Engineer engineer = new Engineer();
        readonly ReplayRecorder rec = new ReplayRecorder();
        Marshals marshals; RacingLine racingLine;

        /// <summary>Seconds until the weather changes (for the radar), and whether it is rain coming or the rain going.</summary>
        public float WeatherEta(out bool toRain, out bool hasChange)
        {
            hasChange = !weatherChanged && weatherChangeAt >= 0f && activePlan != null;
            toRain = hasChange && activePlan.changeToRain;
            return hasChange ? Mathf.Max(0f, weatherChangeAt - clock) : 0f;
        }
        public float RadarAngle { get { return track != null ? (track.def.id.GetHashCode() & 255) / 255f * 6.283f : 0f; } }

        /// <summary>Grip from where you are relative to the racing line (only matters on a wet road).</summary>
        public float LineGrip(float along, float lateral)
        {
            return racingLine == null ? 1f : racingLine.GripAt(along, lateral, roadWetness, clock - raceStartClock, isRaining ? rainOpacity : 0f);
        }
        readonly Debris debris = new Debris();
        public string flagBanner = "";
        public float[] incidentUntil = new float[3];
        public float raceStartClock = 9999f;
        float lookYaw; bool finishFireworks;
        // replay / drone
        float replayT, replaySpeed = 1f; bool replayPlay = true, replayHideUi, trackCamSet;
        int replayCam, replayFocus; int[] replayHints = new int[8]; Vector3 trackCamPos;
        string toast = ""; float toastUntil;
        Camera cam;
        CarAudio audioFx;
        GameObject trackRoot;
        Light sun;
        ParticleSystem rain;
        enum CamMode { Cockpit, Halo, Helmet, Chase, Tv }
        CamMode camMode = CamMode.Cockpit;
        CamMode lastCamMode = CamMode.Cockpit;
        bool cockpitView { get { return camMode == CamMode.Cockpit; } }                       // the full cockpit with the steering wheel and mirrors
        bool firstPerson { get { return camMode == CamMode.Cockpit || camMode == CamMode.Helmet; } }
        static readonly string[] CamNames = { "COCKPIT", "HALO CAM", "HELMET CAM", "CHASE", "TV BROADCAST" };
        // smooth blending between views
        float camBlend = 1f; Vector3 camFromPos; Quaternion camFromRot = Quaternion.identity; float camFromFov = 78f;
        // the TV director: cuts between trackside, helicopter and low cameras
        int tvShot = -1; float tvTimer; Vector3 tvPos; bool tvShotChanged;
        TutorialGuide tutorial;

        // race control
        public Caution caution;
        public bool vscActive, redFlag, rolling;
        public float trackTemp = 30f;                  // degrees C, drives the tyre temperature windows

        // endurance races, practice sessions, the gym
        public static readonly int[] ENDURANCE_LAPS = { 6, 10, 15 };
        public bool fuelOn; public float fuelLaps = 4f;
        public float FuelRangeMeters { get { return track.roadLength * fuelLaps; } }
        public float timeOfDay;                        // 0 = bright afternoon .. 1 = night (endurance races)
        public int practiceDrill;                      // 0 smooth laps, 1 fast laps, 2 long run, 3 setup run, 4 perfect lap, 5 braking test, 6 tyre saver
        float chLine, chNeedSpeed, chStopGap; bool chArmed, chDone, chTimedOut;      // the braking test: where the line is, the speed you must reach first, how it ended
        readonly Gym gym = new Gym();
        readonly List<Light> headlights = new List<Light>();
        public Puddles puddles;
        public RoadDetails road;                        // tarmac patches, drain covers and bumps
        float puddleLevel;                              // standing water that builds up while it rains and drains when it is dry
        RecoveryTruck recovery;
        public int cautionCount;                      // how many cautions this race (AI drivers decide on a stop once per caution)
        readonly List<ParticleSystem> sprays = new List<ParticleSystem>();
        float sunElev = 52f, sunAz = -35f;            // degrees; low sun glares
        Light cockpitLamp;

        static bool IsRaceMode(Mode m) { return m == Mode.Quick || m == Mode.Career || m == Mode.Tournament || m == Mode.Custom || m == Mode.Endurance || m == Mode.Multiplayer; }
        public bool IsRace { get { return IsRaceMode(mode); } }
        public readonly Visor visor = new Visor();
        Floodlights floods; Stars stars;
        // ---- online play ----
        public readonly NetSession net = new NetSession();
        bool netPanel, mpMenu; string joinCode = "", hostAddr = ""; List<NetPlayer> mpRoster; int mpSeed;
        public readonly Broadcast bcast = new Broadcast();
        public WindField wind;
        public float GlareLevel;                                  // how blinded you are by the sun right now, 0..1
        float sunNext;
        const float SUN_TIMELAPSE = 14f;                          // the sun moves 14x faster than the clock, so shadows creep during a race
        /// <summary>Only endurance races refuel; in a sprint race the tank has to last.</summary>
        public bool Refuels { get { return mode == Mode.Endurance; } }
        /// <summary>DRS rules right now: 0 closed, 1 only within a second of the car ahead at the detection point.</summary>
        public int DrsRule
        {
            get
            {
                if (state != State.Racing || !IsRace || safetyCarActive || vscActive || redFlag || rolling || introActive) return 0;
                if (roadWetness > 0.3f || isRaining) return 0;
                return clock - raceStartClock > 15f ? 1 : 0;
            }
        }       // TV graphics: banners for overtakes, pit stops and laps, plus the radar
        readonly Rewinder rewind = new Rewinder();                // three rewinds per race (Backspace)
        bool showBoard = true;                    // the leaderboard at the top right (L)
        float boardNext; List<Racer> boardOrder = new List<Racer>();
        int startRank = 1;                      // where you started the race (for the sponsors)
        public readonly Stewards stewards = new Stewards();
        public readonly CallCenter calls = new CallCenter();
        public QualiSession quali;
        public Weekend weekend;                        // an unfinished race weekend, if any
        public bool formationLap;                      // the rolling start is a full formation lap from the grid
        bool introActive; const float INTRO_LEN = 5.5f; string introCaption = "";     // the grid-walk cutscene before a formation lap
        bool showWall;                                 // the pit-wall strategy screen (T)
        public Racer blueCar;                          // a car a lap ahead that is catching you (blue flag)
        float blueTimer;
        Caution plannedCaution; float cautionAt = -1f, lastCautionEnd = -99f;
        int redPhase; float redPhaseStart, redStartClock, redFlagLost; bool isRestart;
        WeatherPlan weatherPlan, activePlan; int planTrack = -1;
        float weatherChangeAt = -1f; bool weatherChanged, weatherWarned;
        readonly Dictionary<Racer, float> prevRel = new Dictionary<Racer, float>();
        readonly Dictionary<Racer, float> finishTimes = new Dictionary<Racer, float>();
        readonly TelemetryView telemetry = new TelemetryView(); bool showTelemetry;
        CockpitRig rig; Camera overlayCam;
        float countdownLeft;
        State stateBeforePause;
        int builtTrackIndex = -1;

        // ghost
        GameObject ghostGo; Vector2[] ghostSamples; float ghostDuration; int ghostIdx;

        // menu + results
        Tab tab = Tab.Garage;
        Vector2 scroll;
        string finishSummary = "";
        readonly List<string> finishOrder = new List<string>();
        readonly List<string> finishNotes = new List<string>();
        Run finishRun;

        static readonly int[] PAYOUT = { 2500, 1500, 800, 300, 150, 100 };
        static readonly float[] GRID = { 0f, -22f, -42f, -62f, -82f, -102f };
        static readonly Color[] AICOLORS =
        {
            new Color(0.09f, 0.85f, 0.77f), new Color(1f, 0.69f, 0.13f), new Color(0.24f, 0.86f, 0.52f),
            new Color(0.54f, 0.36f, 1f), new Color(1f, 0.37f, 0.63f)
        };

        // ------------------------------------------------------------------ lifecycle
        void Awake()
        {
            GameSave.Load();
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.fieldOfView = 78f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 4000f;
            audioFx = new CarAudio(cam.gameObject);
            hud = new Hud();
            Voice.Start(cam.gameObject);
            BuildOverlayCam();
            BuildRain();
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.53f, 0.60f, 0.73f) * 0.85f;
            var lgo = new GameObject("Sun");
            sun = lgo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.91f, 0.82f);
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            Scenery.SetupShadows(sun);
            lgo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            stars = new Stars();
            CarSetups.Switch(Tracks.All[GameSave.trackIndex].id);       // the car of the selected circuit is in the garage
            EnterMenu();
        }

        /// <summary>Tyre an AI driver starts on: slicks in the dry, inters or wets if the race starts wet.</summary>
        public Compound StartCompoundForAI()
        {
            if (isRaining) return Random.value < 0.55f ? Compound.Inter : Compound.Wet;
            return (Compound)Random.Range(0, 3);
        }

        /// <summary>Tyre an AI driver fits at a stop, based on how wet the road is right now.</summary>
        public Compound PitCompoundForAI(Compound current)
        {
            if (roadWetness > 0.78f) return Compound.Wet;
            if (roadWetness > 0.3f) return Compound.Inter;
            if (current >= Compound.Inter) return Random.value < 0.5f ? Compound.Medium : Compound.Hard;
            return current;
        }

        /// <summary>Hot tracks cook the soft tyres, cold tracks leave the hard ones stone cold; wet-weather tyres do not mind the cold.</summary>
        public float AiTempFactor(Compound c)
        {
            float hot = Mathf.Max(0f, trackTemp - 38f), cold = Mathf.Max(0f, 24f - trackTemp);
            float[] heatSens = { 1.0f, 0.7f, 0.45f, 0.8f, 1.0f };
            float[] coldSens = { 0.5f, 0.8f, 1.0f, 0f, 0f };
            return Mathf.Max(0.85f, 1f - 0.008f * hot * heatSens[(int)c] - 0.008f * cold * coldSens[(int)c]);
        }

        // The radio has priorities, so it can talk less without going quiet:
        //   0 chatter (gaps, lap times, tips) - said only after a long silence, dropped if it cannot be said now
        //   1 information (rival stops, tyres, fuel) - waits a little and is dropped if it is stale
        //   2 events (damage, flags, stewards) - said soon, waits in the queue
        //   3 critical or an answer to something you just did - said at once
        struct RadioMsg { public string text; public int prio; public float at; }
        readonly List<RadioMsg> radioQ = new List<RadioMsg>();
        float lastRadioAt = -99f;
        static readonly float[] RADIO_GAP = { 22f, 10f, 3.5f, 0f };       // silence needed before a message of this priority
        static readonly float[] RADIO_LIFE = { 0f, 9f, 25f, 25f };        // how long a waiting message is still worth saying

        public void Radio(string s, int prio = 2)
        {
            prio = Mathf.Clamp(prio, 0, 3);
            bool free = state == State.Replay || mode == Mode.Tutorial || mode == Mode.Practice || mode == Mode.Qualifying || !(state == State.Racing || state == State.Countdown);
            if (free || prio >= 3 || (radioQ.Count == 0 && Time.time - lastRadioAt >= RADIO_GAP[prio])) { SpeakRadio(s); return; }
            if (prio == 0) return;
            for (int i = 0; i < radioQ.Count; i++) if (radioQ[i].text == s) return;
            radioQ.Add(new RadioMsg { text = s, prio = prio, at = Time.time });
            while (radioQ.Count > 3)
            {
                int worst = 0;
                for (int i = 1; i < radioQ.Count; i++) if (radioQ[i].prio < radioQ[worst].prio || (radioQ[i].prio == radioQ[worst].prio && radioQ[i].at < radioQ[worst].at)) worst = i;
                radioQ.RemoveAt(worst);
            }
        }

        void SpeakRadio(string s)
        {
            lastRadioAt = Time.time;
            radioText = s; radioUntil = Time.time + 3.5f;
            if (audioFx != null) audioFx.Radio();
            if (state != State.Replay) Voice.Say(Voice.RADIO, 3, Voice.RadioText(s, RacerProfile.DisplayName));       // the engineer speaks
        }

        void RadioTick()
        {
            if (radioQ.Count == 0) return;
            float now = Time.time;
            for (int i = radioQ.Count - 1; i >= 0; i--) if (now - radioQ[i].at > RADIO_LIFE[radioQ[i].prio]) radioQ.RemoveAt(i);
            if (radioQ.Count == 0) return;
            int best = 0;
            for (int i = 1; i < radioQ.Count; i++) if (radioQ[i].prio > radioQ[best].prio || (radioQ[i].prio == radioQ[best].prio && radioQ[i].at < radioQ[best].at)) best = i;
            if (now - lastRadioAt < RADIO_GAP[radioQ[best].prio]) return;
            string text = radioQ[best].text; radioQ.RemoveAt(best);
            SpeakRadio(text);
        }

        /// <summary>Pit crew chatter over the radio: shown, and shouted in a faster voice.</summary>
        public void CrewSay(string s)
        {
            radioText = s; radioUntil = Time.time + 2.5f;
            if (audioFx != null) audioFx.Radio();
            if (state != State.Replay) Voice.Say(Voice.RADIO, 5, s);
        }

        // ---- commentary: written down during the race, spoken only in the replay ----
        public readonly CommentaryLog commentary = new CommentaryLog();
        readonly CommentaryPlayer commPlayer = new CommentaryPlayer();
        List<Racer> lastOrder = new List<Racer>();
        readonly Dictionary<Racer, bool> wasPitting = new Dictionary<Racer, bool>();
        float nextOrderCheck; bool finalLapSaid;

        public string NameOf(Racer r)
        {
            if (r == null) return "Somebody";
            if (r.isPlayer) return RacerProfile.DisplayName;
            var a = r as AICar;
            return a != null ? a.DriverName : r.label;
        }

        public void Commentate(string text, int prio)
        {
            if (IsRace && state != State.Replay) commentary.Add(clock, text, prio);
        }

        static bool InPits(Racer r)
        {
            var a = r as AICar;
            if (a != null) return a.InLane || a.pitStopActive;
            var pc = r as PlayerCar;
            return pc != null && (pc.inPitLaneNow || pc.pitStopActive);
        }

        // watches the running order: lead changes, overtakes, cars coming into the pits, the final lap
        void TrackOrder()
        {
            if (clock < nextOrderCheck || rolling) return;
            nextOrderCheck = clock + 0.5f;
            var order = new List<Racer>();
            foreach (var r in racers) if (!r.gone && !r.dnf) order.Add(r);
            order.Sort((a, b) => b.dist.CompareTo(a.dist));
            if (lastOrder.Count > 0 && order.Count > 1 && clock - raceStartClock > 6f)
            {
                if (order[0] != lastOrder[0] && lastOrder.Contains(order[0]) && !InPits(order[1]))
                { Commentate(NameOf(order[0]) + " takes the lead of the race!", 2); bcast.Push("lead change", NameOf(order[0]) + " takes the lead", "P1", new Color(1f, 0.82f, 0.2f), 4.5f); }
                else
                {
                    for (int i = 0; i < order.Count - 1; i++)
                    {
                        int before = lastOrder.IndexOf(order[i]);
                        if (before > i && !InPits(order[i]) && !InPits(order[i + 1]))
                        {
                            Commentate(NameOf(order[i]) + " goes past " + NameOf(order[i + 1]) + " for P" + (i + 1) + ".", 1);
                            if (order[i].isPlayer || order[i + 1].isPlayer || i < 3) bcast.Push("overtake", NameOf(order[i]) + " passes " + NameOf(order[i + 1]), "for P" + (i + 1), new Color(0.2f, 0.9f, 0.8f), 3.5f);
                            break;
                        }
                    }
                }
            }
            lastOrder = order;
            foreach (var r in racers)
            {
                bool was;
                wasPitting.TryGetValue(r, out was);
                if (r.pitStopActive && !was)
                {
                    Commentate(NameOf(r) + " comes into the pits.", 1);
                    bcast.Push("pit stop", "PIT  " + NameOf(r), "stop " + (r.pitCount + 1) + "  -  " + Tuning.CompoundNames[(int)r.compound] + " tyres at " + Mathf.RoundToInt(r.tireWear) + "%", new Color(1f, 0.75f, 0.2f), 4f);
                }
                else if (!r.pitStopActive && was && !r.gone)
                {
                    int pos = order.IndexOf(r) + 1;
                    bcast.Push("pit stop", "OUT  " + NameOf(r), "now on " + Tuning.CompoundNames[(int)r.compound] + "  -  P" + Mathf.Max(1, pos), new Color(0.4f, 0.8f, 1f), 4f);
                }
                wasPitting[r] = r.pitStopActive;
            }
            if (!finalLapSaid && raceLaps > 1 && order.Count > 0 && Mathf.FloorToInt(order[0].dist / track.roadLength) >= raceLaps - 1)
            {
                finalLapSaid = true;
                Commentate("The leader starts the final lap! Everything to play for.", 2);
                bcast.Push("final lap", "FINAL LAP", NameOf(order[0]) + " leads", new Color(1f, 0.3f, 0.25f), 6f);
            }
        }
        public void Flash(string s) { warnText = s; warnUntil = Time.time + 1.2f; }
        public void Thud() { audioFx.Thud(); }
        public void Cheer() { audioFx.Cheer(); }

        static int TrackIndexOf(string id)
        {
            for (int i = 0; i < Tracks.All.Length; i++) if (Tracks.All[i].id == id) return i;
            return 0;
        }

        // ------------------------------------------------------------------ world building
        void BuildWorld(int trackIndex)
        {
            if (trackRoot != null) Destroy(trackRoot);
            var def = Tracks.All[trackIndex];
            track = new TrackModel(def);
            trackRoot = TrackBuilder.Build(track, new System.Random(def.id.GetHashCode()));
            floods = Floodlights.Build(track, trackRoot.transform);
            floods.SetNight(Mathf.SmoothStep(0f, 1f, timeOfDay));
            marshals = Marshals.Build(track, trackRoot.transform);
            racingLine = RacingLine.Build(track, trackRoot.transform);
            Drs.BuildSigns(track, trackRoot.transform);
            puddles = Puddles.Build(track, trackRoot.transform);
            road = RoadDetails.Build(track, trackRoot.transform);
            builtTrackIndex = trackIndex;
            hud.SetTrack(track);
        }

        void ApplyWeatherLook()
        {
            Color sky = isFoggy ? new Color(0.62f, 0.66f, 0.72f) : isRaining ? new Color(0.36f, 0.40f, 0.47f) : new Color(0.55f, 0.72f, 0.92f);
            // endurance races run from afternoon into night: sky, sun, ambient light and headlights follow timeOfDay
            float night = Mathf.SmoothStep(0f, 1f, timeOfDay);
            float dusk = Mathf.Sin(Mathf.Clamp01(timeOfDay) * Mathf.PI);
            sky = Color.Lerp(sky, new Color(0.02f, 0.03f, 0.07f), night * 0.93f);
            sky = Color.Lerp(sky, new Color(0.85f, 0.42f, 0.22f), dusk * 0.3f * (1f - night));
            cam.backgroundColor = sky;
            RenderSettings.fog = true;
            RenderSettings.fogColor = sky;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            float fogBase = isFoggy ? 0.0075f : isRaining ? 0.0028f : 0.0007f;
            RenderSettings.fogDensity = fogBase * (1f + night * 1.5f);
            float baseSun = isRaining || isFoggy ? 0.6f : 1.1f;
            sun.intensity = baseSun * Mathf.Lerp(1f, 0.1f, night);
            sun.color = Color.Lerp(new Color(1f, 0.91f, 0.82f), new Color(1f, 0.55f, 0.3f), Mathf.Clamp01(dusk * 1.2f));
            sun.transform.rotation = Quaternion.Euler(Mathf.Lerp(sunElev, -6f, timeOfDay), sunAz + 180f, 0f);       // sunAz is the compass bearing of the sun itself
            sun.shadowStrength = (isRaining || isFoggy ? 0.5f : 0.92f) * (1f - 0.25f * dusk);
            RenderSettings.ambientLight = Color.Lerp(new Color(0.53f, 0.60f, 0.73f) * 0.85f, new Color(0.10f, 0.12f, 0.20f), night);
            foreach (var l in headlights) if (l != null) { l.intensity = 2.8f * night; l.enabled = night > 0.04f; }
            if (floods != null) floods.SetNight(Mathf.SmoothStep(0.25f, 0.8f, timeOfDay));      // the floodlights come on gradually as the light fades
            if (stars != null) stars.SetNight(night);
            if (cockpitLamp != null) cockpitLamp.intensity = 0.9f * night;
        }

        // within about 55 m of a grandstand (same spots as TrackBuilder.Grandstands) the engine noise echoes back
        bool NearGrandstand()
        {
            float[] stands = { 0f, 0.28f, 0.55f, 0.75f };
            float L = track.roadLength;
            foreach (float f in stands)
            {
                float d = Mathf.FloorToInt(TrackModel.N * f) / (float)TrackModel.N * L;
                if (Mathf.Abs(Mathf.Repeat(player.dist - d + L * 0.5f, L) - L * 0.5f) < 55f) return true;
            }
            return false;
        }

        void SetupSpray()
        {
            sprays.Clear();
            foreach (var r in racers) sprays.Add(Effects.MakeSpray(r.go.transform));
        }

        // the rooster tail of spray behind each car: more speed and a wetter road mean more of it
        void UpdateSpray()
        {
            for (int i = 0; i < sprays.Count && i < racers.Count; i++)
            {
                if (sprays[i] == null) continue;
                float rate = roadWetness > 0.3f ? Mathf.Clamp01((racers[i].speed - 15f) / 55f) * roadWetness * 260f : 0f;
                var em = sprays[i].emission;
                em.rateOverTime = rate;
            }
        }

        void SetupHeadlights()
        {
            headlights.Clear();
            foreach (var r in racers)
            {
                var g = new GameObject("Headlight");
                g.transform.SetParent(r.go.transform, false);
                g.transform.localPosition = new Vector3(0f, 0.6f, 2.2f);
                g.transform.localRotation = Quaternion.Euler(5f, 0f, 0f);
                var l = g.AddComponent<Light>();
                l.type = LightType.Spot; l.range = 75f; l.spotAngle = 60f; l.intensity = 0f;
                l.color = new Color(1f, 0.96f, 0.85f); l.shadows = LightShadows.None;
                l.cullingMask = ~(1 << CockpitRig.LAYER); l.enabled = false;
                headlights.Add(l);
            }
        }

        // the cockpit is drawn by a second camera (own layer, depth-only clear) so terrain on steep hills
        // can never poke through the dash - same reason the web version disables depth-testing on its panels
        void BuildOverlayCam()
        {
            var oc = new GameObject("CockpitCam");
            oc.transform.SetParent(cam.transform, false);
            overlayCam = oc.AddComponent<Camera>();
            overlayCam.clearFlags = CameraClearFlags.Depth;
            overlayCam.cullingMask = 1 << CockpitRig.LAYER;
            overlayCam.fieldOfView = 78f;
            overlayCam.nearClipPlane = 0.05f;
            overlayCam.farClipPlane = 60f;
            overlayCam.depth = cam.depth + 1f;
            cam.cullingMask = ~(1 << CockpitRig.LAYER);
            // a dim lamp that only lights the cockpit, so the dash stays readable at night
            var lg = new GameObject("CockpitLamp");
            lg.transform.SetParent(oc.transform, false);
            lg.transform.localPosition = new Vector3(0f, 0.3f, 0.2f);
            cockpitLamp = lg.AddComponent<Light>();
            cockpitLamp.type = LightType.Point; cockpitLamp.range = 4f; cockpitLamp.intensity = 0f;
            cockpitLamp.shadows = LightShadows.None; cockpitLamp.cullingMask = 1 << CockpitRig.LAYER;
        }

        void BuildRain()
        {
            var go = new GameObject("Rain");
            rain = go.AddComponent<ParticleSystem>();
            var main = rain.main;
            main.loop = true; main.startLifetime = 0.9f; main.startSpeed = 0f; main.startSize = 0.05f;
            main.maxParticles = 4000; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.78f, 0.86f, 1f, 0.55f);
            var shape = rain.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(60, 1, 60);
            var vel = rain.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f); vel.y = new ParticleSystem.MinMaxCurve(-38f); vel.z = new ParticleSystem.MinMaxCurve(0f);
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch; rend.lengthScale = 3f; rend.velocityScale = 0.01f;
            rend.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            var em = rain.emission; em.rateOverTime = 0f;
        }

        // ------------------------------------------------------------------ state changes
        void EnterMenu()
        {
            state = State.Menu;
            mpMenu = false; net.inRace = false; net.cars.Clear();
            if (net.Active) { tab = Tab.Race; netPanel = true; }
            tutorial = null;
            hud.SetVisible(false);
            if (rig != null) rig.root.SetActive(false);
            ClearRacers();
            safetyCarActive = false; vscActive = false; scRestart = false; redFlag = false; rolling = false; formationLap = false; introActive = false; caution = Caution.None; showTelemetry = false;
            isRaining = isFoggy = false; roadWetness = 0f; timeOfDay = 0f; fuelOn = false; sunElev = 52f; sunAz = -35f;
            if (builtTrackIndex != GameSave.trackIndex) BuildWorld(GameSave.trackIndex);
            ApplyWeatherLook();
            if (weatherPlan == null || planTrack != GameSave.trackIndex) { weatherPlan = WeatherPlan.Roll(Tracks.All[GameSave.trackIndex]); planTrack = GameSave.trackIndex; }
        }

        void ClearRacers()
        {
            foreach (var r in racers) if (r.go != null) Destroy(r.go);
            racers.Clear();
            headlights.Clear();
            sprays.Clear();
            if (audioFx != null) audioFx.ClearRivals();
            player = null;
            if (ghostGo != null) { Destroy(ghostGo); ghostGo = null; }
            if (scGo != null) { Destroy(scGo); scGo = null; }
            debris.Clear();
            if (pitCrews != null) { foreach (var pc in pitCrews) pc.Destroy(); pitCrews = null; }
            if (recovery != null) { recovery.Destroy(); recovery = null; }
            pitBoxes.Reset();
        }

        void FinishTutorial(bool completedAll)
        {
            if (completedAll && !GameSave.TutorialDone)
            {
                GameSave.TutorialDone = true;
                Profile.AddXp(40);
            }
            EnterMenu();
        }

        Run ActiveRun() { return mode == Mode.Career ? Run.Career : mode == Mode.Tournament ? Run.Tournament : mode == Mode.Custom ? Run.Custom : null; }

        void StartRace(Mode m)
        {
            ClearRacers();
            GameSave.Save();
            mode = m;
            var run = ActiveRun();
            int trackIndex = run != null ? TrackIndexOf(run.CurrentTrackId) : GameSave.trackIndex;
            CarSetups.Switch(Tracks.All[trackIndex].id);                 // every circuit has its own car with its own gearing, setup and livery
            BuildWorld(trackIndex);
            var def = track.def;
            raceLaps = m == Mode.Qualifying ? 1 : m == Mode.Tutorial ? 99 : m == Mode.Practice ? PracticeLaps() : m == Mode.Endurance ? ENDURANCE_LAPS[Mathf.Clamp(GameSave.enduranceLaps, 0, 2)]
                     : m == Mode.Custom && run != null ? run.customLaps : (run != null ? 3 : GameSave.laps);

            // the tyre strategy plan chooses the starting tyres
            Strategy.Load(def.id);
            if (IsRaceMode(m) && m != Mode.Qualifying && m != Mode.Practice && Strategy.ActiveFor(raceLaps)) GameSave.compound = (Compound)Strategy.plan.compound[0];

            // the weather was decided before the start so the garage could show a forecast
            if (m == Mode.Multiplayer)
            {
                Random.InitState(mpSeed);                                  // everybody rolls the same weather
                weatherPlan = WeatherPlan.Roll(def); planTrack = trackIndex;
                Random.InitState(System.Environment.TickCount);
            }
            else if (weatherPlan == null || planTrack != trackIndex) { weatherPlan = WeatherPlan.Roll(def); planTrack = trackIndex; }
            activePlan = weatherPlan; weatherPlan = null;
            if (m == Mode.Custom && run != null) activePlan = WeatherPlan.Roll(def, run.customWeather);       // the season's own weather odds
            bool dryOnly = m == Mode.Tutorial || m == Mode.Practice;
            isRaining = !dryOnly && activePlan.wetStart;
            isFoggy = !dryOnly && activePlan.fog;
            roadWetness = isRaining ? 0.35f : 0f; rainOpacity = isRaining ? 0.4f : 0f;
            trackTemp = dryOnly ? 30f : activePlan.StartTemp;
            timeOfDay = (m == Mode.Endurance || m == Mode.Tutorial || m == Mode.Practice) ? 0f : GameSave.raceTime == 2 ? 1f : GameSave.raceTime == 1 ? 0.55f : 0f;     // day, dusk or a floodlit night
            fuelOn = IsRaceMode(m);                                    // the tank is real in every race; only endurance races refuel
            sunElev = activePlan.sunElev; sunAz = activePlan.sunAz; sunNext = 0f;
            wind = WindField.Make(dryOnly ? 0f : activePlan.windSpeed, activePlan.windDir, activePlan.windGust, (m == Mode.Multiplayer ? mpSeed * 0.013f : Random.value * 40f) + 3.7f);
            Crowd.WindStrength = wind.mean;
            if (fuelOn) fuelLaps = m == Mode.Endurance ? FuelLapsFor(track) : Mathf.Max(1f, raceLaps * (raceLaps <= 1 ? 1f : raceLaps == 2 ? 0.93f : 0.86f));
            ApplyWeatherLook();

            int diffIdx = m == Mode.Custom && run != null ? run.customDiff : GameSave.difficulty;
            float diffMistake = diffIdx == 0 ? 1.8f : diffIdx == 2 ? 0.45f : 1f;
            aiDifficulty = Tuning.DifficultyMult[diffIdx] * (m == Mode.Tournament ? 1.08f : 1f);
            aiMistakeMult = diffMistake * (m == Mode.Tournament ? 0.6f : 1f);

            bool formation = IsRaceMode(m) && GameSave.startType == 2;                 // grid walk, then a formation lap
            bool roll = IsRaceMode(m) && (GameSave.startType == 1 || formation);      // rolling start
            float shift = formation ? -track.roadLength : roll ? -170f : 0f;
            if (m == Mode.Qualifying && (quali == null || quali.done)) quali = QualiSession.Begin(track.roadLength / 33f, aiDifficulty);
            stewards.Reset(); calls.Reset();

            Color body = Livery.Colors[GameSave.bodyColor % Livery.Colors.Length];
            Color accent = Livery.Colors[GameSave.accentColor % Livery.Colors.Length];

            if (m == Mode.Qualifying || m == Mode.Tutorial || m == Mode.Practice)
            {
                // a solo flying lap from the start line - the result decides your grid slot for this circuit
                var pv = CarVisual.Build(body, accent, GameSave.pattern, false, GameSave.compound, RacerProfile.number, RacerProfile.HelmetColor);
                player = new PlayerCar(this, pv, 0f, 0f);
                racers.Add(player);
            }
            else if (m == Mode.Multiplayer)
            {
                // the people in the room, in grid order; the other cars are driven by their own computers
                net.cars.Clear();
                for (int i = 0; i < mpRoster.Count; i++)
                {
                    var mp = mpRoster[i];
                    float gridOff = (i % 2 == 0) ? -2.6f : 2.6f;
                    if (mp.slot == net.mySlot)
                    {
                        var pv = CarVisual.Build(body, accent, GameSave.pattern, false, GameSave.compound, RacerProfile.number, RacerProfile.HelmetColor);
                        player = new PlayerCar(this, pv, GRID[i] + shift, gridOff);
                        racers.Add(player);
                    }
                    else
                    {
                        Color mb = Livery.Colors[((mp.body % Livery.Colors.Length) + Livery.Colors.Length) % Livery.Colors.Length];
                        Color ma = Livery.Colors[((mp.accent % Livery.Colors.Length) + Livery.Colors.Length) % Livery.Colors.Length];
                        var v = CarVisual.Build(mb, ma, Mathf.Max(0, mp.pattern), false, Compound.Medium, mp.number);
                        float d0 = GRID[i] + shift;
                        Vector2 gp = track.PointAt(d0), gt = track.TangentAt(d0), gn = TrackModel.Normal(gt);
                        v.transform.position = TrackModel.W(gp + gn * gridOff, track.ElevationAt(d0));
                        v.transform.rotation = Quaternion.Euler(0f, (Mathf.PI - Mathf.Atan2(gt.x, gt.y)) * Mathf.Rad2Deg, 0f);
                        var nr = new NetRacer(v, mp.slot, mp.name, Time.time) { dist = d0 };
                        racers.Add(nr); net.cars[mp.slot] = nr;
                    }
                }
                net.inRace = true;
            }
            else
            {
                int saved = GameSave.GridSlot(def.id);
                int playerSlot = Mathf.Clamp((saved >= 0 ? saved : 3) + GameSave.gridPenalty, 0, 5);      // replacing too many engine parts costs places
                var ratings = new List<float>();
                for (int i = 0; i < 5; i++) ratings.Add(4.5f + Random.value * 3.5f);
                ratings.Sort((a, b) => b.CompareTo(a));
                for (int ti = 0; ti < ratings.Count; ti++) ratings[ti] += (TeamCareer.Rating(TeamCareer.TeamOfAi(ti)) - 0.5f) * 3f;      // better teams have faster cars
                // every rival gets a different character, shuffled each race
                var pers = new List<Personality> { Personality.Balanced, Personality.Aggressive, Personality.Cautious, Personality.TireSaver, Personality.WetSpecialist };
                for (int i = pers.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); var tmp = pers[i]; pers[i] = pers[j]; pers[j] = tmp; }
                int ai = 0;
                for (int slot = 0; slot < 6; slot++)
                {
                    float offset = (slot % 2 == 0) ? -2.6f : 2.6f;
                    if (slot == playerSlot)
                    {
                        var pv = CarVisual.Build(body, accent, GameSave.pattern, false, GameSave.compound, RacerProfile.number, RacerProfile.HelmetColor);
                        player = new PlayerCar(this, pv, GRID[slot] + shift, offset);
                        racers.Add(player);
                    }
                    else
                    {
                        var comp = StartCompoundForAI();
                        // your teammate (AI 0) wears your livery; the other two teams wear their own colours
                        var v = ai == 0 ? CarVisual.Build(body, accent, GameSave.pattern, false, comp, AI_NUMBERS[0])
                                        : CarVisual.Build(TeamCareer.Teams[TeamCareer.TeamOfAi(ai)].color, new Color(0.9f, 0.9f, 0.95f), 0, false, comp, AI_NUMBERS[ai % AI_NUMBERS.Length]);
                        var aiCar = new AICar(this, v, ai, GRID[slot] + shift, ratings[ai], offset, pers[ai]);
                        aiCar.compound = comp;
                        if (ai == 0) aiCar.label = aiCar.DriverName + " (teammate)";
                        racers.Add(aiCar);
                        ai++;
                    }
                }
            }
            // a few rivals will suffer mechanical failures at some point in the race
            foreach (var r1 in racers)
            {
                var a1 = r1 as AICar;
                if (a1 != null && Random.value < (m == Mode.Endurance ? 0.16f : 0.09f)) a1.failureDist = a1.startDist + (0.2f + 0.7f * Random.value) * raceLaps * track.roadLength;
            }
            cautionCount = 0;
            recovery = new RecoveryTruck();
            Voice.Warm(Voice.RADIO);
            visor.Reset();
            commentary.Reset(); lastOrder = new List<Racer>(); wasPitting.Clear(); nextOrderCheck = 0f; finalLapSaid = false;
            if (roll) { foreach (var r0 in racers) r0.speed = formation ? 0f : 28f; player.tireTemp = 52f; }       // rolling: already moving, tyres still cold
            player.ApplyTransform();
            if (rig != null) Destroy(rig.root);
            rig = new CockpitRig(overlayCam.transform, body, accent);
            pitBoxes.Reset();
            pitCrews = new PitCrew[TrackModel.PIT_BOX_COUNT];
            for (int b = 0; b < pitCrews.Length; b++)
            {
                Color suit = body, helm = accent;                       // box 3 is yours; the others belong to the rival teams
                if (b != 2) { suit = AICOLORS[Mathf.Max(0, System.Array.IndexOf(AI_HOME_BOX, b)) % AICOLORS.Length]; helm = new Color(0.92f, 0.92f, 0.95f); }
                pitCrews[b] = new PitCrew(track, b, suit, helm);
            }
            if (m == Mode.Endurance || timeOfDay > 0.3f) SetupHeadlights();
            SetupSpray();
            SetupChallenge(m);
            rewind.Reset(GameSave.rewinds && m != Mode.Tutorial && m != Mode.Practice && m != Mode.Multiplayer ? 3 : 0);
            bcast.Reset();
            radioQ.Clear(); lastRadioAt = -99f;
            audioFx.SetRivals(racers);
            puddleLevel = isRaining ? 0.35f : 0f;
            if (puddles != null) puddles.Update(roadWetness, puddleLevel);
            ApplyWeatherLook();

            // ghost of your best lap on this circuit
            ghostSamples = Ghost.Load(def.id, out ghostDuration);
            ghostIdx = 0;
            if (ghostSamples != null && ghostDuration > 1f) ghostGo = CarVisual.Build(Color.white, Color.white, 0, true);
            else ghostSamples = null;

            safetyCarActive = false; vscActive = false; scRestart = false; redFlag = false; redPhase = 0; caution = Caution.None;
            plannedCaution = Caution.None; cautionAt = -1f; lastCautionEnd = -99f; redFlagLost = 0f; isRestart = false;
            weatherChangeAt = -1f; weatherChanged = false; weatherWarned = false;
            blueCar = null; blueTimer = 0f; prevRel.Clear(); finishTimes.Clear(); showTelemetry = false;
            rolling = roll; formationLap = formation; introActive = formation; showWall = false;

            hud.SetVisible(true);
            clock = 0f; countdownLeft = m == Mode.Tutorial ? 1.99f : 3.99f;
            engineer = new Engineer(); rec.Reset(); incidentUntil = new float[3]; lookYaw = 0f; raceStartClock = 9999f; finishFireworks = false; flagBanner = "";
            countdownText = "";
            state = roll && !formation ? State.Racing : State.Countdown;
            if (formation) countdownLeft = INTRO_LEN;
            if (roll) player.BeginRace(clock, -2f);
            tutorial = m == Mode.Tutorial ? new TutorialGuide() : null;
            Radio(m == Mode.Multiplayer ? "Online race - " + mpRoster.Count + " drivers on the grid. Lights out in a moment." : m == Mode.Practice ? PracticeBrief() : m == Mode.Endurance ? "Endurance race - watch the fuel and the tyres, your driver will tire and night is coming. Press F to change the fuel load."
                  : m == Mode.Tutorial ? "Practice session - follow the steps on the left" : m == Mode.Qualifying ? "Qualifying lap - give it everything"
                  : formation ? "Grid walk, then a formation lap - stay in order and weave to warm the tyres. Green flag when we reach the line again."
                  : roll ? "Rolling start - hold your slot and weave to warm the tyres. Green flag at the line."
                  : isRaining ? "Rain on the circuit - watch your grip" : isFoggy ? "Fog out there, take it easy" : "Lights out in a moment, good luck");
            if (IsRaceMode(m) && GameSave.gridPenalty > 0) Radio("Grid penalty: you start " + GameSave.gridPenalty + " places further back for the extra engine parts.", 3);
        }

        // ------------------------------------------------------------------ finishing
        // finish time of every car: those who crossed the line have a recorded time, the rest are estimated from the gap
        List<Racer> RankedByTime()
        {
            float target = raceLaps * track.roadLength;
            float now = clock - redFlagLost;
            finishTimes.Clear();
            foreach (var r in racers)
            {
                float t = r.dnf ? 1e5f + Mathf.Max(0f, target - r.dist)
                        : r.finishTime >= 0f ? r.finishTime : now + Mathf.Max(0f, target - r.dist) / Mathf.Max(25f, r.speed);
                if (r.isPlayer) t += player.penaltySeconds;
                finishTimes[r] = t;
            }
            var l = new List<Racer>(racers);
            l.Sort((a, b) => finishTimes[a].CompareTo(finishTimes[b]));
            return l;
        }

        void Finish()
        {
            state = State.Finished;
            hud.SetVisible(false);
            finishOrder.Clear(); finishNotes.Clear(); finishScroll = Vector2.zero;
            finishRun = null;
            safetyCarActive = false; vscActive = false; scRestart = false; redFlag = false; rolling = false; caution = Caution.None;
            calls.Reset();
            showTelemetry = false; telemetry.Reset(player);
            Voice.Warm(Voice.PLAYBYPLAY); Voice.Warm(Voice.WOMAN);          // the commentators load while you read the results

            if (mode == Mode.Qualifying) { FinishQualifying(); return; }
            if (mode == Mode.Practice) { FinishPractice(); return; }

            var ranked = RankedByTime();
            int rank = ranked.IndexOf(player) + 1;
            finishFireworks = rank == 1;
            int payout = PAYOUT[Mathf.Clamp(rank - 1, 0, PAYOUT.Length - 1)] * (mode == Mode.Endurance ? 2 : 1);
            GameSave.money += payout;
            float t0 = finishTimes[ranked[0]];
            commentary.Add(clock, NameOf(ranked[0]) + " takes the chequered flag and wins the race!", 2);
            for (int i = 0; i < ranked.Count; i++)
                finishOrder.Add((i + 1) + ".  " + ranked[i].label + "     " + (ranked[i].dnf ? "DNF" : i == 0 ? Hud.FormatTime(t0) : "+" + (finishTimes[ranked[i]] - t0).ToString("0.0") + "s"));
            float total = clock - redFlagLost + player.penaltySeconds;
            finishSummary = "P" + rank + "   -   race time " + Hud.FormatTime(total)
                            + (player.penaltySeconds > 0 ? "  (incl. +" + player.penaltySeconds.ToString("0") + "s penalty)" : "")
                            + "\nPrize money: $" + payout;

            // driver XP: career and tournament races are worth more
            int xp = 40 + (6 - rank) * 12;
            if (mode != Mode.Quick) xp = Mathf.RoundToInt(xp * 1.5f);
            if (mode == Mode.Endurance) xp *= 2;
            int levelsGained = Profile.AddXp(xp);
            finishNotes.Add("+" + xp + " XP" + (levelsGained > 0 ? "   LEVEL UP! Now level " + Profile.level : ""));
            foreach (var s in stewards.log) finishNotes.Add("Stewards: " + s);
            if (engineer != null && engineer.followedCalls + engineer.ignoredCalls > 0) finishNotes.Add("Race engineer trust: " + engineer.TrustText);
            if (mode == Mode.Endurance) Award("endurance");
            // the team: R&D points for every race, a salary in the career season, and the engine and gearbox wear down
            int rd = 2 + Mathf.Max(0, 6 - rank);
            TeamCareer.AddRd(rd);
            finishNotes.Add("R&D points +" + rd + "  (" + TeamCareer.Teams[TeamCareer.team].name + ")");
            if (mode == Mode.Career)
            {
                int pay = TeamCareer.OnCareerRace(rank);
                GameSave.money += pay;
                finishNotes.Add("Salary $" + pay);
            }
            float pushShare = player.activeTime > 1f ? player.pushTime / player.activeTime : 0f;
            float wearMul = 1f - 0.04f * GameSave.reliability;
            float ewAdd = (14f + 10f * pushShare) * wearMul, gwAdd = 10f * wearMul;
            GameSave.engineWear = Mathf.Min(100f, GameSave.engineWear + ewAdd);
            GameSave.gearboxWear = Mathf.Min(100f, GameSave.gearboxWear + gwAdd);
            GameSave.brakeWear = Mathf.Min(100f, GameSave.brakeWear + player.brakeWearRace);
            finishNotes.Add("Brakes: +" + Mathf.RoundToInt(player.brakeWearRace) + "% wear (now " + Mathf.RoundToInt(GameSave.brakeWear) + "%)" + (GameSave.brakeWear > 75f ? "  - time for new brakes" : ""));
            finishNotes.Add("Wear: engine +" + Mathf.RoundToInt(ewAdd) + "% (now " + Mathf.RoundToInt(GameSave.engineWear) + "%), gearbox +" + Mathf.RoundToInt(gwAdd) + "% (now " + Mathf.RoundToInt(GameSave.gearboxWear) + "%)");
            GameSave.gridPenalty = 0;
            finishNotes.Add(GiveTraining(6 * Mathf.Min(raceLaps, 15) + (mode == Mode.Endurance ? 40 : 0), player.damage < 5f && player.penaltySeconds <= 0f ? 20 : 8, rank <= 3 ? 14 : 6, true));
            RacerProfile.AddEnergy(2);
            RacerProfile.day += 7;
            string detrain = RacerProfile.ApplyDetraining();
            if (detrain != null) finishNotes.Add(detrain);
            if (weekend != null && weekend.stage >= 4 && mode == Mode.Quick)
            {
                int wb = 400 + 100 * (6 - rank);
                GameSave.money += wb;
                Profile.AddXp(40);
                finishNotes.Add("RACE WEEKEND COMPLETE!  Bonus $" + wb + " and +40 XP.");
                weekend = null;
            }

            if (Facilities.RdBonus > 0) { TeamCareer.AddRd(Facilities.RdBonus); finishNotes.Add("Factory: +" + Facilities.RdBonus + " R&D point" + (Facilities.RdBonus > 1 ? "s" : "")); }
            if (Staff.Aero > 0) { TeamCareer.AddRd(Staff.Aero); finishNotes.Add("Aerodynamicist: +" + Staff.Aero + " R&D point" + (Staff.Aero > 1 ? "s" : "")); }
            string sponsorLine = Sponsors.Evaluate(rank, startRank, player.penaltySeconds, player.damage, player.setPbThisRace);
            if (sponsorLine != null) finishNotes.Add(sponsorLine);

            // achievements
            if (rank == 1) Award("firstWin");
            if (rank <= 3) Award("podium");
            if (player.damage < 0.5f && player.penaltySeconds <= 0f) Award("cleanRace");
            if (player.damage >= 60f) Award("survivor");
            if (rank == 1 && isRaining) Award("wetMaster");

            // championship bookkeeping
            var run = ActiveRun();
            if (run != null)
            {
                bool last = run.AddRound(ranked);
                finishRun = run;
                if (last)
                {
                    bool won = run.PlayerWon();
                    if (mode == Mode.Career)
                    {
                        int sr = 1;
                        foreach (var kv in run.Standings()) { if (kv.Key == RacerProfile.DisplayName) break; sr++; }
                        finishNotes.AddRange(TeamCareer.EndSeason(sr, run.playerPts, run.aiPts[0]));
                    }
                    if (won)
                    {
                        int bonus = mode == Mode.Tournament ? Profile.TOURNAMENT_BONUS : mode == Mode.Custom ? 700 * run.calendar.Count : 3000;
                        GameSave.money += bonus;
                        finishNotes.Add((mode == Mode.Tournament ? "TOURNAMENT" : mode == Mode.Custom ? run.customName.ToUpper() : "SEASON") + " CHAMPION!  Bonus $" + bonus);
                        Award(mode == Mode.Tournament ? "tournamentChampion" : mode == Mode.Custom ? "customChampion" : "seasonChampion");
                    }
                    else finishNotes.Add((mode == Mode.Tournament ? "Tournament" : "Season") + " over - you did not take the title.");
                    run.End();
                }
            }
            GameSave.Save();
        }

        // ---------------------------------------------------------------- training: practice sessions and race experience
        static float FuelLapsFor(TrackModel t) { return Mathf.Max(2.5f, 1.6f * 75f / (t.roadLength * 0.015f)); }

        // ---- practice challenges ----
        void SetupChallenge(Mode m)
        {
            chDone = false; chArmed = false; chTimedOut = false; chStopGap = 0f;
            if (m != Mode.Practice || practiceDrill != 5) return;
            // the line goes down the first straight: 60% of the way to the first corner (but not too near or too far)
            float dc = 0f;
            for (float d = 20f; d < track.roadLength * 0.5f; d += 8f) { if (track.CurvatureAt(d) > 0.07f) break; dc = d; }
            chLine = Mathf.Clamp(dc * 0.62f, 90f, 420f);
            chNeedSpeed = Mathf.Clamp(0.7f * Mathf.Sqrt(2f * 8f * chLine), 28f, 62f);
            var white = Mats.Emissive(new Color(0.95f, 0.95f, 0.95f), 0.6f);
            var red = Mats.Make(new Color(0.9f, 0.2f, 0.1f), 0.3f);
            var amber = Mats.Emissive(new Color(1f, 0.75f, 0.1f), 0.5f);
            float[] marks = { chLine, chLine - 40f, chLine - 80f };
            for (int i = 0; i < marks.Length; i++)
            {
                Vector2 t = track.TangentAt(marks[i]);
                Vector3 c = TrackModel.W(track.PointAt(marks[i]), track.ElevationAt(marks[i]) + 0.03f);
                var line = Prim.Box(trackRoot.transform, c, new Vector3(TrackModel.ROAD_WIDTH, 0.03f, i == 0 ? 0.7f : 0.35f), i == 0 ? white : amber, i == 0 ? "StopLine" : "BrakeMark");
                line.transform.rotation = Quaternion.LookRotation(TrackModel.W(t, 0f));
            }
            foreach (int side in new[] { -1, 1 })
            {
                Vector2 n = TrackModel.Normal(track.TangentAt(chLine));
                Vector3 c = TrackModel.W(track.PointAt(chLine) + n * side * 11.5f, track.ElevationAt(chLine));
                Prim.Cyl(trackRoot.transform, c + Vector3.up * 0.5f, new Vector3(0.5f, 0.5f, 0.5f), Quaternion.identity, red, "Cone");
            }
        }

        void UpdateChallenge()
        {
            if (mode != Mode.Practice || practiceDrill != 5 || chDone || player == null) return;
            float v = player.speed, gap = player.dist + 2.7f - chLine;        // the nose of the car against the line: negative = short of it
            if (v >= chNeedSpeed) chArmed = true;
            if (chArmed && v < 0.6f) { chStopGap = gap; chDone = true; Finish(); }
            else if (gap > 30f) { chStopGap = gap; chDone = true; Finish(); }                       // you drove straight through it
            else if (clock - raceStartClock > 90f) { chTimedOut = true; chStopGap = gap; chDone = true; Finish(); }
        }

        int PracticeLaps() { return practiceDrill == 2 ? 5 : practiceDrill == 3 ? 2 : practiceDrill == 4 ? 2 : practiceDrill == 5 ? 99 : 3; }

        string PracticeBrief()
        {
            return practiceDrill == 0 ? "Smooth laps: no wall hits, no track-limit offences. Trains Focus."
                 : practiceDrill == 1 ? "Fast laps: set a lap close to the target time. Trains Reflexes."
                 : practiceDrill == 3 ? "Setup run: try your setup, then read the engineer's feedback."
                 : practiceDrill == 4 ? "Perfect lap: two laps. Your quickest clean lap (no wall hits or track limits) wins a medal."
                 : practiceDrill == 5 ? "Braking test: reach " + Mathf.RoundToInt(chNeedSpeed * 3.6f) + " km/h, then stop with the nose on the white line."
                 : practiceDrill == 6 ? "Tyre saver: three laps, each inside the time limit, using as little tyre as you can."
                 : "Long run: five laps of stamina. Trains Fitness.";
        }

        /// <summary>Adds training XP to the racer's stats and describes it for the results screen.</summary>
        string GiveTraining(int fit, int foc, int refl, bool fromRace)
        {
            int[] amounts = { fit, foc, refl };
            var parts = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                int gained = RacerProfile.AddXp(i, amounts[i]);
                string s = RacerProfile.StatNames[i] + " +" + amounts[i];
                if (gained > 0) s += " (LEVEL " + RacerProfile.level[i] + "!)";
                parts.Add(s);
                if (RacerProfile.level[i] >= 5) Award("trainer");
            }
            return (fromRace ? "Racer training: " : "Training: ") + string.Join(",  ", parts.ToArray());
        }

        void FinishChallenge()
        {
            string id = track.def.id; int c = practiceDrill - Challenges.FIRST_DRILL;
            float rl = Challenges.RefLap(id);
            int medal = 0; string line;
            if (c == 0)
            {
                float lap = player.bestCleanLap;
                medal = Challenges.LapMedal(lap, rl);
                line = lap > 0f ? "Best clean lap " + Hud.FormatTime(lap) : "No clean lap - keep it off the walls and inside the white lines";
                finishSummary = "PERFECT LAP:  " + Challenges.MedalNames[medal].ToUpper() + "\n" + line;
                finishNotes.Add("Times: gold " + Hud.FormatTime(Challenges.GoldLap(rl)) + ", silver " + Hud.FormatTime(Challenges.SilverLap(rl)) + ", bronze " + Hud.FormatTime(Challenges.BronzeLap(rl)));
            }
            else if (c == 1)
            {
                medal = chTimedOut || !chArmed ? 0 : Challenges.BrakeMedal(chStopGap);
                string where = !chArmed ? "You never reached " + Mathf.RoundToInt(chNeedSpeed * 3.6f) + " km/h" : chTimedOut ? "Out of time" : chStopGap > 0f ? "Stopped " + chStopGap.ToString("0.0") + " m past the line" : "Stopped " + (-chStopGap).ToString("0.0") + " m short of the line";
                finishSummary = "BRAKING TEST:  " + Challenges.MedalNames[medal].ToUpper() + "\n" + where;
                finishNotes.Add("Gold needs the nose within 2.5 m of the line (past it counts 2.5 times as much), silver 6 m, bronze 12 m.");
            }
            else
            {
                float limit = Challenges.BronzeLap(rl);
                bool timesOk = player.lapTimes.Count >= 3;
                foreach (float lt in player.lapTimes) if (lt > limit) timesOk = false;
                float refLeft = Challenges.SaverRef(id);
                medal = Challenges.SaverMedal(player.tireWear, refLeft, timesOk);
                finishSummary = "TYRE SAVER:  " + Challenges.MedalNames[medal].ToUpper() + "\nTyres left " + Mathf.RoundToInt(player.tireWear) + "%  (a normal pace leaves about " + Mathf.RoundToInt(refLeft) + "%)" + (timesOk ? "" : "  - a lap was slower than " + Hud.FormatTime(limit));
            }
            bool isNew = Challenges.Record(id, c, medal);
            if (medal > 0)
            {
                GameSave.money += Challenges.Cash[medal]; Profile.AddXp(Challenges.Xp[medal]); TeamCareer.AddRd(medal);
                finishNotes.Add(Challenges.MedalNames[medal] + " medal!  +$" + Challenges.Cash[medal] + "  +" + Challenges.Xp[medal] + " XP  +" + medal + " R&D" + (isNew ? "   (a new best here)" : ""));
            }
            else finishNotes.Add("No medal this time. Try again!");
        }

        void FinishPractice()
        {
            float reference = track.roadLength / 33f;
            int laps = player.lapTimes.Count, clean = player.cleanLaps;
            float best = player.bestLapThisRace;
            float closeness = best > 0f ? Mathf.Clamp01((reference * 1.2f - best) / (reference * 0.2f)) : 0f;
            int fit = 6 * laps + (practiceDrill == 2 ? 14 * laps : 0);
            int foc = 8 * clean + (practiceDrill == 0 ? 14 * clean : 0);
            int refl = 6 * laps + Mathf.RoundToInt(30f * closeness * (practiceDrill == 1 ? 1.8f : 1f));
            finishSummary = "Practice complete - " + laps + " laps, " + clean + " clean, best " + (best > 0f ? Hud.FormatTime(best) : "-") + "   (target " + Hud.FormatTime(reference) + ")";
            if (practiceDrill >= Challenges.FIRST_DRILL) FinishChallenge();
            finishNotes.Add(GiveTraining(fit, foc, refl, false));
            int cash = 40 * clean;
            GameSave.money += cash;
            int lv = Profile.AddXp(10 + 5 * clean);
            finishNotes.Add("+$" + cash + "   +" + (10 + 5 * clean) + " XP" + (lv > 0 ? "   LEVEL UP! Now level " + Profile.level : ""));
            GameSave.engineWear = Mathf.Min(100f, GameSave.engineWear + 3f); GameSave.gearboxWear = Mathf.Min(100f, GameSave.gearboxWear + 2f);
            TeamCareer.AddRd(clean);
            RacerProfile.AddEnergy(1);
            RacerProfile.lastTrainDay = RacerProfile.day;
            RacerProfile.day += 1;
            RacerProfile.Save();
            // the engineer reads the setup back to you
            string trackId = track.def.id;
            var fbLines = Setup.Feedback(trackId);
            Setup.Remember(trackId, fbLines);
            finishNotes.Add("Setup rating " + Mathf.RoundToInt(Setup.Fit(trackId) * 100f) + "%");
            for (int i = 0; i < fbLines.Count && i < 3; i++) finishNotes.Add("Engineer: " + fbLines[i]);
            if (weekend != null && weekend.stage <= 2)
            {
                weekend.stage++;
                finishNotes.Add("Weekend: next up " + weekend.StageName + (weekend.stage == 3 ? " - the setup locks once it starts." : "."));
            }
            GameSave.Save();
        }

        void FinishQualifying()
        {
            float lap = player.lapTimes.Count > 0 ? player.lapTimes[0] : clock;
            var q = quali;
            q.PlayerLap(lap);
            string sess = "Q" + q.lastSession;
            int pos = 1;
            for (int i = 0; i < q.lastResult.Count; i++)
            {
                var e = q.lastResult[i];
                if (e.isPlayer) pos = i + 1;
                finishOrder.Add((i + 1) + ".  " + e.name + "   " + Hud.FormatTime(e.time) + (e.outIn == q.lastSession ? "     KNOCKED OUT" : "     through"));
            }
            finishSummary = sess + " complete - you were P" + pos + (q.playerAdvanced && !q.done ? " and go through to Q" + q.session + "." : q.playerAdvanced ? " in the pole shootout." : " and are knocked out.")
                            + "   lap " + Hud.FormatTime(lap);
            int xpQ = q.done ? 30 : 12;
            GameSave.engineWear = Mathf.Min(100f, GameSave.engineWear + 3f);
            int lv = Profile.AddXp(xpQ);
            finishNotes.Add("+" + xpQ + " XP" + (lv > 0 ? "   LEVEL UP! Now level " + Profile.level : ""));
            if (q.done)
            {
                if (weekend != null) weekend.stage = 4;
                GameSave.SaveGridSlot(track.def.id, q.playerSlot);
                finishNotes.Add("FINAL GRID: you start the race from P" + (q.playerSlot + 1) + (q.playerSlot == 0 ? " - POLE POSITION!" : "."));
                for (int i = 0; i < q.classification.Count; i++) finishNotes.Add("   P" + (i + 1) + "  " + q.classification[i].name);
                if (q.playerSlot == 0) Award("pole");
            }
            GameSave.Save();
        }

        void Award(string id)
        {
            if (Profile.Unlock(id)) finishNotes.Add("Achievement unlocked: " + Profile.AchName(id));
        }

        // ------------------------------------------------------------------ ranking
        List<Racer> RankedRacers()
        {
            var l = new List<Racer>(racers);
            l.Sort((a, b) => b.dist.CompareTo(a.dist));
            return l;
        }

        public int PlayerRank()
        {
            if (player == null) return 1;
            int r = 1;
            foreach (var c in racers) if (c != player && !c.dnf && c.dist > player.dist) r++;
            return r;
        }

        // ------------------------------------------------------------------ per-frame
        void OnDestroy() { net.Close(); Voice.Stop(); CarSetups.StoreCurrent(); PlayerPrefs.Save(); }
        void OnApplicationQuit() { net.Close(); Voice.Stop(); CarSetups.StoreCurrent(); PlayerPrefs.Save(); }

        // ------------------------------------------------------------------ online play
        void NetTick()
        {
            if (!net.Active) return;
            net.Poll(Time.time);
            if (net.startPending)
            {
                net.startPending = false;
                if (!(mode == Mode.Multiplayer && (state == State.Racing || state == State.Countdown))) StartMultiplayer();
            }
            if (mode == Mode.Multiplayer && net.inRace && player != null && (state == State.Racing || state == State.Countdown || state == State.Finished))
            {
                foreach (var c in net.cars.Values) c.Tick(Time.deltaTime, Time.time);
                net.SendState(player, clock);
                if (state != State.Finished)
                    net.CheckTimeouts(Time.time, c => { c.dnf = true; c.gone = true; if (c.go != null) c.go.SetActive(false); Radio(c.label + " has left the race.", 1); });
            }
        }

        void StartMultiplayer()
        {
            var list = net.startRoster;
            if (list == null || list.Count == 0 || list.FindIndex(p => p.slot == net.mySlot) < 0) return;
            int oLaps = GameSave.laps, oTime = GameSave.raceTime, oStart = GameSave.startType;
            GameSave.trackIndex = Mathf.Clamp(net.trackIndex, 0, Tracks.All.Length - 1);
            GameSave.laps = Mathf.Clamp(net.laps, 1, 9); GameSave.raceTime = Mathf.Clamp(net.raceTime, 0, 2); GameSave.startType = Mathf.Clamp(net.startType, 0, 1);
            mpRoster = list; mpSeed = net.startSeed; weekend = null; mpMenu = false;
            StartRace(Mode.Multiplayer);
            GameSave.laps = oLaps; GameSave.raceTime = oTime; GameSave.startType = oStart;       // the host's choices are for this race only
            GameSave.Save();
        }

        void Update()
        {
            Voice.Update();
            NetTick();
            RadioTick();
            if (state == State.Menu)
            {
                float t = Time.time * 0.08f;
                Vector2 c = Vector2.zero;
                foreach (var p in track.spaced) c += p;
                c /= track.spaced.Length;
                Vector3 centre = TrackModel.W(c, 0f);
                cam.transform.position = centre + new Vector3(Mathf.Cos(t) * 380f, 170f, Mathf.Sin(t) * 380f);
                cam.transform.LookAt(centre);
                gym.Update(Time.deltaTime);
                audioFx.Tick(false, 0f, false);
                return;
            }

            if (Inp.Pressed(KeyCode.P) || Inp.Pressed(KeyCode.Escape))
            {
                if (mode == Mode.Multiplayer) { if (state == State.Racing || state == State.Countdown) mpMenu = !mpMenu; }       // online the race cannot be paused
                else if (state == State.Paused) state = stateBeforePause;
                else if (state == State.Racing || state == State.Countdown) { stateBeforePause = state; state = State.Paused; }
            }
            if (state == State.Replay) { UpdateReplay(); audioFx.Tick(false, 0f, false); return; }
            if (state == State.Finished)
            {
                Crowd.Tick(Time.deltaTime, true, finishFireworks);
                if (marshals != null) marshals.Update(this, Time.time);
                debris.Tick(Time.deltaTime);
            }
            if (state == State.Paused || state == State.Finished) { audioFx.Tick(false, 0f, false); return; }

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            clock += dt;

            if (state == State.Countdown)
            {
                countdownLeft -= dt;
                if (introActive)
                {
                    countdownText = "";
                    if (countdownLeft <= 0f)
                    {
                        introActive = false;
                        state = State.Racing;
                        player.BeginRace(clock, -2f); startRank = PlayerRank();
                        hud.SetVisible(true);
                        Commentate("The cars leave the grid for the formation lap.", 1);
                        Radio("Formation lap - weave to warm the tyres and keep your position.", 3);
                    }
                }
                else
                {
                    int n = Mathf.CeilToInt(countdownLeft);
                    countdownText = n > 0 ? n.ToString() : "GO";
                    if (countdownLeft <= 0f)
                    {
                        state = State.Racing;
                        player.BeginRace(clock, -2f); startRank = PlayerRank();
                        raceStartClock = clock;
                        Commentate(isRestart ? "And we are racing again!" : "And it is lights out and away we go!", 2);
                        if (isRestart) isRestart = false; else ScheduleRaceEvents();
                        Radio("Go go go!", 3);
                    }
                }
            }
            else if (countdownText != "") countdownText = "";

            if (state == State.Racing)
            {
                ReadInput();
                rewind.Record(this, player, clock);
                player.Tick(dt, clock);
                visor.Update(this, player, dt, firstPerson);
                UpdatePitCrews();
                foreach (var r in racers) { var a = r as AICar; if (a != null) a.Tick(dt, clock); }
                player.ResolveCollisions(clock);
                if (tutorial != null)
                {
                    tutorial.Update(player, dt);
                    if (tutorial.finished) { FinishTutorial(tutorial.completed); return; }
                }
                UpdateWeather(dt);
                UpdateSun();
                UpdateWindVisuals();
                UpdateRaceControl(dt);
                UpdateGhost();
                rec.Record(clock, racers);
                if (tutorial == null) engineer.Update(this, player, clock);
                if (marshals != null) marshals.Update(this, Time.time);
                if (racingLine != null) racingLine.Update(clock - raceStartClock, roadWetness);
                Crowd.Tick(dt, true, false);
                UpdateChallenge();
                if (floods != null) floods.Tick(player.go.transform.position);
                debris.Tick(dt);
                float maxSpd = 45f + GameSave.power * 6.5f;
                audioFx.Tick(true, player.speed / maxSpd, player.overCorner && !player.offTrack);
                audioFx.Extras(player, roadWetness, NearGrandstand(), firstPerson);
                if (player.lap >= raceLaps) Finish();
            }
            else audioFx.Tick(false, 0f, false);
            hud.Refresh(this);
        }

        // every box has its own crew: whoever is stopped in a box gets worked on by that box's mechanics
        void UpdatePitCrews()
        {
            if (pitCrews == null) return;
            for (int i = 0; i < pitCrews.Length; i++)
            {
                Racer occupant = null;
                foreach (var r in racers) if (r.pitStopActive && r.pitBox == i) { occupant = r; break; }
                pitCrews[i].Update(occupant, clock);
            }
        }

        void ReadInput()
        {
            player.inThrottle = Inp.Held(KeyCode.W) || Inp.Held(KeyCode.UpArrow);
            player.inBrake = Inp.Held(KeyCode.S) || Inp.Held(KeyCode.DownArrow);
            player.inLeft = Inp.Held(KeyCode.A) || Inp.Held(KeyCode.LeftArrow);
            player.inRight = Inp.Held(KeyCode.D) || Inp.Held(KeyCode.RightArrow);
            player.inDrs = Inp.Held(KeyCode.Q);
            player.inDeploy = Inp.Held(KeyCode.LeftShift) || Inp.Held(KeyCode.RightShift);
            if (Inp.Pressed(KeyCode.B)) player.TogglePitRequest();
            if (Inp.Pressed(KeyCode.T)) showWall = !showWall;
            // look left / right / behind (hold Z, X, C) - the cockpit swings away so you can check the mirrors' blind spots
            float lookTarget = Inp.Held(KeyCode.Z) ? 85f : Inp.Held(KeyCode.X) ? -85f : Inp.Held(KeyCode.C) ? 180f : 0f;
            lookYaw += (lookTarget - lookYaw) * Mathf.Min(1f, Time.deltaTime * 9f);
            if (Inp.Pressed(KeyCode.E)) player.CycleEngineMode();
            if (Refuels && Inp.Pressed(KeyCode.F)) player.CycleFuelTarget();
            if (Inp.Pressed(KeyCode.V)) { camMode = (CamMode)(((int)camMode + 1) % 5); Flash("CAMERA: " + CamNames[(int)camMode]); }
            if (Inp.Pressed(KeyCode.M)) player.dyn.CycleErsMap();
            if (Inp.Pressed(KeyCode.L)) showBoard = !showBoard;
            if (Inp.Pressed(KeyCode.Backspace)) DoRewind();
            if (Inp.Pressed(KeyCode.H)) visor.TearOff(this);
            if (Inp.Pressed(KeyCode.G)) { GameSave.manualGears = !GameSave.manualGears; Flash(GameSave.manualGears ? "MANUAL GEARS  ( . up   , down )" : "AUTOMATIC GEARS"); GameSave.Save(); }
            if (GameSave.manualGears) { if (Inp.Pressed(KeyCode.Period)) player.dyn.RequestShift(1); if (Inp.Pressed(KeyCode.Comma)) player.dyn.RequestShift(-1); }
            if (Inp.Pressed(KeyCode.R)) player.RecoverToCentre();
            if (redFlag) { player.inThrottle = false; player.inBrake = true; player.inLeft = false; player.inRight = false; player.inDrs = false; player.inDeploy = false; }
            if (player.pitStopActive || (redFlag && redPhase == 2))
            {
                if (Inp.Pressed(KeyCode.Alpha1)) player.ChooseCompound(Compound.Soft);
                if (Inp.Pressed(KeyCode.Alpha2)) player.ChooseCompound(Compound.Medium);
                if (Inp.Pressed(KeyCode.Alpha3)) player.ChooseCompound(Compound.Hard);
                if (Inp.Pressed(KeyCode.Alpha4)) player.ChooseCompound(Compound.Inter);
                if (Inp.Pressed(KeyCode.Alpha5)) player.ChooseCompound(Compound.Wet);
            }
        }

        void UpdateWeather(float dt)
        {
            if (!weatherChanged && weatherChangeAt >= 0f && activePlan != null)
            {
                float left = weatherChangeAt - clock;
                if (!weatherWarned && left < 32f && left > 3f)
                {
                    weatherWarned = true;
                    if (activePlan.changeToRain && player.compound < Compound.Inter)
                        calls.Ask(this, "wx", "Rain is coming in about 30 seconds. Box now for intermediates?", "Box for Inters", "Stay out on slicks",
                                  () => player.RequestPit((int)Compound.Inter), () => Radio("Staying out. We will keep watching the radar."), false, 14f);
                    else if (!activePlan.changeToRain && player.compound >= Compound.Inter)
                        calls.Ask(this, "wx", "The track is about to dry out. Box for slicks?", "Box for Mediums", "Stay out on wets",
                                  () => player.RequestPit((int)Compound.Medium), () => Radio("Staying out. We will see how quickly it dries."), false, 14f);
                    else Radio(activePlan.changeToRain ? "Rain is on the way in about 30 seconds." : "The track should start drying soon.");
                }
                if (left <= 0f)
                {
                    weatherChanged = true;
                    isRaining = activePlan.changeToRain;
                    ApplyWeatherLook();
                    Commentate(isRaining ? "And the rain is starting to fall!" : "The rain is easing and the track will begin to dry.", 2);
                    Radio(isRaining ? "It is starting to rain!" : "The rain is easing - the track will dry.");
                    Flash(isRaining ? "RAIN" : "RAIN STOPPING");
                }
            }
            float target = isRaining ? 1f : 0f;
            puddleLevel = Mathf.Clamp01(puddleLevel + dt * (isRaining ? 0.010f * Mathf.Max(0.3f, rainOpacity) : -0.004f));      // water builds up in a long wet spell and drains when it stops
            if (puddles != null) { puddles.Update(roadWetness, puddleLevel); puddles.Drift(dt, isRaining && puddleLevel > 0.3f); }
            if (activePlan != null && mode != Mode.Tutorial && mode != Mode.Practice)
                trackTemp = Mathf.MoveTowards(trackTemp, isRaining ? activePlan.wetTemp : (activePlan.fog ? activePlan.dryTemp - 4f : activePlan.dryTemp), dt * 0.15f);
            roadWetness = Mathf.MoveTowards(roadWetness, target, dt * (isRaining ? 0.12f : 0.05f));
            rainOpacity = Mathf.MoveTowards(rainOpacity, target, dt * 0.25f);
        }

        // the sun creeps across the sky during a race (faster than the clock), so shadows lengthen and turn
        void UpdateSun()
        {
            if (activePlan == null || mode == Mode.Tutorial || mode == Mode.Endurance || Time.time < sunNext) return;
            sunNext = Time.time + 3f;
            float hour = activePlan.startHour + Mathf.Max(0f, clock - raceStartClock) * SUN_TIMELAPSE / 3600f;
            float e, a;
            SunPath.Compute(activePlan.latitude, activePlan.dayOfYear, hour, out e, out a);
            sunElev = Mathf.Max(3f, e); sunAz = a;
            ApplyWeatherLook();
        }

        // rain is blown sideways by the wind
        void UpdateWindVisuals()
        {
            if (rain == null || wind == null) return;
            Vector2 w = wind.VectorAt(clock);
            var vel = rain.velocityOverLifetime;
            vel.x = new ParticleSystem.MinMaxCurve(w.x * 0.9f); vel.z = new ParticleSystem.MinMaxCurve(-w.y * 0.9f);
        }

        /// <summary>Called when the race really starts (lights out or the rolling-start line): schedules the weather change and any caution.</summary>
        void ScheduleRaceEvents()
        {
            bool raceMode = IsRace;
            float dur = raceLaps * track.roadLength / 38f;
            weatherChanged = false; weatherWarned = false;
            weatherChangeAt = (raceMode && activePlan != null && activePlan.Changes) ? clock + activePlan.changeFrac * dur : -1f;
            plannedCaution = Caution.None; cautionAt = -1f;
            if (raceMode && mode != Mode.Multiplayer)
            {
                float r = Random.value;
                plannedCaution = r < 0.20f ? Caution.Safety : r < 0.40f ? Caution.Virtual : r < 0.46f ? Caution.Red : Caution.None;
                if (plannedCaution != Caution.None) cautionAt = clock + (0.2f + Random.value * 0.55f) * dur;
            }
        }

        // ---- cautions: safety car, virtual safety car, red flag ----
        public bool TriggerCaution(Caution kind, string why)
        {
            if (state != State.Racing || caution != Caution.None || player == null) return false;
            if (!IsRace || mode == Mode.Multiplayer) return false;                  // no random safety cars online: every computer would see a different race
            if (rolling || clock - raceStartClock < 12f || clock - lastCautionEnd < 40f) return false;
            if (raceLaps > 1 && player.lap >= raceLaps - 1 && kind != Caution.Red) return false;
            caution = kind; cautionCount++;
            Commentate(kind == Caution.Safety ? "The safety car has been deployed!" : kind == Caution.Virtual ? "We have a virtual safety car." : "A red flag! The race has been stopped.", 2);
            if (kind == Caution.Safety)
            {
                safetyCarActive = true; cautionEndAt = clock + 14f; scRestart = false;
                scDist = LeaderDist() + 45f;                        // the safety car starts ahead of the leader and the field bunches up behind it
                Radio(why ?? "Safety car deployed - hold station, no overtaking", 3);
                if (scGo == null) scGo = CarVisual.Build(new Color(1f, 0.83f, 0f), new Color(0.08f, 0.09f, 0.11f));
                scGo.SetActive(true);
            }
            else if (kind == Caution.Virtual)
            {
                vscActive = true; cautionEndAt = clock + 16f;
                Radio(why ?? "Virtual safety car - stay under the delta, no overtaking", 3);
            }
            else
            {
                redFlag = true; redPhase = 1; redPhaseStart = clock; redStartClock = clock;
                calls.active = null;
                Radio(why ?? "RED FLAG - the race is stopped. Slow down and stop.", 3);
                Flash("RED FLAG");
            }
            return true;
        }

        // ---- the safety car: the field bunches up behind it, then it comes in and the LEADER sets the pace until the control line ----
        float LeaderDist() { float b = 0f; foreach (var r in racers) if (!r.gone && !r.dnf && r.dist > b) b = r.dist; return b; }

        Racer LeaderRacer() { Racer l = null; foreach (var r in racers) if (!r.gone && !r.dnf && (l == null || r.dist > l.dist)) l = r; return l; }

        /// <summary>The top speed a car may do under the safety car: it catches up to close the gap to the car ahead (about 12 m), the leader to the safety car.</summary>
        public float SafetyCarCapFor(Racer r)
        {
            float cap = Tuning.SAFETY_CAR_SPEED_CAP;
            if (track == null) return cap;
            float L = track.roadLength;
            Racer ahead = null; float bestGap = 1e9f;
            foreach (var c in racers)
            {
                if (c == r || c.gone || c.dnf) continue;
                float g = TrackModel.Mod(c.dist - r.dist, L);
                if (g > 0.5f && g < bestGap) { bestGap = g; ahead = c; }
            }
            bool leader = r.dist >= LeaderDist() - 0.01f;
            if (scRestart)
            {
                if (leader) return r.isPlayer ? cap * 1.25f : cap * 1.15f;                        // the leader chooses the pace, within limits
                return Mathf.Max(cap, (ahead != null ? ahead.speed : cap) * 1.1f + 2f);            // the others follow him
            }
            float gap = leader ? Mathf.Max(0f, scDist - r.dist) : bestGap - 5.2f;
            float target = leader ? 28f : 12f;
            return cap * Mathf.Clamp(1f + (gap - target) / 40f, 0.85f, 1.5f);
        }

        void BeginSafetyRestart()
        {
            scRestart = true; slowRestart = 0f;
            restartLine = Mathf.Ceil((LeaderDist() + 260f) / track.roadLength) * track.roadLength;
            if (scGo != null) scGo.SetActive(false);                                               // the safety car pulls into the pits
            Radio("Safety car in this lap. Stay in order: the leader sets the pace and it is green at the line.", 3);
            Commentate("The safety car is coming in. The leader will control the restart.", 2);
        }

        void UpdateSafetyRestart(float dt)
        {
            var lead = LeaderRacer();
            if (lead == null) { EndCaution("Racing resumes."); return; }
            if (lead.dist >= restartLine) { EndCaution("Green flag, green flag! Racing resumes."); Flash("GREEN FLAG"); return; }
            // brake-testing the field is allowed, but driving erratically is not
            if (lead == player)
            {
                if (player.speed < Tuning.SAFETY_CAR_SPEED_CAP * 0.5f) slowRestart += dt; else slowRestart = Mathf.Max(0f, slowRestart - dt);
                if (slowRestart > 3f) { slowRestart = 0f; stewards.Offend(this, player, "restart", "driving erratically at the restart", 5f); }
            }
        }

        void EndCaution(string msg)
        {
            safetyCarActive = false; vscActive = false; scRestart = false; caution = Caution.None; lastCautionEnd = clock;
            if (scGo != null) scGo.SetActive(false);
            Commentate("We are back to green flag racing.", 2);
            Radio(msg);
        }

        /// <summary>A big hit by the player: debris out on the track brings out a caution.</summary>
        public void HeavyImpact(float speed, bool contact)
        {
            if (player == null) return;
            if (contact) { if (speed > 22f) TriggerCaution(Caution.Safety, "Heavy contact - safety car deployed, hold station."); }
            else if (speed > 62f && player.damage > 55f && Random.value < 0.5f) TriggerCaution(Caution.Red, "Big crash! Red flag - the race is stopped.");
            else if (speed > 45f) TriggerCaution(Caution.Virtual, "Debris on the track - virtual safety car.");
        }

        void UpdateRedFlag()
        {
            if (!redFlag) return;
            if (redPhase == 1)
            {
                bool stopped = true;
                foreach (var r in racers) if (r.speed > 1.5f) stopped = false;
                if (stopped || clock - redPhaseStart > 9f)
                {
                    redPhase = 2; redPhaseStart = clock;
                    foreach (var r in racers) r.speed = 0f;
                    Radio("Red flag - the race is suspended. Free tyre change and repairs while we wait.", 3);
                }
            }
            else if (redPhase == 2)
            {
                if (Inp.Pressed(KeyCode.Return) || clock - redPhaseStart > 60f) RestartAfterRedFlag();
            }
        }

        void RestartAfterRedFlag()
        {
            var ranked = RankedRacers();
            float L = track.roadLength;
            float baseDist = (Mathf.Floor(ranked[0].dist / L) + 1f) * L - 10f;       // everyone lines up just before the next line, in race order
            for (int i = 0; i < ranked.Count; i++)
            {
                float d = baseDist + GRID[Mathf.Min(i, GRID.Length - 1)];
                float off = i % 2 == 0 ? -2.6f : 2.6f;
                var a = ranked[i] as AICar;
                if (a != null)
                {
                    a.ResetTo(d, off);
                    a.tireWear = 100f; a.compound = PitCompoundForAI(a.compound);
                    var wr = a.go.GetComponent<WheelRig>();
                    if (wr != null && wr.sidewallMat != null) wr.sidewallMat.mainTexture = Tex.Sidewall(a.compound);
                }
                else { player.ResetTo(d, off); player.RedFlagService(); }
            }
            redFlagLost += clock - redStartClock;
            redFlag = false; redPhase = 0; caution = Caution.None; lastCautionEnd = clock;
            isRestart = true;
            countdownLeft = 3.99f; state = State.Countdown;
            Radio("Standing restart - same order as before the red flag.", 3);
        }

        // ---- everything the race director watches each frame ----
        void UpdateRaceControl(float dt)
        {
            bool raceMode = IsRace;

            // rolling start: hold formation until the leader reaches the line, then green flag
            if (rolling)
            {
                float lead = float.MinValue;
                foreach (var r in racers) lead = Mathf.Max(lead, r.dist);
                if (lead >= 0f)
                {
                    rolling = false; raceStartClock = clock;
                    player.BeginRace(clock, -2f);
                    ScheduleRaceEvents();
                    Commentate("The green flag is out and they are racing!", 2);
                    Radio("Green flag - go go go!", 3); Flash("GO GO GO");
                }
            }

            if (raceMode)
            {
                if (plannedCaution != Caution.None && cautionAt >= 0f && clock >= cautionAt)
                {
                    var k = plannedCaution; plannedCaution = Caution.None;
                    TriggerCaution(k, null);
                }
                bool trackClear = !(recovery != null && recovery.Busy);                                                                         // not until the track is clear
                if (caution == Caution.Safety && safetyCarActive && !scRestart && clock >= cautionEndAt && trackClear) BeginSafetyRestart();
                else if (caution == Caution.Virtual && clock >= cautionEndAt && trackClear) EndCaution("Virtual safety car ending - green flag!");
                if (scRestart) UpdateSafetyRestart(dt);
                UpdateRedFlag();
            }

            if (safetyCarActive && !scRestart) scDist += Tuning.SAFETY_CAR_SPEED_CAP * dt;
            if (safetyCarActive && !scRestart && scGo != null)
            {
                float d = scDist;
                Vector2 p = track.PointAt(d), t = track.TangentAt(d);
                scGo.transform.position = TrackModel.W(p, track.ElevationAt(d));
                scGo.transform.rotation = Quaternion.Euler(0f, (Mathf.PI - Mathf.Atan2(t.x, t.y)) * Mathf.Rad2Deg, 0f);
            }

            float finishDist = raceLaps * track.roadLength;
            foreach (var r in racers) if (r.finishTime < 0f && r.dist >= finishDist) r.finishTime = clock - redFlagLost;

            if (raceMode && !rolling && !redFlag) { UpdateBlueFlags(dt); UpdateYellowOvertakes(); }
            calls.Update(this);
            TrackOrder();
            UpdateSpray();
            if (recovery != null) recovery.Tick(this, dt, clock);

            // endurance races run from afternoon into night
            if (mode == Mode.Endurance)
            {
                float prog = Mathf.Clamp01(player.dist / (raceLaps * track.roadLength));
                float tod = Mathf.Clamp01((prog - 0.2f) / 0.6f);
                if (Mathf.Abs(tod - timeOfDay) > 0.01f) { timeOfDay = tod; ApplyWeatherLook(); }
            }
        }

        // blue flags: a car a lap ahead catching you has to be let through. Ignore it and the stewards step in.
        void UpdateBlueFlags(float dt)
        {
            float L = track.roadLength;
            Racer best = null; float bestGap = 1e9f;
            foreach (var c in racers)
            {
                if (c == player) continue;
                float behind = TrackModel.Mod(player.dist - c.dist, L);
                if (behind > 2f && behind < 130f && c.dist > player.dist + L * 0.5f && behind < bestGap) { bestGap = behind; best = c; }
            }
            blueCar = best;
            if (best != null && bestGap < 45f && !player.pitStopActive && !player.inPitLaneNow)
            {
                float cLat = best is AICar ? ((AICar)best).lateral : 0f;
                float myLat = track.SignedLateral(new Vector2(player.pos.x, player.pos.z));
                bool blocking = Mathf.Abs(myLat - cLat) < 2.4f;
                blueTimer = Mathf.Max(0f, blueTimer + (blocking ? dt : -dt * 2f));
                if (blueTimer > 7f) { stewards.Offend(this, player, "blue", "ignoring blue flags", 5f); blueTimer = 2f; }
            }
            else blueTimer = Mathf.Max(0f, blueTimer - dt * 2f);
        }

        // passing someone while the sector is under yellow (or a safety car / VSC) is a penalty
        void UpdateYellowOvertakes()
        {
            int mySector = Mathf.Min(2, Mathf.FloorToInt(TrackModel.Mod(player.dist, track.roadLength) / (track.roadLength / 3f)));
            bool yellow = safetyCarActive || vscActive || incidentUntil[mySector] > clock;
            foreach (var r in racers)
            {
                if (r == player) continue;
                float rel = r.dist - player.dist, prev;
                if (prevRel.TryGetValue(r, out prev) && yellow && prev > 0f && rel <= 0f && Mathf.Abs(rel) < 15f && r.speed > 12f
                    && !player.pitStopActive && !player.inPitLaneNow)
                    stewards.Offend(this, player, "yellow", "overtaking under yellow flags", 5f);
                prevRel[r] = rel;
            }
        }

        // translucent pace car replaying your best lap on this circuit, lined up against your current lap time
        void UpdateGhost()
        {
            if (ghostGo == null || ghostSamples == null) return;
            float elapsed = TrackModel.Mod(clock - player.lapStart, ghostDuration);
            if (ghostIdx >= ghostSamples.Length || ghostSamples[ghostIdx].x > elapsed) ghostIdx = 0;
            while (ghostIdx < ghostSamples.Length - 2 && ghostSamples[ghostIdx + 1].x <= elapsed) ghostIdx++;
            Vector2 a = ghostSamples[ghostIdx], b = ghostSamples[Mathf.Min(ghostIdx + 1, ghostSamples.Length - 1)];
            float span = Mathf.Max(0.0001f, b.x - a.x);
            float k = Mathf.Clamp01((elapsed - a.x) / span);
            float gd = a.y + (b.y - a.y) * k;
            if (b.y < a.y - 1f) gd = a.y; // lap wrap
            Vector2 p = track.PointAt(gd), t = track.TangentAt(gd), n = TrackModel.Normal(t);
            ghostGo.transform.position = TrackModel.W(p + n * 2.4f, track.ElevationAt(gd));
            ghostGo.transform.rotation = Quaternion.Euler(0f, (Mathf.PI - Mathf.Atan2(t.x, t.y)) * Mathf.Rad2Deg, 0f);
        }

        // the grid walk: the camera sweeps along the grid from the back to the front while a caption names each car
        void IntroCamera()
        {
            float t = Mathf.Clamp01(1f - countdownLeft / INTRO_LEN);
            float e = t * t * (3f - 2f * t);
            float d = Mathf.Lerp(GRID[GRID.Length - 1] - 14f, 14f, e);
            Vector2 p = track.PointAt(d), n = TrackModel.Normal(track.TangentAt(d));
            cam.fieldOfView = 52f;
            cam.transform.position = TrackModel.W(p + n * 14f, track.ElevationAt(d) + 2.6f);
            cam.transform.LookAt(TrackModel.W(track.PointAt(d + 9f), track.ElevationAt(d + 9f) + 0.9f), Vector3.up);
            if (rig != null) rig.root.SetActive(false);
            hud.SetVisible(false);
            foreach (var rend in player.go.GetComponentsInChildren<Renderer>()) rend.enabled = true;
            Racer near = null; float best = 1e9f;
            foreach (var r in racers)
            {
                float dd = Mathf.Abs(Mathf.Repeat(r.dist - d + track.roadLength * 0.5f, track.roadLength) - track.roadLength * 0.5f);
                if (dd < best) { best = dd; near = r; }
            }
            if (near != null)
            {
                int rank = 1;
                foreach (var r in racers) if (r != near && r.dist > near.dist) rank++;
                var ai = near as AICar;
                string team = ai != null ? TeamCareer.Teams[TeamCareer.TeamOfAi(ai.id)].name : TeamCareer.Teams[TeamCareer.team].name;
                introCaption = "P" + rank + "     " + NameOf(near) + "     -     " + team;
            }
        }

        void LateUpdate()
        {
            if (stars != null && cam != null) stars.Follow(cam.transform.position);
            if (state == State.Menu || state == State.Replay || player == null) return;
            if (state == State.Countdown && introActive) { IntroCamera(); return; }
            var em = rain.emission;
            em.rateOverTime = 3000f * rainOpacity;
            rain.transform.position = cam.transform.position + Vector3.up * 14f;

            Vector3 p = player.pos;
            float h = player.heading;
            float hv = h + (firstPerson ? lookYaw * Mathf.Deg2Rad : 0f);
            Vector3 fwdWeb = new Vector3(Mathf.Sin(hv), 0f, Mathf.Cos(hv));
            bool hideHelmet = camMode == CamMode.Helmet;
            foreach (var rend in player.go.GetComponentsInChildren<Renderer>()) rend.enabled = camMode != CamMode.Cockpit && !(hideHelmet && rend.name == "helmet");
            if (rig != null)
            {
                bool showRig = cockpitView && Mathf.Abs(lookYaw) < 25f;
                rig.root.SetActive(showRig);
                if (showRig)
                {
                    rig.Refresh(player, Time.deltaTime, player.MaxSpeedNow);
                    rig.UpdateMirrors(player.go.transform.position, player.go.transform.eulerAngles.y, cam.backgroundColor);
                }
            }

            // ---- where the camera wants to be in this view ----
            Vector3 wantPos; Quaternion wantRot; float wantFov = 78f;
            if (camMode == CamMode.Cockpit || camMode == CamMode.Helmet)
            {
                bool helmet = camMode == CamMode.Helmet;
                Vector3 camWeb = p + Vector3.up * (helmet ? 0.98f : 1.05f) - fwdWeb * (helmet ? 0.12f : 0.3f);
                Vector3 look = camWeb + fwdWeb * 10f;
                look.y += 0.4f - Mathf.Min(0.35f, Mathf.Abs(player.steerSmooth) * 0.15f) - player.pitchSmooth * 0.22f;
                camWeb.y = Mathf.Max(camWeb.y, player.surfaceY + 0.6f);
                wantPos = TrackModel.W(camWeb.x, camWeb.y, camWeb.z);
                wantRot = Quaternion.LookRotation(TrackModel.W(look.x, look.y, look.z) - wantPos, Vector3.up);
                wantRot = wantRot * Quaternion.Euler(0f, 0f, player.steerSmooth * 1.5f) * Quaternion.Euler(player.bodyPitch * 0.7f, 0f, player.bodyRoll * 0.5f);      // weight transfer
                wantFov = helmet ? 88f : 78f;
            }
            else if (camMode == CamMode.Halo)
            {
                // the camera sits on top of the halo, looking down the front of the car: the nose, the front wing and both front tyres
                Vector3 camWeb = p + Vector3.up * 1.32f + fwdWeb * 0.25f;
                Vector3 look = camWeb + fwdWeb * 14f; look.y -= 1.1f;
                wantPos = TrackModel.W(camWeb.x, Mathf.Max(camWeb.y, player.surfaceY + 0.9f), camWeb.z);
                wantRot = Quaternion.LookRotation(TrackModel.W(look.x, look.y, look.z) - wantPos, Vector3.up) * Quaternion.Euler(player.bodyPitch * 0.5f, 0f, player.bodyRoll * 0.4f);
                wantFov = 84f;
            }
            else if (camMode == CamMode.Chase)
            {
                Vector3 camWeb = p - fwdWeb * 7.5f + Vector3.up * 2.6f;
                camWeb.y = Mathf.Max(camWeb.y, player.surfaceY + 0.8f);
                wantPos = TrackModel.W(camWeb.x, camWeb.y, camWeb.z);
                wantRot = Quaternion.LookRotation(TrackModel.W(p.x, p.y + 0.9f, p.z) - wantPos, Vector3.up);
                wantFov = 72f;
            }
            else
            {
                TvDirector(p, fwdWeb, Time.deltaTime, out wantPos, out wantRot, out wantFov);
            }

            // ---- blend from the old view to the new one ----
            if (camMode != lastCamMode || tvShotChanged)
            {
                camBlend = 0f; camFromPos = cam.transform.position; camFromRot = cam.transform.rotation; camFromFov = cam.fieldOfView;
                lastCamMode = camMode; tvShotChanged = false;
            }
            if (camBlend < 1f)
            {
                camBlend = Mathf.Min(1f, camBlend + Time.deltaTime / 0.55f);
                float k = camBlend * camBlend * (3f - 2f * camBlend);
                wantPos = Vector3.Lerp(camFromPos, wantPos, k);
                wantRot = Quaternion.Slerp(camFromRot, wantRot, k);
                wantFov = Mathf.Lerp(camFromFov, wantFov, k);
            }
            cam.transform.position = wantPos; cam.transform.rotation = wantRot; cam.fieldOfView = wantFov;

            float shakeAmp = Mathf.Max(player.rumble * 0.03f, Mathf.Max(0f, player.shakeUntil - clock) * 0.12f);
            if (shakeAmp > 0.0005f) cam.transform.position += Random.insideUnitSphere * Mathf.Min(0.08f, shakeAmp) * (firstPerson ? 1f : 0.4f);
            if (player.dyn != null) player.dyn.ApplyHead(cam.transform, firstPerson);       // the driver's head moves with the g-forces
        }

        // ---- the TV director: trackside, helicopter, low front-quarter and low chase shots, cutting every few seconds ----
        void TvDirector(Vector3 p, Vector3 fwdWeb, float dt, out Vector3 pos, out Quaternion rot, out float fov)
        {
            Vector3 carW = TrackModel.W(p.x, p.y + 0.8f, p.z);
            Vector3 fwdW = TrackModel.W(fwdWeb.x, 0f, fwdWeb.z);
            tvTimer -= dt;
            if (tvShot < 0 || tvTimer <= 0f || (tvShot == 0 && Vector3.Dot(carW - tvPos, fwdW) > 28f)) NextTvShot(p);
            Vector3 right = Vector3.Cross(Vector3.up, fwdW).normalized;
            Vector3 target = carW;
            switch (tvShot)
            {
                case 0:                                         // a fixed trackside camera that follows the car past
                    pos = tvPos; fov = 34f; break;
                case 1:                                         // helicopter
                    pos = carW - fwdW * 55f + right * 18f + Vector3.up * 42f; fov = 46f; break;
                case 2:                                         // low in front of the car, off to one side
                    pos = carW + fwdW * 10f + right * 3.8f + Vector3.up * 0.2f; fov = 62f; break;
                default:                                        // low chase
                    pos = carW - fwdW * 8.5f + Vector3.up * 1.0f; target = carW + fwdW * 6f; fov = 66f; break;
            }
            rot = Quaternion.LookRotation(target - pos, Vector3.up);
        }

        void NextTvShot(Vector3 p)
        {
            int next = tvShot;
            while (next == tvShot) next = Random.Range(0, 4);
            tvShot = next; tvShotChanged = true;
            tvTimer = 5.5f + Random.value * 3.5f;
            if (tvShot == 0)
            {
                float d = player.dist + 90f + Random.value * 40f;
                Vector2 pt = track.PointAt(d), n = TrackModel.Normal(track.TangentAt(d));
                float side = Random.value < 0.5f ? -1f : 1f;
                tvPos = TrackModel.W(pt + n * side * (14f + Random.value * 6f), track.ElevationAt(d) + 1.7f + Random.value * 1.4f);
            }
        }

        // the dark edge of a helmet visor around the picture
        void DrawHelmetFrame(float W, float H)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.9f); GUI.DrawTexture(new Rect(0f, 0f, W, H * 0.07f), Texture2D.whiteTexture);
            GUI.color = new Color(0f, 0f, 0f, 0.45f); GUI.DrawTexture(new Rect(0f, H * 0.07f, W, H * 0.05f), Texture2D.whiteTexture);
            GUI.color = new Color(0f, 0f, 0f, 0.9f); GUI.DrawTexture(new Rect(0f, H * 0.93f, W, H * 0.07f), Texture2D.whiteTexture);
            GUI.color = new Color(0f, 0f, 0f, 0.45f); GUI.DrawTexture(new Rect(0f, H * 0.88f, W, H * 0.05f), Texture2D.whiteTexture);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0f, 0f, W * 0.04f, H), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(W * 0.96f, 0f, W * 0.04f, H), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------ incidents, debris
        public void NoteIncident(float d)
        {
            int s = Mathf.Min(2, Mathf.FloorToInt(TrackModel.Mod(d, track.roadLength) / (track.roadLength / 3f)));
            incidentUntil[s] = clock + 9f;
        }

        public void MirrorLost(int side, bool lost) { if (rig != null) rig.SetMirrorLost(side, lost); }
        public void SpawnWingDebris(Vector3 pos, Vector3 side, int stage) { debris.Spawn(pos, side, stage); }
        public void SpawnWheelDebris(Vector3 pos, Vector3 vel) { debris.SpawnWheel(pos, vel); }

        /// <summary>A rival has broken down on track: yellow flags, a recovery truck and often a safety car or VSC.</summary>
        public void AiRetired(AICar a)
        {
            a.dnf = true;
            if (recovery != null) recovery.Dispatch(a);
            if (IsRace)
            {
                Caution kind = Random.value < 0.6f ? Caution.Safety : Caution.Virtual;
                TriggerCaution(kind, a.label + " is stopped on the track - " + (kind == Caution.Safety ? "safety car deployed." : "virtual safety car."));
            }
        }

        // ------------------------------------------------------------------ replay (drone camera + photo mode)
        void EnterReplay()
        {
            if (rec.frames.Count < 2) return;
            state = State.Replay;
            hud.SetVisible(false);
            if (rig != null) rig.root.SetActive(false);
            foreach (var r in racers) foreach (var rend in r.go.GetComponentsInChildren<Renderer>()) rend.enabled = true;
            replayT = 0f; replayPlay = true; replayHideUi = false; replayCam = 0; trackCamSet = false;
            replayFocus = Mathf.Max(0, racers.IndexOf(player));
            replayHints = new int[racers.Count];
            commPlayer.Begin(commentary, 0f);
            Voice.Warm(Voice.PLAYBYPLAY); Voice.Warm(Voice.WOMAN);
        }

        void TakePhoto()
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, "ApexDrive_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            ScreenCapture.CaptureScreenshot(path);
            toast = "Photo saved: " + path; toastUntil = Time.time + 4f;
        }

        void UpdateReplay()
        {
            float dt = Time.deltaTime;
            if (Inp.Pressed(KeyCode.Escape)) { state = State.Finished; return; }
            if (Inp.Pressed(KeyCode.Space)) replayPlay = !replayPlay;
            if (Inp.Pressed(KeyCode.LeftArrow)) replayT -= 3f;
            if (Inp.Pressed(KeyCode.RightArrow)) replayT += 3f;
            if (Inp.Pressed(KeyCode.Alpha1)) replayCam = 0;
            if (Inp.Pressed(KeyCode.Alpha2)) { replayCam = 1; trackCamSet = false; }
            if (Inp.Pressed(KeyCode.Alpha3)) replayCam = 2;
            if (Inp.Pressed(KeyCode.Alpha4)) replayFocus = (replayFocus + 1) % racers.Count;
            if (Inp.Pressed(KeyCode.F)) replayHideUi = !replayHideUi;
            if (Inp.Pressed(KeyCode.Return)) TakePhoto();
            if (replayPlay) replayT += dt * replaySpeed;
            replayT = Mathf.Clamp(replayT, 0f, rec.Duration);
            if (replayT >= rec.Duration) replayPlay = false;
            commPlayer.Tick(commentary, replayT, replayPlay);

            for (int i = 0; i < racers.Count; i++)
            {
                Vector3 p; float yaw;
                if (rec.Sample(replayT, i, ref replayHints[i], out p, out yaw))
                {
                    racers[i].go.transform.position = p;
                    racers[i].go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                }
            }

            Vector3 f = racers[replayFocus].go.transform.position;
            Vector3 lookAt = f + Vector3.up * 0.8f;
            if (replayCam == 0)
            {
                // drone: orbits the car, swooping in and out and up and down
                float a = Time.time * 0.35f, rad = 11f + 5f * Mathf.Sin(Time.time * 0.23f), hgt = 6f + 3.5f * Mathf.Sin(Time.time * 0.31f + 1f);
                Vector3 want = f + new Vector3(Mathf.Cos(a) * rad, hgt, Mathf.Sin(a) * rad);
                cam.transform.position = Vector3.Lerp(cam.transform.position, want, 1f - Mathf.Exp(-9f * dt));
            }
            else if (replayCam == 1)
            {
                if (!trackCamSet || Vector3.Distance(trackCamPos, f) > 75f)
                {
                    float d = track.ProjectDist(new Vector2(f.x, -f.z)) + 55f;
                    Vector2 n = TrackModel.Normal(track.TangentAt(d));
                    float side = (Mathf.FloorToInt(d / 150f) % 2 == 0) ? 1f : -1f;
                    trackCamPos = TrackModel.W(track.PointAt(d) + n * (track.WallOffsetAt(d) - 1f) * side, track.ElevationAt(d) + 1.6f);
                    trackCamSet = true;
                }
                cam.transform.position = trackCamPos;
            }
            else
            {
                float yawR = racers[replayFocus].go.transform.eulerAngles.y * Mathf.Deg2Rad;
                cam.transform.position = f - new Vector3(Mathf.Sin(yawR), 0f, Mathf.Cos(yawR)) * 8f + Vector3.up * 2.6f;
            }
            cam.transform.LookAt(lookAt, Vector3.up);
        }

        void DrawReplay(float W, float H)
        {
            if (Time.time < toastUntil) GUI.Label(new Rect(20, H - 90, W - 40, 24), toast, Big(14, FontStyle.Normal));
            if (Time.time < commPlayer.subtitleUntil)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.7f);
                GUI.DrawTexture(new Rect(W * 0.5f - 400f, H - 150f, 800f, 62f), Texture2D.whiteTexture);
                GUI.color = new Color(0.2f, 0.9f, 0.8f);
                GUI.Label(new Rect(W * 0.5f - 390f, H - 148f, 780f, 20f), commPlayer.speaker, Big(11));
                GUI.color = Color.white;
                GUI.Label(new Rect(W * 0.5f - 390f, H - 130f, 780f, 40f), commPlayer.subtitle, Big(16, FontStyle.Normal));
            }
            if (replayHideUi) return;
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(0, H - 60, W, 60), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(20, 10, W - 40, 24), "REPLAY   -   " + Hud.FormatTime(replayT) + " / " + Hud.FormatTime(rec.Duration) + "   -   following " + racers[replayFocus].label, Big(16));
            GUI.Label(new Rect(20, 34, W - 40, 22), "Space pause   Left/Right scrub   1 drone  2 trackside  3 chase   4 next car   F hide UI   Enter photo   Esc exit", Big(12, FontStyle.Normal));
            if (GUI.Button(new Rect(12, H - 50, 70, 34), replayPlay ? "Pause" : "Play")) replayPlay = !replayPlay;
            float nt = GUI.HorizontalSlider(new Rect(94, H - 41, W - 700, 18), replayT, 0f, Mathf.Max(0.1f, rec.Duration));
            if (Mathf.Abs(nt - replayT) > 0.01f) replayT = nt;
            float bx = W - 590;
            string[] names = { "Drone", "Trackside", "Chase" };
            for (int i = 0; i < 3; i++)
            {
                GUI.backgroundColor = replayCam == i ? ON : Color.white;
                if (GUI.Button(new Rect(bx + i * 78, H - 50, 74, 34), names[i])) { replayCam = i; trackCamSet = false; }
            }
            GUI.backgroundColor = Color.white;
            if (GUI.Button(new Rect(bx + 238, H - 50, 74, 34), "Next car")) replayFocus = (replayFocus + 1) % racers.Count;
            if (GUI.Button(new Rect(bx + 316, H - 50, 74, 34), "Hide UI")) replayHideUi = true;
            if (GUI.Button(new Rect(bx + 394, H - 50, 74, 34), "Photo")) TakePhoto();
            if (GUI.Button(new Rect(bx + 472, H - 50, 74, 34), "Exit")) state = State.Finished;
        }

        // ------------------------------------------------------------------ menus (IMGUI)
        void OnGUI()
        {
            float s = Screen.height / 720f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float W = Screen.width / s, H = 720f;

            if (tutorial != null && (state == State.Racing || state == State.Countdown)) tutorial.Draw(W, H, cockpitView);
            if (state == State.Racing || state == State.Countdown) DrawRaceOverlays(W, H);
            if (showWall && state == State.Racing && mode != Mode.Tutorial) PitWall.Draw(this, W, H);
            if (state == State.Menu) { DrawMenu(W, H); gym.Draw(W, H); }
            else if (state == State.Paused) DrawPause(W, H);
            if (mpMenu && (state == State.Racing || state == State.Countdown)) DrawOnlineMenu(W, H);
            else if (state == State.Finished)
            {
                if (showTelemetry) { if (telemetry.Draw(W, H, player, stewards.log)) showTelemetry = false; }
                else DrawFinish(W, H);
            }
            else if (state == State.Replay) DrawReplay(W, H);
        }

        GUIStyle Big(int size, FontStyle fs = FontStyle.Bold)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs };
            st.normal.textColor = Color.white;
            return st;
        }

        static readonly Color ON = new Color(0.2f, 0.9f, 0.8f);

        void DrawMenu(float W, float H)
        {
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, 460, H), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float x = 24;
            GUI.Label(new Rect(x, 14, 420, 46), "APEX DRIVE", Big(36));
            GUI.Label(new Rect(x, 56, 420, 22), RacerProfile.DisplayName + "   -   $" + GameSave.money + "   -   level " + Profile.level + "   -   " + Profile.UnlockedCount + "/" + Profile.AchIds.Length + " achievements", Big(14, FontStyle.Normal));

            string[] names = { "GARAGE", "RACE", "RACER", "TEAM", "CREW", "PROFILE" };
            for (int i = 0; i < 6; i++)
            {
                GUI.backgroundColor = (int)tab == i ? ON : Color.white;
                if (GUI.Button(new Rect(x + i * 70, 84, 67, 30), names[i])) { tab = (Tab)i; scroll = Vector2.zero; }
            }
            GUI.backgroundColor = Color.white;

            float contentH = tab == Tab.Race && netPanel ? 760f : tab == Tab.Garage ? 1420f : tab == Tab.Race ? 1840f : tab == Tab.Racer ? 1000f : tab == Tab.Team ? 1100f : tab == Tab.Crew ? 1500f : 700f;
            scroll = GUI.BeginScrollView(new Rect(0, 122, 460, H - 122), scroll, new Rect(0, 0, 440, contentH));
            if (tab == Tab.Race && netPanel) DrawNet(x);
            else if (tab == Tab.Garage) DrawGarage(x);
            else if (tab == Tab.Race) DrawRace(x);
            else if (tab == Tab.Racer) DrawRacer(x);
            else if (tab == Tab.Team) DrawTeam(x);
            else if (tab == Tab.Crew) DrawCrew(x);
            else DrawProfile(x);
            GUI.EndScrollView();
        }

        void DrawGarage(float x)
        {
            float y = 0;
            // three cars, one for each circuit: pick the one to edit, its livery, tyres and setup below are that car's own
            GUI.Label(new Rect(x, y, 420, 24), "YOUR CARS  (one for each circuit)", Big(15)); y += 26;
            for (int ci = 0; ci < CarSetups.Ids.Length; ci++)
            {
                string cid = CarSetups.Ids[ci];
                GUI.backgroundColor = CarSetups.Current == cid ? ON : Color.white;
                if (GUI.Button(new Rect(x + (ci % 3) * 138, y + (ci / 3) * 34, 134, 30), CarSetups.NameOf(cid))) CarSetups.Switch(cid);
            }
            GUI.backgroundColor = Color.white; y += 34f * ((CarSetups.Ids.Length + 2) / 3) + 4f;
            string carCircuit = Tracks.All[TrackIndexOf(CarSetups.Current)].name;
            GUI.Label(new Rect(x, y, 420, 20), "Editing " + CarSetups.NameOf(CarSetups.Current) + "  -  races at " + carCircuit, Big(13));  y += 22;
            GUI.Label(new Rect(x, y, 300, 44), CarSetups.BlurbOf(CarSetups.Current), Big(11, FontStyle.Normal));
            GUI.enabled = !(weekend != null && weekend.locked && Tracks.All[weekend.trackIndex].id == CarSetups.Current);
            if (GUI.Button(new Rect(x + 310, y + 4, 100, 26), "Base setup")) CarSetups.ResetToBase(CarSetups.Current);
            GUI.enabled = true;
            y += 48;
            for (int ci = 0; ci < CarSetups.Ids.Length; ci++)
            {
                string cid = CarSetups.Ids[ci];
                GUI.color = CarSetups.Current == cid ? Color.white : new Color(0.75f, 0.78f, 0.85f);
                GUI.Label(new Rect(x, y, 420, 20), CarSetups.NameOf(cid) + ":  " + CarSetups.Summary(cid), Big(11, FontStyle.Normal));
                y += 20;
            }
            GUI.color = Color.white;
            y += 10;
            GUI.Label(new Rect(x, y, 420, 24), "PARTS SHOP", Big(15)); y += 24;
            y = Upgrade(x, y, "Engine", ref GameSave.power);
            y = Upgrade(x, y, "Aero", ref GameSave.aero);
            y = Upgrade(x, y, "Tires & Suspension", ref GameSave.grip);
            y = Upgrade(x, y, "Reliability", ref GameSave.reliability);

            y += 6;
            GUI.Label(new Rect(x, y, 420, 24), "POWER UNIT & GEARBOX", Big(15)); y += 24;
            y = WearRow(x, y, "Engine", ref GameSave.engineWear, 1100);
            y = WearRow(x, y, "Gearbox", ref GameSave.gearboxWear, 600);
            // brakes wear with use (hot brakes wear faster); a new set costs money but no grid penalty
            GUI.Label(new Rect(x, y, 130, 24), "Brakes  " + Mathf.RoundToInt(GameSave.brakeWear) + "%", Big(13, FontStyle.Normal));
            GUI.color = new Color(0.1f, 0.12f, 0.16f); GUI.DrawTexture(new Rect(x + 130, y + 7, 120, 10), Texture2D.whiteTexture);
            GUI.color = GameSave.brakeWear < 50f ? new Color(0.3f, 0.85f, 0.45f) : GameSave.brakeWear < 80f ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 0.3f, 0.25f);
            GUI.DrawTexture(new Rect(x + 130, y + 7, 120f * Mathf.Clamp01(GameSave.brakeWear / 100f), 10), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.enabled = GameSave.money >= 400 && GameSave.brakeWear > 1f;
            if (GUI.Button(new Rect(x + 262, y, 148, 24), "New brakes  $400")) { GameSave.money -= 400; GameSave.brakeWear = 0f; GameSave.Save(); }
            GUI.enabled = true;
            y += 28;
            GUI.Label(new Rect(x, y, 420, 48), "Every race wears them out (push mode wears the engine faster) and a worn engine loses power and can fail. You get two replacements a season; each one after that costs 3 grid places. Used so far: " + GameSave.partsUsed + "."
                      + (GameSave.gridPenalty > 0 ? "   GRID PENALTY NEXT RACE: " + GameSave.gridPenalty + " places." : ""), Big(11, FontStyle.Normal));
            y += 52;

            y += 6;
            GUI.Label(new Rect(x, y, 420, 24), "DRIVER", Big(15)); y += 24;
            for (int i = 0; i < Drivers.All.Length; i++)
            {
                GUI.backgroundColor = GameSave.driver == i ? ON : Color.white;
                if (GUI.Button(new Rect(x, y, 410, 26), Drivers.All[i].name + "  -  " + Drivers.All[i].sub)) GameSave.driver = i;
                y += 28;
            }
            GUI.backgroundColor = Color.white;

            y += 6;
            GUI.Label(new Rect(x, y, 420, 24), "LIVERY", Big(15)); y += 24;
            y = Swatches(x, y, "Body", ref GameSave.bodyColor);
            y = Swatches(x, y, "Accent", ref GameSave.accentColor);
            GUI.Label(new Rect(x, y, 70, 24), "Pattern", Big(13, FontStyle.Normal));
            for (int i = 0; i < Livery.PatternNames.Length; i++)
            {
                GUI.backgroundColor = GameSave.pattern == i ? ON : Color.white;
                if (GUI.Button(new Rect(x + 70 + i * 112, y, 106, 24), Livery.PatternNames[i])) GameSave.pattern = i;
            }
            GUI.backgroundColor = Color.white;
            y += 32;

            GUI.Label(new Rect(x, y, 420, 24), "SETUP  -  " + CarSetups.NameOf(CarSetups.Current), Big(15)); y += 24;
            GUI.Label(new Rect(x, y, 70, 24), "Tires", Big(13, FontStyle.Normal));
            for (int i = 0; i < Tuning.CompoundNames.Length; i++)
            {
                GUI.backgroundColor = (int)GameSave.compound == i ? ON : Color.white;
                if (GUI.Button(new Rect(x + 70 + i * 68, y, 64, 24), Tuning.CompoundNames[i])) GameSave.compound = (Compound)i;
            }
            GUI.backgroundColor = Color.white;
            y += 30;
            string tid = CarSetups.Current;
            bool locked = weekend != null && weekend.locked && Tracks.All[weekend.trackIndex].id == tid;
            GUI.enabled = !locked;
            y = Slider(x, y, "Wing angle" + Setup.Hint(tid, 4), ref GameSave.wing, 0, 100);
            y = Slider(x, y, "Gear ratio" + Setup.Hint(tid, 5), ref GameSave.gear, 0, 100);
            y = Slider(x, y, "Brake bias", ref GameSave.brakeBias, 40, 80);
            y = Slider(x, y, "Brake ducts", ref GameSave.brakeDuct, 0, 100);
            y = Slider(x, y, "Ride height" + Setup.Hint(tid, 0), ref GameSave.rideHeight, 0, 100);
            y = Slider(x, y, "Camber" + Setup.Hint(tid, 1), ref GameSave.camber, 0, 100);
            y = Slider(x, y, "Differential" + Setup.Hint(tid, 2), ref GameSave.diff, 0, 100);
            y = Slider(x, y, "Tyre pressure" + Setup.Hint(tid, 3), ref GameSave.pressure, 0, 100);
            GUI.enabled = true;
            GUI.Label(new Rect(x, y + 2, 420, 52), locked ? "PARC FERME: the setup is locked until the race weekend is over. Tyres can still be chosen."
                      : "Wing: grip vs top speed. Gear: short = punchy. Ride height: low = downforce but bottoms out. Camber: cornering grip vs tyre heat. Differential: locked = traction on exit, push on entry. Pressure: high = less wear, less grip.", Big(11, FontStyle.Normal));
            y += 56;
            GUI.enabled = GameSave.money >= 300 && Setup.NextUnrevealed(tid) >= 0;
            if (GUI.Button(new Rect(x, y, 200, 26), "WIND TUNNEL  $300"))
            {
                string lines = "Wind tunnel for " + CarSetups.NameOf(tid) + ":  ";
                for (int run = 0; run < Facilities.TunnelReveals; run++)
                {
                    int wk = Setup.NextUnrevealed(tid);
                    if (wk < 0) break;
                    float worth = SetupLab.GainIfSet(tid, wk, Setup.Fit(tid));
                    Setup.Reveal(tid, wk);
                    lines += Setup.Names[wk].ToLower() + " " + Mathf.RoundToInt(Setup.Ideal(tid, wk)) + " (you " + Mathf.RoundToInt(Setup.Value(wk)) + ", worth " + worth.ToString("0.00") + " s a lap);  ";
                }
                GameSave.money -= 300; RacerProfile.day++; GameSave.Save(); RacerProfile.Save();
                Setup.simResult = lines + "The readings are for the car as it is now: upgrades or new rules make them out of date.";
            }
            GUI.enabled = GameSave.money >= Facilities.SimCost;
            if (GUI.Button(new Rect(x + 210, y, 200, 26), "SIMULATOR  $" + Facilities.SimCost))
            {
                var simFb = Setup.Feedback(tid);
                Setup.Remember(tid, simFb);
                float fit = Setup.Fit(tid);
                GameSave.money -= Facilities.SimCost; RacerProfile.day++; GameSave.Save(); RacerProfile.Save();
                float lapNow, lapBest, lapGain;
                SetupLab.Estimate(tid, fit, out lapNow, out lapBest, out lapGain);
                Setup.simResult = "Simulator: setup rating " + Mathf.RoundToInt(fit * 100f) + "%. A clean lap with this setup: " + Hud.FormatTime(lapNow) + ". The best possible setup for this car would be about " + lapGain.ToString("0.00") + " s per lap quicker.  " + simFb[0];
                if (Facilities.SimShowsAll)
                {
                    string worth = "  Worth per setting:";
                    for (int k = 0; k < SetupLab.COUNT; k++) worth += "  " + Setup.Names[k].ToLower() + " " + SetupLab.GainIfSet(tid, k, fit).ToString("0.00") + " s";
                    Setup.simResult += worth;
                }
            }
            GUI.enabled = true;
            y += 32;
            if (Setup.simResult.Length > 0) { GUI.Label(new Rect(x, y, 420, 52), Setup.simResult, Big(11, FontStyle.Normal)); y += 56; }
            string fb = Setup.LastFeedbackFor(Tracks.All[GameSave.trackIndex].id);
            GUI.Label(new Rect(x, y, 420, 70), fb.Length > 0 ? "ENGINEER (last practice here): " + fb : "Run a practice session on this circuit to get setup feedback from your engineer.", Big(11, FontStyle.Normal));
        }

        void DrawRace(float x)
        {
            float y = 0;
            GUI.Label(new Rect(x, y, 420, 24), weekend != null ? "CIRCUIT  (locked for the race weekend)" : "CIRCUIT", Big(15)); y += 24;
            GUI.enabled = weekend == null;
            for (int i = 0; i < Tracks.All.Length; i++)
            {
                bool sel = GameSave.trackIndex == i;
                GUI.backgroundColor = sel ? ON : Color.white;
                int q = GameSave.GridSlot(Tracks.All[i].id);
                string label = Tracks.All[i].name + (q >= 0 ? "   (grid P" + (q + 1) + ")" : "");
                if (GUI.Button(new Rect(x, y, 410, 26), label)) { GameSave.trackIndex = i; CarSetups.Switch(Tracks.All[i].id); BuildWorld(i); ApplyWeatherLook(); weatherPlan = WeatherPlan.Roll(Tracks.All[i]); planTrack = i; }
                y += 28;
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            float best = GameSave.BestLap(Tracks.All[GameSave.trackIndex].id);
            GUI.Label(new Rect(x, y, 420, 22), Tracks.All[GameSave.trackIndex].sub + (best > 0 ? "   |   best " + Hud.FormatTime(best) : ""), Big(11, FontStyle.Normal));
            y += 20;
            GUI.Label(new Rect(x, y, 420, 22), "Your car here: " + CarSetups.NameOf(Tracks.All[GameSave.trackIndex].id) + "  (" + CarSetups.Summary(Tracks.All[GameSave.trackIndex].id) + ")", Big(11));
            y += 26;

            if (weatherPlan != null)
            {
                GUI.color = weatherPlan.DescribeColor();
                GUI.Label(new Rect(x, y, 420, 66), "FORECAST: " + weatherPlan.Describe(GameSave.laps), WrapStyle(12));
                GUI.color = Color.white;
                y += 70;
            }
            GUI.Label(new Rect(x, y, 70, 24), "Start", Big(13, FontStyle.Normal));
            string[] startNames = { "Standing", "Rolling", "Formation" };
            for (int i = 0; i < 3; i++)
            {
                GUI.backgroundColor = GameSave.startType == i ? ON : Color.white;
                if (GUI.Button(new Rect(x + 70 + i * 114, y, 108, 24), startNames[i])) GameSave.startType = i;
            }
            GUI.backgroundColor = Color.white;
            y += 28;
            GUI.Label(new Rect(x, y, 70, 24), "Time", Big(13, FontStyle.Normal));
            string[] todNames = { "Day", "Dusk", "Night" };
            for (int i = 0; i < 3; i++)
            {
                GUI.backgroundColor = GameSave.raceTime == i ? ON : Color.white;
                if (GUI.Button(new Rect(x + 70 + i * 114, y, 108, 24), todNames[i])) { GameSave.raceTime = i; GameSave.Save(); }
            }
            GUI.backgroundColor = Color.white;
            y += 28;
            GUI.Label(new Rect(x, y, 70, 24), "Laps", Big(13, FontStyle.Normal));
            int[] lapOptions = { 1, 3, 5 };
            for (int i = 0; i < lapOptions.Length; i++)
            {
                GUI.backgroundColor = GameSave.laps == lapOptions[i] ? ON : Color.white;
                if (GUI.Button(new Rect(x + 70 + i * 60, y, 54, 24), lapOptions[i].ToString())) GameSave.laps = lapOptions[i];
            }
            y += 28;
            GUI.backgroundColor = Color.white;
            GUI.Label(new Rect(x, y, 80, 24), "AI level", Big(13, FontStyle.Normal));
            for (int i = 0; i < 3; i++)
            {
                GUI.backgroundColor = GameSave.difficulty == i ? ON : Color.white;
                if (GUI.Button(new Rect(x + 70 + i * 90, y, 84, 24), Tuning.DifficultyNames[i])) GameSave.difficulty = i;
            }
            GUI.backgroundColor = Color.white;
            y += 34;

            y = DrawWeekendBlock(x, y);
            y = DrawStrategy(x, y);

            GUI.backgroundColor = new Color(1f, 0.35f, 0.25f);
            if (GUI.Button(new Rect(x, y, 200, 40), "QUICK RACE")) StartRace(Mode.Quick);
            GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
            if (GUI.Button(new Rect(x + 210, y, 200, 40), "QUALIFY  (Q1-Q3)")) { quali = null; StartRace(Mode.Qualifying); }
            GUI.backgroundColor = Color.white;
            y += 50;

            GUI.backgroundColor = new Color(0.35f, 0.75f, 1f);
#if UNITY_WEBGL
            GUI.enabled = false;
            GUI.Button(new Rect(x, y, 410, 32), "ONLINE MULTIPLAYER  -  desktop version only");
            GUI.enabled = true;
#else
            if (GUI.Button(new Rect(x, y, 410, 32), net.Active ? "ONLINE MULTIPLAYER  -  room " + net.code : "ONLINE MULTIPLAYER  -  host or join with a room code")) netPanel = true;
#endif
            GUI.backgroundColor = Color.white;
            y += 42;

            GUI.backgroundColor = GameSave.TutorialDone ? Color.white : new Color(0.3f, 0.9f, 0.4f);
            if (GUI.Button(new Rect(x, y, 410, 32), GameSave.TutorialDone ? "TUTORIAL  (replay)" : "TUTORIAL  -  learn the controls (recommended)")) StartRace(Mode.Tutorial);
            GUI.backgroundColor = Color.white;
            y += 42;

            y = DrawEnduranceBlock(x, y);
            y = DrawRunBlock(x, y, Run.Career, Mode.Career, "CAREER SEASON", 0);
            y = DrawRunBlock(x, y, Run.Tournament, Mode.Tournament, "TOURNAMENT", Profile.TOURNAMENT_UNLOCK_LEVEL);
            y = DrawCustomBlock(x, y);
        }

        // ---- the tyre strategy planner ----
        float DrawStrategy(float x, float y)
        {
            string id = Tracks.All[GameSave.trackIndex].id;
            Strategy.Load(id);
            int laps = GameSave.laps;
            GUI.Label(new Rect(x, y, 420, 24), "TYRE STRATEGY  -  " + laps + (laps == 1 ? " lap" : " laps") + " at " + Tracks.All[GameSave.trackIndex].name, Big(15)); y += 24;
            if (laps < 2)
            {
                GUI.Label(new Rect(x, y, 420, 24), "A one-lap race needs no stop. Pick 3 or 5 laps to plan stops.", Big(11, FontStyle.Normal));
                return y + 34f;
            }
            Strategy.Tidy(laps);
            var pl = Strategy.plan;
            bool changed = false;
            GUI.Label(new Rect(x, y, 70, 24), "Stops", Big(13, FontStyle.Normal));
            for (int i = 0; i <= 2; i++)
            {
                GUI.backgroundColor = pl.stops == i ? ON : Color.white;
                GUI.enabled = i <= laps - 1;
                if (GUI.Button(new Rect(x + 70 + i * 72, y, 68, 24), i == 0 ? "None" : i.ToString())) { pl.stops = i; changed = true; }
            }
            GUI.enabled = true; GUI.backgroundColor = Color.white;
            y += 28;
            for (int s = 0; s <= pl.stops; s++)
            {
                GUI.Label(new Rect(x, y, 70, 24), "Stint " + (s + 1), Big(13, FontStyle.Normal));
                if (GUI.Button(new Rect(x + 70, y, 90, 24), Strategy.Names[pl.compound[s]])) { pl.compound[s] = (pl.compound[s] + 1) % 3; changed = true; }
                if (s < pl.stops)
                {
                    GUI.Label(new Rect(x + 170, y, 90, 24), "box on lap", Big(12, FontStyle.Normal));
                    if (GUI.Button(new Rect(x + 262, y, 26, 24), "-")) { pl.stopLap[s]--; changed = true; }
                    GUI.Label(new Rect(x + 296, y, 30, 24), pl.stopLap[s].ToString(), Big(14));
                    if (GUI.Button(new Rect(x + 322, y, 26, 24), "+")) { pl.stopLap[s]++; changed = true; }
                }
                y += 28;
            }
            var res = Strategy.Simulate(id, laps, pl);
            GUI.Label(new Rect(x, y, 420, 22), "Predicted race time  " + Hud.FormatTime(res.total) + (pl.stops > 0 ? "   (each stop costs about " + Strategy.PIT_LOSS.ToString("0.0") + " s)" : ""), Big(12)); y += 22;
            for (int s = 0; s < res.stintAvg.Count; s++)
            {
                float end = res.stintEndWear[s];
                GUI.color = end < 10f ? new Color(1f, 0.4f, 0.3f) : end < 30f ? new Color(1f, 0.8f, 0.3f) : Color.white;
                GUI.Label(new Rect(x + 8, y, 410, 20), "Stint " + (s + 1) + "  " + Strategy.Names[pl.compound[s]] + "  " + res.stintLaps[s] + " laps  avg " + Hud.FormatTime(res.stintAvg[s]) + "  tyres end at " + Mathf.RoundToInt(end) + "%" + (end < 10f ? "  (finished!)" : ""), Big(11, FontStyle.Normal));
                GUI.color = Color.white;
                y += 20;
            }
            if (GUI.Button(new Rect(x, y + 4, 200, 26), "Find the fastest plan")) { Strategy.plan = Strategy.Suggest(id, laps); Strategy.enabled = true; changed = true; }
            bool follow = GUI.Toggle(new Rect(x + 210, y + 6, 210, 24), Strategy.enabled, "  Follow it in the race");
            if (follow != Strategy.enabled) { Strategy.enabled = follow; changed = true; }
            y += 34;
            GUI.Label(new Rect(x, y, 420, 48), "The prediction uses the lap simulation with this circuit's car and its current setup. When you follow the plan the race starts on the first tyre and the engineer asks you to box on the planned laps.", Big(10, FontStyle.Normal));
            y += 52;
            if (changed)
            {
                Strategy.Tidy(laps);
                Strategy.Save(id);
                if (Strategy.enabled) GameSave.compound = (Compound)Strategy.plan.compound[0];
            }
            return y;
        }

        float DrawWeekendBlock(float x, float y)
        {
            GUI.Label(new Rect(x, y, 420, 24), "RACE WEEKEND", Big(15)); y += 24;
            if (weekend == null)
            {
                GUI.Label(new Rect(x, y, 420, 36), "Three practice sessions, qualifying, then the race on one circuit. Tune the setup in practice - it locks when qualifying starts.", Big(11, FontStyle.Normal)); y += 38;
                GUI.backgroundColor = new Color(0.95f, 0.8f, 0.2f);
                if (GUI.Button(new Rect(x, y, 410, 32), "START RACE WEEKEND  -  " + Tracks.All[GameSave.trackIndex].name)) { weekend = new Weekend { trackIndex = GameSave.trackIndex }; StartWeekendSession(); }
                GUI.backgroundColor = Color.white;
                return y + 42;
            }
            GUI.Label(new Rect(x, y, 420, 22), Tracks.All[weekend.trackIndex].name + "  -  up next: " + weekend.StageName + (weekend.locked ? "   (parc ferme)" : ""), Big(13, FontStyle.Normal)); y += 26;
            GUI.backgroundColor = ON;
            if (GUI.Button(new Rect(x, y, 260, 32), "CONTINUE - " + weekend.StageName.ToUpper())) StartWeekendSession();
            GUI.backgroundColor = Color.white;
            if (GUI.Button(new Rect(x + 270, y, 140, 32), "Abandon")) weekend = null;
            return y + 42;
        }

        void StartWeekendSession()
        {
            if (weekend == null) return;
            GameSave.trackIndex = weekend.trackIndex;
            if (weekend.stage <= 2) { practiceDrill = 3; StartRace(Mode.Practice); }
            else if (weekend.stage == 3) { weekend.locked = true; quali = null; StartRace(Mode.Qualifying); }
            else StartRace(Mode.Quick);
        }

        float DrawEnduranceBlock(float x, float y)
        {
            GUI.Label(new Rect(x, y, 420, 24), "ENDURANCE RACE", Big(15)); y += 24;
            GUI.Label(new Rect(x, y, 420, 34), "A long race from afternoon into night: fuel burns off, the driver tires, tyres wear. Plan your stops. Pays double.", Big(11, FontStyle.Normal)); y += 38;
            GUI.Label(new Rect(x, y, 70, 24), "Laps", Big(13, FontStyle.Normal));
            for (int i = 0; i < ENDURANCE_LAPS.Length; i++)
            {
                GUI.backgroundColor = GameSave.enduranceLaps == i ? ON : Color.white;
                if (GUI.Button(new Rect(x + 70 + i * 60, y, 54, 24), ENDURANCE_LAPS[i].ToString())) GameSave.enduranceLaps = i;
            }
            GUI.backgroundColor = Color.white;
            y += 28;
            float fl = FuelLapsFor(track);
            float tyreLaps = Mathf.Max(1f, 75f / (track.roadLength * 0.015f * Tuning.TireWear[(int)GameSave.compound]));
            float stint = Mathf.Min(fl, tyreLaps);
            int laps = ENDURANCE_LAPS[Mathf.Clamp(GameSave.enduranceLaps, 0, 2)];
            int stops = Mathf.Max(0, Mathf.CeilToInt(laps / stint) - 1);
            GUI.Label(new Rect(x, y, 420, 50), "Plan: a full tank lasts about " + fl.ToString("0.0") + " laps, " + Tuning.CompoundNames[(int)GameSave.compound] + " tyres about "
                      + tyreLaps.ToString("0.0") + ". Roughly " + stops + " stop" + (stops == 1 ? "" : "s") + " (" + stint.ToString("0.0") + " laps a stint). Harder tyres mean fewer stops.", Big(11, FontStyle.Normal));
            y += 54;
            GUI.backgroundColor = new Color(0.55f, 0.4f, 1f);
            if (GUI.Button(new Rect(x, y, 410, 34), "START ENDURANCE RACE")) StartRace(Mode.Endurance);
            GUI.backgroundColor = Color.white;
            return y + 46;
        }

        float DrawCustomBlock(float x, float y)
        {
            var run = Run.Custom;
            GUI.Label(new Rect(x, y, 420, 24), "CUSTOM SEASON", Big(15)); y += 24;
            if (run.active)
            {
                string trackName = Tracks.All[TrackIndexOf(run.CurrentTrackId)].name;
                GUI.Label(new Rect(x, y, 420, 22), run.customName + "  -  round " + (run.round + 1) + "/" + run.calendar.Count + "  -  " + trackName, Big(13, FontStyle.Normal)); y += 22;
                foreach (var kv in run.Standings()) { GUI.Label(new Rect(x + 8, y, 300, 20), kv.Key + "    " + kv.Value + " pts", Big(12, FontStyle.Normal)); y += 18; }
                y += 4;
                GUI.backgroundColor = ON;
                if (GUI.Button(new Rect(x, y, 260, 32), "CONTINUE - ROUND " + (run.round + 1))) StartRace(Mode.Custom);
                GUI.backgroundColor = Color.white;
                if (GUI.Button(new Rect(x + 270, y, 140, 32), "Abandon")) run.End();
                return y + 40;
            }
            GUI.Label(new Rect(x, y, 420, 34), "Build your own championship: pick the circuits, laps, weather and AI level. It saves automatically.", Big(11, FontStyle.Normal)); y += 36;
            GUI.Label(new Rect(x, y, 60, 24), "Name", Big(13, FontStyle.Normal));
            string nm = GUI.TextField(new Rect(x + 60, y, 250, 24), run.customName, 20);
            if (nm != run.customName) { run.customName = nm; run.SaveConfig(); }
            y += 30;
            for (int i = 0; i < run.calendar.Count; i++)
            {
                GUI.Label(new Rect(x, y, 70, 24), "Round " + (i + 1), Big(13, FontStyle.Normal));
                int ti = TrackIndexOf(run.calendar[i]);
                if (GUI.Button(new Rect(x + 70, y, 250, 24), Tracks.All[ti].name)) { ti = (ti + 1) % Tracks.All.Length; run.calendar[i] = Tracks.All[ti].id; run.SaveConfig(); }
                GUI.enabled = run.calendar.Count > 1;
                if (GUI.Button(new Rect(x + 330, y, 80, 24), "Remove")) { run.calendar.RemoveAt(i); run.SaveConfig(); GUI.enabled = true; break; }
                GUI.enabled = true;
                y += 28;
            }
            GUI.enabled = run.calendar.Count < 6;
            if (GUI.Button(new Rect(x, y, 200, 24), "+ Add round")) { run.calendar.Add(Tracks.All[0].id); run.SaveConfig(); }
            GUI.enabled = true;
            y += 32;
            GUI.Label(new Rect(x, y, 70, 24), "Laps", Big(13, FontStyle.Normal));
            int[] lapOpts = { 1, 3, 5 };
            for (int i = 0; i < lapOpts.Length; i++)
            {
                GUI.backgroundColor = run.customLaps == lapOpts[i] ? ON : Color.white;
                if (GUI.Button(new Rect(x + 70 + i * 60, y, 54, 24), lapOpts[i].ToString())) { run.customLaps = lapOpts[i]; run.SaveConfig(); }
            }
            y += 28;
            GUI.backgroundColor = Color.white;
            GUI.Label(new Rect(x, y, 70, 24), "Weather", Big(13, FontStyle.Normal));
            string[] wx = { "Dry", "Mixed", "Wet" };
            for (int i = 0; i < 3; i++)
            {
                GUI.backgroundColor = run.customWeather == i ? ON : Color.white;
                if (GUI.Button(new Rect(x + 70 + i * 90, y, 84, 24), wx[i])) { run.customWeather = i; run.SaveConfig(); }
            }
            y += 28;
            GUI.backgroundColor = Color.white;
            GUI.Label(new Rect(x, y, 70, 24), "AI level", Big(13, FontStyle.Normal));
            for (int i = 0; i < 3; i++)
            {
                GUI.backgroundColor = run.customDiff == i ? ON : Color.white;
                if (GUI.Button(new Rect(x + 70 + i * 90, y, 84, 24), Tuning.DifficultyNames[i])) { run.customDiff = i; run.SaveConfig(); }
            }
            y += 34;
            GUI.backgroundColor = new Color(1f, 0.6f, 0.2f);
            if (GUI.Button(new Rect(x, y, 410, 34), "START  \"" + run.customName + "\"  (" + run.calendar.Count + " rounds)")) { run.Begin(); StartRace(Mode.Custom); }
            GUI.backgroundColor = Color.white;
            return y + 46;
        }

        float DrawRunBlock(float x, float y, Run run, Mode m, string title, int unlockLevel)
        {
            GUI.Label(new Rect(x, y, 420, 24), title, Big(15)); y += 24;
            if (Profile.level < unlockLevel)
            {
                GUI.Label(new Rect(x, y, 420, 40), "Locked - reach driver level " + unlockLevel + " (you are level " + Profile.level + ").\nTougher AI, 3 back-to-back races, $" + Profile.TOURNAMENT_BONUS + " for the win.", Big(12, FontStyle.Normal));
                return y + 48;
            }
            if (!run.active)
            {
                GUI.Label(new Rect(x, y, 420, 22), "3 rounds: Harborline, Desert, Sprint. Points 25-18-15-12-10-8.", Big(11, FontStyle.Normal)); y += 22;
                if (GUI.Button(new Rect(x, y, 410, 32), "START " + title)) { run.Begin(); if (m == Mode.Career) TeamCareer.BeginSeason(); StartRace(m); }
                return y + 40;
            }
            string trackName = Tracks.All[TrackIndexOf(run.CurrentTrackId)].name;
            GUI.Label(new Rect(x, y, 420, 22), "Round " + (run.round + 1) + "/3 - " + trackName, Big(13, FontStyle.Normal)); y += 22;
            foreach (var kv in run.Standings()) { GUI.Label(new Rect(x + 8, y, 300, 20), kv.Key + "    " + kv.Value + " pts", Big(12, FontStyle.Normal)); y += 18; }
            y += 4;
            GUI.backgroundColor = ON;
            if (GUI.Button(new Rect(x, y, 260, 32), "CONTINUE - ROUND " + (run.round + 1))) StartRace(m);
            GUI.backgroundColor = Color.white;
            if (GUI.Button(new Rect(x + 270, y, 140, 32), "Abandon")) run.End();
            return y + 40;
        }

        void DrawRacer(float x)
        {
            float y = 0;
            GUI.Label(new Rect(x, y, 420, 24), "YOUR RACER", Big(15)); y += 26;
            GUI.Label(new Rect(x, y, 60, 24), "Name", Big(13, FontStyle.Normal));
            string nm = GUI.TextField(new Rect(x + 60, y, 250, 24), RacerProfile.name, RacerProfile.MAX_NAME);
            if (nm != RacerProfile.name) { RacerProfile.SetName(nm); RacerProfile.Save(); }
            if (nm.Length == 0) GUI.Label(new Rect(x + 316, y, 100, 24), "(shows as You)", Big(10, FontStyle.Normal));
            y += 30;
            GUI.Label(new Rect(x, y, 420, 22), RacerProfile.DisplayName + "  -  " + RacerProfile.Rank + "  (racer level " + RacerProfile.RacerLevel + ")", Big(14)); y += 28;
            for (int i = 0; i < 3; i++)
            {
                int lv = RacerProfile.level[i];
                GUI.Label(new Rect(x, y, 200, 22), RacerProfile.StatNames[i] + "   L" + lv + "/" + RacerProfile.MAX_LEVEL, Big(14, FontStyle.Normal));
                float need = RacerProfile.XpForNext(lv);
                GUI.color = new Color(0.1f, 0.12f, 0.16f); GUI.DrawTexture(new Rect(x + 200, y + 6, 210, 10), Texture2D.whiteTexture);
                GUI.color = ON; GUI.DrawTexture(new Rect(x + 200, y + 6, 210f * (lv >= RacerProfile.MAX_LEVEL ? 1f : Mathf.Clamp01(RacerProfile.xp[i] / need)), 10), Texture2D.whiteTexture);
                GUI.color = Color.white;
                y += 22;
                GUI.Label(new Rect(x, y, 420, 34), RacerProfile.StatDesc[i], Big(11, FontStyle.Normal)); y += 34;
            }
            GUI.Label(new Rect(x, y, 420, 22), "ENERGY  " + RacerProfile.energy + " / " + RacerProfile.MAX_ENERGY, Big(14)); y += 20;
            GUI.Label(new Rect(x, y, 420, 34), "Each gym exercise costs 1. Finishing a race refills 2, a practice session 1.", Big(11, FontStyle.Normal)); y += 40;

            GUI.Label(new Rect(x, y, 420, 24), "APPEARANCE", Big(15)); y += 26;
            GUI.Label(new Rect(x, y, 70, 24), "Number", Big(13, FontStyle.Normal));
            if (GUI.Button(new Rect(x + 70, y, 30, 24), "-")) { RacerProfile.number = RacerProfile.number <= 1 ? 99 : RacerProfile.number - 1; RacerProfile.Save(); }
            GUI.Label(new Rect(x + 108, y, 60, 24), "#" + RacerProfile.number, Big(15));
            if (GUI.Button(new Rect(x + 170, y, 30, 24), "+")) { RacerProfile.number = RacerProfile.number >= 99 ? 1 : RacerProfile.number + 1; RacerProfile.Save(); }
            y += 28;
            int helmBefore = RacerProfile.helmet;
            y = Swatches(x, y, "Helmet", ref RacerProfile.helmet);
            if (helmBefore != RacerProfile.helmet) RacerProfile.Save();
            GUI.Label(new Rect(x, y, 70, 24), "Nation", Big(13, FontStyle.Normal));
            if (GUI.Button(new Rect(x + 70, y, 80, 24), RacerProfile.NationCode)) { RacerProfile.nation = (RacerProfile.nation + 1) % RacerProfile.NationCodes.Length; RacerProfile.Save(); }
            var flag = RacerProfile.NationColors[((RacerProfile.nation % RacerProfile.NationColors.Length) + RacerProfile.NationColors.Length) % RacerProfile.NationColors.Length];
            for (int fi = 0; fi < 3; fi++)
            {
                GUI.color = flag[fi];
                GUI.DrawTexture(new Rect(x + 164 + fi * 18, y, 18, 24), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
            y += 36;

            GUI.Label(new Rect(x, y, 420, 24), "TRAINING CALENDAR", Big(15)); y += 26;
            int since = RacerProfile.DaysSinceTraining;
            string form = since <= 4 ? "in form" : since <= 10 ? "OK" : "rusty - Fitness will fade after the next race";
            GUI.Label(new Rect(x, y, 420, 22), "Day " + RacerProfile.day + "  -  last trained " + since + " day" + (since == 1 ? "" : "s") + " ago  (" + form + ")", Big(12, FontStyle.Normal)); y += 22;
            GUI.Label(new Rect(x, y, 420, 34), "A race takes a week. Go more than 10 days without a gym session or practice and your Fitness starts to fade.", Big(11, FontStyle.Normal)); y += 38;
            if (GUI.Button(new Rect(x, y, 200, 28), "REST DAY  (+1 energy)")) RacerProfile.Rest();
            y += 38;

            GUI.Label(new Rect(x, y, 420, 24), "GYM EXERCISES", Big(15)); y += 26;
            string[] ex = { "Cardio sprint  -  trains Fitness", "Reaction lights  -  trains Reflexes", "Focus drill  -  trains Focus" };
            GUI.enabled = RacerProfile.energy > 0;
            for (int i = 0; i < 3; i++)
            {
                if (GUI.Button(new Rect(x, y, 410, 30), ex[i])) gym.Open(i);
                y += 34;
            }
            GUI.enabled = true;
            y += 6;

            GUI.Label(new Rect(x, y, 420, 24), "PRACTICE TRACKS", Big(15)); y += 26;
            GUI.Label(new Rect(x, y, 420, 34), "Free practice, no rivals, dry. Circuit: " + Tracks.All[GameSave.trackIndex].name + " (change it on the Race tab).", Big(11, FontStyle.Normal)); y += 38;
            string[] dr = { "Smooth laps (3)  -  trains Focus", "Fast laps (3)  -  trains Reflexes", "Long run (5)  -  trains Fitness" };
            for (int i = 0; i < 3; i++)
            {
                GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
                if (GUI.Button(new Rect(x, y, 410, 30), dr[i])) { practiceDrill = i; StartRace(Mode.Practice); }
                y += 34;
            }
            GUI.backgroundColor = Color.white;
            y += 4;
            GUI.Label(new Rect(x, y, 420, 24), "CHALLENGES  -  medals at " + Tracks.All[GameSave.trackIndex].name, Big(15)); y += 26;
            string[] chTxt = { "Perfect lap  -  a quick, clean lap", "Braking test  -  stop on the line", "Tyre saver  -  three laps, least wear" };
            string cid = Tracks.All[GameSave.trackIndex].id;
            for (int i = 0; i < 3; i++)
            {
                int med = Challenges.Best(cid, i);
                GUI.backgroundColor = new Color(1f, 0.82f, 0.3f);
                if (GUI.Button(new Rect(x, y, 310, 30), chTxt[i])) { practiceDrill = Challenges.FIRST_DRILL + i; StartRace(Mode.Practice); }
                GUI.backgroundColor = Color.white;
                GUI.color = Challenges.MedalColor(med);
                GUI.Label(new Rect(x + 318, y + 3, 100, 24), Challenges.MedalNames[med], Big(13));
                GUI.color = Color.white;
                y += 34;
            }
        }

        void DrawTeam(float x)
        {
            float y = 0;
            var t = TeamCareer.Teams[TeamCareer.team];
            GUI.Label(new Rect(x, y, 420, 24), "YOUR TEAM", Big(15)); y += 26;
            GUI.color = t.color; GUI.DrawTexture(new Rect(x, y + 2, 14, 20), Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(new Rect(x + 22, y, 300, 24), t.name, Big(16)); y += 28;
            GUI.Label(new Rect(x, y, 130, 22), "Car rating", Big(12, FontStyle.Normal));
            GUI.color = new Color(0.1f, 0.12f, 0.16f); GUI.DrawTexture(new Rect(x + 130, y + 6, 200, 10), Texture2D.whiteTexture);
            GUI.color = ON; GUI.DrawTexture(new Rect(x + 130, y + 6, 200f * TeamCareer.PlayerRating, 10), Texture2D.whiteTexture); GUI.color = Color.white;
            y += 24;
            GUI.Label(new Rect(x, y, 420, 22), "Contract: $" + TeamCareer.salary + " per career race, " + Mathf.Max(0, TeamCareer.seasonsLeft) + " season" + (TeamCareer.seasonsLeft == 1 ? "" : "s") + " left   (season " + TeamCareer.season + ")", Big(12, FontStyle.Normal)); y += 22;
            GUI.Label(new Rect(x, y, 420, 22), "REGULATIONS:  " + Regs.Heading, Big(12)); y += 20;
            GUI.Label(new Rect(x, y, 420, 48), Regs.Current.text + "  (changes in " + Regs.SeasonsUntilChange + " season" + (Regs.SeasonsUntilChange == 1 ? "" : "s") + ")", Big(11, FontStyle.Normal)); y += 50;
            GUI.Label(new Rect(x, y, 420, 34), "Objective (bonus $" + TeamCareer.objectiveReward + "): " + TeamCareer.ObjectiveText[TeamCareer.objective] + ". Wins so far this season: " + TeamCareer.seasonWins + ".", Big(12, FontStyle.Normal)); y += 40;
            GUI.Label(new Rect(x, y, 130, 22), "Team rapport", Big(12, FontStyle.Normal));
            GUI.color = new Color(0.1f, 0.12f, 0.16f); GUI.DrawTexture(new Rect(x + 130, y + 6, 200, 10), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.8f, 0.3f); GUI.DrawTexture(new Rect(x + 130, y + 6, 200f * TeamCareer.rapport / 100f, 10), Texture2D.whiteTexture); GUI.color = Color.white;
            y += 28;
            GUI.Label(new Rect(x, y, 420, 40), "Your teammate is " + Personalities.Drivers[0] + ". Let him through when the pit wall asks and rapport rises; ignore the orders and it falls.", Big(11, FontStyle.Normal)); y += 46;

            GUI.Label(new Rect(x, y, 420, 24), "R&D  -  " + TeamCareer.rdPoints + " points", Big(15)); y += 24;
            GUI.Label(new Rect(x, y, 420, 34), "Points come from finishing races and clean practice laps. Development belongs to the team you are with.", Big(11, FontStyle.Normal)); y += 38;
            for (int b = 0; b < 3; b++)
            {
                int lvl = TeamCareer.RdLevel(b);
                GUI.Label(new Rect(x, y, 200, 22), TeamCareer.BranchNames[b] + "   L" + lvl + "/3", Big(13, FontStyle.Normal));
                GUI.enabled = TeamCareer.CanDevelop(b);
                if (GUI.Button(new Rect(x + 220, y, 190, 24), lvl >= 3 ? "MAX" : "Develop  (" + TeamCareer.RdCost[lvl] + " pts)")) TeamCareer.Develop(b);
                GUI.enabled = true;
                y += 24;
                GUI.Label(new Rect(x + 10, y, 400, 18), TeamCareer.BranchEffect[b], Big(10, FontStyle.Normal)); y += 24;
            }
            y += 6;

            GUI.Label(new Rect(x, y, 420, 24), "THE OTHER TEAMS", Big(15)); y += 24;
            for (int i = 0; i < TeamCareer.Teams.Length; i++)
            {
                if (i == TeamCareer.team) continue;
                var o = TeamCareer.Teams[i];
                GUI.color = o.color; GUI.DrawTexture(new Rect(x, y + 3, 10, 16), Texture2D.whiteTexture); GUI.color = Color.white;
                GUI.Label(new Rect(x + 16, y, 280, 22), o.name + "   rating " + Mathf.RoundToInt(TeamCareer.Rating(i) * 100f), Big(12, FontStyle.Normal));
                y += 22;
            }
            GUI.Label(new Rect(x, y, 420, 34), "The rival teams keep developing their cars every season - the strong ones fastest.", Big(11, FontStyle.Normal)); y += 40;

            GUI.Label(new Rect(x, y, 420, 24), "CONTRACT OFFERS", Big(15)); y += 24;
            if (TeamCareer.offers.Count == 0)
            {
                GUI.Label(new Rect(x, y, 420, 34), "None right now. Finish a career season well (top 2) or reach the end of your contract and the other teams will make offers.", Big(11, FontStyle.Normal));
                y += 40;
            }
            else
            {
                for (int i = 0; i < TeamCareer.offers.Count; i++)
                {
                    var of = TeamCareer.offers[i];
                    var ot = TeamCareer.Teams[of.team];
                    GUI.color = ot.color; GUI.DrawTexture(new Rect(x, y + 3, 10, 16), Texture2D.whiteTexture); GUI.color = Color.white;
                    GUI.Label(new Rect(x + 16, y, 400, 22), ot.name + "  -  $" + of.salary + " per race, " + of.seasons + " seasons, rating " + Mathf.RoundToInt(TeamCareer.Rating(of.team) * 100f), Big(12));
                    y += 22;
                    GUI.Label(new Rect(x + 16, y, 400, 34), "Objective: " + TeamCareer.ObjectiveText[of.objective], Big(11, FontStyle.Normal));
                    y += 30;
                    if (GUI.Button(new Rect(x + 16, y, 150, 26), "ACCEPT")) { TeamCareer.Accept(of); break; }
                    y += 34;
                }
                if (GUI.Button(new Rect(x, y, 200, 26), "Decline all (stay)")) TeamCareer.Decline();
                y += 34;
            }
        }

        void DrawCrew(float x)
        {
            float y = 0;
            GUI.Label(new Rect(x, y, 420, 24), "STAFF", Big(15)); y += 26;
            GUI.Label(new Rect(x, y, 420, 34), "Hire better people with your prize money. Each role has three levels.", Big(11, FontStyle.Normal)); y += 34;
            for (int i = 0; i < 3; i++)
            {
                int lv = Staff.level[i];
                GUI.Label(new Rect(x, y, 250, 22), Staff.Names[i] + "   L" + lv + "/3", Big(13));
                GUI.enabled = Staff.CanBuy(i);
                if (GUI.Button(new Rect(x + 250, y, 160, 24), lv >= 3 ? "MAX" : "Hire  $" + Staff.Cost[lv])) Staff.Buy(i);
                GUI.enabled = true;
                y += 24;
                GUI.Label(new Rect(x + 8, y, 400, 52), Staff.Effects[i], Big(10, FontStyle.Normal)); y += 54;
            }
            y += 8;
            GUI.Label(new Rect(x, y, 420, 24), "SPONSORS", Big(15)); y += 26;
            GUI.Label(new Rect(x, y, 420, 34), "Choose one sponsor. Meet its goal in a race and it pays. Every two successes the deal grows, and every level makes the parts shop 3% cheaper (now " + Mathf.RoundToInt(Sponsors.PartsDiscount * 100f) + "%).", Big(11, FontStyle.Normal)); y += 40;
            for (int i = 0; i < Sponsors.All.Length; i++)
            {
                var sp = Sponsors.All[i];
                GUI.Label(new Rect(x, y, 260, 22), sp.name + "   deal L" + Sponsors.Level(i) + "   pays $" + Sponsors.Pay(i), Big(13));
                GUI.backgroundColor = Sponsors.chosen == i ? ON : Color.white;
                if (GUI.Button(new Rect(x + 300, y, 110, 24), Sponsors.chosen == i ? "ACTIVE" : "Choose")) { Sponsors.chosen = Sponsors.chosen == i ? -1 : i; Sponsors.Save(); }
                GUI.backgroundColor = Color.white;
                y += 24;
                GUI.Label(new Rect(x + 8, y, 400, 34), sp.goal + ".   Successes: " + Sponsors.successes[i], Big(10, FontStyle.Normal)); y += 36;
            }
            DrawCrewExtras(x, y + 10f);
        }

        void DrawCrewExtras(float x, float y)
        {
            GUI.Label(new Rect(x, y, 420, 24), "FACILITIES", Big(15)); y += 26;
            GUI.Label(new Rect(x, y, 420, 34), "Invest in the team's buildings over the seasons. Each has three levels.", Big(11, FontStyle.Normal)); y += 30;
            for (int i = 0; i < 3; i++)
            {
                int lv = Facilities.level[i];
                GUI.Label(new Rect(x, y, 250, 22), Facilities.Names[i] + "   L" + lv + "/3", Big(13));
                GUI.enabled = Facilities.CanBuy(i);
                if (GUI.Button(new Rect(x + 250, y, 160, 24), lv >= 3 ? "MAX" : "Build  $" + Facilities.Cost[lv])) Facilities.Buy(i);
                GUI.enabled = true;
                y += 24;
                GUI.Label(new Rect(x + 8, y, 400, 40), Facilities.Effects[i], Big(10, FontStyle.Normal)); y += 42;
            }
            y += 8;
            GUI.Label(new Rect(x, y, 420, 24), "TEST DATA", Big(15)); y += 26;
            GUI.Label(new Rect(x, y, 420, 34), "Overheating, gearbox failures and heavy impacts teach the engineers. Enough data unlocks a fix.", Big(11, FontStyle.Normal)); y += 34;
            for (int i = 0; i < 3; i++)
            {
                int lv = TestData.Level(i);
                float next = lv >= 3 ? TestData.Thresholds[2] : TestData.Thresholds[lv];
                GUI.Label(new Rect(x, y, 300, 22), TestData.Names[i] + "   fix L" + lv + "/3   data " + Mathf.RoundToInt(TestData.points[i]) + (lv >= 3 ? "" : " / " + Mathf.RoundToInt(next)), Big(13));
                y += 22;
                GUI.color = new Color(0.1f, 0.12f, 0.16f); GUI.DrawTexture(new Rect(x + 8, y, 300, 8), Texture2D.whiteTexture);
                GUI.color = ON; GUI.DrawTexture(new Rect(x + 8, y, 300f * Mathf.Clamp01(TestData.points[i] / TestData.Thresholds[2]), 8), Texture2D.whiteTexture); GUI.color = Color.white;
                y += 12;
                GUI.Label(new Rect(x + 8, y, 400, 34), TestData.FixText[i], Big(10, FontStyle.Normal)); y += 36;
            }
        }

        void DrawProfile(float x)
        {
            float y = 0;
            GUI.Label(new Rect(x, y, 420, 24), "DRIVER LEVEL " + Profile.level, Big(15)); y += 26;
            float need = Profile.XpForNext(Profile.level);
            GUI.color = new Color(0.1f, 0.12f, 0.16f); GUI.DrawTexture(new Rect(x, y, 410, 12), Texture2D.whiteTexture);
            GUI.color = ON; GUI.DrawTexture(new Rect(x, y, 410 * Mathf.Clamp01(Profile.xp / need), 12), Texture2D.whiteTexture);
            GUI.color = Color.white;
            y += 16;
            GUI.Label(new Rect(x, y, 420, 40), Profile.xp + " / " + (int)need + " XP.  Every finish trains your driver: steering aid +" + Mathf.RoundToInt((Profile.TrainingBonus() - 1f) * 100f) + "% (max 15%).", Big(11, FontStyle.Normal));
            y += 46;

            GUI.Label(new Rect(x, y, 420, 24), "ACHIEVEMENTS", Big(15)); y += 26;
            for (int i = 0; i < Profile.AchIds.Length; i++)
            {
                bool got = Profile.Has(Profile.AchIds[i]);
                GUI.color = got ? new Color(0.24f, 0.86f, 0.52f) : new Color(0.6f, 0.62f, 0.68f);
                GUI.Label(new Rect(x, y, 420, 22), (got ? "[x] " : "[ ] ") + Profile.AchNames[i], Big(13));
                GUI.color = Color.white;
                GUI.Label(new Rect(x + 24, y + 18, 400, 18), Profile.AchDesc[i], Big(11, FontStyle.Normal));
                y += 40;
            }
            y = Slider(x, y + 4, "Volume", ref GameSave.volume, 0, 1);
            GameSave.voices = GUI.Toggle(new Rect(x, y + 6, 410, 24), GameSave.voices, "  Spoken radio and replay commentary");
            y += 30;
            GameSave.advHandling = GUI.Toggle(new Rect(x, y + 6, 410, 24), GameSave.advHandling, "  Weight transfer: understeer, oversteer, lift-off slides");
            y += 26;
            GameSave.manualGears = GUI.Toggle(new Rect(x, y + 6, 410, 24), GameSave.manualGears, "  Manual gearbox (. up, , down; G in a race)");
            y += 26;
            GameSave.spotter = GUI.Toggle(new Rect(x, y + 6, 410, 24), GameSave.spotter, "  Spotter calls for cars alongside");
            y += 26;
            GameSave.rewinds = GUI.Toggle(new Rect(x, y + 6, 410, 24), GameSave.rewinds, "  Three rewinds per race to undo a mistake (Backspace)");
            y += 30;
        }

        float WearRow(float x, float y, string label, ref float wear, int cost)
        {
            GUI.Label(new Rect(x, y, 130, 24), label + "  " + Mathf.RoundToInt(wear) + "%", Big(13, FontStyle.Normal));
            GUI.color = new Color(0.1f, 0.12f, 0.16f); GUI.DrawTexture(new Rect(x + 130, y + 7, 120, 10), Texture2D.whiteTexture);
            GUI.color = wear < 50f ? new Color(0.3f, 0.85f, 0.45f) : wear < 80f ? new Color(1f, 0.75f, 0.2f) : new Color(1f, 0.3f, 0.25f);
            GUI.DrawTexture(new Rect(x + 130, y + 7, 120f * Mathf.Clamp01(wear / 100f), 10), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.enabled = GameSave.money >= cost && wear > 1f;
            if (GUI.Button(new Rect(x + 262, y, 148, 24), "Replace  $" + cost))
            {
                GameSave.money -= cost; wear = 0f; GameSave.partsUsed++;
                if (GameSave.partsUsed > 2) GameSave.gridPenalty += 3;
                GameSave.Save();
            }
            GUI.enabled = true;
            return y + 28;
        }

        float Upgrade(float x, float y, string label, ref int level)
        {
            int cost = Mathf.RoundToInt(280 * level * Mathf.Max(0.3f, 1f - Sponsors.PartsDiscount - Facilities.PartsDiscount));          // the sponsors' parts deals and the factory
            GUI.Label(new Rect(x, y, 250, 24), label + "   L" + level + "/10", Big(14, FontStyle.Normal));
            GUI.enabled = level < 10 && GameSave.money >= cost;
            if (GUI.Button(new Rect(x + 270, y, 140, 24), level >= 10 ? "MAX" : "Upgrade  $" + cost))
            {
                GameSave.money -= cost; level++; GameSave.Save();
            }
            GUI.enabled = true;
            return y + 28;
        }

        float Swatches(float x, float y, string label, ref int sel)
        {
            GUI.Label(new Rect(x, y, 60, 24), label, Big(13, FontStyle.Normal));
            for (int i = 0; i < Livery.Colors.Length; i++)
            {
                GUI.backgroundColor = Livery.Colors[i];
                if (GUI.Button(new Rect(x + 70 + i * 42, y, 38, 24), sel == i ? "X" : "")) sel = i;
            }
            GUI.backgroundColor = Color.white;
            return y + 28;
        }

        float Slider(float x, float y, string label, ref float v, float lo, float hi)
        {
            GUI.Label(new Rect(x, y, 170, 22), label + " " + (hi <= 1f ? Mathf.RoundToInt(v * 100f) : Mathf.RoundToInt(v)), Big(13, FontStyle.Normal));
            v = GUI.HorizontalSlider(new Rect(x + 170, y + 6, 240, 18), v, lo, hi);
            return y + 26;
        }

        // standing-start rev meter, radio calls, damage panel and the red-flag screen
        // ---- the engine and gearbox temperatures ----
        void DrawHealth(float W, float H)
        {
            if (player == null || player.health == null || mode == Mode.Tutorial) return;
            float x = W - 232f, y = H - 168f;
            Bar(x, y, "ENGINE", player.health.engine, 125f, 82f, 100f);
            Bar(x, y + 20f, player.health.gearStuck ? "GEARBOX  STUCK" : "GEARBOX", player.health.gearbox, 125f, 85f, 100f);
        }

        void Bar(float x, float y, string label, float v, float max, float warn, float crit)
        {
            Color c = v > crit ? new Color(1f, 0.25f, 0.2f) : v > warn ? new Color(1f, 0.75f, 0.2f) : new Color(0.3f, 0.85f, 0.55f);
            GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(new Rect(x - 4f, y - 1f, 188f, 19f), Texture2D.whiteTexture);
            GUI.color = c; GUI.Label(new Rect(x, y - 1f, 110f, 18f), label, Big(10));
            GUI.color = new Color(1f, 1f, 1f, 0.15f); GUI.DrawTexture(new Rect(x + 62f, y + 5f, 116f, 7f), Texture2D.whiteTexture);
            GUI.color = c; GUI.DrawTexture(new Rect(x + 62f, y + 5f, 116f * Mathf.Clamp01(v / max), 7f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ---- rewind: undo a mistake ----
        void DoRewind()
        {
            if (rewind.left <= 0 && !rewind.CanRewind(this, player)) { Flash(GameSave.rewinds && mode != Mode.Tutorial && mode != Mode.Practice ? "NO REWINDS LEFT" : "REWIND IS OFF HERE"); return; }
            float undone = rewind.Rewind(this, player, clock);
            if (undone <= 0f) { Flash("CANNOT REWIND NOW"); return; }
            redFlagLost += undone;                                   // the undone time does not count against the race clock
            Flash("REWIND  (" + rewind.left + " left)");
        }

        // ---- the leaderboard: a live timing tower at the top right ----
        static readonly string[] BoardTyre = { "S", "M", "H", "I", "W" };
        static readonly Color[] BoardTyreColor =
        {
            new Color(1f, 0.25f, 0.2f), new Color(1f, 0.85f, 0.15f), new Color(0.93f, 0.93f, 0.96f), new Color(0.25f, 0.85f, 0.4f), new Color(0.3f, 0.55f, 1f)
        };

        void DrawLeaderboard(float W, float H)
        {
            if (!showBoard || showWall || !IsRace || racers.Count < 2 || mode == Mode.Tutorial || player == null) return;
            if (Time.time > boardNext)
            {
                boardNext = Time.time + 0.35f;
                boardOrder = new List<Racer>(racers);
                boardOrder.RemoveAll(r => r.gone);
                boardOrder.Sort((a, b) =>
                {
                    bool fa = a.finishTime >= 0f, fb = b.finishTime >= 0f;
                    if (fa && fb) return a.finishTime.CompareTo(b.finishTime);
                    if (fa != fb) return fa ? -1 : 1;
                    if (a.dnf != b.dnf) return a.dnf ? 1 : -1;
                    return b.dist.CompareTo(a.dist);
                });
            }
            if (boardOrder.Count == 0) return;
            const float rowH = 19f, w = 226f;
            int shown = Mathf.Min(10, boardOrder.Count);
            float x = W - w - 14f, y = 84f;
            float h = 24f + shown * rowH + 6f;
            GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = new Color(0.2f, 0.9f, 0.8f); GUI.DrawTexture(new Rect(x, y, 3f, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 8f, y + 2f, w - 10f, 20f), "LEADERBOARD   lap " + Mathf.Clamp(boardOrder[0].lap + 1, 1, raceLaps) + "/" + raceLaps + "   [L]", Big(11));
            var lead = boardOrder[0];
            int playerIdx = boardOrder.IndexOf(player);
            for (int i = 0; i < shown; i++)
            {
                int idx = i;
                if (i == shown - 1 && playerIdx >= shown) idx = playerIdx;          // keep your own place on the board
                var r = boardOrder[idx];
                float ry = y + 24f + i * rowH;
                if (r == player) { GUI.color = new Color(0.2f, 0.9f, 0.8f, 0.28f); GUI.DrawTexture(new Rect(x + 4f, ry, w - 8f, rowH - 1f), Texture2D.whiteTexture); }
                GUI.color = r.dnf ? new Color(0.6f, 0.45f, 0.45f) : Color.white;
                string name = NameOf(r); if (name.Length > 13) name = name.Substring(0, 13);
                GUI.Label(new Rect(x + 8f, ry, 24f, rowH), (idx + 1).ToString(), Big(11, FontStyle.Normal));
                GUI.Label(new Rect(x + 32f, ry, 110f, rowH), name, Big(11, r == player ? FontStyle.Bold : FontStyle.Normal));
                string gap = r.dnf ? "OUT" : r.finishTime >= 0f && r != lead ? "FIN" : r == lead ? (r.finishTime >= 0f ? "FIN" : "") : "+" + (Mathf.Max(0f, lead.dist - r.dist) / Mathf.Max(30f, r.speed)).ToString("0.0");
                GUI.Label(new Rect(x + 128f, ry, 62f, rowH), gap, Big(11, FontStyle.Normal));
                int ci = Mathf.Clamp((int)r.compound, 0, 4);
                GUI.color = BoardTyreColor[ci];
                GUI.Label(new Rect(x + 200f, ry, 20f, rowH), BoardTyre[ci], Big(11));
                GUI.color = Color.white;
            }
        }

        void DrawRaceOverlays(float W, float H)
        {
            if (state == State.Countdown && introActive)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(new Rect(0f, H - 120f, W, 120f), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(40f, H - 112f, 600f, 40f), "GRID WALK", Big(30));
                GUI.Label(new Rect(40f, H - 66f, W - 80f, 30f), introCaption, Big(20, FontStyle.Normal));
                GUI.Label(new Rect(W - 330f, H - 112f, 300f, 24f), "Formation lap next", Big(13, FontStyle.Normal));
                return;
            }
            if (state != State.Racing) return;
            Broadcast.DrawSunset(cam, timeOfDay, isRaining || isFoggy, W, H);
            if (Time.time < rewind.flashUntil)
            {
                GUI.color = new Color(0.55f, 0.8f, 1f, Mathf.Clamp01((rewind.flashUntil - Time.time) / 0.7f) * 0.45f);
                GUI.DrawTexture(new Rect(0f, 0f, W, H), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            if (rewind.left > 0 || GameSave.rewinds) { if (mode != Mode.Tutorial && mode != Mode.Practice && GameSave.rewinds) GUI.Label(new Rect(16f, H - 292f, 260f, 22f), "REWINDS  " + rewind.left + "   [Backspace]", Big(12)); }
            DrawLeaderboard(W, H);
            if (mode == Mode.Multiplayer) DrawOnlineBadge(W, H);
            DrawHealth(W, H);
            bcast.Draw(W, H);
            if (mode != Mode.Tutorial) bcast.DrawRadar(this, W, H);
            if (mode != Mode.Tutorial && wind != null) bcast.DrawWind(wind, player, clock, W, H);
            calls.Draw(this, W, H);
            if (DamageUi.Needed(player)) DamageUi.Draw(24f, H - 352f, player);
            DrawGlare(W, H);
            if (firstPerson) visor.Draw(W, H);
            if (camMode == CamMode.Helmet) DrawHelmetFrame(W, H);
            if (mode == Mode.Endurance) DrawEnduranceOverlay(W, H);
            if (mode == Mode.Practice) DrawPracticeOverlay(W, H);
            if (redFlag && redPhase == 2) DrawRedFlag(W, H);
        }

        // a low sun straight ahead dazzles you: a bright wash over the screen plus a glow where the sun is
        void DrawGlare(float W, float H)
        {
            GlareLevel = 0f;
            if (sun == null || cam == null || mode == Mode.Tutorial) return;
            float night = Mathf.SmoothStep(0f, 1f, timeOfDay);
            float elev = Mathf.Lerp(sunElev, -6f, timeOfDay);
            if (elev < 2f) return;
            Vector3 toSun = -sun.transform.forward;
            float facing = Mathf.Clamp01(Vector3.Dot(cam.transform.forward, toSun));
            float low = Mathf.Clamp01((55f - elev) / 45f);
            float k = Mathf.Pow(facing, 4f) * (0.25f + 0.75f * low) * (1f - night) * (isRaining || isFoggy ? 0.25f : 1f);
            // trees along the track flicker the low sun as you drive past, and your eyes take a moment to recover from it
            if (elev < 32f && player != null && player.speed > 20f) k *= 1f - 0.18f * Mathf.PerlinNoise(player.dist * 0.16f, 3.3f);
            k *= 1f + 0.6f * visor.Dirt;                                       // a dirty or misted visor scatters the light
            k = visor.SmoothGlare(k, Time.deltaTime);
            GlareLevel = k;
            if (k < 0.01f) return;
            GUI.color = new Color(1f, 0.96f, 0.82f, Mathf.Clamp01(k * 0.7f));
            GUI.DrawTexture(new Rect(0f, 0f, W, H), Texture2D.whiteTexture);
            Vector3 sp = cam.WorldToScreenPoint(cam.transform.position + toSun * 200f);
            if (sp.z > 0f)
            {
                float s = Screen.height / 720f;
                float gx = sp.x / s, gy = (Screen.height - sp.y) / s, gs = 900f * Mathf.Clamp01(k * 1.3f);
                GUI.color = new Color(1f, 0.97f, 0.88f, Mathf.Clamp01(k));
                GUI.DrawTexture(new Rect(gx - gs * 0.5f, gy - gs * 0.5f, gs, gs), Effects.Soft());
                // lens-flare ghosts along the line from the sun through the middle of the screen
                Vector2 centre = new Vector2(W * 0.5f, H * 0.5f), sunPos = new Vector2(gx, gy);
                float[] ghostAt = { 0.45f, 0.75f, 1.35f, 1.75f }, ghostSize = { 60f, 110f, 80f, 150f };
                for (int gi = 0; gi < ghostAt.Length; gi++)
                {
                    Vector2 q = Vector2.Lerp(sunPos, centre, ghostAt[gi]);
                    float ga = Mathf.Clamp01(k) * (gi % 2 == 0 ? 0.28f : 0.16f);
                    GUI.color = gi % 2 == 0 ? new Color(1f, 0.8f, 0.5f, ga) : new Color(0.6f, 0.85f, 1f, ga);
                    GUI.DrawTexture(new Rect(q.x - ghostSize[gi] * 0.5f, q.y - ghostSize[gi] * 0.5f, ghostSize[gi], ghostSize[gi]), Effects.Soft());
                }
            }
            GUI.color = Color.white;
        }

        void DrawEnduranceOverlay(float W, float H)
        {
            float x = W - 330f, y = H - 280f, w = 300f;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x - 8f, y - 6f, w + 16f, 104f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float lapsLeft = player.fuel / (100f / fuelLaps);
            GUI.Label(new Rect(x, y, w, 20f), "FUEL " + Mathf.RoundToInt(player.fuel) + "%   about " + lapsLeft.ToString("0.0") + " laps   [F] fill to " + player.FuelTargetName, Big(12));
            GUI.color = new Color(1f, 1f, 1f, 0.15f); GUI.DrawTexture(new Rect(x, y + 22f, w, 8f), Texture2D.whiteTexture);
            GUI.color = player.fuel > 25f ? new Color(0.3f, 0.85f, 0.45f) : new Color(1f, 0.3f, 0.25f);
            GUI.DrawTexture(new Rect(x, y + 22f, w * Mathf.Clamp01(player.fuel / 100f), 8f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float energy = 1f - player.fatigue;
            GUI.Label(new Rect(x, y + 34f, w, 20f), "DRIVER " + Mathf.RoundToInt(energy * 100f) + "%" + (player.swapPlanned ? "   (relief driver at the next stop)" : ""), Big(12));
            GUI.color = new Color(1f, 1f, 1f, 0.15f); GUI.DrawTexture(new Rect(x, y + 56f, w, 8f), Texture2D.whiteTexture);
            GUI.color = energy > 0.4f ? new Color(0.3f, 0.7f, 1f) : new Color(1f, 0.6f, 0.2f);
            GUI.DrawTexture(new Rect(x, y + 56f, w * Mathf.Clamp01(energy), 8f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float hours = 15.5f + timeOfDay * 6f;
            int hh = Mathf.FloorToInt(hours), mm = Mathf.FloorToInt((hours - hh) * 60f);
            GUI.Label(new Rect(x, y + 68f, w, 20f), "TIME " + hh.ToString("00") + ":" + mm.ToString("00") + "   " + (timeOfDay < 0.3f ? "AFTERNOON" : timeOfDay < 0.7f ? "DUSK" : "NIGHT"), Big(12));
        }

        void DrawPracticeOverlay(float W, float H)
        {
            float x = 16f, y = 120f, w = 330f;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x - 6f, y - 6f, w + 12f, 96f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            string[] names = { "SMOOTH LAPS", "FAST LAPS", "LONG RUN", weekend != null ? weekend.StageName.ToUpper() : "SETUP RUN", "PERFECT LAP", "BRAKING TEST", "TYRE SAVER" };
            GUI.Label(new Rect(x, y, w, 22f), "PRACTICE - " + names[Mathf.Clamp(practiceDrill, 0, 6)], Big(14));
            if (practiceDrill == 5 && player != null)
            {
                float toLine = chLine - (player.dist + 2.7f);
                GUI.Label(new Rect(x, y + 22f, w, 20f), chArmed ? "Speed reached - BRAKE and stop on the line!" : "Reach " + Mathf.RoundToInt(chNeedSpeed * 3.6f) + " km/h first (now " + Mathf.RoundToInt(player.speed * 3.6f) + ")", Big(12));
                GUI.Label(new Rect(x, y + 42f, w, 20f), (toLine >= 0f ? toLine.ToString("0") + " m to the line" : (-toLine).ToString("0") + " m past the line"), Big(12, FontStyle.Normal));
                GUI.Label(new Rect(x, y + 62f, w, 20f), PracticeBrief(), Big(10, FontStyle.Normal));
                return;
            }
            GUI.Label(new Rect(x, y + 22f, w, 20f), "Clean laps: " + player.cleanLaps + "   incidents: " + player.incidents, Big(12, FontStyle.Normal));
            GUI.Label(new Rect(x, y + 42f, w, 20f), "Target lap " + Hud.FormatTime(track.roadLength / 33f) + (player.bestLapThisRace > 0f ? "   best " + Hud.FormatTime(player.bestLapThisRace) : ""), Big(12, FontStyle.Normal));
            GUI.Label(new Rect(x, y + 62f, w, 20f), PracticeBrief(), Big(10, FontStyle.Normal));
        }

        void DrawRedFlag(float W, float H)
        {
            float w = 520f, x = W * 0.5f - w * 0.5f, y = 150f;
            GUI.color = new Color(0.05f, 0f, 0f, 0.85f);
            GUI.DrawTexture(new Rect(x, y, w, 330f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.15f, 0.12f);
            GUI.DrawTexture(new Rect(x, y, w, 5f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 20f, y + 14f, w - 40f, 44f), "RED FLAG", Big(34));
            GUI.Label(new Rect(x + 20f, y + 58f, w - 40f, 60f), "The race is suspended and the cars are in the pit lane. Tyres and repairs are free: damage to the wing and floor is fixed, engine damage is not.", Big(13, FontStyle.Normal));
            GUI.Label(new Rect(x + 20f, y + 126f, w - 40f, 22f), "TYRES FOR THE RESTART", Big(14));
            for (int i = 0; i < Tuning.CompoundNames.Length; i++)
            {
                GUI.backgroundColor = player.compound == (Compound)i ? ON : Color.white;
                if (GUI.Button(new Rect(x + 20f + i * 98f, y + 152f, 92f, 30f), "[" + (i + 1) + "] " + Tuning.CompoundNames[i])) player.ChooseCompound((Compound)i);
            }
            GUI.backgroundColor = Color.white;
            GUI.Label(new Rect(x + 20f, y + 194f, w - 40f, 40f), "Standing restart in the same order. The first line crossing is not a timed lap. " + (isRaining ? "It is wet - intermediates or wets are the right call." : ""), Big(12, FontStyle.Normal));
            GUI.backgroundColor = new Color(0.3f, 0.9f, 0.4f);
            if (GUI.Button(new Rect(x + 20f, y + 262f, w - 40f, 44f), "RESTART THE RACE   (Enter)")) RestartAfterRedFlag();
            GUI.backgroundColor = Color.white;
        }

        void DrawOnlineBadge(float W, float H)
        {
            var st = Big(12); st.alignment = TextAnchor.UpperCenter;
            GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(new Rect(W * 0.5f - 130f, 4f, 260f, 20f), Texture2D.whiteTexture);
            GUI.color = Color.white; GUI.Label(new Rect(W * 0.5f - 130f, 5f, 260f, 20f), net.Badge(), st);
        }

        void DrawOnlineMenu(float W, float H)
        {
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float cx = W / 2f - 150;
            GUI.Label(new Rect(cx + 20, 200, 320, 50), "ONLINE RACE", Big(32));
            GUI.Label(new Rect(cx, 248, 320, 22), "The race keeps running while this is open.", Big(12, FontStyle.Normal));
            if (GUI.Button(new Rect(cx, 280, 300, 44), "RESUME")) mpMenu = false;
            if (GUI.Button(new Rect(cx, 334, 300, 44), "LEAVE THE RACE")) EnterMenu();
        }

        GUIStyle WrapStyle(int size) { var st = Big(size, FontStyle.Normal); st.wordWrap = true; return st; }

        // ---- the online lobby (Race tab) ----
        void DrawNet(float x)
        {
            float y = 0;
            GUI.Label(new Rect(x, y, 420, 26), "ONLINE MULTIPLAYER", Big(18)); y += 30;
            if (!net.Active)
            {
                GUI.Label(new Rect(x, y, 410, 66), "Race up to 6 people. One of you hosts a room and shares its 8-letter code; everybody else types it in. It works on the same Wi-Fi or network. No account or server is needed.", WrapStyle(12));
                y += 72;
                GUI.Label(new Rect(x, y, 420, 22), "HOST A ROOM", Big(15)); y += 24;
                if (hostAddr.Length == 0) hostAddr = RoomCode.LocalIp().ToString();
                GUI.Label(new Rect(x, y, 130, 24), "Your address", Big(12, FontStyle.Normal));
                hostAddr = GUI.TextField(new Rect(x + 120, y, 180, 24), hostAddr, 15);
                y += 28;
                GUI.Label(new Rect(x, y, 410, 52), "This is filled in for your network. To play over the internet, put your public IP here and forward UDP ports 47777 to 47784 on your router to this computer.", WrapStyle(11));
                y += 54;
                GUI.backgroundColor = new Color(0.35f, 0.75f, 1f);
                if (GUI.Button(new Rect(x, y, 410, 36), "CREATE ROOM"))
                {
                    if (net.Host(hostAddr)) { net.trackIndex = GameSave.trackIndex; net.laps = GameSave.laps; net.raceTime = GameSave.raceTime; net.startType = Mathf.Min(1, GameSave.startType); }
                }
                GUI.backgroundColor = Color.white; y += 50;
                GUI.Label(new Rect(x, y, 420, 22), "JOIN A ROOM", Big(15)); y += 24;
                GUI.Label(new Rect(x, y, 100, 24), "Room code", Big(12, FontStyle.Normal));
                joinCode = GUI.TextField(new Rect(x + 100, y, 160, 28), joinCode, 9);
                GUI.enabled = joinCode.Length >= 8;
                if (GUI.Button(new Rect(x + 270, y, 140, 28), "JOIN")) net.Join(joinCode);
                GUI.enabled = true; y += 36;
                if (net.error.Length > 0) { GUI.color = new Color(1f, 0.45f, 0.4f); GUI.Label(new Rect(x, y, 410, 60), net.error, WrapStyle(12)); GUI.color = Color.white; y += 64; }
                if (GUI.Button(new Rect(x, y + 6, 200, 30), "Back")) { netPanel = false; net.error = ""; }
                return;
            }

            bool host = net.role == NetSession.Role.Host;
            GUI.Label(new Rect(x, y, 150, 22), "ROOM CODE", Big(13, FontStyle.Normal));
            GUI.Label(new Rect(x + 110, y - 6, 220, 34), net.code, Big(26));
            if (GUI.Button(new Rect(x + 340, y, 70, 24), "Copy")) GUIUtility.systemCopyBuffer = net.code;
            y += 34;
            if (!net.Joined) { GUI.Label(new Rect(x, y, 410, 24), net.status, Big(13, FontStyle.Normal)); y += 28; }
            else
            {
                GUI.Label(new Rect(x, y, 420, 22), "PLAYERS  (" + net.roster.Count + "/" + NetSession.MAX_PLAYERS + ")", Big(15)); y += 24;
                foreach (var p in net.roster)
                {
                    GUI.color = Livery.Colors[((p.body % Livery.Colors.Length) + Livery.Colors.Length) % Livery.Colors.Length];
                    GUI.DrawTexture(new Rect(x, y + 4, 8, 16), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(x + 14, y, 380, 24), "#" + p.number + "  " + p.name + (p.slot == 0 ? "   (host)" : "") + (p.slot == net.mySlot ? "   (you)" : ""), Big(13, FontStyle.Normal));
                    y += 24;
                }
                y += 8;
                GUI.Label(new Rect(x, y, 420, 22), host ? "RACE SETTINGS" : "RACE SETTINGS  (the host chooses)", Big(15)); y += 24;
                GUI.enabled = host;
                for (int i = 0; i < Tracks.All.Length; i++)
                {
                    GUI.backgroundColor = net.trackIndex == i ? ON : Color.white;
                    if (GUI.Button(new Rect(x + (i % 2) * 208, y + (i / 2) * 28, 202, 25), Tracks.All[i].name)) { net.trackIndex = i; }
                }
                y += 28f * ((Tracks.All.Length + 1) / 2) + 4f;
                GUI.Label(new Rect(x, y, 70, 24), "Laps", Big(13, FontStyle.Normal));
                int[] lapOpt = { 1, 3, 5 };
                for (int i = 0; i < lapOpt.Length; i++) { GUI.backgroundColor = net.laps == lapOpt[i] ? ON : Color.white; if (GUI.Button(new Rect(x + 70 + i * 60, y, 54, 24), lapOpt[i].ToString())) net.laps = lapOpt[i]; }
                y += 28;
                GUI.Label(new Rect(x, y, 70, 24), "Time", Big(13, FontStyle.Normal));
                string[] tod = { "Day", "Dusk", "Night" };
                for (int i = 0; i < 3; i++) { GUI.backgroundColor = net.raceTime == i ? ON : Color.white; if (GUI.Button(new Rect(x + 70 + i * 114, y, 108, 24), tod[i])) net.raceTime = i; }
                y += 28;
                GUI.Label(new Rect(x, y, 70, 24), "Start", Big(13, FontStyle.Normal));
                string[] stn = { "Standing", "Rolling" };
                for (int i = 0; i < 2; i++) { GUI.backgroundColor = net.startType == i ? ON : Color.white; if (GUI.Button(new Rect(x + 70 + i * 114, y, 108, 24), stn[i])) net.startType = i; }
                GUI.backgroundColor = Color.white; GUI.enabled = true;
                y += 34;
                GUI.Label(new Rect(x, y, 410, 34), "Everybody gets the same weather. Your own car, setup and tyres are used. Cars can touch, and there are no random safety cars online.", WrapStyle(11));
                y += 38;
                if (host)
                {
                    GUI.backgroundColor = new Color(1f, 0.35f, 0.25f);
                    if (GUI.Button(new Rect(x, y, 410, 40), net.roster.Count < 2 ? "START RACE  (nobody else has joined yet)" : "START RACE")) net.HostStart();
                    GUI.backgroundColor = Color.white; y += 48;
                }
                else { GUI.Label(new Rect(x, y, 410, 30), "Waiting for the host to start the race...", Big(14)); y += 34; }
            }
            if (net.error.Length > 0) { GUI.color = new Color(1f, 0.45f, 0.4f); GUI.Label(new Rect(x, y, 410, 40), net.error, WrapStyle(12)); GUI.color = Color.white; y += 44; }
            if (GUI.Button(new Rect(x, y, 200, 30), "LEAVE ROOM")) { net.Close(); }
            if (GUI.Button(new Rect(x + 210, y, 200, 30), "Back to the menu")) netPanel = false;
        }

        void DrawPause(float W, float H)
        {
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float cx = W / 2f - 130;
            GUI.Label(new Rect(cx + 70, 200, 260, 50), "PAUSED", Big(36));
            if (GUI.Button(new Rect(cx, 270, 260, 44), "RESUME")) state = stateBeforePause;
            if (GUI.Button(new Rect(cx, 324, 260, 44), "RESTART RACE")) StartRace(mode);
            if (GUI.Button(new Rect(cx, 378, 260, 44), "QUIT TO GARAGE")) EnterMenu();
        }

        Vector2 finishScroll;

        void DrawFinish(float W, float H)
        {
            GUI.color = new Color(0, 0, 0, 0.72f);
            GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float cx = W / 2f - 230;
            var run = ActiveRun();

            // the buttons are pinned to the bottom of the screen; everything above them scrolls, so a long list of results,
            // notes and standings (the end of a season has plenty) can never push the buttons off screen
            float barH = 144f, barY = H - barH;
            var wrap = Big(15, FontStyle.Normal); wrap.wordWrap = true;
            var wrapGold = Big(15); wrapGold.wordWrap = true;

            float contentH = 60f + 50f + 70f + finishOrder.Count * 22f + 6f;
            foreach (var n in finishNotes) contentH += Mathf.Max(22f, wrapGold.CalcHeight(new GUIContent(n), 460f));
            if (finishRun != null) contentH += 4f + 22f + finishRun.Standings().Count * 20f;
            contentH += 16f;

            finishScroll = GUI.BeginScrollView(new Rect(cx - 20f, 0f, 500f, barY), finishScroll, new Rect(0f, 0f, 480f, contentH));
            float x0 = 20f;
            GUI.Label(new Rect(x0, 60, 460, 50), mode == Mode.Qualifying ? (quali != null && !quali.done ? "Q" + quali.lastSession + " COMPLETE" : "QUALIFYING COMPLETE") : mode == Mode.Practice ? "PRACTICE COMPLETE" : "RACE COMPLETE", Big(34));
            GUI.Label(new Rect(x0, 110, 460, 70), finishSummary, Big(17, FontStyle.Normal));
            float y = 185;
            foreach (var line in finishOrder) { GUI.Label(new Rect(x0, y, 460, 22), line, wrap); y += 22; }
            y += 6;
            foreach (var n in finishNotes)
            {
                float nh = Mathf.Max(22f, wrapGold.CalcHeight(new GUIContent(n), 460f));
                GUI.color = new Color(1f, 0.83f, 0.2f); GUI.Label(new Rect(x0, y, 460, nh), n, wrapGold); y += nh;
            }
            GUI.color = Color.white;
            if (finishRun != null)
            {
                y += 4;
                GUI.Label(new Rect(x0, y, 460, 22), mode == Mode.Tournament ? "TOURNAMENT STANDINGS" : "SEASON STANDINGS", Big(14)); y += 22;
                foreach (var kv in finishRun.Standings()) { GUI.Label(new Rect(x0 + 8, y, 300, 20), kv.Key + "    " + kv.Value + " pts", Big(13, FontStyle.Normal)); y += 20; }
            }
            GUI.EndScrollView();

            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, barY, W, barH), Texture2D.whiteTexture);
            GUI.color = Color.white;
            y = barY + 8f;
            if (run != null && run.active)
            {
                if (GUI.Button(new Rect(cx, y, 220, 42), "NEXT ROUND")) StartRace(mode);
            }
            else if (weekend != null && mode == Mode.Practice)
            {
                GUI.backgroundColor = ON;
                if (GUI.Button(new Rect(cx, y, 220, 42), "NEXT: " + weekend.StageName.ToUpper())) StartWeekendSession();
                GUI.backgroundColor = Color.white;
            }
            else if (mode == Mode.Qualifying && quali != null && !quali.done)
            {
                GUI.backgroundColor = ON;
                if (GUI.Button(new Rect(cx, y, 220, 42), "NEXT SESSION  (Q" + quali.session + ")")) StartRace(Mode.Qualifying);
                GUI.backgroundColor = Color.white;
            }
            else if (mode == Mode.Multiplayer)
            {
                GUI.backgroundColor = ON;
                if (GUI.Button(new Rect(cx, y, 220, 42), "BACK TO LOBBY")) EnterMenu();
                GUI.backgroundColor = Color.white;
            }
            else if (GUI.Button(new Rect(cx, y, 220, 42), mode == Mode.Qualifying ? "RACE FROM GRID" : mode == Mode.Practice ? "PRACTICE AGAIN" : "RACE AGAIN"))
                StartRace(mode == Mode.Qualifying ? Mode.Quick : (run != null ? Mode.Quick : mode));
            if (GUI.Button(new Rect(cx + 240, y, 220, 42), weekend != null && weekend.stage < 3 ? "GARAGE  (change setup)" : "GARAGE")) EnterMenu();
            if (GUI.Button(new Rect(cx, y + 50, 460, 36), "WATCH REPLAY  (drone camera, photo mode)")) EnterReplay();
            if (mode != Mode.Qualifying && GUI.Button(new Rect(cx, y + 92, 460, 36), "TELEMETRY  (compare laps, speed, brake and throttle)")) { showTelemetry = true; telemetry.Reset(player); }
        }
    }

    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (Object.FindFirstObjectByType<RaceManager>() != null) return;
            var host = new GameObject("ApexDrive");
            host.AddComponent<RaceManager>();
            if (Application.platform == RuntimePlatform.WebGLPlayer) host.AddComponent<WebTuning>();       // the browser build tunes its graphics to the visitor's machine
        }
    }
}
