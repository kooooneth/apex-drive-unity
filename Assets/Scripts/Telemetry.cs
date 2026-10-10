using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>One lap of recorded data: speed, throttle and brake against distance round the lap.</summary>
    public class LapTrace
    {
        public float lapTime;
        public int lapNumber;
        public bool valid = true;
        public readonly List<float> t = new List<float>(), d = new List<float>(), v = new List<float>(), th = new List<float>(), br = new List<float>();

        public void Add(float time, float frac, float speed, bool throttle, bool brake)
        {
            if (d.Count > 0 && frac <= d[d.Count - 1]) return;          // distance must keep increasing
            t.Add(time); d.Add(frac); v.Add(speed); th.Add(throttle ? 1f : 0f); br.Add(brake ? 1f : 0f);
        }

        public int Count { get { return d.Count; } }

        int Seek(float frac)
        {
            // last index whose distance is <= frac
            int lo = 0, hi = d.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (d[mid] <= frac) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        public float ValueAt(List<float> series, float frac)
        {
            if (d.Count == 0) return 0f;
            if (frac <= d[0]) return series[0];
            if (frac >= d[d.Count - 1]) return series[series.Count - 1];
            int i = Seek(frac);
            float span = Mathf.Max(0.00001f, d[i + 1] - d[i]);
            return Mathf.Lerp(series[i], series[i + 1], (frac - d[i]) / span);
        }

        public float TimeAt(float frac)
        {
            if (frac >= 0.999f) return lapTime;
            return ValueAt(t, frac);
        }

        public float MaxSpeed()
        {
            float m = 0f; foreach (var s in v) m = Mathf.Max(m, s); return m;
        }

        public float AvgSpeed()
        {
            if (v.Count == 0) return 0f;
            float s = 0f; foreach (var x in v) s += x; return s / v.Count;
        }

        public float Share(List<float> series)
        {
            if (series.Count == 0) return 0f;
            float s = 0f; foreach (var x in series) s += x; return s / series.Count;
        }
    }

    /// <summary>The post-race telemetry screen: pick two laps and compare speed, throttle, brake and the time gap round the lap.</summary>
    public class TelemetryView
    {
        public int lapA = -1, lapB = -1;
        static GUIStyle sSmall, sTitle, sTiny;

        static void Fill(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; }

        static void Label(float x, float y, float w, string s, GUIStyle st, Color c)
        {
            var old = st.normal.textColor; st.normal.textColor = c;
            GUI.Label(new Rect(x, y, w, 20f), s, st);
            st.normal.textColor = old;
        }

        public void Reset(PlayerCar p)
        {
            lapA = lapB = -1;
            if (p == null) return;
            // default: A = your best lap, B = your last lap (or the one before)
            int best = -1;
            for (int i = 0; i < p.traces.Count; i++)
                if (p.traces[i].valid && (best < 0 || p.traces[i].lapTime < p.traces[best].lapTime)) best = i;
            lapA = best;
            for (int i = p.traces.Count - 1; i >= 0; i--) if (p.traces[i].valid && i != best) { lapB = i; break; }
        }

        /// <returns>true when the player pressed Back.</returns>
        public bool Draw(float W, float H, PlayerCar p, List<string> stewardLog)
        {
            if (sSmall == null)
            {
                sSmall = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                sTitle = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
                sTitle.normal.textColor = Color.white;
                sTiny = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            }
            Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.86f));
            bool back = false;
            GUI.Label(new Rect(40f, 20f, 600f, 40f), "TELEMETRY", sTitle);
            if (GUI.Button(new Rect(W - 170f, 24f, 130f, 32f), "BACK")) back = true;

            var laps = new List<int>();
            for (int i = 0; i < p.traces.Count; i++) if (p.traces[i].valid && p.traces[i].Count > 5) laps.Add(i);
            if (laps.Count == 0)
            {
                Label(40f, 90f, 700f, "No complete laps were recorded this race.", sSmall, Color.white);
                return back;
            }
            if (lapA < 0 || lapA >= p.traces.Count) lapA = laps[0];
            if (lapB < 0 || lapB >= p.traces.Count) lapB = laps[laps.Count - 1];

            Color cA = new Color(0.2f, 0.9f, 0.95f), cB = new Color(1f, 0.6f, 0.15f);
            Label(40f, 66f, 60f, "Lap A", sSmall, cA);
            Label(40f, 94f, 60f, "Lap B", sSmall, cB);
            for (int k = 0; k < laps.Count; k++)
            {
                int li = laps[k];
                string txt = p.traces[li].lapNumber + "  " + Hud.FormatTime(p.traces[li].lapTime);
                GUI.backgroundColor = lapA == li ? cA : Color.white;
                if (GUI.Button(new Rect(90f + k * 118f, 64f, 112f, 24f), txt)) lapA = li;
                GUI.backgroundColor = lapB == li ? cB : Color.white;
                if (GUI.Button(new Rect(90f + k * 118f, 92f, 112f, 24f), txt)) lapB = li;
            }
            GUI.backgroundColor = Color.white;

            LapTrace A = p.traces[lapA], B = p.traces[lapB];
            float gx = 80f, gw = Mathf.Min(W - 160f, 1100f), step = 3f;

            // ----- speed graph -----
            float sy = 140f, sh = 200f;
            float vmax = Mathf.Max(Mathf.Max(A.MaxSpeed(), B.MaxSpeed()), 30f) * 1.05f;
            Fill(new Rect(gx, sy, gw, sh), new Color(1f, 1f, 1f, 0.05f));
            for (int kmh = 0; kmh <= vmax * 3.6f; kmh += 100)
            {
                float yy = sy + sh - kmh / 3.6f / vmax * sh;
                Fill(new Rect(gx, yy, gw, 1f), new Color(1f, 1f, 1f, 0.12f));
                Label(gx - 38f, yy - 9f, 40f, kmh.ToString(), sTiny, new Color(0.7f, 0.75f, 0.85f));
            }
            Label(gx, sy - 18f, 300f, "SPEED (km/h)", sTiny, new Color(0.7f, 0.75f, 0.85f));
            for (float px = 0f; px < gw; px += step)
            {
                float f = px / gw;
                float ya = sy + sh - A.ValueAt(A.v, f) / vmax * sh;
                float yb = sy + sh - B.ValueAt(B.v, f) / vmax * sh;
                Fill(new Rect(gx + px, yb - 1f, step, 2.5f), cB);
                Fill(new Rect(gx + px, ya - 1f, step, 2.5f), cA);
            }

            // ----- throttle / brake strips -----
            float ty = sy + sh + 14f;
            Label(gx - 38f, ty - 2f, 40f, "A", sTiny, cA); Label(gx - 38f, ty + 20f, 40f, "B", sTiny, cB);
            for (float px = 0f; px < gw; px += step)
            {
                float f = px / gw;
                DrawStrip(gx + px, ty, step, A, f);
                DrawStrip(gx + px, ty + 22f, step, B, f);
            }
            Label(gx, ty + 44f, 400f, "green = throttle    red = brake", sTiny, new Color(0.7f, 0.75f, 0.85f));

            // ----- delta -----
            float dy = ty + 66f, dh = 80f;
            Fill(new Rect(gx, dy, gw, dh), new Color(1f, 1f, 1f, 0.05f));
            Fill(new Rect(gx, dy + dh * 0.5f, gw, 1f), new Color(1f, 1f, 1f, 0.25f));
            float maxAbs = 0.3f;
            for (float px = 0f; px < gw; px += step) maxAbs = Mathf.Max(maxAbs, Mathf.Abs(A.TimeAt(px / gw) - B.TimeAt(px / gw)));
            Label(gx, dy - 18f, 600f, "TIME GAP, A vs B  (below the line = A is ahead)   scale +/-" + maxAbs.ToString("0.0") + "s", sTiny, new Color(0.7f, 0.75f, 0.85f));
            for (float px = 0f; px < gw; px += step)
            {
                float f = px / gw;
                float dlt = A.TimeAt(f) - B.TimeAt(f);                       // + = A is slower here
                float h = Mathf.Clamp(dlt / maxAbs, -1f, 1f) * dh * 0.5f;
                Fill(new Rect(gx + px, h >= 0f ? dy + dh * 0.5f : dy + dh * 0.5f + h, step, Mathf.Max(1f, Mathf.Abs(h))),
                     h >= 0f ? new Color(1f, 0.3f, 0.25f, 0.85f) : new Color(0.3f, 0.95f, 0.45f, 0.85f));
            }
            for (int s = 1; s < 3; s++) Fill(new Rect(gx + gw * s / 3f, sy, 1f, dy + dh - sy), new Color(1f, 1f, 1f, 0.15f));
            Label(gx + 4f, dy + dh + 2f, 100f, "S1", sTiny, Color.gray);
            Label(gx + gw / 3f + 4f, dy + dh + 2f, 100f, "S2", sTiny, Color.gray);
            Label(gx + gw * 2f / 3f + 4f, dy + dh + 2f, 100f, "S3", sTiny, Color.gray);

            // ----- numbers -----
            float ny = dy + dh + 26f;
            Label(gx, ny, 400f, "LAP A  " + Hud.FormatTime(A.lapTime) + "    top " + Mathf.RoundToInt(A.MaxSpeed() * 3.6f) + " km/h   avg " + Mathf.RoundToInt(A.AvgSpeed() * 3.6f)
                  + "   full throttle " + Mathf.RoundToInt(A.Share(A.th) * 100f) + "%   braking " + Mathf.RoundToInt(A.Share(A.br) * 100f) + "%", sSmall, cA);
            Label(gx, ny + 20f, 400f, "LAP B  " + Hud.FormatTime(B.lapTime) + "    top " + Mathf.RoundToInt(B.MaxSpeed() * 3.6f) + " km/h   avg " + Mathf.RoundToInt(B.AvgSpeed() * 3.6f)
                  + "   full throttle " + Mathf.RoundToInt(B.Share(B.th) * 100f) + "%   braking " + Mathf.RoundToInt(B.Share(B.br) * 100f) + "%", sSmall, cB);
            float diff = A.lapTime - B.lapTime;
            Label(gx, ny + 40f, 400f, "Gap: A is " + Mathf.Abs(diff).ToString("0.00") + "s " + (diff <= 0f ? "faster" : "slower") + " than B.", sSmall, Color.white);
            string notes = "Damage " + Mathf.RoundToInt(p.damage) + "%   wing " + Mathf.RoundToInt(p.wingDamage) + "%   floor " + Mathf.RoundToInt(p.floorDamage) + "%   engine " + Mathf.RoundToInt(p.engineDamage) + "%";
            Label(gx, ny + 64f, 700f, notes, sSmall, new Color(1f, 0.8f, 0.3f));
            if (stewardLog != null && stewardLog.Count > 0)
            {
                Label(gx, ny + 84f, 400f, "STEWARDS", sSmall, new Color(1f, 0.45f, 0.35f));
                for (int i = 0; i < stewardLog.Count && i < 4; i++) Label(gx + 10f, ny + 102f + i * 17f, 700f, stewardLog[i], sTiny, Color.white);
            }
            return back;
        }

        static void DrawStrip(float x, float y, float w, LapTrace tr, float f)
        {
            float th = tr.ValueAt(tr.th, f), br = tr.ValueAt(tr.br, f);
            Color c = br > 0.5f ? new Color(1f, 0.28f, 0.22f) : th > 0.5f ? new Color(0.25f, 0.9f, 0.4f) : new Color(1f, 1f, 1f, 0.08f);
            Fill(new Rect(x, y, w, 16f), c);
        }
    }

    /// <summary>A small top-down car in the corner showing what is broken, coloured green to red.</summary>
    public static class DamageUi
    {
        static GUIStyle sCap, sPunct;

        static Color Tint(float d)
        {
            return d < 8f ? new Color(0.3f, 0.85f, 0.45f) : d < 35f ? new Color(1f, 0.85f, 0.2f) : d < 65f ? new Color(1f, 0.55f, 0.15f) : new Color(1f, 0.25f, 0.2f);
        }

        static void Fill(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; }

        public static bool Needed(PlayerCar p)
        {
            return p.wingDamage > 2f || p.floorDamage > 2f || p.engineDamage > 2f || p.damage > 8f || p.punctured || p.parts.TotalLost > 0;
        }

        public static void Draw(float x, float y, PlayerCar p)
        {
            Fill(new Rect(x - 6f, y - 18f, 110f, 150f), new Color(0f, 0f, 0f, 0.55f));
            if (sCap == null)
            {
                sCap = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold };
                sPunct = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
                sPunct.normal.textColor = new Color(1f, 0.3f, 0.25f);
            }
            GUI.Label(new Rect(x, y - 18f, 100f, 16f), "CAR DAMAGE", sCap);
            // wings
            Fill(new Rect(x + 8f, y, 84f, 8f), Tint(p.wingDamage));                       // front wing
            Fill(new Rect(x + 14f, y + 118f, 72f, 8f), Tint(p.damage));                   // rear wing
            // wheels
            foreach (var wx in new[] { x + 2f, x + 80f })
            {
                Fill(new Rect(wx, y + 14f, 18f, 26f), Tint(p.punctured && p.punctureWheel < 2 ? 100f : 0f));
                Fill(new Rect(wx, y + 84f, 18f, 30f), Tint(p.punctured && p.punctureWheel >= 2 ? 100f : 0f));
            }
            Fill(new Rect(x + 40f, y + 10f, 20f, 36f), Tint(p.damage * 0.6f));               // nose / cockpit
            Fill(new Rect(x + 28f, y + 46f, 44f, 36f), Tint(p.floorDamage));                  // floor + sidepods
            Fill(new Rect(x + 36f, y + 84f, 28f, 30f), Tint(p.engineDamage));                 // engine
            if (p.punctured) GUI.Label(new Rect(x - 2f, y + 128f, 120f, 16f), "PUNCTURE", sPunct);
            if (p.parts.TotalLost > 0)
            {
                Fill(new Rect(x - 6f, y + 132f, 190f, 18f), new Color(0f, 0f, 0f, 0.55f));
                GUI.Label(new Rect(x - 2f, y + 132f, 190f, 18f), "PARTS LOST: " + p.parts.TotalLost + "  (mirror, plates, boards)", sCap);
            }
        }
    }
}
