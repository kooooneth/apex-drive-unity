using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>Builds every piece of track geometry procedurally - no scene authoring needed.</summary>
    public static class TrackBuilder
    {
        const float WALL_THICKNESS = 0.35f, WALL_HEIGHT = 1.15f;

        static Vector3 OffI(TrackModel m, int i, float offset, float yExtra)
        {
            int k = i % TrackModel.N;
            float d = (float)k / TrackModel.N * m.roadLength;
            Vector2 n = TrackModel.Normal(m.TangentAt(d));
            return TrackModel.W(m.spaced[k] + n * offset, m.ElevationAt(d) + yExtra);
        }

        static Vector3 OffD(TrackModel m, float d, float offset, float yExtra, bool smooth = false)
        {
            Vector2 t = smooth ? m.TangentSmoothAt(d) : m.TangentAt(d);
            Vector2 n = TrackModel.Normal(t);
            return TrackModel.W(m.PointAt(d) + n * offset, m.ElevationAt(d) + yExtra);
        }

        public static Material RoadMat;

        public static GameObject Build(TrackModel m, System.Random rng)
        {
            Crowd.Reset();
            var root = new GameObject("Track_" + m.def.id);
            Road(m, root.transform);
            RoadBlock(m, root.transform);
            Curbs(m, root.transform);
            Ground(m, root.transform);
            Gravel(m, root.transform);
            Walls(m, root.transform);
            PitLane(m, root.transform);
            PitWall(m, root.transform);
            StartFinish(m, root.transform);
            PitFurniture(m, root.transform);
            Grandstands(m, root.transform);
            Scenery.Build(m, root.transform, rng);
            Hills(root.transform, rng);
            return root;
        }

        static void Ribbon(MeshBuilder mb, TrackModel m, float innerOff, float outerOff, float y, bool bothSides, System.Func<int, bool> skip = null)
        {
            for (int s = bothSides ? -1 : 1; s <= 1; s += 2)
            {
                for (int i = 0; i < TrackModel.N; i++)
                {
                    if (skip != null && skip(i)) continue;
                    Vector3 a = OffI(m, i, innerOff * s, y), b = OffI(m, i, outerOff * s, y);
                    Vector3 c = OffI(m, i + 1, outerOff * s, y), d = OffI(m, i + 1, innerOff * s, y);
                    mb.Quad(a, b, c, d, Vector3.up);
                }
            }
        }

        static void Road(TrackModel m, Transform parent)
        {
            var mb = new MeshBuilder();
            for (int i = 0; i < TrackModel.N; i++)
            {
                Vector3 l0 = OffI(m, i, TrackModel.ROAD_WIDTH / 2f, 0.01f), r0 = OffI(m, i, -TrackModel.ROAD_WIDTH / 2f, 0.01f);
                Vector3 l1 = OffI(m, i + 1, TrackModel.ROAD_WIDTH / 2f, 0.01f), r1 = OffI(m, i + 1, -TrackModel.ROAD_WIDTH / 2f, 0.01f);
                mb.Quad(l0, r0, r1, l1, Vector3.up);
            }
            mb.PlanarUV(0.125f);
            var roadMat = Mats.Make(new Color(0.24f, 0.26f, 0.33f), 0.3f);
            Mats.SetTex(roadMat, Tex.Asphalt());
            RoadMat = roadMat;
            mb.Build("Road", parent, roadMat);
        }

        // The road is a solid slab, not a floating ribbon: both edges and the underside run down well below the
        // terrain, so on a hill there is never a gap under the surface for a car (or the camera) to get into.
        static void RoadBlock(TrackModel m, Transform parent)
        {
            var mb = new MeshBuilder();
            const float depth = 8f, edge = TrackModel.CURB_OUTER;
            for (int i = 0; i < TrackModel.N; i++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 t0 = OffI(m, i, edge * s, 0.0f), t1 = OffI(m, i + 1, edge * s, 0.0f);
                    Vector3 b0 = t0 + Vector3.down * depth, b1 = t1 + Vector3.down * depth;
                    Vector3 outward = (OffI(m, i, edge * s, 0f) - OffI(m, i, 0f, 0f)); outward.y = 0f;
                    mb.Quad(t0, t1, b1, b0, outward);
                }
                Vector3 l0 = OffI(m, i, edge, -depth), r0 = OffI(m, i, -edge, -depth);
                Vector3 l1 = OffI(m, i + 1, edge, -depth), r1 = OffI(m, i + 1, -edge, -depth);
                mb.Quad(l0, r0, r1, l1, Vector3.down);
            }
            mb.Build("RoadBlock", parent, Mats.Make(new Color(0.30f, 0.31f, 0.34f), 0.15f));
        }

        static void Curbs(TrackModel m, Transform parent)
        {
            var red = new MeshBuilder(); var white = new MeshBuilder();
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < TrackModel.N; i++)
                {
                    var mb = (i / 2) % 2 == 0 ? red : white;
                    float inner = TrackModel.ROAD_WIDTH / 2f + 0.05f, outer = TrackModel.CURB_OUTER;
                    mb.Quad(OffI(m, i, inner * s, 0.02f), OffI(m, i, outer * s, 0.02f), OffI(m, i + 1, outer * s, 0.02f), OffI(m, i + 1, inner * s, 0.02f), Vector3.up);
                }
            red.Build("CurbRed", parent, Mats.Make(new Color(1f, 0.29f, 0.18f), 0.4f));
            white.Build("CurbWhite", parent, Mats.Make(new Color(0.93f, 0.94f, 0.97f), 0.4f));
        }

        static float Sm(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        static void Ground(TrackModel m, Transform parent)
        {
            var mat = Mats.Make(new Color(0.10f, 0.20f, 0.13f), 0.05f);
            Mats.SetTex(mat, Tex.Grass());
            var outer = new MeshBuilder();
            outer.PlanarUV(0.07f);
            // The world's flat level sits under the lowest point of the road: on a circuit that dips below zero (a descent) a flat
            // ground at zero would lie over the road, which is what buried the downhill sections in grass.
            float lowest = 0f;
            if (m.def.elevation != null) for (int i = 0; i < TrackModel.N; i++) lowest = Mathf.Min(lowest, m.ElevationAt((float)i / TrackModel.N * m.roadLength));
            float level = m.def.elevation != null ? Mathf.Min(0f, lowest - 1.2f) - 0.08f : -0.02f;      // the flat world around the circuit
            // on a flat circuit the ground sits well under the road: a few centimetres is not enough, the road and the ground fight over
            // the same pixels (they flash) when seen from far away, because the depth buffer cannot tell them apart
            float baseY = m.def.elevation != null ? level - 0.03f : -0.12f;
            outer.Quad(new Vector3(-3000, baseY, -3000), new Vector3(3000, baseY, -3000), new Vector3(3000, baseY, 3000), new Vector3(-3000, baseY, 3000), Vector3.up);
            outer.Build("Ground", parent, mat);
            if (m.def.elevation == null) return;

            // On a hilly circuit the grass is one continuous height-field mesh: each point takes the height of the
            // nearest bit of road and eases back down to the flat world with distance. (An offset ribbon along the
            // track folds over itself on tight bends, which left sheets of grass floating at the wrong height.)
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in m.spaced) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y); }
            const float margin = 450f, cell = 10f;
            float x0 = minX - margin, z0 = minZ - margin;
            int nx = Mathf.CeilToInt((maxX - minX + 2f * margin) / cell), nz = Mathf.CeilToInt((maxZ - minZ + 2f * margin) / cell);
            var h = new float[nx + 1, nz + 1];
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                {
                    Vector2 p = new Vector2(x0 + i * cell, z0 + j * cell);
                    Vector2 near; int bi;
                    float d = m.NearestCenter(p, out near, out bi);
                    float along = m.ProjectDist(p);
                    float e = m.ElevationAt(along);
                    float w = 1f - Sm((d - 45f) / 300f);
                    float y = Mathf.Lerp(level, e - 0.08f, w);
                    // dips under the road slab so it never pokes through: deeper on a steep slope, where a grid cell spans a big height change
                    float slope = Mathf.Abs(m.ElevationAt(along + 4f) - m.ElevationAt(along - 4f)) / 8f;
                    y -= (0.6f + slope * cell * 1.2f) * (1f - Sm((d - 11f) / 2.5f));
                    h[i, j] = y;
                }
            var mb = new MeshBuilder();
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float xa = x0 + i * cell, xb = xa + cell, za = z0 + j * cell, zb = za + cell;
                    mb.Quad(TrackModel.W(xa, h[i, j], za), TrackModel.W(xb, h[i + 1, j], za), TrackModel.W(xb, h[i + 1, j + 1], zb), TrackModel.W(xa, h[i, j + 1], zb), Vector3.up);
                }
            mb.PlanarUV(0.07f);
            mb.Build("Terrain", parent, mat);
        }

        static bool NearPit(TrackModel m, int i, float range)
        {
            float d = (float)i / TrackModel.N * m.roadLength;
            float pz = m.PitZoneDist;
            float dd = Mathf.Abs(d - pz);
            dd = Mathf.Min(dd, m.roadLength - dd);
            return dd < range;
        }

        static void Gravel(TrackModel m, Transform parent)
        {
            var mb = new MeshBuilder();
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < TrackModel.N; i++)
                {
                    if (NearPit(m, i, 45f) || NearPit(m, i + 1, 45f)) continue;
                    float d0 = (float)i / TrackModel.N * m.roadLength, d1 = (float)(i + 1) / TrackModel.N * m.roadLength;
                    float o0 = Mathf.Max(TrackModel.CURB_OUTER + 0.5f, m.WallOffsetAt(d0) - 0.5f);
                    float o1 = Mathf.Max(TrackModel.CURB_OUTER + 0.5f, m.WallOffsetAt(d1) - 0.5f);
                    mb.Quad(OffI(m, i, TrackModel.CURB_OUTER * s, 0.015f), OffI(m, i, o0 * s, 0.015f), OffI(m, i + 1, o1 * s, 0.015f), OffI(m, i + 1, TrackModel.CURB_OUTER * s, 0.015f), Vector3.up);
                }
            mb.PlanarUV(0.2f);
            var gravelMat = Mats.Make(new Color(0.79f, 0.74f, 0.60f), 0.05f);
            Mats.SetTex(gravelMat, Tex.Gravel());
            mb.Build("Gravel", parent, gravelMat);
        }

        static void Walls(TrackModel m, Transform parent)
        {
            var red = new MeshBuilder(); var white = new MeshBuilder();
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < TrackModel.N; i++)
                {
                    var mb = (i / 2) % 2 == 0 ? white : red;
                    float d0 = (float)i / TrackModel.N * m.roadLength, d1 = (float)(i + 1) / TrackModel.N * m.roadLength;
                    float in0 = m.WallOffsetAt(d0), in1 = m.WallOffsetAt(d1);
                    float out0 = in0 + WALL_THICKNESS, out1 = in1 + WALL_THICKNESS;
                    Vector3 i0 = SmoothPt(m, d0, in0 * s), i1 = SmoothPt(m, d1, in1 * s);
                    Vector3 o0 = SmoothPt(m, d0, out0 * s), o1 = SmoothPt(m, d1, out1 * s);
                    i0 += Vector3.down * 0.5f; i1 += Vector3.down * 0.5f; o0 += Vector3.down * 0.5f; o1 += Vector3.down * 0.5f;
                    Vector3 up = Vector3.up * (WALL_HEIGHT + 0.5f);
                    Vector3 toTrack = (TrackModel.W(m.PointAt(d0), 0) - i0); toTrack.y = 0;
                    // inner face (toward the track), top, outer face
                    mb.Quad(i0, i1, i1 + up, i0 + up, toTrack);
                    mb.Quad(i0 + up, i1 + up, o1 + up, o0 + up, Vector3.up);
                    mb.Quad(o0, o1, o1 + up, o0 + up, -toTrack);
                }
            white.Build("WallWhite", parent, Mats.Make(new Color(0.9f, 0.9f, 0.92f), 0.3f));
            red.Build("WallRed", parent, Mats.Make(new Color(1f, 0.29f, 0.18f), 0.3f));
        }

        static Vector3 SmoothPt(TrackModel m, float d, float offset)
        {
            Vector2 n = TrackModel.Normal(m.TangentSmoothAt(d));
            return TrackModel.W(m.PointAt(d) + n * offset, m.ElevationAt(d));
        }

        static void PitLane(TrackModel m, Transform parent)
        {
            float pz = m.PitZoneDist;
            var samples = new List<Vector3>();
            for (float d = TrackModel.PIT_ENTRY; d <= TrackModel.PIT_EXIT; d += 2f)
            {
                Vector3 p;
                if (m.PitLanePosAt(pz + d, out p)) samples.Add(p);
            }
            if (samples.Count < 2) return;
            float hw = TrackModel.PIT_LANE_HALFWIDTH;
            var strip = new MeshBuilder(); var line = new MeshBuilder();
            for (int i = 0; i < samples.Count - 1; i++)
            {
                Vector3 p0 = samples[i], p1 = samples[i + 1];
                Vector3 pn = samples[Mathf.Min(samples.Count - 1, i + 2)], pp = samples[Mathf.Max(0, i - 1)];
                Vector2 n0 = SegNormal(pp, p1), n1 = SegNormal(p0, pn);
                Vector3 a = LanePt(p0, n0, hw), b = LanePt(p0, n0, -hw), c = LanePt(p1, n1, -hw), d2 = LanePt(p1, n1, hw);
                strip.Quad(a, b, c, d2, Vector3.up);
                line.Quad(LanePt(p0, n0, hw - 0.12f, 0.004f), LanePt(p0, n0, hw + 0.05f, 0.004f), LanePt(p1, n1, hw + 0.05f, 0.004f), LanePt(p1, n1, hw - 0.12f, 0.004f), Vector3.up);
                line.Quad(LanePt(p0, n0, -hw - 0.05f, 0.004f), LanePt(p0, n0, -hw + 0.12f, 0.004f), LanePt(p1, n1, -hw + 0.12f, 0.004f), LanePt(p1, n1, -hw - 0.05f, 0.004f), Vector3.up);
            }
            strip.Build("PitLane", parent, Mats.Make(new Color(0.60f, 0.59f, 0.55f), 0.2f));
            line.Build("PitLaneLines", parent, Mats.Emissive(new Color(1f, 0.54f, 0.24f), 0.4f));
        }

        static Vector2 SegNormal(Vector3 a, Vector3 b)
        {
            Vector2 d = new Vector2(b.x - a.x, b.z - a.z);
            if (d.sqrMagnitude < 1e-8f) return Vector2.up;
            d.Normalize();
            return new Vector2(-d.y, d.x);
        }

        // p is web space (x, y, z); returns a Unity world position offset sideways by 'side' meters
        static Vector3 LanePt(Vector3 p, Vector2 n, float side, float yExtra = 0f)
        {
            return TrackModel.W(p.x + n.x * side, p.y + yExtra, p.z + n.y * side);
        }

        static void PitWall(TrackModel m, Transform parent)
        {
            if (!m.pitLaneIsWide) return;
            float pz = m.PitZoneDist;
            var samples = new List<Vector3>();
            for (float d = TrackModel.PIT_ENTRY; d <= TrackModel.PIT_EXIT; d += 2f)
            {
                Vector3 p;
                if (m.PitWallPosAt(pz + d, out p)) samples.Add(p);
            }
            if (samples.Count < 2) return;
            float ht = TrackModel.PIT_WALL_THICKNESS / 2f, h = TrackModel.PIT_WALL_HEIGHT;
            var slab = new MeshBuilder(); var stripe = new MeshBuilder();
            for (int i = 0; i < samples.Count - 1; i++)
            {
                Vector3 p0 = samples[i], p1 = samples[i + 1];
                Vector3 pn = samples[Mathf.Min(samples.Count - 1, i + 2)], pp = samples[Mathf.Max(0, i - 1)];
                Vector2 n0 = SegNormal(pp, p1), n1 = SegNormal(p0, pn);
                Vector3 ob0 = LanePt(p0, n0, ht), ob1 = LanePt(p1, n1, ht);
                Vector3 ib0 = LanePt(p0, n0, -ht), ib1 = LanePt(p1, n1, -ht);
                Vector3 up = Vector3.up * h;
                Vector3 outward = ob0 - ib0;
                slab.Quad(ob0, ob1, ob1 + up, ob0 + up, outward);
                slab.Quad(ib0, ib1, ib1 + up, ib0 + up, -outward);
                slab.Quad(ob0 + up, ob1 + up, ib1 + up, ib0 + up, Vector3.up);
                Vector3 s = Vector3.up * 0.01f;
                stripe.Quad(ob0 + up + s, ob1 + up + s, ib1 + up + s, ib0 + up + s, Vector3.up);
            }
            slab.Build("PitWall", parent, Mats.Make(new Color(0.85f, 0.83f, 0.78f), 0.2f));
            stripe.Build("PitWallStripe", parent, Mats.Make(new Color(1f, 0.29f, 0.18f), 0.3f));
        }

        static Texture2D CheckerTex()
        {
            var t = new Texture2D(64, 16, TextureFormat.RGBA32, false);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 64; x++)
                    t.SetPixel(x, y, (((x / 8) + (y / 8)) % 2 == 0) ? new Color(0.93f, 0.94f, 0.97f) : new Color(0.04f, 0.05f, 0.08f));
            t.filterMode = FilterMode.Point;
            t.Apply();
            return t;
        }

        static void StartFinish(TrackModel m, Transform parent)
        {
            Vector2 p0 = m.PointAt(0), t0 = m.TangentAt(0), n0 = TrackModel.Normal(t0);
            float e = m.ElevationAt(0);
            float hw = TrackModel.ROAD_WIDTH / 2f;
            Vector3 a = TrackModel.W(p0 + n0 * hw, e + 0.025f), b = TrackModel.W(p0 - n0 * hw, e + 0.025f);
            Vector3 c = TrackModel.W(p0 - n0 * hw + t0 * 3f, e + 0.025f), d = TrackModel.W(p0 + n0 * hw + t0 * 3f, e + 0.025f);
            var mb = new MeshBuilder();
            mb.Quad(a, b, c, d, Vector3.up);
            var mat = Mats.Make(Color.white, 0.1f);
            mat.mainTexture = CheckerTex();
            mat.mainTextureScale = new Vector2(1, 1);
            mb.Build("StartFinishLine", parent, mat);

            var gantry = Mats.Make(new Color(0.17f, 0.2f, 0.27f), 0.4f, 0.4f);
            Vector3 la = TrackModel.W(p0 + n0 * (hw + 1f), e), ra = TrackModel.W(p0 - n0 * (hw + 1f), e);
            Prim.Cyl(parent, la + Vector3.up * 3.5f, new Vector3(0.36f, 3.5f, 0.36f), Quaternion.identity, gantry, "GantryPostL");
            Prim.Cyl(parent, ra + Vector3.up * 3.5f, new Vector3(0.36f, 3.5f, 0.36f), Quaternion.identity, gantry, "GantryPostR");
            Vector3 mid = TrackModel.W(p0, e + 7f);
            var beam = Prim.Box(parent, mid, new Vector3(0.5f, 0.5f, hw * 2f + 3f), gantry, "GantryBeam");
            beam.transform.rotation = Quaternion.LookRotation(la - ra) * Quaternion.Euler(0, 90, 0);
            beam.transform.localScale = new Vector3(hw * 2f + 3f, 0.5f, 0.5f);
        }

        // yellow painted pit-box markings like the overhead photos: a crew line along the garage side with end
        // ticks, a stop line across the lane at the nose, and an arrow pointing at it - for each of the six boxes
        static void PitBoxLines(TrackModel m, Transform parent)
        {
            var mb = new MeshBuilder();
            for (int box = 0; box < TrackModel.PIT_BOX_COUNT; box++)
            {
                Vector2 c; float y, h; Vector2 f, right; float os;
                m.PitBoxFrame(box, out c, out y, out h, out f, out right, out os);
                float yy = y + 0.035f;
                System.Action<float, float, float, float, float> seg = (x0, z0, x1, z1, w) =>
                {
                    Vector2 a = c + right * x0 + f * z0, b = c + right * x1 + f * z1;
                    Vector2 d = (b - a).normalized; Vector2 n = new Vector2(-d.y, d.x) * (w * 0.5f);
                    mb.Quad(TrackModel.W(a - n, yy), TrackModel.W(a + n, yy), TrackModel.W(b + n, yy), TrackModel.W(b - n, yy), Vector3.up);
                };
                float lx = os * 3.0f;
                seg(lx, -4.6f, lx, 4.6f, 0.2f);
                seg(lx, -4.6f, lx - os * 0.9f, -4.6f, 0.2f);
                seg(lx, 4.6f, lx - os * 0.9f, 4.6f, 0.2f);
                seg(-1.6f, 3.7f, 1.6f, 3.7f, 0.22f);
                seg(0f, 3.0f, 0f, 4.4f, 0.18f);
                seg(-0.5f, 3.9f, 0f, 4.5f, 0.18f);
                seg(0.5f, 3.9f, 0f, 4.5f, 0.18f);
            }
            mb.Build("PitBoxLines", parent, Mats.Emissive(new Color(1f, 0.8f, 0.1f), 0.5f));
        }

        public static readonly Color[] TeamColors =
        {
            new Color(1f, 0.69f, 0.13f), new Color(0.24f, 0.86f, 0.52f), new Color(0.09f, 0.85f, 0.77f),
            new Color(0.54f, 0.36f, 1f), new Color(1f, 0.37f, 0.63f), new Color(1f, 0.29f, 0.18f)
        };

        // a coloured sign above every box with its number as a row of small white blocks (1 block = box 1, ...)
        static void PitFurniture(TrackModel m, Transform parent)
        {
            PitBoxLines(m, parent);
            var barMat = Mats.Make(new Color(0.95f, 0.95f, 0.97f), 0.2f);
            for (int box = 0; box < TrackModel.PIT_BOX_COUNT; box++)
            {
                Vector2 c; float y, h; Vector2 f, right; float os;
                m.PitBoxFrame(box, out c, out y, out h, out f, out right, out os);
                Vector2 outward = right * os;
                Vector2 signPos = c + outward * 6.6f;
                var signMat = Mats.Emissive(TeamColors[box % TeamColors.Length], 0.5f);
                var sign = Prim.Box(parent, TrackModel.W(signPos, y + 2.8f), new Vector3(0.08f, 1.1f, 2.4f), signMat, "PitSign" + (box + 1));
                sign.transform.rotation = Quaternion.LookRotation(TrackModel.W(f, 0));
                for (int k = 0; k <= box; k++)
                {
                    Vector2 bp = signPos - outward * 0.07f + f * (-1.0f + k * 0.4f);
                    Prim.Box(parent, TrackModel.W(bp, y + 2.8f), new Vector3(0.06f, 0.55f, 0.2f), barMat, "PitNumber");
                }
            }
        }

        static void Grandstands(TrackModel m, Transform parent)
        {
            var stand = Mats.Make(new Color(0.16f, 0.19f, 0.25f), 0.3f);
            var roof = Mats.Make(new Color(0.09f, 0.11f, 0.15f), 0.4f);
            var crowd = Mats.Make(Color.white, 0.05f);
            Mats.SetTex(crowd, Tex.Crowd());
            var flagCols = new[] { new Color(0.9f, 0.1f, 0.1f), new Color(0.1f, 0.4f, 0.9f), new Color(0.95f, 0.85f, 0.1f), new Color(0.15f, 0.7f, 0.3f), new Color(0.95f, 0.95f, 0.95f) };
            int[] spots = { 0, Mathf.FloorToInt(TrackModel.N * 0.28f), Mathf.FloorToInt(TrackModel.N * 0.55f), Mathf.FloorToInt(TrackModel.N * 0.75f) };
            foreach (int idx in spots)
            {
                float d = (float)idx / TrackModel.N * m.roadLength;
                Vector2 p = m.spaced[idx], n = TrackModel.Normal(m.TangentAt(d));
                float e = m.ElevationAt(d);
                var g = new GameObject("Grandstand");
                g.transform.SetParent(parent, false);
                g.transform.position = TrackModel.W(p + n * 34f, e);
                g.transform.rotation = Quaternion.LookRotation(TrackModel.W(-n, 0));
                for (int tier = 0; tier < 4; tier++)
                {
                    Prim.Box(g.transform, new Vector3(0, 1f + tier * 1.9f, -tier * 2.3f), new Vector3(26, 1.6f, 5), stand, "tier");
                    var row = Prim.Box(g.transform, new Vector3(0, 1.7f + tier * 1.9f, -tier * 2.3f + 2.4f), new Vector3(25, 1.3f, 0.1f), crowd, "crowd");
                    Crowd.AddRow(row.transform);
                }
                Prim.Box(g.transform, new Vector3(0, 9.2f, -4f), new Vector3(27, 0.4f, 7), roof, "roof");
                for (int fi = 0; fi < 6; fi++)
                {
                    float fx = -11f + fi * 4.4f;
                    Prim.Cyl(g.transform, new Vector3(fx, 10.6f, -4f), new Vector3(0.07f, 1.4f, 0.07f), Quaternion.identity, roof, "flagPole");
                    var fl = Prim.Box(g.transform, new Vector3(fx, 11.7f, -3.6f), new Vector3(0.9f, 0.55f, 0.03f), Mats.Make(flagCols[fi % flagCols.Length], 0.1f), "flag");
                    Crowd.AddFlag(fl.transform);
                }
                Crowd.AddStand(g.transform.position);
            }
        }

        static void Hills(Transform parent, System.Random rng)
        {
            var mat = Mats.Make(new Color(0.20f, 0.28f, 0.26f), 0.05f);
            for (int i = 0; i < 16; i++)
            {
                float ang = (float)i / 16f * Mathf.PI * 2f + (float)rng.NextDouble() * 0.3f;
                float r = 520f + (float)rng.NextDouble() * 180f;
                float h = 40f + (float)rng.NextDouble() * 60f;
                Prim.Sphere(parent, new Vector3(Mathf.Cos(ang) * r, h * 0.3f - 20f, Mathf.Sin(ang) * r), new Vector3(220f + (float)rng.NextDouble() * 120f, h, 220f + (float)rng.NextDouble() * 120f), mat, "hill");
            }
        }
    }
}
