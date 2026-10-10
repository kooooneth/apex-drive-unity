using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The driver's visor: rain droplets collect on it and the wind slowly clears them, it mists up when you go slowly in the
    /// wet or in fog, and a tear-off strip (H) peels the dirty layer away. A low sun also lingers in your eyes for a moment
    /// after you look away from it.
    /// </summary>
    public class Visor
    {
        struct Drop { public Vector2 p; public float size, vx, vy, alpha, wobble; }
        readonly List<Drop> drops = new List<Drop>();
        public float fog;                  // 0..1 condensation
        public int tearOffs = 4;           // strips left on the visor
        float haze;                        // spray from the car ahead in the wet
        float glare;                       // smoothed sun glare (it fades more slowly than it arrives)
        float nextWarn;
        float tearFlash;
        readonly System.Random rng = new System.Random(5);

        public void Reset() { drops.Clear(); fog = 0f; tearOffs = 4; glare = 0f; tearFlash = 0f; }

        public int DropCount { get { return drops.Count; } }
        /// <summary>How dirty the visor is, 0..1: drops and mist scatter bright light.</summary>
        public float Dirt { get { return Mathf.Clamp01(drops.Count / 45f + fog); } }

        public void TearOff(RaceManager rm)
        {
            if (tearOffs <= 0) { rm.Flash("NO TEAR-OFFS LEFT"); return; }
            if (drops.Count < 3 && fog < 0.1f) return;                     // nothing worth tearing off
            tearOffs--; drops.Clear(); fog = 0f; tearFlash = 0.25f;
            rm.Flash("VISOR CLEAR  (" + tearOffs + " left)");
        }

        float R() { return (float)rng.NextDouble(); }

        public void Update(RaceManager rm, PlayerCar p, float dt, bool cockpit)
        {
            if (!cockpit) { drops.Clear(); fog = Mathf.Max(0f, fog - dt * 0.5f); return; }
            float speed = p != null ? p.speed : 0f;
            // following a car closely in the wet: its spray hangs in front of you
            float hazeTarget = 0f;
            if (p != null && rm.roadWetness > 0.35f && speed > 35f)
            {
                float gap = 1e9f;
                foreach (var c in rm.racers)
                {
                    if (c == p || c.gone) continue;
                    float g = TrackModel.Mod(c.dist - p.dist, rm.track.roadLength);
                    if (g > 3f && g < gap) gap = g;
                }
                if (gap < 34f) hazeTarget = Mathf.Clamp01(1f - (gap - 5f) / 29f) * rm.roadWetness;
            }
            haze = Mathf.MoveTowards(haze, hazeTarget, dt * (hazeTarget > haze ? 1.2f : 0.6f));

            // droplets: rain in the air lands on the visor; the faster you go the more arrive and the faster the wind pushes them up and away
            if (rm.isRaining && rm.rainOpacity > 0.05f && drops.Count < 90)
            {
                float rate = rm.rainOpacity * (6f + speed * 0.25f);
                int n = Mathf.FloorToInt(rate * dt + R());
                for (int i = 0; i < n; i++)
                    drops.Add(new Drop { p = new Vector2(R(), R()), size = 10f + R() * 26f, vx = (R() - 0.5f) * 0.01f, vy = 0f, alpha = 0.35f + R() * 0.4f, wobble = R() * 6.28f });
            }
            for (int i = drops.Count - 1; i >= 0; i--)
            {
                var d = drops[i];
                float wind = Mathf.Clamp01(speed / 70f);
                // big drops slide towards the top of the visor in the airflow, small ones just sit and slowly evaporate
                d.vy = -wind * (0.015f + d.size * 0.0012f);
                d.p += new Vector2(d.vx + Mathf.Sin(Time.time * 3f + d.wobble) * 0.002f * wind, d.vy) * dt * 6f;
                d.alpha -= dt * (0.02f + wind * 0.03f + (rm.isRaining ? 0f : 0.05f));
                if (d.alpha <= 0f || d.p.y < -0.05f) drops.RemoveAt(i); else drops[i] = d;
            }

            // condensation: slow in the wet or in fog, cleared by the airflow
            bool damp = rm.isRaining || rm.isFoggy;
            float target = damp ? 0.55f : 0f;
            if (damp && speed < 28f) fog = Mathf.MoveTowards(fog, target, dt * (0.05f + (28f - speed) * 0.002f));
            else fog = Mathf.MoveTowards(fog, 0f, dt * (0.04f + speed * 0.0015f));
            if (tearFlash > 0f) tearFlash -= dt;

            if ((drops.Count > 28 || fog > 0.35f) && Time.time > nextWarn && tearOffs > 0)
            {
                nextWarn = Time.time + 40f;
                rm.Radio(fog > 0.35f ? "Your visor is misting up, tear it off with H." : "The visor is getting dirty, tear one off with H.", 1);
            }
        }

        /// <summary>Smooths the raw glare amount: it comes on quickly and takes a second or two to fade, like eyes adapting.</summary>
        public float SmoothGlare(float raw, float dt)
        {
            glare += (raw - glare) * Mathf.Min(1f, dt * (raw > glare ? 7f : 1.3f));
            return glare;
        }

        public void Draw(float W, float H)
        {
            var soft = Effects.Soft();
            if (haze > 0.02f)
            {
                GUI.color = new Color(0.80f, 0.84f, 0.90f, haze * 0.30f);
                GUI.DrawTexture(new Rect(0f, 0f, W, H), Texture2D.whiteTexture);
                GUI.color = new Color(0.86f, 0.9f, 0.95f, haze * 0.5f);
                GUI.DrawTexture(new Rect(W * 0.2f, H * 0.2f, W * 0.6f, H * 0.5f), soft);                     // the thickest cloud is straight ahead
            }
            if (fog > 0.02f)
            {
                // a greyish haze that is thickest at the edges of the visor
                GUI.color = new Color(0.82f, 0.86f, 0.9f, fog * 0.28f);
                GUI.DrawTexture(new Rect(0f, 0f, W, H), Texture2D.whiteTexture);
                GUI.color = new Color(0.88f, 0.9f, 0.94f, fog * 0.55f);
                float e = 160f;
                GUI.DrawTexture(new Rect(0f, 0f, W, e), soft);
                GUI.DrawTexture(new Rect(0f, H - e, W, e), soft);
            }
            foreach (var d in drops)
            {
                float sx = d.p.x * W, sy = d.p.y * H, s = d.size;
                GUI.color = new Color(0.78f, 0.86f, 0.95f, d.alpha * 0.35f);
                GUI.DrawTexture(new Rect(sx - s * 0.5f, sy - s * 0.5f, s, s * 1.25f), soft);
                GUI.color = new Color(1f, 1f, 1f, d.alpha * 0.45f);                  // the bright speck where the droplet bends the light
                GUI.DrawTexture(new Rect(sx - s * 0.18f, sy - s * 0.3f, s * 0.3f, s * 0.3f), soft);
            }
            if (tearFlash > 0f)
            {
                GUI.color = new Color(1f, 1f, 1f, tearFlash * 1.2f);
                GUI.DrawTexture(new Rect(0f, 0f, W, H), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }
    }
}
