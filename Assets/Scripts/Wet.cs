using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// Standing water. A few puddles sit on the racing surface; they fill as the road gets wet. Drive through one fast on
    /// the wrong tyres and the car aquaplanes: no steering, no braking, a twitchy rear.
    /// </summary>
    public class Puddles
    {
        class Puddle { public float d, len, lat, width, thresh, drift; public GameObject go; }
        TrackModel track;
        readonly List<Puddle> list = new List<Puddle>();
        Material mat;
        GameObject root;

        public static Puddles Build(TrackModel m, Transform parent)
        {
            var p = new Puddles { lapLength = m.roadLength, track = m };
            var rnd = new System.Random(m.def.id.GetHashCode() ^ 0x5bd1);
            int count = Mathf.Max(3, Mathf.RoundToInt(m.roadLength / 180f));
            for (int tries = 0; tries < 300 && p.list.Count < count; tries++)
            {
                float d = (float)rnd.NextDouble() * m.roadLength;
                float fromPit = Mathf.Abs(TrackModel.Mod(d - m.PitZoneDist + m.roadLength * 0.5f, m.roadLength) - m.roadLength * 0.5f);
                if (fromPit < TrackModel.PIT_EXIT + 25f) continue;                       // not in the pit lane approach
                if (Mathf.Min(d, m.roadLength - d) < 60f) continue;                       // keep the start line clear
                bool ok = true;
                foreach (var q in p.list)
                    if (Mathf.Abs(TrackModel.Mod(q.d - d + m.roadLength * 0.5f, m.roadLength) - m.roadLength * 0.5f) < 50f) ok = false;
                if (!ok) continue;
                p.list.Add(new Puddle
                {
                    d = d, len = 14f + (float)rnd.NextDouble() * 14f,
                    lat = ((float)rnd.NextDouble() * 2f - 1f) * 5.5f, width = 3.5f + (float)rnd.NextDouble() * 2.5f,
                    thresh = (float)rnd.NextDouble() * 0.7f,             // the water level at which this puddle appears
                    drift = rnd.NextDouble() < 0.5 ? -1f : 1f             // which way the water creeps across the road
                });
            }

            p.mat = Mats.Fade(new Color(0.04f, 0.06f, 0.09f), 0f);
            p.root = new GameObject("Puddles"); p.root.transform.SetParent(parent, false);
            const int S = 10;
            foreach (var q in p.list)
            {
                var mb = new MeshBuilder();
                for (int i = 0; i < S; i++)
                {
                    float s0 = (float)i / S, s1 = (float)(i + 1) / S;
                    float w0 = q.width * Mathf.Sqrt(Mathf.Max(0f, 1f - (2f * s0 - 1f) * (2f * s0 - 1f)));
                    float w1 = q.width * Mathf.Sqrt(Mathf.Max(0f, 1f - (2f * s1 - 1f) * (2f * s1 - 1f)));
                    float d0 = q.d + (s0 - 0.5f) * q.len, d1 = q.d + (s1 - 0.5f) * q.len;
                    mb.Quad(P(m, d0, q.lat - w0 * 0.5f), P(m, d0, q.lat + w0 * 0.5f), P(m, d1, q.lat + w1 * 0.5f), P(m, d1, q.lat - w1 * 0.5f), Vector3.up);
                }
                q.go = mb.Build("Puddle", p.root.transform, p.mat);
                q.go.SetActive(false);
            }
            return p;
        }

        static Vector3 P(TrackModel m, float d, float offset)
        {
            Vector2 n = TrackModel.Normal(m.TangentAt(d));
            return TrackModel.W(m.PointAt(d) + n * offset, m.ElevationAt(d) + 0.03f);
        }

        /// <summary>Call with the road wetness: puddles only appear once the road is properly wet.</summary>
        /// <param name="level">how much standing water has built up (0..1): it grows through a long wet spell, so more and bigger puddles appear</param>
        public void Update(float wetness, float level = 1f)
        {
            this.level = level;
            float a = Mathf.Clamp01((wetness - 0.45f) * 1.4f) * (0.3f + 0.4f * level);
            foreach (var q in list) if (q.go != null) q.go.SetActive(a > 0.02f && level >= q.thresh);
            if (mat != null) mat.color = new Color(0.04f, 0.06f, 0.09f, a);
        }

        float level = 1f;

        /// <summary>The water moves: in a long wet spell the puddles creep sideways across the road (and back to the middle when it dries), so the line to take changes.</summary>
        public void Drift(float dt, bool raining)
        {
            if (track == null) return;
            foreach (var q in list)
            {
                float want = raining ? q.drift * 6.0f : 0f;
                if (q.go == null) continue;
                float move = Mathf.Clamp(want - q.lat, -0.12f * dt, 0.12f * dt);
                if (Mathf.Abs(move) < 1e-6f) continue;
                q.lat += move;
                Vector2 n = TrackModel.Normal(track.TangentAt(q.d));
                q.go.transform.position += TrackModel.W(n * move, 0f);
            }
        }

        /// <summary>0..1 how deep the water is under a car at this distance along the lap and sideways offset (0 = none).</summary>
        public float DepthAt(float dist, float lateral)
        {
            foreach (var q in list)
            {
                if (level < q.thresh) continue;                                      // this puddle has not formed yet
                float dd = Mathf.Repeat(dist - q.d + lapLength * 0.5f, lapLength) - lapLength * 0.5f;
                if (Mathf.Abs(dd) > q.len * 0.5f) continue;
                float along = 1f - Mathf.Abs(dd) / (q.len * 0.5f);
                float side = 1f - Mathf.Abs(lateral - q.lat) / (q.width * 0.5f + 1f);
                if (side > 0f) return Mathf.Clamp01(Mathf.Min(along * 2f, side * 1.5f)) * (0.55f + 0.45f * level);
            }
            return 0f;
        }

        float lapLength = 1f;
    }
}
