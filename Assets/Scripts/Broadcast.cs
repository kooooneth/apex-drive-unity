using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// TV-style graphics: lower-third banners for overtakes, pit stops (with the tyre change), lap times with the delta to your best, and the final
    /// lap. Plus a small weather radar and the sunset glow at the horizon.
    /// </summary>
    public class Broadcast
    {
        class Item { public string kind, title, sub; public Color col; public float start, until; }
        readonly List<Item> items = new List<Item>();
        float bestLap = -1f;
        GUIStyle sKind, sTitle, sSub, sSmall;

        void Styles()
        {
            if (sKind != null) return;
            sKind = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold }; sKind.normal.textColor = Color.white;
            sTitle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold }; sTitle.normal.textColor = Color.white;
            sSub = new GUIStyle(GUI.skin.label) { fontSize = 11 }; sSub.normal.textColor = new Color(0.85f, 0.88f, 0.95f);
            sSmall = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold }; sSmall.normal.textColor = new Color(0.8f, 0.85f, 0.95f);
        }

        public void Reset() { items.Clear(); bestLap = -1f; }

        public void Push(string kind, string title, string sub, Color col, float seconds = 4f)
        {
            // the same kind of banner replaces the old one instead of piling up
            items.RemoveAll(i => i.kind == kind);
            items.Add(new Item { kind = kind, title = title, sub = sub, col = col, start = Time.time, until = Time.time + seconds });
            while (items.Count > 3) items.RemoveAt(0);
        }

        /// <summary>You finished a lap: the time, and how it compares with your best so far in this race.</summary>
        public void Lap(int lapNumber, float lapSec)
        {
            string time = Hud.FormatTime(lapSec);
            if (bestLap < 0f) { bestLap = lapSec; Push("lap", "LAP " + lapNumber + "   " + time, "First timed lap", new Color(0.3f, 0.7f, 1f)); return; }
            float delta = lapSec - bestLap;
            if (delta < 0f)
            {
                Push("lap", "LAP " + lapNumber + "   " + time, "BEST LAP OF THE RACE   " + delta.ToString("0.00") + " s", new Color(0.7f, 0.35f, 1f), 5f);
                bestLap = lapSec;
            }
            else Push("lap", "LAP " + lapNumber + "   " + time, "+" + delta.ToString("0.00") + " s to your best", delta < 0.3f ? new Color(0.3f, 0.85f, 0.45f) : new Color(1f, 0.35f, 0.3f));
        }

        public void Draw(float W, float H)
        {
            if (items.Count == 0) return;
            Styles();
            float x = 214f, w = 380f, hItem = 50f;
            int slot = 0;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var it = items[i];
                if (Time.time > it.until) { items.RemoveAt(i); continue; }
                float age = Time.time - it.start, left = it.until - Time.time;
                float slide = Mathf.Clamp01(age / 0.25f), fade = Mathf.Clamp01(left / 0.4f);
                float ox = (1f - slide * slide * (3f - 2f * slide)) * -w;
                float y = H - 226f - slot * (hItem + 6f);
                GUI.color = new Color(0f, 0f, 0f, 0.72f * fade); GUI.DrawTexture(new Rect(x + ox, y, w, hItem), Texture2D.whiteTexture);
                GUI.color = new Color(it.col.r, it.col.g, it.col.b, fade); GUI.DrawTexture(new Rect(x + ox, y, 6f, hItem), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, fade);
                GUI.Label(new Rect(x + ox + 14f, y + 1f, w - 20f, 16f), it.kind.ToUpper(), sKind);
                GUI.Label(new Rect(x + ox + 14f, y + 11f, w - 20f, 24f), it.title, sTitle);
                GUI.Label(new Rect(x + ox + 14f, y + 31f, w - 20f, 18f), it.sub, sSub);
                GUI.color = Color.white;
                slot++;
            }
        }

        // ---------------------------------------------------------------- the weather radar
        /// <summary>A small rain map: you are in the centre, rain cells drift in from one side. The countdown comes from the weather plan.</summary>
        public void DrawRadar(RaceManager rm, float W, float H)
        {
            Styles();
            float x = 16f, y = 96f, s = 96f;
            GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(new Rect(x - 4f, y - 4f, s + 8f, s + 30f), Texture2D.whiteTexture);
            GUI.color = new Color(0.05f, 0.11f, 0.08f, 0.95f); GUI.DrawTexture(new Rect(x, y, s, s), Texture2D.whiteTexture);
            var soft = Effects.Soft();
            // range rings and the cross
            GUI.color = new Color(0.3f, 0.65f, 0.4f, 0.5f);
            for (int r = 1; r <= 2; r++) { float rr = s * 0.5f * r / 2f; GUI.DrawTexture(new Rect(x + s * 0.5f - rr, y + s * 0.5f - rr, rr * 2f, 1f), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(x + s * 0.5f - 1f, y + s * 0.5f - rr, 1f, rr * 2f), Texture2D.whiteTexture); }
            GUI.DrawTexture(new Rect(x, y + s * 0.5f, s, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + s * 0.5f, y, 1f, s), Texture2D.whiteTexture);

            // rain cells: where they are depends on when the weather changes
            float eta = rm.WeatherEta(out bool toRain, out bool hasChange);
            float ang = rm.RadarAngle;
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            string label;
            if (rm.isRaining && !hasChange) { Cell(x, y, s, soft, Vector2.zero, 1.05f, 0.7f); label = "RAIN CONTINUES"; }
            else if (rm.isRaining && hasChange) { float k = Mathf.Clamp01(eta / 60f); Cell(x, y, s, soft, dir * (1f - k) * 0.55f, 1.0f - 0.35f * (1f - k), 0.7f); label = "EASING IN " + Clock(eta); }
            else if (hasChange && toRain)
            {
                float k = Mathf.Clamp01(eta / 60f);
                Cell(x, y, s, soft, dir * (0.12f + 0.78f * k), 0.75f, 0.6f);
                Cell(x, y, s, soft, dir * (0.25f + 0.85f * k) + new Vector2(-dir.y, dir.x) * 0.25f, 0.45f, 0.45f);
                label = "RAIN IN " + Clock(eta);
            }
            else { float t = Time.time * 0.01f; Cell(x, y, s, soft, new Vector2(Mathf.Cos(t + 2f), Mathf.Sin(t + 2f)) * 1.1f, 0.4f, 0.3f); label = "CLEAR"; }
            // you
            GUI.color = Color.white; GUI.DrawTexture(new Rect(x + s * 0.5f - 2f, y + s * 0.5f - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = new Color(0.7f, 0.95f, 0.8f);
            GUI.Label(new Rect(x - 2f, y + s + 2f, s + 8f, 20f), "RADAR  " + label, sSmall);
            GUI.color = Color.white;
        }

        // ---------------------------------------------------------------- the wind
        static Texture2D arrowTex;

        static Texture2D Arrow()
        {
            if (arrowTex != null) return arrowTex;
            const int S = 32;
            arrowTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float fx = (x + 0.5f) / S - 0.5f, fy = (y + 0.5f) / S;                 // fy 0 is the bottom, 1 the tip
                    bool head = fy > 0.55f && Mathf.Abs(fx) < (1f - fy) * 0.55f / 0.45f * 0.5f;
                    bool shaft = fy <= 0.6f && fy > 0.05f && Mathf.Abs(fx) < 0.09f;
                    arrowTex.SetPixel(x, y, head || shaft ? Color.white : new Color(1f, 1f, 1f, 0f));
                }
            arrowTex.Apply();
            return arrowTex;
        }

        /// <summary>A small wind gauge: the arrow shows where the wind blows relative to your car (up = the way you are heading).</summary>
        public void DrawWind(WindField wind, PlayerCar p, float clock, float W, float H)
        {
            if (wind == null || p == null || wind.mean < 0.3f) return;
            Styles();
            float x = 16f, y = 232f, w = 104f, h = 62f;
            float head, cross;
            wind.Relative(new Vector2(Mathf.Sin(p.heading), Mathf.Cos(p.heading)), clock, out head, out cross);
            float spd = wind.SpeedAt(clock);
            GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(new Rect(x - 4f, y - 4f, w + 8f, h + 8f), Texture2D.whiteTexture);
            GUI.color = new Color(0.7f, 0.95f, 0.8f);
            GUI.Label(new Rect(x, y - 2f, w, 16f), "WIND  " + spd.ToString("0") + " m/s", sSmall);
            float ang = Mathf.Atan2(cross, -head) * Mathf.Rad2Deg;
            Vector2 c = new Vector2(x + 24f, y + 38f);
            float len = Mathf.Lerp(20f, 40f, Mathf.Clamp01(spd / 12f));
            var saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, c);
            GUI.color = new Color(0.45f, 0.85f, 1f);
            GUI.DrawTexture(new Rect(c.x - len * 0.3f, c.y - len * 0.5f, len * 0.6f, len), Arrow());
            GUI.matrix = saved;
            GUI.color = new Color(0.85f, 0.9f, 0.97f);
            string a = head > 1.5f ? "head " + head.ToString("0") : head < -1.5f ? "tail " + (-head).ToString("0") : "calm";
            string b = Mathf.Abs(cross) > 1.5f ? "cross " + (cross > 0f ? "R " : "L ") + Mathf.Abs(cross).ToString("0") : "";
            GUI.Label(new Rect(x + 50f, y + 16f, w - 48f, 16f), a, sSmall);
            GUI.Label(new Rect(x + 50f, y + 32f, w - 48f, 16f), b, sSmall);
            GUI.color = Color.white;
        }

        static string Clock(float sec) { int m = Mathf.FloorToInt(sec / 60f), s = Mathf.Max(0, Mathf.CeilToInt(sec - m * 60f)); return m + ":" + s.ToString("00"); }

        static void Cell(float x, float y, float s, Texture2D soft, Vector2 off, float size, float alpha)
        {
            float px = x + s * 0.5f + off.x * s * 0.5f, py = y + s * 0.5f + off.y * s * 0.5f, sz = s * size;
            // clip the blob to the radar window by drawing it in a group
            GUI.BeginGroup(new Rect(x, y, s, s));
            GUI.color = new Color(0.25f, 0.6f, 1f, alpha); GUI.DrawTexture(new Rect(px - x - sz * 0.5f, py - y - sz * 0.5f, sz, sz), soft);
            GUI.color = new Color(0.9f, 0.85f, 0.2f, alpha * 0.8f); GUI.DrawTexture(new Rect(px - x - sz * 0.22f, py - y - sz * 0.22f, sz * 0.44f, sz * 0.44f), soft);       // the heavy core
            GUI.EndGroup();
            GUI.color = Color.white;
        }

        // ---------------------------------------------------------------- the sunset glow
        static Texture2D glowTex;

        static Texture2D Glow()
        {
            if (glowTex != null) return glowTex;
            const int S = 64;
            glowTex = new Texture2D(2, S, TextureFormat.RGBA32, false);
            glowTex.wrapMode = TextureWrapMode.Clamp;
            for (int i = 0; i < S; i++)
            {
                float v = (i + 0.5f) / S, d = (v - 0.5f) / 0.18f;
                float a = Mathf.Exp(-d * d);
                for (int j = 0; j < 2; j++) glowTex.SetPixel(j, i, new Color(1f, 1f, 1f, a));
            }
            glowTex.Apply();
            return glowTex;
        }

        /// <summary>A band of orange and pink light along the horizon at dusk.</summary>
        public static void DrawSunset(Camera cam, float timeOfDay, bool wet, float W, float H)
        {
            if (cam == null) return;
            float dusk = Mathf.Sin(Mathf.Clamp01(timeOfDay) * Mathf.PI);
            float night = Mathf.SmoothStep(0f, 1f, timeOfDay);
            float a = dusk * (1f - night * 0.9f) * (wet ? 0.3f : 0.6f);
            if (a < 0.02f) return;
            float pitch = Mathf.Asin(Mathf.Clamp(cam.transform.forward.y, -1f, 1f));
            float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float horizon = H * 0.5f + Mathf.Tan(pitch) / tanHalf * H * 0.5f;
            var g = Glow();
            GUI.color = new Color(1f, 0.52f, 0.22f, a);
            GUI.DrawTexture(new Rect(0f, horizon - H * 0.22f, W, H * 0.44f), g);
            GUI.color = new Color(1f, 0.35f, 0.5f, a * 0.55f);
            GUI.DrawTexture(new Rect(0f, horizon - H * 0.38f, W, H * 0.5f), g);
            GUI.color = Color.white;
        }
    }
}
