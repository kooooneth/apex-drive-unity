using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>The rear wing's upper flap: DRS tips it open (less drag) and it snaps shut again.</summary>
    public class DrsFlap : MonoBehaviour
    {
        public Transform flap;
        Quaternion closedRot; Vector3 closedPos; bool init;

        public void Set(float open)
        {
            if (flap == null) return;
            if (!init) { closedRot = flap.localRotation; closedPos = flap.localPosition; init = true; }
            flap.localRotation = closedRot * Quaternion.Euler(-17f * open, 0f, 0f);
            flap.localPosition = closedPos + new Vector3(0f, 0.045f * open, 0f);
        }
    }

    public class DrsZone { public float start, len, detect; }

    /// <summary>
    /// DRS zones are found on the circuit's longest straights: each has a detection point some way before it. Cross the detection point within
    /// one second of the car ahead and the flap may be opened in the zone, until you brake or the zone ends.
    /// </summary>
    public static class Drs
    {
        static TrackModel cachedFor; static List<DrsZone> cached;

        const float STEP = 5f;

        public static List<DrsZone> Zones(TrackModel tm)
        {
            if (cachedFor == tm && cached != null) return cached;
            cachedFor = tm; cached = Find(tm);
            return cached;
        }

        static List<DrsZone> Find(TrackModel tm)
        {
            float L = tm.roadLength;
            int n = Mathf.Max(8, Mathf.RoundToInt(L / STEP));
            float step = L / n;
            var kap = new float[n];
            for (int i = 0; i < n; i++)
            {
                float d = i * step;
                Vector2 a = tm.TangentAt(d - 15f), b = tm.TangentAt(d + 15f);
                kap[i] = Mathf.Acos(Mathf.Clamp(Vector2.Dot(a, b), -1f, 1f)) / 30f;
            }
            // these circuits are compact, so a "straight" is the flattest stretch the circuit has: start with a strict radius and relax it
            // until there is something long enough (a tenth of the lap, at least 90 m)
            float minRun = Mathf.Clamp(0.11f * L, 90f, 280f);
            float[] radii = { 450f, 300f, 220f, 160f, 120f, 90f, 70f };
            var result = new List<DrsZone>();
            int wanted = L >= 1500f ? 2 : 1;
            foreach (float r in radii)
            {
                var zones = FindRuns(kap, n, step, 1f / r, minRun, 20f);
                zones.Sort((x, y) => y.len.CompareTo(x.len));
                foreach (var z in zones)
                {
                    bool clash = false;
                    foreach (var q in result) if (Mathf.Abs(TrackModel.Mod(z.start - q.start + L * 0.5f, L) - L * 0.5f) < 0.3f * L) clash = true;
                    if (!clash && result.Count < wanted) result.Add(z);
                }
                if (result.Count >= wanted) break;
            }
            result.Sort((x, y) => x.start.CompareTo(y.start));
            return result;
        }

        static List<DrsZone> FindRuns(float[] kap, int n, float step, float maxKappa, float minRun, float gapOk)
        {
            var res = new List<DrsZone>();
            float L = n * step;
            // start the scan on a bend so a straight is never cut by the start of the array
            int s0 = -1;
            for (int i = 0; i < n; i++) if (kap[i] >= maxKappa) { s0 = i; break; }
            if (s0 < 0) return res;
            int run = -1, lastStraight = -1;
            for (int k = 1; k <= n + 1; k++)
            {
                int i = (s0 + k) % n;
                bool straight = k <= n && kap[i] < maxKappa;
                if (straight)
                {
                    if (run < 0) run = k;
                    lastStraight = k;
                }
                else if (run >= 0 && (k - lastStraight) * step > gapOk)
                {
                    float runLen = (lastStraight - run + 1) * step;
                    if (runLen >= minRun)
                    {
                        float start = ((s0 + run) % n) * step + 12f;
                        float len = runLen - 12f - 45f;                    // the zone begins just after the corner and ends before the next braking point
                        float detect = Mathf.Clamp(0.07f * L, 60f, 110f);
                        if (len >= Mathf.Max(70f, minRun * 0.6f)) res.Add(new DrsZone { start = TrackModel.Mod(start, L), len = len, detect = TrackModel.Mod(start - detect, L) });
                    }
                    run = -1;
                }
            }
            return res;
        }

        // ---------------------------------------------------------------- trackside boards
        static Font font;

        public static void BuildSigns(TrackModel tm, Transform parent)
        {
            var root = new GameObject("DRS");
            root.transform.SetParent(parent, false);
            if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "Liberation Sans" }, 64);
            Material board = Mats.Make(new Color(0.05f, 0.34f, 0.2f), 0.2f, 0f);
            Material post = Mats.Make(new Color(0.55f, 0.57f, 0.6f), 0.4f, 0.4f);
            Material white = Mats.Make(new Color(0.93f, 0.94f, 0.96f), 0.2f, 0f);
            foreach (var z in Zones(tm))
            {
                Sign(root.transform, tm, z.start, "DRS", board, post);
                Sign(root.transform, tm, z.detect, "DRS\nDETECTION", board, post);
                Sign(root.transform, tm, TrackModel.Mod(z.start + z.len, tm.roadLength), "DRS END", board, post);
                // the detection line across the road
                Vector2 p = tm.PointAt(z.detect), t = tm.TangentAt(z.detect);
                var line = Prim.Box(root.transform, TrackModel.W(p, tm.ElevationAt(z.detect) + 0.04f), new Vector3(TrackModel.ROAD_WIDTH * 0.92f, 0.015f, 0.45f), white);
                line.transform.rotation = Quaternion.Euler(0f, (Mathf.PI - Mathf.Atan2(t.x, t.y)) * Mathf.Rad2Deg, 0f);
            }
        }

        static void Sign(Transform parent, TrackModel tm, float d, string text, Material board, Material post)
        {
            Vector2 p = tm.PointAt(d), t = tm.TangentAt(d), n = TrackModel.Normal(t);
            float lateral = TrackModel.ROAD_WIDTH * 0.5f + 5.5f;
            Vector2 pos = p + n * lateral;
            float y = tm.ElevationAt(d);
            Quaternion rot = Quaternion.Euler(0f, (Mathf.PI - Mathf.Atan2(t.x, t.y)) * Mathf.Rad2Deg, 0f);
            Vector3 basePos = TrackModel.W(pos, y);
            foreach (float s in new[] { -0.9f, 0.9f })
            {
                var leg = Prim.Box(parent, basePos + rot * new Vector3(s, 1.1f, 0f), new Vector3(0.1f, 2.2f, 0.1f), post);
                leg.transform.rotation = rot;
            }
            var b = Prim.Box(parent, basePos + rot * new Vector3(0f, 2.5f, 0f), new Vector3(2.6f, 1.2f, 0.08f), board);
            b.transform.rotation = rot;
            var go = new GameObject("drsText");
            go.transform.SetParent(parent, false);
            go.transform.position = basePos + rot * new Vector3(0f, 2.5f, 0.06f);
            go.transform.rotation = rot;
            var tmesh = go.AddComponent<TextMesh>();
            tmesh.font = font; tmesh.text = text; tmesh.fontSize = 64; tmesh.characterSize = text.Contains("\n") ? 0.055f : 0.08f;
            tmesh.anchor = TextAnchor.MiddleCenter; tmesh.alignment = TextAlignment.Center; tmesh.color = Color.white;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
    }

    /// <summary>The DRS state of one car (yours or an AI car): detection, the zone, and the flap.</summary>
    public class DrsTracker
    {
        readonly RaceManager rm; readonly Racer me;
        DrsFlap flap;
        readonly bool[] enabled = new bool[4];
        float prev = float.NaN; int zone = -1; bool closedByBrake;
        public float open;                 // the flap, 0 shut .. 1 open
        public bool available, active;     // may open it now / is open now
        public float lastGap = -1f;        // seconds to the car ahead at the last detection point

        public DrsTracker(RaceManager manager, Racer car) { rm = manager; me = car; flap = car.go != null ? car.go.GetComponent<DrsFlap>() : null; }

        public void Reset() { for (int i = 0; i < enabled.Length; i++) enabled[i] = false; zone = -1; prev = float.NaN; closedByBrake = false; }

        static bool Crossed(float a, float b, float x, float L)
        {
            float moved = b - a;
            if (moved <= 0f || moved > 60f) return false;
            return TrackModel.Mod(x - a, L) <= moved;
        }

        float GapSeconds(float dist, float speed)
        {
            float L = rm.track.roadLength, best = 1e9f;
            foreach (var c in rm.racers)
            {
                if (c == me || c.gone || c.dnf || c.pitStopActive) continue;
                float a = TrackModel.Mod(c.dist - dist, L);
                if (a > 1f && a < best) best = a;
            }
            return best / Mathf.Max(30f, speed);
        }

        /// <param name="want">the driver is asking for it open</param>
        /// <param name="braking">on the brakes (or heading for a corner): the flap shuts and stays shut until the zone ends</param>
        /// <param name="blocked">in the pit lane, off the track, etc.</param>
        public bool Update(float dt, float dist, float speed, bool want, bool braking, bool blocked)
        {
            var tm = rm.track;
            var zones = Drs.Zones(tm);
            float L = tm.roadLength;
            int rule = rm.DrsRule;
            if (rule == 0 || blocked)
            {
                for (int i = 0; i < enabled.Length; i++) enabled[i] = false;
                available = false; active = false;
                Animate(dt);
                prev = dist;
                return false;
            }
            if (!float.IsNaN(prev))
            {
                for (int z = 0; z < zones.Count && z < enabled.Length; z++)
                    if (Crossed(prev, dist, zones[z].detect, L))
                    {
                        lastGap = rule == 2 ? 0f : GapSeconds(dist, speed);
                        enabled[z] = rule == 2 || lastGap < 1.0f;
                    }
            }
            prev = dist;

            int now = -1;
            for (int z = 0; z < zones.Count && z < enabled.Length; z++) if (TrackModel.Mod(dist - zones[z].start, L) < zones[z].len) now = z;
            if (now != zone)
            {
                if (zone >= 0) enabled[zone] = false;                         // leaving the zone switches it off for the next time round
                closedByBrake = false;
                zone = now;
                if (zone >= 0 && rule == 2) enabled[zone] = true;
            }
            available = zone >= 0 && enabled[zone];
            if (available && braking) closedByBrake = true;
            active = available && want && !closedByBrake;
            Animate(dt);
            return active;
        }

        void Animate(float dt)
        {
            open = Mathf.MoveTowards(open, active ? 1f : 0f, dt * (active ? 4f : 7f));
            me.drsOpen = open;
            if (flap == null && me.go != null) flap = me.go.GetComponent<DrsFlap>();
            if (flap != null) flap.Set(open);
        }
    }
}
