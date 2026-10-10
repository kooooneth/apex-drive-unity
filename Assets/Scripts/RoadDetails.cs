using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The small things on a real road surface: patches of old and new tarmac (a little less or more grip), drain covers at the edges (slippery when
    /// wet) and bumps that shake the car and squash its floor. They are drawn on the road and felt by the car.
    /// </summary>
    public class RoadDetails
    {
        class Patch { public float d, len, lat, width, grip; }
        class Drain { public float d, lat; }
        class Bump { public float d, lat, width, len, size; }

        readonly List<Patch> patches = new List<Patch>();
        readonly List<Drain> drains = new List<Drain>();
        readonly List<Bump> bumps = new List<Bump>();
        float lapLength = 1f;

        static Vector3 P(TrackModel m, float d, float offset, float lift)
        {
            Vector2 n = TrackModel.Normal(m.TangentAt(d));
            return TrackModel.W(m.PointAt(d) + n * offset, m.ElevationAt(d) + lift);
        }

        static bool NearPit(TrackModel m, float d)
        {
            float dd = Mathf.Abs(TrackModel.Mod(d - m.PitZoneDist + m.roadLength * 0.5f, m.roadLength) - m.roadLength * 0.5f);
            return dd < TrackModel.PIT_EXIT + 20f;
        }

        public static RoadDetails Build(TrackModel m, Transform parent)
        {
            var r = new RoadDetails { lapLength = m.roadLength };
            var rnd = new System.Random(m.def.id.GetHashCode() ^ 0x77a1);
            var root = new GameObject("RoadDetails"); root.transform.SetParent(parent, false);
            float L = m.roadLength;

            // ---- patches of old (darker, a little less grip) and new (lighter, a little more grip) tarmac
            var darkMb = new MeshBuilder(); var lightMb = new MeshBuilder();
            int pc = Mathf.Max(5, Mathf.RoundToInt(L / 95f));
            for (int i = 0; i < pc; i++)
            {
                float d = (float)rnd.NextDouble() * L;
                if (NearPit(m, d) || Mathf.Min(d, L - d) < 40f) continue;
                bool fresh = rnd.NextDouble() < 0.45;
                var pt = new Patch { d = d, len = 10f + (float)rnd.NextDouble() * 22f, lat = ((float)rnd.NextDouble() * 2f - 1f) * 5.2f, width = 3.5f + (float)rnd.NextDouble() * 5f, grip = fresh ? 1.025f : 0.965f };
                r.patches.Add(pt);
                var mb = fresh ? lightMb : darkMb;
                const int S = 6;
                for (int s = 0; s < S; s++)
                {
                    float d0 = d + (s / (float)S - 0.5f) * pt.len, d1 = d + ((s + 1) / (float)S - 0.5f) * pt.len;
                    // a rounded patch: the width tapers towards the ends
                    float t0 = Mathf.Sqrt(Mathf.Max(0.1f, 1f - Mathf.Pow(2f * s / S - 1f, 2f))), t1 = Mathf.Sqrt(Mathf.Max(0.1f, 1f - Mathf.Pow(2f * (s + 1f) / S - 1f, 2f)));
                    float w0 = pt.width * t0 * 0.5f, w1 = pt.width * t1 * 0.5f;
                    mb.Quad(P(m, d0, pt.lat - w0, 0.012f), P(m, d0, pt.lat + w0, 0.012f), P(m, d1, pt.lat + w1, 0.012f), P(m, d1, pt.lat - w1, 0.012f), Vector3.up);
                }
            }
            darkMb.Build("OldTarmac", root.transform, Mats.Fade(new Color(0.02f, 0.02f, 0.03f), 0.22f));
            lightMb.Build("NewTarmac", root.transform, Mats.Fade(new Color(0.55f, 0.57f, 0.62f), 0.14f));

            // ---- drain covers along the edges of the road
            var cover = Mats.Make(new Color(0.16f, 0.17f, 0.2f), 0.4f, 0.6f);
            var frame = Mats.Make(new Color(0.34f, 0.35f, 0.38f), 0.3f, 0.5f);
            for (float d = 60f; d < L - 40f; d += 95f + (float)rnd.NextDouble() * 45f)
            {
                if (NearPit(m, d)) continue;
                int side = rnd.NextDouble() < 0.5 ? -1 : 1;
                float lat = side * (7.2f + (float)rnd.NextDouble() * 1.4f);
                r.drains.Add(new Drain { d = d, lat = lat });
                Vector3 c = P(m, d, lat, 0.02f);
                Vector2 t = m.TangentAt(d);
                var rot = Quaternion.LookRotation(TrackModel.W(t, 0f));
                var f = Prim.Box(root.transform, c, new Vector3(0.62f, 0.02f, 0.98f), frame, "drainFrame"); f.transform.rotation = rot;
                var g = Prim.Box(root.transform, c + Vector3.up * 0.012f, new Vector3(0.5f, 0.02f, 0.86f), cover, "drain"); g.transform.rotation = rot;
            }

            // ---- bumps: low ridges across part of the road
            var ridge = Mats.Make(new Color(0.30f, 0.32f, 0.38f), 0.25f);
            int bc = Mathf.Max(2, Mathf.RoundToInt(L / 260f));
            for (int i = 0; i < bc; i++)
            {
                float d = (float)rnd.NextDouble() * L;
                if (NearPit(m, d) || Mathf.Min(d, L - d) < 70f) continue;
                var b = new Bump { d = d, lat = ((float)rnd.NextDouble() * 2f - 1f) * 4f, width = 5f + (float)rnd.NextDouble() * 5f, len = 1.6f, size = 0.4f + (float)rnd.NextDouble() * 0.6f };
                r.bumps.Add(b);
                Vector3 c = P(m, d, b.lat, 0.025f + 0.02f * b.size);
                var q = Prim.Box(root.transform, c, new Vector3(b.width, 0.03f + 0.05f * b.size, b.len), ridge, "bump");
                q.transform.rotation = Quaternion.LookRotation(TrackModel.W(m.TangentAt(d), 0f));
            }
            return r;
        }

        /// <summary>The grip of the surface under a car (patches) and how slippery the drain covers are in the wet.</summary>
        public float GripAt(float dist, float lateral, float wetness)
        {
            float g = 1f;
            foreach (var p in patches)
            {
                float dd = Mathf.Abs(Mathf.Repeat(dist - p.d + lapLength * 0.5f, lapLength) - lapLength * 0.5f);
                if (dd < p.len * 0.5f && Mathf.Abs(lateral - p.lat) < p.width * 0.5f) g *= p.grip;
            }
            if (wetness > 0.15f)
                foreach (var dr in drains)
                {
                    float dd = Mathf.Abs(Mathf.Repeat(dist - dr.d + lapLength * 0.5f, lapLength) - lapLength * 0.5f);
                    if (dd < 0.9f && Mathf.Abs(lateral - dr.lat) < 0.8f) g *= 1f - 0.25f * wetness;
                }
            return g;
        }

        /// <summary>How hard the road bumps the car at this point (0 none .. about 1): the ridge under a wheel line.</summary>
        public float BumpAt(float dist, float lateral)
        {
            foreach (var b in bumps)
            {
                float dd = Mathf.Abs(Mathf.Repeat(dist - b.d + lapLength * 0.5f, lapLength) - lapLength * 0.5f);
                if (dd < b.len * 0.5f + 0.8f && Mathf.Abs(lateral - b.lat) < b.width * 0.5f) return b.size;
            }
            return 0f;
        }

        /// <summary>Is the car on a drain cover right now (for the wet-weather twitch)?</summary>
        public bool OnDrain(float dist, float lateral)
        {
            foreach (var dr in drains)
            {
                float dd = Mathf.Abs(Mathf.Repeat(dist - dr.d + lapLength * 0.5f, lapLength) - lapLength * 0.5f);
                if (dd < 0.9f && Mathf.Abs(lateral - dr.lat) < 0.8f) return true;
            }
            return false;
        }
    }
}
