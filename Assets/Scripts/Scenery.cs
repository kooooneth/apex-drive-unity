using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ApexDrive
{
    /// <summary>
    /// Trackside scenery: several tree species built from lumpy leaf clusters with a leaf texture and a barked, tapered trunk,
    /// bushes, boulders, a deeper forest backdrop and tyre walls on the outside of the tight corners. Everything is merged
    /// into a few big meshes per material, so it costs a handful of draw calls, and all of it casts and receives sun shadows.
    /// </summary>
    public static class Scenery
    {
        // ---------------------------------------------------------------- mesh batches
        class Batch
        {
            public Material mat; public string name;
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();

            // adds a triangle wound so that it faces away from the segment a-b (the inside of the shape)
            public void Tri(int i0, int i1, int i2, Vector3 a, Vector3 b)
            {
                Vector3 p0 = v[i0], p1 = v[i1], p2 = v[i2];
                Vector3 cen = (p0 + p1 + p2) / 3f;
                Vector3 ab = b - a; float l2 = ab.sqrMagnitude;
                Vector3 inside = l2 < 1e-6f ? a : a + ab * Mathf.Clamp01(Vector3.Dot(cen - a, ab) / l2);
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                if (Vector3.Dot(n, cen - inside) >= 0f) { t.Add(i0); t.Add(i1); t.Add(i2); }
                else { t.Add(i0); t.Add(i2); t.Add(i1); }
            }

            public void Build(Transform parent)
            {
                if (v.Count == 0) return;
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                var mesh = new Mesh();
                mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        static Batch NewBatch(string name, Color c, Texture tex, float smooth = 0.05f)
        {
            var m = Mats.Make(c, smooth);
            if (tex != null) Mats.SetTex(m, tex);
            return new Batch { name = name, mat = m };
        }

        // ---------------------------------------------------------------- procedural textures
        static Texture2D leafTex, barkTex, rockTex;

        static Texture2D MakeTex(int S, System.Func<int, int, float> f)
        {
            var t = new Texture2D(S, S, TextureFormat.RGBA32, true);
            t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Bilinear;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++) { float g = Mathf.Clamp01(f(x, y)); px[y * S + x] = new Color(g, g, g, 1f); }
            t.SetPixels(px); t.Apply();
            return t;
        }

        // value noise that tiles
        static float Noise(int x, int y, int S, int cell, int seed)
        {
            int cx = x / cell, cy = y / cell, n = S / cell;
            float fx = (x % cell) / (float)cell, fy = (y % cell) / (float)cell;
            float a = H(cx % n, cy % n, seed), b = H((cx + 1) % n, cy % n, seed), c = H(cx % n, (cy + 1) % n, seed), d = H((cx + 1) % n, (cy + 1) % n, seed);
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float H(int x, int y, int seed)
        {
            int h = x * 374761393 + y * 668265263 + seed * 144665;
            h = (h ^ (h >> 13)) * 1274126177; h ^= h >> 16;
            return (h & 0xffff) / 65535f;
        }

        static Texture2D LeafTex()
        {
            if (leafTex != null) return leafTex;
            leafTex = MakeTex(128, (x, y) =>
            {
                float big = Noise(x, y, 128, 32, 3), mid = Noise(x, y, 128, 8, 5), fine = Noise(x, y, 128, 2, 9);
                float v = 0.5f + (big - 0.5f) * 0.5f + (mid - 0.5f) * 0.5f + (fine - 0.5f) * 0.35f;
                if (mid < 0.2f && fine < 0.5f) v *= 0.5f;                  // dark gaps between the leaves
                if (fine > 0.9f) v += 0.2f;                                // a few lit leaf tips
                return v + 0.12f;
            });
            return leafTex;
        }

        static Texture2D BarkTex()
        {
            if (barkTex != null) return barkTex;
            barkTex = MakeTex(64, (x, y) =>
            {
                float streak = Noise(x * 6 % 64, y, 64, 16, 21);          // tall stretched grain
                float fine = Noise(x, y, 64, 4, 23);
                return 0.38f + streak * 0.42f + (fine - 0.5f) * 0.3f;
            });
            return barkTex;
        }

        static Texture2D RockTex()
        {
            if (rockTex != null) return rockTex;
            rockTex = MakeTex(64, (x, y) => 0.45f + Noise(x, y, 64, 16, 31) * 0.3f + Noise(x, y, 64, 4, 33) * 0.25f);
            return rockTex;
        }

        // ---------------------------------------------------------------- shapes
        /// <summary>A lumpy ellipsoid: a leaf cluster, a bush or a boulder.</summary>
        static void Blob(Batch b, Vector3 c, Vector3 radii, int seed, float rough, int seg = 10, int rings = 7)
        {
            int start = b.v.Count;
            float so = seed * 1.37f;
            for (int i = 0; i <= rings; i++)
            {
                float phi = Mathf.PI * i / rings;
                for (int j = 0; j <= seg; j++)
                {
                    float th = Mathf.PI * 2f * (j % seg) / seg;
                    Vector3 dir = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    float n = 1f + rough * (Mathf.PerlinNoise(dir.x * 1.8f + so, dir.z * 1.8f + dir.y * 1.6f + so * 0.7f) - 0.5f) * 2f
                                 + rough * 0.5f * (H(i, j % seg, seed) - 0.5f);
                    Vector3 p = c + new Vector3(dir.x * radii.x, dir.y * radii.y, dir.z * radii.z) * n;
                    b.v.Add(p);
                    b.uv.Add(new Vector2((float)j / seg * 2f + so * 0.1f, (float)i / rings * 1.5f));
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < seg; j++)
                {
                    int a0 = start + i * (seg + 1) + j, a1 = a0 + 1, a2 = a0 + seg + 1, a3 = a2 + 1;
                    b.Tri(a0, a1, a2, c, c); b.Tri(a1, a3, a2, c, c);
                }
        }

        /// <summary>A ruffled cone for a pine tier.</summary>
        static void Cone(Batch b, Vector3 baseC, float r, float h, int seed, int sides = 10)
        {
            int start = b.v.Count;
            Vector3 apex = baseC + Vector3.up * h;
            b.v.Add(apex); b.uv.Add(new Vector2(0.5f, 1.2f));
            for (int j = 0; j <= sides; j++)
            {
                float th = Mathf.PI * 2f * (j % sides) / sides;
                float rr = r * (0.82f + 0.36f * H(j % sides, seed, 41));
                float droop = -0.18f * h * H(j % sides, seed, 43);
                b.v.Add(baseC + new Vector3(Mathf.Cos(th) * rr, droop, Mathf.Sin(th) * rr));
                b.uv.Add(new Vector2((float)j / sides * 2f, 0f));
            }
            Vector3 axA = baseC, axB = apex;
            for (int j = 0; j < sides; j++) b.Tri(start, start + 1 + j, start + 2 + j, axA, axB);
            // the underside, so the tier does not look hollow from below
            int c0 = b.v.Count; b.v.Add(baseC + Vector3.up * -0.05f * h); b.uv.Add(new Vector2(0.5f, 0.5f));
            for (int j = 0; j < sides; j++) b.Tri(c0, start + 1 + j, start + 2 + j, baseC + Vector3.up * h, baseC + Vector3.up * h * 2f);
        }

        /// <summary>A tapered, slightly leaning trunk with a flared root.</summary>
        static void Trunk(Batch b, Vector3 baseP, float h, float r0, float r1, Vector3 lean, int sides = 8)
        {
            int start = b.v.Count;
            const int rings = 4;
            for (int i = 0; i <= rings; i++)
            {
                float f = (float)i / rings;
                float r = Mathf.Lerp(r0, r1, f) * (i == 0 ? 1.35f : 1f);
                Vector3 c = baseP + Vector3.up * (h * f) + lean * (f * f);
                for (int j = 0; j <= sides; j++)
                {
                    float th = Mathf.PI * 2f * (j % sides) / sides;
                    float bump = 1f + 0.12f * (H(j % sides, i, 51) - 0.5f);
                    b.v.Add(c + new Vector3(Mathf.Cos(th) * r * bump, 0f, Mathf.Sin(th) * r * bump));
                    b.uv.Add(new Vector2((float)j / sides * 2f, f * 3f));
                }
            }
            Vector3 a = baseP, bb = baseP + Vector3.up * h + lean;
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a0 = start + i * (sides + 1) + j, a1 = a0 + 1, a2 = a0 + sides + 1, a3 = a2 + 1;
                    b.Tri(a0, a1, a2, a, bb); b.Tri(a1, a3, a2, a, bb);
                }
        }

        // ---------------------------------------------------------------- the scenery set
        class Set
        {
            public Batch bark, rock;
            public Batch[] oak, pine, poplar, autumn, bush;
        }

        static Set MakeSet()
        {
            var s = new Set();
            s.bark = NewBatch("TreeBark", new Color(0.62f, 0.5f, 0.4f), BarkTex(), 0.05f);
            s.rock = NewBatch("Boulders", new Color(0.62f, 0.6f, 0.58f), RockTex(), 0.1f);
            s.oak = new[]
            {
                NewBatch("OakDark", new Color(0.40f, 0.62f, 0.30f), LeafTex()), NewBatch("OakMid", new Color(0.52f, 0.76f, 0.36f), LeafTex()), NewBatch("OakLight", new Color(0.68f, 0.88f, 0.42f), LeafTex())
            };
            s.pine = new[]
            {
                NewBatch("PineDark", new Color(0.24f, 0.46f, 0.30f), LeafTex()), NewBatch("PineMid", new Color(0.32f, 0.56f, 0.36f), LeafTex())
            };
            s.poplar = new[]
            {
                NewBatch("PoplarDark", new Color(0.36f, 0.60f, 0.28f), LeafTex()), NewBatch("PoplarLight", new Color(0.56f, 0.82f, 0.34f), LeafTex())
            };
            s.autumn = new[]
            {
                NewBatch("AutumnDark", new Color(0.72f, 0.42f, 0.16f), LeafTex()), NewBatch("AutumnLight", new Color(0.92f, 0.68f, 0.22f), LeafTex())
            };
            s.bush = new[]
            {
                NewBatch("BushA", new Color(0.34f, 0.58f, 0.28f), LeafTex()), NewBatch("BushB", new Color(0.46f, 0.68f, 0.30f), LeafTex())
            };
            return s;
        }

        static void Oak(Set s, Vector3 p, float sc, System.Random rng, bool autumn)
        {
            var leaves = autumn ? s.autumn : s.oak;
            float th = (2.1f + (float)rng.NextDouble() * 0.8f) * sc;
            Vector3 lean = new Vector3(((float)rng.NextDouble() - 0.5f) * 0.6f, 0f, ((float)rng.NextDouble() - 0.5f) * 0.6f) * sc;
            Trunk(s.bark, p, th, 0.26f * sc, 0.15f * sc, lean);
            Vector3 top = p + Vector3.up * th + lean;
            int seed = rng.Next(1000);
            Blob(leaves[0], top + Vector3.up * 1.7f * sc, new Vector3(2.9f, 1.9f, 2.9f) * sc, seed, 0.2f);
            int sat = 5 + rng.Next(3);
            for (int i = 0; i < sat; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f, rad = (1.5f + (float)rng.NextDouble() * 1.1f) * sc;
                float up = (0.3f + (float)rng.NextDouble() * 2.6f) * sc;
                float size = (1.2f + (float)rng.NextDouble() * 0.9f) * sc;
                Blob(leaves[up < 1.2f * sc ? 0 : up < 2.2f * sc ? (rng.Next(2) == 0 ? 0 : 1) : 1], top + new Vector3(Mathf.Cos(ang) * rad, up, Mathf.Sin(ang) * rad), new Vector3(size, size * 0.8f, size), seed + i + 1, 0.22f, 9, 6);
            }
            Blob(leaves[leaves.Length - 1], top + Vector3.up * 3.3f * sc + new Vector3(0.3f, 0f, -0.2f) * sc, new Vector3(1.9f, 1.1f, 1.9f) * sc, seed + 40, 0.2f, 9, 6);
        }

        static void Pine(Set s, Vector3 p, float sc, System.Random rng)
        {
            float th = (1.6f + (float)rng.NextDouble() * 0.5f) * sc;
            Trunk(s.bark, p, th + 5.5f * sc, 0.21f * sc, 0.07f * sc, Vector3.zero, 7);
            int seed = rng.Next(1000);
            int tiers = 5 + rng.Next(2);
            for (int i = 0; i < tiers; i++)
            {
                float f = (float)i / tiers;
                Cone(s.pine[i % 2], p + Vector3.up * (th + i * 1.25f * sc), (2.3f - 2.0f * f) * sc, (2.4f - 0.8f * f) * sc, seed + i * 7);
            }
        }

        static void Poplar(Set s, Vector3 p, float sc, System.Random rng)
        {
            Trunk(s.bark, p, 2.2f * sc, 0.2f * sc, 0.12f * sc, Vector3.zero, 7);
            int seed = rng.Next(1000);
            Blob(s.poplar[0], p + Vector3.up * 4.0f * sc, new Vector3(1.3f, 2.4f, 1.3f) * sc, seed, 0.14f, 9, 6);
            Blob(s.poplar[1], p + Vector3.up * 6.4f * sc, new Vector3(1.1f, 2.6f, 1.1f) * sc, seed + 1, 0.16f, 9, 6);
            Blob(s.poplar[0], p + Vector3.up * 8.6f * sc, new Vector3(0.7f, 1.8f, 0.7f) * sc, seed + 2, 0.14f, 8, 5);
        }

        static void Bush(Set s, Vector3 p, float sc, System.Random rng)
        {
            int seed = rng.Next(1000);
            int n = 2 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f, rad = (float)rng.NextDouble() * 0.9f * sc;
                float size = (0.65f + (float)rng.NextDouble() * 0.55f) * sc;
                Blob(s.bush[rng.Next(2)], p + new Vector3(Mathf.Cos(ang) * rad, size * 0.55f, Mathf.Sin(ang) * rad), new Vector3(size, size * 0.75f, size), seed + i, 0.25f, 8, 5);
            }
        }

        static void Boulder(Set s, Vector3 p, float sc, System.Random rng)
        {
            int seed = rng.Next(1000);
            Blob(s.rock, p + Vector3.up * 0.35f * sc, new Vector3(1.1f, 0.7f, 0.9f) * sc, seed, 0.3f, 8, 5);
            if (rng.NextDouble() < 0.6) Blob(s.rock, p + new Vector3(0.9f, 0.2f, 0.4f) * sc, new Vector3(0.6f, 0.4f, 0.5f) * sc, seed + 1, 0.3f, 7, 4);
        }

        public static void Build(TrackModel m, Transform parent, System.Random rng)
        {
            var root = new GameObject("Scenery");
            root.transform.SetParent(parent, false);
            var s = MakeSet();

            // a position is fine for a tree when it is clear of every part of the circuit (the track can run close to itself)
            System.Func<Vector2, float, bool> clear = (pos, minDist) =>
            {
                Vector2 near; int bi;
                return m.NearestCenter(pos, out near, out bi) > minDist;
            };

            int trees = 0;
            for (int idx = 0; idx < TrackModel.N; idx += 5)
            {
                if (NearPit(m, idx, 45f)) continue;
                if (rng.NextDouble() < 0.3) continue;
                float d = (float)idx / TrackModel.N * m.roadLength;
                Vector2 n = TrackModel.Normal(m.TangentAt(d));
                float e = m.ElevationAt(d);
                foreach (int side in new[] { -1, 1 })
                {
                    if (rng.NextDouble() < 0.4) continue;
                    float baseDist = 36f + (float)rng.NextDouble() * 24f;
                    int count = 1 + rng.Next(3);
                    for (int j = 0; j < count; j++)
                    {
                        Vector2 pos = m.spaced[idx] + n * side * baseDist + new Vector2((float)(rng.NextDouble() - 0.5) * 16f, (float)(rng.NextDouble() - 0.5) * 16f);
                        if (!clear(pos, 32f)) continue;
                        Vector3 w = TrackModel.W(pos, e);
                        float sc = 0.8f + (float)rng.NextDouble() * 0.7f;
                        double kind = rng.NextDouble();
                        if (kind < 0.46) Oak(s, w, sc, rng, false);
                        else if (kind < 0.52) Oak(s, w, sc, rng, true);
                        else if (kind < 0.80) Pine(s, w, sc * 1.05f, rng);
                        else Poplar(s, w, sc, rng);
                        trees++;
                        // undergrowth round the foot of the tree
                        if (rng.NextDouble() < 0.55)
                        {
                            Vector2 bp = pos + new Vector2((float)(rng.NextDouble() - 0.5) * 7f, (float)(rng.NextDouble() - 0.5) * 7f);
                            if (clear(bp, 30f)) Bush(s, TrackModel.W(bp, e), 0.8f + (float)rng.NextDouble() * 0.6f, rng);
                        }
                    }
                    // a few bushes and rocks in the open ground nearer the track
                    if (rng.NextDouble() < 0.45)
                    {
                        Vector2 bp = m.spaced[idx] + n * side * (28f + (float)rng.NextDouble() * 8f);
                        if (clear(bp, 26f)) Bush(s, TrackModel.W(bp, e), 0.7f + (float)rng.NextDouble() * 0.5f, rng);
                    }
                    if (rng.NextDouble() < 0.12)
                    {
                        Vector2 rp = m.spaced[idx] + n * side * (30f + (float)rng.NextDouble() * 20f);
                        if (clear(rp, 28f)) Boulder(s, TrackModel.W(rp, e), 0.8f + (float)rng.NextDouble() * 0.9f, rng);
                    }
                }
            }

            // a deeper, taller forest behind the first row gives the horizon some depth
            for (int idx = 0; idx < TrackModel.N; idx += 6)
            {
                if (NearPit(m, idx, 60f)) continue;
                float d = (float)idx / TrackModel.N * m.roadLength;
                Vector2 n = TrackModel.Normal(m.TangentAt(d));
                float e = m.ElevationAt(d);
                foreach (int side in new[] { -1, 1 })
                {
                    if (rng.NextDouble() < 0.35) continue;
                    Vector2 pos = m.spaced[idx] + n * side * (80f + (float)rng.NextDouble() * 45f) + new Vector2((float)(rng.NextDouble() - 0.5) * 20f, (float)(rng.NextDouble() - 0.5) * 20f);
                    if (!clear(pos, 60f)) continue;
                    Vector3 w = TrackModel.W(pos, e);
                    float sc = 1.2f + (float)rng.NextDouble() * 0.9f;
                    if (rng.NextDouble() < 0.55) Pine(s, w, sc, rng); else Oak(s, w, sc, rng, false);
                    trees++;
                }
            }

            foreach (var b in s.oak) b.Build(root.transform);
            foreach (var b in s.pine) b.Build(root.transform);
            foreach (var b in s.poplar) b.Build(root.transform);
            foreach (var b in s.autumn) b.Build(root.transform);
            foreach (var b in s.bush) b.Build(root.transform);
            s.bark.Build(root.transform);
            s.rock.Build(root.transform);

            TyreWalls(m, root.transform);
        }

        static bool NearPit(TrackModel m, int i, float range)
        {
            float d = (float)i / TrackModel.N * m.roadLength;
            float dd = Mathf.Abs(d - m.PitZoneDist);
            dd = Mathf.Min(dd, m.roadLength - dd);
            return dd < range;
        }

        // ---------------------------------------------------------------- tyre walls on the outside of tight corners
        static void TyreWalls(TrackModel m, Transform parent)
        {
            var black = NewBatch("TyreWallTyres", new Color(0.08f, 0.08f, 0.09f), null, 0.2f);
            var white = NewBatch("TyreWallStripes", new Color(0.92f, 0.92f, 0.94f), null, 0.2f);
            var red = NewBatch("TyreWallRed", new Color(0.82f, 0.1f, 0.1f), null, 0.2f);
            int added = 0;
            for (int idx = 0; idx < TrackModel.N; idx += 2)
            {
                float d = (float)idx / TrackModel.N * m.roadLength;
                if (NearPit(m, idx, 80f)) continue;
                float curv = m.CurvatureAt(d);
                if (curv < 0.16f) continue;
                // which way is the corner? the outside is the other side
                Vector2 t0 = m.TangentAt(d - 6f), t1 = m.TangentAt(d + 6f);
                float cross = t0.x * t1.y - t0.y * t1.x;
                int outSide = cross > 0f ? -1 : 1;                            // TrackModel.Normal(t) is the left-hand side in web space
                Vector2 n = TrackModel.Normal(m.TangentAt(d));
                float wall = m.WallOffsetAt(d);
                Vector2 pos = m.spaced[idx] + n * outSide * (wall + 1.6f);
                float e = m.ElevationAt(d);
                Vector3 w = TrackModel.W(pos, e);
                Vector3 tang = TrackModel.W(m.TangentAt(d), 0f);
                Vector3 rightV = Vector3.Cross(Vector3.up, tang).normalized;
                // a stack of tyres: two rows high, with a white and a red tyre among the black ones
                for (int row = 0; row < 3; row++)
                    for (int k = 0; k < 2; k++)
                    {
                        Vector3 c = w + rightV * (k * 0.9f - 0.45f) + Vector3.up * (0.2f + row * 0.4f);
                        Batch tgt = (row == 1 && k == 0 && idx % 6 == 0) ? white : (row == 2 && k == 1 && idx % 10 == 0) ? red : black;
                        Tyre(tgt, c, 0.46f, 0.4f);
                    }
                added++;
            }
            black.Build(parent); white.Build(parent); red.Build(parent);
        }

        static void Tyre(Batch b, Vector3 c, float r, float h)
        {
            int start = b.v.Count; const int sides = 10;
            for (int ring = 0; ring < 2; ring++)
                for (int j = 0; j <= sides; j++)
                {
                    float th = Mathf.PI * 2f * (j % sides) / sides;
                    b.v.Add(c + new Vector3(Mathf.Cos(th) * r, (ring == 0 ? -h : h) * 0.5f, Mathf.Sin(th) * r));
                    b.uv.Add(new Vector2((float)j / sides, ring));
                }
            for (int j = 0; j < sides; j++)
            {
                int a0 = start + j, a1 = a0 + 1, a2 = a0 + sides + 1, a3 = a2 + 1;
                b.Tri(a0, a1, a2, c, c); b.Tri(a1, a3, a2, c, c);
            }
            int top = b.v.Count; b.v.Add(c + Vector3.up * h * 0.5f); b.uv.Add(new Vector2(0.5f, 0.5f));
            for (int j = 0; j < sides; j++) b.Tri(top, start + sides + 1 + j, start + sides + 2 + j, c, c);
        }

        // ---------------------------------------------------------------- shadow quality
        /// <summary>Crisp, long-range, soft sun shadows from trees, grandstands, walls and the cars themselves.</summary>
        public static void SetupShadows(Light sun)
        {
            QualitySettings.shadows = ShadowQuality.All;
#if UNITY_WEBGL
            QualitySettings.shadowResolution = ShadowResolution.High;          // a browser has far less graphics memory and speed
            QualitySettings.shadowDistance = 220f;
            QualitySettings.shadowCascades = 2;
#else
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowDistance = 320f;
            QualitySettings.shadowCascades = 4;
#endif
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            if (sun != null)
            {
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.92f;
                sun.shadowBias = 0.04f;
                sun.shadowNormalBias = 0.5f;
            }
        }

        // ---------------------------------------------------------------- contact shadow under a car
        static Texture2D softTex;
        static Material contactMat;

        /// <summary>A soft dark patch under a car and its tyres, so it sits on the road even where the sun shadow is thin.</summary>
        public static void AddContactShadow(GameObject car)
        {
            if (softTex == null)
            {
                const int S = 64;
                softTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
                softTex.wrapMode = TextureWrapMode.Clamp;
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float dx = (x - S / 2f + 0.5f) / (S / 2f), dy = (y - S / 2f + 0.5f) / (S / 2f);
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        softTex.SetPixel(x, y, new Color(0f, 0f, 0f, a * a * 0.75f));
                    }
                softTex.Apply();
                contactMat = Mats.Fade(Color.black, 1f);
                Mats.SetTex(contactMat, softTex);
            }
            var q = PrimitiveMeshes.Create(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = "ContactShadow";
            q.transform.SetParent(car.transform, false);
            q.transform.localPosition = new Vector3(0f, 0.03f, 0.2f);
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            q.transform.localScale = new Vector3(2.9f, 5.8f, 1f);
            var r = q.GetComponent<Renderer>();
            r.sharedMaterial = contactMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }
}
