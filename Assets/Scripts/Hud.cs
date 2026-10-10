using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ApexDrive
{
    /// <summary>Race HUD built entirely in code with uGUI (no prefabs).</summary>
    public class Hud
    {
        readonly GameObject root;
        readonly Font font;
        Text speed, lapText, posText, lapTime, radio, countdown, pitText, pitArrow, tiresLabel;
        Text banSC, banWeather, banWarn, banOff, banDrs, banDraft, banDamage, banPenalty, banTire, banMode;
        Text banFlag, banDirty, banSlide, banPit, ersLabel, tempLabel, banInfo;
        RectTransform ersFill;
        readonly Text[] sectors = new Text[3];
        RectTransform tireFill;
        Image tireFillImg;
        RawImage minimap;
        Texture2D miniTex;
        readonly List<Image> dots = new List<Image>();
        Vector2 miniMin; float miniScale;
        const int MINI = 150, PAD = 14;

        public Hud()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "Segoe UI", "Liberation Sans" }, 16);
            root = new GameObject("HUD");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;

            lapText = T("Lap", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(-120, -14), 30, Color.white);
            posText = T("Pos", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(120, -14), 30, Color.white);
            lapTime = T("LapTime", TextAnchor.UpperRight, new Vector2(1, 1), new Vector2(-20, -14), 22, new Color(0.7f, 0.75f, 0.85f));
            for (int i = 0; i < 3; i++)
                sectors[i] = T("Sector" + i, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(18, -14 - i * 24), 18, Color.gray);

            banWeather = T("BanWeather", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -56), 18, new Color(0.62f, 0.77f, 0.91f));
            banWarn = T("BanWarn", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -86), 22, new Color(1f, 0.69f, 0.13f));
            banOff = T("BanOff", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -120), 20, new Color(1f, 0.29f, 0.18f));
            banTire = T("BanTire", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -150), 20, new Color(1f, 0.29f, 0.18f));
            banDrs = T("BanDrs", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -180), 20, new Color(0.09f, 0.85f, 0.77f));
            banDamage = T("BanDamage", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -210), 18, new Color(1f, 0.69f, 0.13f));
            banPenalty = T("BanPenalty", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -236), 18, new Color(1f, 0.29f, 0.18f));
            banDraft = T("BanDraft", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -262), 18, new Color(0.37f, 0.66f, 1f));
            banSC = T("BanSC", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -290), 24, new Color(1f, 0.83f, 0f));
            banMode = T("BanMode", TextAnchor.UpperRight, new Vector2(1, 1), new Vector2(-20, -50), 20, Color.white);
            banFlag = T("BanFlag", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -320), 22, new Color(1f, 0.85f, 0f));
            banDirty = T("BanDirty", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -350), 18, new Color(0.7f, 0.75f, 0.85f));
            banSlide = T("BanSlide", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -376), 18, new Color(1f, 0.55f, 0.2f));
            banInfo = T("BanInfo", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -436), 20, new Color(0.9f, 0.92f, 1f));
            banPit = T("BanPit", TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -406), 22, new Color(1f, 0.85f, 0.2f));

            radio = T("Radio", TextAnchor.LowerCenter, new Vector2(0.5f, 0), new Vector2(0, 90), 22, Color.white);
            countdown = T("Countdown", TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 40), 140, new Color(0.09f, 0.85f, 0.77f));
            pitText = T("Pit", TextAnchor.LowerCenter, new Vector2(0.5f, 0), new Vector2(0, 130), 26, new Color(1f, 0.29f, 0.18f));
            pitArrow = T("PitArrow", TextAnchor.MiddleCenter, new Vector2(1, 0.5f), new Vector2(-60, 0), 52, new Color(0.09f, 0.85f, 0.77f));
            pitArrow.text = "^";

            speed = T("Speed", TextAnchor.LowerRight, new Vector2(1, 0), new Vector2(-24, 20), 64, new Color(0.09f, 0.85f, 0.77f));
            tiresLabel = T("TiresLabel", TextAnchor.LowerRight, new Vector2(1, 0), new Vector2(-190, 52), 16, new Color(0.7f, 0.75f, 0.85f));
            var barBg = Rect("TireBarBg", new Vector2(1, 0), new Vector2(-190, 38), new Vector2(130, 10), new Color(0.1f, 0.12f, 0.16f, 0.9f));
            var fill = Rect("TireBar", new Vector2(1, 0), new Vector2(-190, 38), new Vector2(130, 10), new Color(0.24f, 0.86f, 0.52f));
            fill.rectTransform.pivot = new Vector2(1, 0);
            tireFill = fill.rectTransform; tireFillImg = fill;
            barBg.rectTransform.pivot = new Vector2(1, 0);
            var ersBg = Rect("ErsBg", new Vector2(1, 0), new Vector2(-190, 76), new Vector2(130, 8), new Color(0.1f, 0.12f, 0.16f, 0.9f));
            ersBg.rectTransform.pivot = new Vector2(1, 0);
            var ersF = Rect("ErsBar", new Vector2(1, 0), new Vector2(-190, 76), new Vector2(130, 8), new Color(0.37f, 0.66f, 1f));
            ersF.rectTransform.pivot = new Vector2(1, 0);
            ersFill = ersF.rectTransform;
            ersLabel = T("ErsLabel", TextAnchor.LowerRight, new Vector2(1, 0), new Vector2(-190, 86), 14, new Color(0.7f, 0.75f, 0.85f));
            tempLabel = T("TempLabel", TextAnchor.LowerRight, new Vector2(1, 0), new Vector2(-190, 102), 14, new Color(0.7f, 0.75f, 0.85f));

            var hint = T("Hint", TextAnchor.LowerCenter, new Vector2(0.5f, 0), new Vector2(0, 8), 14, new Color(0.6f, 0.65f, 0.75f));
            hint.text = "W/Up throttle   S/Down brake   A D / Left Right steer   Q DRS   E engine mode   V camera   R recover   B box   Y/N radio   P pause   1-5 tires in pit";

            BuildMinimap();
            root.SetActive(false);
        }

        public void SetVisible(bool v) { root.SetActive(v); }

        // ---------- creation helpers ----------
        Text T(string name, TextAnchor align, Vector2 anchor, Vector2 pos, int size, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var t = go.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.color = c; t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(600, size + 10);
            if (align == TextAnchor.UpperCenter || align == TextAnchor.LowerCenter || align == TextAnchor.MiddleCenter) rt.pivot = new Vector2(0.5f, anchor.y);
            var sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.8f); sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        Image Rect(string name, Vector2 anchor, Vector2 pos, Vector2 size, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var im = go.AddComponent<Image>();
            im.color = c;
            var rt = im.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return im;
        }

        void BuildMinimap()
        {
            miniTex = new Texture2D(MINI, MINI, TextureFormat.RGBA32, false);
            miniTex.filterMode = FilterMode.Bilinear;
            var go = new GameObject("Minimap");
            go.transform.SetParent(root.transform, false);
            minimap = go.AddComponent<RawImage>();
            minimap.texture = miniTex;
            var rt = minimap.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0); rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(24, 24); rt.sizeDelta = new Vector2(MINI, MINI);
            for (int i = 0; i < 7; i++)
            {
                var d = Rect("Dot" + i, new Vector2(0, 0), Vector2.zero, new Vector2(8, 8), Color.white);
                d.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                d.transform.SetParent(go.transform, false);
                d.rectTransform.anchorMin = d.rectTransform.anchorMax = new Vector2(0, 0);
                dots.Add(d);
            }
        }

        public void SetTrack(TrackModel tm)
        {
            var px = new Color32[MINI * MINI];
            Color32 bg = new Color32(11, 14, 20, 190);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in tm.spaced) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y); }
            miniScale = Mathf.Min((MINI - PAD * 2f) / (maxX - minX), (MINI - PAD * 2f) / (maxZ - minZ));
            miniMin = new Vector2(minX, minZ);
            Color32 line = new Color32(86, 100, 128, 255);
            for (int i = 0; i < tm.spaced.Length; i++)
            {
                Vector2 a = tm.spaced[i], b = tm.spaced[(i + 1) % tm.spaced.Length];
                for (int s = 0; s <= 4; s++)
                {
                    Vector2 q = Vector2.Lerp(a, b, s / 4f);
                    Vector2 m = ToMini(q);
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int x = Mathf.RoundToInt(m.x) + dx, y = Mathf.RoundToInt(m.y) + dy;
                            if (x >= 0 && y >= 0 && x < MINI && y < MINI) px[y * MINI + x] = line;
                        }
                }
            }
            miniTex.SetPixels32(px); miniTex.Apply();
        }

        // web-space (x,z) -> minimap pixel; z is flipped to match the Unity-world orientation of the circuit
        Vector2 ToMini(Vector2 p)
        {
            return new Vector2(PAD + (p.x - miniMin.x) * miniScale, MINI - (PAD + (p.y - miniMin.y) * miniScale));
        }

        static void Set(Text t, string s, bool on) { t.text = on ? s : ""; }

        // ---------- per-frame ----------
        public void Refresh(RaceManager rm)
        {
            var p = rm.player;
            if (p == null) return;
            speed.text = Mathf.Max(0, Mathf.RoundToInt(p.speed * 3.6f)) + " <size=18>KM/H</size>";
            int shown = Mathf.Clamp(p.lap + 1, 1, rm.raceLaps);
            lapText.text = rm.mode == RaceManager.Mode.Tutorial ? "TUTORIAL"
                         : rm.mode == RaceManager.Mode.Qualifying && rm.quali != null ? rm.quali.SessionName
                         : rm.mode == RaceManager.Mode.Practice ? "PRACTICE " + shown + "/" + rm.raceLaps
                         : rm.rolling ? "FORMATION" : "LAP " + shown + "/" + rm.raceLaps;
            posText.text = "P" + rm.PlayerRank();
            float lt = rm.state == RaceManager.State.Racing ? rm.Now - p.lapStart : 0f;
            lapTime.text = FormatTime(lt) + (p.bestLapThisRace > 0 ? "\n<size=16>best " + FormatTime(p.bestLapThisRace) + "</size>" : "");

            for (int i = 0; i < 3; i++)
            {
                sectors[i].text = p.sectorText[i];
                sectors[i].color = p.sectorState[i] == 1 ? new Color(0.24f, 0.86f, 0.52f) : p.sectorState[i] == 2 ? new Color(1f, 0.69f, 0.13f) : Color.gray;
            }

            Set(banSC, rm.redFlag ? "RED FLAG - RACE SUSPENDED"
                     : rm.vscActive ? "VIRTUAL SAFETY CAR - MAX " + Mathf.RoundToInt(Tuning.VSC_SPEED_CAP * 3.6f) + " KM/H" : rm.scRestart ? "SAFETY CAR IN - RESTART AT THE LINE" : "SAFETY CAR - HOLD STATION",
                rm.safetyCarActive || rm.vscActive || rm.redFlag);
            Set(banWeather, rm.isRaining ? "RAIN - reduced grip" : rm.isFoggy ? "FOG - reduced visibility" : "", rm.isRaining || rm.isFoggy);
            Set(banWarn, rm.warnText, Time.time < rm.warnUntil);
            Set(banOff, "OFF TRACK", p.offTrack);
            Set(banTire, "PIT NOW - TIRES CRITICAL", p.tireWear < 20f && !p.pitStopActive);
            Set(banDrs, p.drsActive ? "DRS ACTIVE" : "DRS AVAILABLE - HOLD Q", p.drsAvailable);
            Set(banDamage, "DAMAGE " + Mathf.RoundToInt(p.damage) + "%", p.damage > 5f);
            Set(banPenalty, "PENALTY +" + p.penaltySeconds.ToString("0.0") + "s", p.penaltySeconds > 0f);
            Set(banDraft, p.slingshot > 0.03f ? "SLINGSHOT!  +" + Mathf.RoundToInt(p.slingshot * 10f) + "% top speed"
                                              : "SLIPSTREAM +" + Mathf.RoundToInt(p.draft * 100f) + "%   tow " + Mathf.RoundToInt(p.towCharge * 100f) + "%",
                p.draft > 0.005f || p.slingshot > 0.03f);
            banMode.text = p.engineMode.ToString().ToUpper() + "  |  " + Tuning.CompoundNames[(int)p.compound].ToUpper();
            banMode.color = p.engineMode == EngineMode.Push ? new Color(1f, 0.29f, 0.18f) : p.engineMode == EngineMode.Save ? new Color(0.24f, 0.86f, 0.52f) : new Color(0.09f, 0.85f, 0.77f);

            radio.text = Time.time < rm.radioUntil ? rm.radioText : "";
            countdown.text = rm.countdownText;

            ersFill.sizeDelta = new Vector2(130f * Mathf.Clamp01(p.ers / 100f), 8f);
            ersLabel.text = "ERS " + Mathf.RoundToInt(p.ers) + "%  " + p.dyn.ErsMapName + (p.ersDeploy ? "  DEPLOY" : p.dyn.ersAuto > 0f ? "  AUTO" : "  [M] map  [SHIFT]");
            tempLabel.text = "BRK " + Mathf.RoundToInt(p.brakeTemp) + "C   TYRE " + Mathf.RoundToInt(p.dyn.AvgSurface) + "/" + Mathf.RoundToInt(p.dyn.AvgCore) + "C   TRACK " + Mathf.RoundToInt(rm.trackTemp) + "C   FUEL " + Mathf.RoundToInt(p.dyn.FuelMass * 100f) + "%"
                + (rm.fuelOn && !rm.Refuels ? " (" + (p.FuelMarginLaps >= 0f ? "+" : "") + p.FuelMarginLaps.ToString("0.0") + " laps)" : "")
                + (p.dyn.grain > 0.12f ? "   GRAINING " + Mathf.RoundToInt(p.dyn.grain * 100f) + "%" : "")
                + (p.dyn.blister > 0.12f ? "   BLISTERS " + Mathf.RoundToInt(p.dyn.blister * 100f) + "%" : "");
            tempLabel.color = p.brakeTemp > 900f ? new Color(1f, 0.29f, 0.18f) : (p.tireTemp > p.TempHighForCompound + 5f || p.tireTemp < p.TempLowForCompound - 5f) ? new Color(1f, 0.69f, 0.13f) : new Color(0.7f, 0.75f, 0.85f);
            Set(banFlag, rm.flagBanner, rm.flagBanner.Length > 0);
            banFlag.color = rm.flagBanner.StartsWith("RED") ? new Color(1f, 0.2f, 0.15f) : rm.flagBanner.StartsWith("BLUE") ? new Color(0.3f, 0.55f, 1f) : rm.flagBanner.StartsWith("GREEN") ? new Color(0.24f, 0.86f, 0.52f) : new Color(1f, 0.85f, 0f);
            Set(banDirty, "DIRTY AIR - FRONT GRIP DOWN", p.dirtyAir > 0.25f);
            Set(banSlide, "", false);
            Set(banPit, p.pitGuide, p.pitGuide.Length > 0);
            string qi = "";
            if (rm.mode == RaceManager.Mode.Qualifying && rm.quali != null && !rm.quali.done)
            {
                float cut = rm.quali.CutoffTime();
                if (cut > 0f) qi = (rm.quali.session == 3 ? "FOR POLE: beat " : "TO ADVANCE: beat ") + FormatTime(cut);
            }
            Set(banInfo, qi, qi.Length > 0);
            tiresLabel.text = "TIRES";
            tireFill.sizeDelta = new Vector2(130f * Mathf.Clamp01(p.tireWear / 100f), 10f);
            tireFillImg.color = p.tireWear > 50f ? new Color(0.24f, 0.86f, 0.52f) : p.tireWear > 25f ? new Color(1f, 0.69f, 0.13f) : new Color(1f, 0.29f, 0.18f);

            if (p.pitStopActive)
            {
                pitText.text = "PITTING " + Mathf.Max(0f, p.pitStopEnd - rm.Now).ToString("0.0") + "s   [1] Soft [2] Med [3] Hard [4] Inter [5] Wet" + (p.pitWork.Length > 0 ? "\n" + p.pitWork : "");
                pitArrow.text = "";
            }
            else
            {
                pitText.text = "";
                float pz = rm.track.PitZoneDist;
                float ahead = TrackModel.Mod(pz - p.dist, rm.track.roadLength);
                Vector2 pit = rm.track.PointAt(pz);
                float bearing = Mathf.Atan2(pit.x - p.pos.x, pit.y - p.pos.z);
                float rel = Mathf.Repeat(bearing - p.heading + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;
                pitArrow.text = p.inPitLaneNow ? "" : "^\n<size=16>PIT " + Mathf.RoundToInt(ahead) + "m</size>";
                pitArrow.rectTransform.localRotation = Quaternion.Euler(0, 0, rel * Mathf.Rad2Deg);
            }

            // minimap dots
            for (int i = 0; i < dots.Count; i++)
            {
                if (i >= rm.racers.Count || rm.racers[i].gone) { dots[i].gameObject.SetActive(false); continue; }
                var r = rm.racers[i];
                dots[i].gameObject.SetActive(true);
                Vector3 w = r.go.transform.position; // Unity world: x, z(-web z)
                Vector2 m = ToMini(new Vector2(w.x, -w.z));
                dots[i].rectTransform.anchoredPosition = m;
                dots[i].color = r.isPlayer ? new Color(1f, 0.29f, 0.18f) : new Color(0.09f, 0.85f, 0.77f);
                dots[i].rectTransform.sizeDelta = r.isPlayer ? new Vector2(10, 10) : new Vector2(7, 7);
            }
        }

        public static string FormatTime(float s)
        {
            if (s < 0) s = 0;
            int m = Mathf.FloorToInt(s / 60f);
            return m + ":" + (s - m * 60f).ToString("00.00");
        }
    }
}
