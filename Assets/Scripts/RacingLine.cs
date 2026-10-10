using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The rubbered-in racing line: a dark strip along the quickest path (outside on entry, apex on the
    /// inside, outside on exit) that gets stronger as the race goes on. In the rain the road turns wet and
    /// glossy but the line stays matte and dry-looking - the "dry line" real drivers hunt for.
    /// </summary>
    public class RacingLine
    {
        Material lineMat, roadMat;
        Color roadBase;
        TrackModel tm;

        public static RacingLine Build(TrackModel m, Transform parent)
        {
            var rl = new RacingLine { tm = m };
            int N = TrackModel.N;
            float[] off = m.LineOffsets;

            var mb = new MeshBuilder();
            const float half = 1.6f;
            for (int i = 0; i < N; i++)
            {
                int j = (i + 1) % N;
                Vector3 a0 = Pt(m, i, off[i] + half), a1 = Pt(m, i, off[i] - half);
                Vector3 b0 = Pt(m, j, off[j] + half), b1 = Pt(m, j, off[j] - half);
                mb.Quad(a0, a1, b1, b0, Vector3.up);
            }
            rl.lineMat = Mats.Fade(new Color(0.02f, 0.02f, 0.025f), 0.02f);
            mb.Build("RacingLine", parent, rl.lineMat);
            rl.roadMat = TrackBuilder.RoadMat;
            if (rl.roadMat != null) rl.roadBase = rl.roadMat.color;
            return rl;
        }

        static Vector3 Pt(TrackModel m, int i, float offset)
        {
            int k = i % TrackModel.N;
            float d = (float)k / TrackModel.N * m.roadLength;
            Vector2 n = TrackModel.Normal(m.TangentAt(d));
            return TrackModel.W(m.spaced[k] + n * offset, m.ElevationAt(d) + 0.014f);
        }

        /// <summary>
        /// The grip of the road under a car in the wet. On the racing line the road dries first (more grip as the race goes on, as long as it is not
        /// pouring); off the line the surface stays greasy. The choice between the dry line and a clear path round the water is yours.
        /// </summary>
        public float GripAt(float dist, float lateral, float wetness, float raceTime, float rain)
        {
            if (tm == null || wetness < 0.12f) return 1f;
            float[] lo = tm.LineOffsets;
            int i = Mathf.FloorToInt(Mathf.Repeat(dist, tm.roadLength) / tm.roadLength * lo.Length) % lo.Length;
            float off = Mathf.Abs(lateral - lo[i]);                                   // how far you are from the racing line
            float wetK = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(wetness * 1.6f));
            float dry = Mathf.Clamp01(raceTime / 110f) * (1f - 0.7f * Mathf.Clamp01(rain));
            float on = 1f - Mathf.SmoothStep(0f, 1f, (off - 1.4f) / 2.2f);            // 1 on the line, 0 more than about 3.6 m away
            return 1f + wetK * (0.16f * dry * on - 0.07f * (1f - on));
        }

        /// <param name="raceTime">seconds of racing so far</param>
        public void Update(float raceTime, float wetness)
        {
            float a = Mathf.Clamp01(raceTime / 220f) * 0.5f;
            // on a wet road the racing line is the first strip to dry: it shows as lighter, dry asphalt on the dark wet surface
            float dry = Mathf.Clamp01(raceTime / 110f);
            float wetK = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(wetness * 1.6f));
            Color rubber = new Color(0.02f, 0.02f, 0.025f), dryAsphalt = new Color(0.30f, 0.32f, 0.38f);
            Color lc = Color.Lerp(rubber, dryAsphalt, wetK);
            float la = Mathf.Max(Mathf.Clamp01(a * (1f + 0.5f * wetness)), wetK * (0.15f + 0.7f * dry));
            lineMat.color = new Color(lc.r, lc.g, lc.b, la);
            if (roadMat != null)
            {
                roadMat.color = Color.Lerp(roadBase, roadBase * 0.55f, wetness);
                roadMat.SetFloat("_Glossiness", Mathf.Lerp(0.3f, 0.85f, wetness));
                roadMat.SetFloat("_Smoothness", Mathf.Lerp(0.3f, 0.85f, wetness));
            }
        }
    }
}
