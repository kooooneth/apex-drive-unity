using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// Pure track math, ported 1:1 from the HTML game. Everything here works in "web space":
    /// (x, z) are the same numbers as in the HTML version, heading h means forward = (sin h, cos h).
    /// Use TrackModel.W() to turn a web-space position into a Unity world position.
    /// </summary>
    public class TrackModel
    {
        public const int N = 400;
        public const float ROAD_WIDTH = 20f;
        public const float CURB_OUTER = ROAD_WIDTH / 2f + 0.9f;

        // The pit lane has six boxes in a row (10 m apart), a fast lane for cars passing and a working lane next to the
        // garages where cars stop. Distances are metres along the track from the middle of the pit zone.
        public const float PIT_ENTRY = -60f, PIT_BOX_START = -30f, PIT_BOX_END = 30f, PIT_EXIT = 60f;
        public const int PIT_BOX_COUNT = 6;
        public const float PIT_BOX_SPACING = 10f;
        public const float PIT_LANE_HALFWIDTH = 2.4f;
        public const float PIT_WORK_SHIFT = 1.3f;          // stopped cars sit this far toward the garages, passing cars this far toward the track
        public const float PIT_WALL_BUMP = 11f;            // the outer wall moves back this much around the pits so the lane and garages fit
        public static float PitBoxD(int i) { return PIT_BOX_START + (i + 0.5f) * PIT_BOX_SPACING; }
        public const float PIT_GAP = 2.6f, WALL_BUFFER = 1.2f;
        public const float PIT_WALL_THICKNESS = 0.4f, PIT_WALL_HEIGHT = 2.4f, PIT_WALL_CLEARANCE = 0.8f;
        public const float PIT_WALL_OFFSET = CURB_OUTER + PIT_GAP / 2f;

        public readonly TrackDef def;
        public float roadLength;
        public Vector2[] spaced;          // N points evenly spaced by arc length
        public float pitLaneOffset = 6.5f;
        public bool pitLaneIsWide;
        Vector2 pitLaneNormal;

        public TrackModel(TrackDef d)
        {
            def = d;
            BuildSpline();
            ComputePitLaneGeometry();
        }

        // Web space (x, y, z) -> Unity world. Three.js is right handed, Unity is left handed; negating z
        // keeps the circuit looking exactly like the web version instead of mirrored.
        public static Vector3 W(Vector2 xz, float y) { return new Vector3(xz.x, y, -xz.y); }
        public static Vector3 W(float x, float y, float z) { return new Vector3(x, y, -z); }
        public static float Mod(float a, float m) { return ((a % m) + m) % m; }

        // ---------- spline ----------
        static Vector2 CR2(Vector2[] p, float t)
        {
            int n = p.Length;
            float s = t * n;
            int i = Mathf.FloorToInt(s);
            float u = s - i;
            i = ((i % n) + n) % n;
            Vector2 p0 = p[(i - 1 + n) % n], p1 = p[i], p2 = p[(i + 1) % n], p3 = p[(i + 2) % n];
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        static float CR1(float[] p, float t)
        {
            int n = p.Length;
            float s = t * n;
            int i = Mathf.FloorToInt(s);
            float u = s - i;
            i = ((i % n) + n) % n;
            float p0 = p[(i - 1 + n) % n], p1 = p[i], p2 = p[(i + 1) % n], p3 = p[(i + 2) % n];
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        void BuildSpline()
        {
            const int M = 8000;
            var dense = new Vector2[M + 1];
            var cum = new float[M + 1];
            for (int i = 0; i <= M; i++)
            {
                dense[i] = CR2(def.points, (float)i / M);
                if (i > 0) cum[i] = cum[i - 1] + Vector2.Distance(dense[i], dense[i - 1]);
            }
            roadLength = cum[M];
            spaced = new Vector2[N];
            int j = 0;
            for (int k = 0; k < N; k++)
            {
                float target = roadLength * k / N;
                while (j < M - 1 && cum[j + 1] < target) j++;
                float seg = Mathf.Max(1e-5f, cum[j + 1] - cum[j]);
                spaced[k] = Vector2.Lerp(dense[j], dense[j + 1], (target - cum[j]) / seg);
            }
        }

        // ---------- queries ----------
        public Vector2 PointAt(float d)
        {
            float dd = Mod(d, roadLength);
            float idx = dd / roadLength * N;
            int i0 = Mathf.FloorToInt(idx) % N;
            int i1 = (i0 + 1) % N;
            float f = idx - Mathf.Floor(idx);
            return Vector2.Lerp(spaced[i0], spaced[i1], f);
        }

        public Vector2 TangentAt(float d)
        {
            Vector2 a = PointAt(d), b = PointAt(d + 1.5f);
            return (b - a).normalized;
        }

        public Vector2 TangentSmoothAt(float d)
        {
            Vector2 a = PointAt(d - 9f), b = PointAt(d + 9f);
            return (b - a).normalized;
        }

        public static Vector2 Normal(Vector2 t) { return new Vector2(-t.y, t.x); }

        public float CurvatureAt(float d)
        {
            Vector2 t0 = TangentAt(d - 6f), t1 = TangentAt(d + 6f);
            float dot = Mathf.Clamp(Vector2.Dot(t0, t1), -1f, 1f);
            return Mathf.Acos(dot);
        }

        public float ElevationAt(float d)
        {
            if (def.elevation == null) return 0f;
            return CR1(def.elevation, Mod(d, roadLength) / roadLength);
        }

        public float MinElevation()
        {
            if (def.elevation == null) return 0f;
            float m = 0f;
            foreach (float e in def.elevation) m = Mathf.Min(m, e);
            return m;
        }

        public float CrestFactorAt(float d)
        {
            if (def.elevation == null) return 1f;
            float d2 = ElevationAt(d + 5f) - 2f * ElevationAt(d) + ElevationAt(d - 5f);
            return Mathf.Max(0.72f, 1f + d2 * 0.06f);
        }

        public float WallOffsetAt(float d)
        {
            float curv = CurvatureAt(d);
            float localRadius = 12f / Mathf.Max(curv, 0.02f);
            float w = Mathf.Max(CURB_OUTER + 1f, Mathf.Min(CURB_OUTER + 8f, localRadius * 0.55f));
            if (def.tightWalls) w = CURB_OUTER + 1.3f;                       // street circuits: walls just beyond the kerbs
            return w + (pitReady ? PitBump(d) : 0f);
        }

        float PitBump(float d)
        {
            float rel = Mathf.Abs(Mod(d - pitZone + roadLength * 0.5f, roadLength) - roadLength * 0.5f);
            float full = PIT_EXIT + 6f, fade = 30f;
            if (rel <= full) return PIT_WALL_BUMP;
            if (rel >= full + fade) return 0f;
            return PIT_WALL_BUMP * 0.5f * (1f + Mathf.Cos((rel - full) / fade * Mathf.PI));
        }

        // ---------- pit lane ----------
        // The pit lane runs parallel to the track, on the outside of the bend, at the straightest stretch near the
        // nominal spot. Its centre line is the track's own centre line pushed sideways by an offset that ramps up
        // from the entry, holds along the six boxes, and ramps back down at the exit.
        float pitZone;
        int pitSide = 1;
        bool pitReady;
        const float LANE_STEP = 2f;
        Vector2[] lanePts; float[] laneS;
        public float PitZoneDist { get { return pitZone; } }

        float SpanMaxCurv(float centre)
        {
            float max = 0f;
            for (float k = PIT_ENTRY - 4f; k <= PIT_EXIT + 4f; k += 4f) max = Mathf.Max(max, CurvatureAt(centre + k));
            return max;
        }

        void ComputePitLaneGeometry()
        {
            float nominal = def.pitFrac * roadLength;
            pitZone = nominal;
            if (SpanMaxCurv(nominal) > 0.16f)
            {
                // the nominal spot is on a tight bend: use the straightest stretch of the lap instead
                float best = float.MaxValue;
                for (float d = 0f; d < roadLength; d += 6f)
                {
                    float off = Mathf.Abs(Mod(d - nominal + roadLength * 0.5f, roadLength) - roadLength * 0.5f);
                    float score = SpanMaxCurv(d) + off * 0.00005f;
                    if (score < best) { best = score; pitZone = d; }
                }
            }
            // put the lane on the outside of any bend
            float turn = 0f;
            for (float k = PIT_ENTRY; k <= PIT_EXIT; k += 6f)
            {
                Vector2 t0 = TangentAt(pitZone + k - 6f), t1 = TangentAt(pitZone + k + 6f);
                turn += t0.x * t1.y - t0.y * t1.x;
            }
            pitSide = turn > 0.02f ? -1 : 1;
            pitLaneIsWide = true;
            pitLaneOffset = CURB_OUTER + PIT_GAP + PIT_LANE_HALFWIDTH;
            pitReady = true;

            int n = Mathf.RoundToInt((PIT_EXIT - PIT_ENTRY) / LANE_STEP) + 1;
            lanePts = new Vector2[n]; laneS = new float[n];
            for (int i = 0; i < n; i++)
            {
                float d = pitZone + PIT_ENTRY + i * LANE_STEP;
                lanePts[i] = LaneXZ(d, PitLaneOffsetAt(d));
                laneS[i] = i == 0 ? 0f : laneS[i - 1] + Vector2.Distance(lanePts[i], lanePts[i - 1]);
            }
        }

        /// <summary>The direction from the track toward the garages (unit vector, web space).</summary>
        public Vector2 PitLaneNormalAt(float d) { return Normal(TangentSmoothAt(d)) * pitSide; }

        Vector2 LaneXZ(float d, float offset) { return PointAt(d) + PitLaneNormalAt(d) * offset; }

        public float PitLaneOffsetAt(float distAlong)
        {
            float d = RelPit(distAlong);
            float factor;
            if (d < PIT_ENTRY || d > PIT_EXIT) factor = 0f;
            else if (d < PIT_BOX_START) factor = (d - PIT_ENTRY) / (PIT_BOX_START - PIT_ENTRY);
            else if (d <= PIT_BOX_END) factor = 1f;
            else factor = 1f - (d - PIT_BOX_END) / (PIT_EXIT - PIT_BOX_END);
            return Mathf.Clamp01(factor) * pitLaneOffset;
        }

        float RelPit(float distAlong)
        {
            return Mod(distAlong - PitZoneDist + roadLength / 2f, roadLength) - roadLength / 2f;
        }

        /// <summary>Position (web space x,y,z) of the pit lane centre line at this distance, or false outside the pit zone.</summary>
        public bool PitLanePosAt(float distAlong, out Vector3 pos)
        {
            pos = Vector3.zero;
            float d = RelPit(distAlong);
            if (d < PIT_ENTRY || d > PIT_EXIT) return false;
            Vector2 p = LaneXZ(distAlong, PitLaneOffsetAt(distAlong));
            pos = new Vector3(p.x, ElevationAt(distAlong) + 0.02f, p.y);
            return true;
        }

        /// <summary>Pit wall centre (web space), fixed offset between curb and lane; false where the wall doesn't exist.</summary>
        public bool PitWallPosAt(float distAlong, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (!pitLaneIsWide) return false;
            Vector3 lane;
            if (!PitLanePosAt(distAlong, out lane)) return false;
            Vector2 center = PointAt(distAlong);
            Vector2 delta = new Vector2(lane.x, lane.z) - center;
            float laneDist = delta.magnitude;
            if (laneDist < 0.05f) return false;
            float laneInner = laneDist - PIT_LANE_HALFWIDTH;
            if (laneInner < PIT_WALL_OFFSET + PIT_WALL_THICKNESS / 2f + PIT_WALL_CLEARANCE) return false;
            Vector2 w = center + delta / laneDist * PIT_WALL_OFFSET;
            pos = new Vector3(w.x, ElevationAt(distAlong) + 0.02f, w.y);
            return true;
        }

        /// <summary>
        /// Distance from p to the pit lane's centre line, and how far along the lane p is relative to the middle of the
        /// box row (negative = before it). Works from the car's real position, so it stays right however the lap odometer drifts.
        /// </summary>
        public float DistToPitLane(Vector2 p, out float alongBox)
        {
            float best = float.MaxValue;
            alongBox = 0f;
            float centreS = laneS[Mathf.RoundToInt(-PIT_ENTRY / LANE_STEP)];
            for (int i = 0; i < lanePts.Length - 1; i++)
            {
                Vector2 a = lanePts[i], b = lanePts[i + 1], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                float d = Vector2.Distance(a + ab * t, p);
                if (d < best) { best = d; alongBox = laneS[i] + t * (laneS[i + 1] - laneS[i]) - centreS; }
            }
            return best;
        }

        /// <summary>How far along the lane (same scale as DistToPitLane's alongBox) the centre of box i is.</summary>
        public float PitBoxAlong(int i)
        {
            float f = (PitBoxD(i) - PIT_ENTRY) / LANE_STEP;
            int i0 = Mathf.Clamp(Mathf.FloorToInt(f), 0, laneS.Length - 2);
            float s = Mathf.Lerp(laneS[i0], laneS[i0 + 1], f - i0);
            return s - laneS[Mathf.RoundToInt(-PIT_ENTRY / LANE_STEP)];
        }

        /// <summary>
        /// Distance along the lap of the point on the centre line closest to p (projected onto the segment, so
        /// it is smooth). Use this - not the lap odometer - to look up the road height under a car.
        /// </summary>
        public float ProjectDist(Vector2 p)
        {
            Vector2 near; int i;
            NearestCenter(p, out near, out i);
            float best = float.MaxValue, bestT = 0f; int bestSeg = i;
            for (int s = -1; s <= 0; s++)
            {
                int a = ((i + s) % N + N) % N, b = (a + 1) % N;
                Vector2 ab = spaced[b] - spaced[a];
                float t = Mathf.Clamp01(Vector2.Dot(p - spaced[a], ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                float d = (spaced[a] + ab * t - p).sqrMagnitude;
                if (d < best) { best = d; bestT = t; bestSeg = a; }
            }
            return (bestSeg + bestT) / N * roadLength;
        }

        /// <summary>
        /// Where a car stops in pit box i (in the working lane) and the axes around it: centre, height, heading, forward and
        /// right (web space) and outSign = +1 if the car's right-hand side is the garage (outer) side.
        /// </summary>
        public void PitBoxFrame(int i, out Vector2 c, out float y, out float heading, out Vector2 f, out Vector2 right, out float outSign)
        {
            float d = PitZoneDist + PitBoxD(i);
            Vector2 outward = PitLaneNormalAt(d);
            Vector2 lane = LaneXZ(d, pitLaneOffset);
            f = (LaneXZ(d + 1.5f, pitLaneOffset) - lane).normalized;
            c = lane + outward * PIT_WORK_SHIFT;
            y = ElevationAt(d) + 0.02f;
            heading = Mathf.Atan2(f.x, f.y);
            right = new Vector2(f.y, -f.x);
            outSign = Vector2.Dot(right, outward) >= 0f ? 1f : -1f;
        }

        float[] lineOffsets;
        /// <summary>Sideways offset of the racing line at each spaced point (outside on entry, inside at the apex, outside on exit).</summary>
        public float[] LineOffsets
        {
            get
            {
                if (lineOffsets != null) return lineOffsets;
                var sc = new float[N];
                for (int i = 0; i < N; i++)
                {
                    float d = (float)i / N * roadLength;
                    Vector2 t0 = TangentAt(d - 6f), t1 = TangentAt(d + 6f);
                    sc[i] = t0.x * t1.y - t0.y * t1.x;                      // signed turn: + toward the Normal side
                }
                var off = new float[N];
                for (int i = 0; i < N; i++) off[i] = 6f * (sc[i] - 0.45f * (sc[(i + 12) % N] + sc[(i - 12 + N) % N]));
                for (int pass = 0; pass < 5; pass++)
                {
                    var tmp = new float[N];
                    for (int i = 0; i < N; i++) tmp[i] = (off[(i - 1 + N) % N] + off[i] * 2f + off[(i + 1) % N]) * 0.25f;
                    off = tmp;
                }
                for (int i = 0; i < N; i++) off[i] = Mathf.Clamp(off[i], -4f, 4f);
                lineOffsets = off;
                return lineOffsets;
            }
        }

        public float LineOffsetAt(float d)
        {
            float idx = Mod(d, roadLength) / roadLength * N;
            int i0 = Mathf.FloorToInt(idx) % N, i1 = (i0 + 1) % N;
            float[] lo = LineOffsets;
            return Mathf.Lerp(lo[i0], lo[i1], idx - Mathf.Floor(idx));
        }

        /// <summary>Which side of the centre line p is on (same sign convention the AI cars use for their lateral offset).</summary>
        public float SignedLateral(Vector2 p)
        {
            float d = ProjectDist(p);
            return Vector2.Dot(p - PointAt(d), Normal(TangentAt(d)));
        }

        /// <summary>Nearest spaced-point lookup used for off-track and wall checks.</summary>
        public float NearestCenter(Vector2 p, out Vector2 nearest, out int index)
        {
            float best = float.MaxValue; int bi = 0;
            for (int i = 0; i < N; i++)
            {
                float dd = (spaced[i] - p).sqrMagnitude;
                if (dd < best) { best = dd; bi = i; }
            }
            nearest = spaced[bi]; index = bi;
            return Mathf.Sqrt(best);
        }
    }
}
