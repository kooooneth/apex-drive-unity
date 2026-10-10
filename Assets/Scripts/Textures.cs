using UnityEngine;

namespace ApexDrive
{
    /// <summary>Procedural textures (generated once at startup) - no image files needed.</summary>
    public static class Tex
    {
        static Texture2D carbon, asphalt, grass, gravel, tread, crowd;
        static readonly Texture2D[] sidewalls = new Texture2D[5];

        static Texture2D New(int w, int h, bool mips = true)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mips);
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Bilinear;
            return t;
        }

        // seamless "noise": a few periodic waves plus per-pixel speckle
        static float Periodic(int x, int y, int size, float seed)
        {
            float fx = (float)x / size * Mathf.PI * 2f, fy = (float)y / size * Mathf.PI * 2f;
            return 0.5f + 0.22f * Mathf.Sin(fx * 2f + seed) * Mathf.Cos(fy * 3f + seed * 1.7f)
                        + 0.16f * Mathf.Sin(fx * 5f + fy * 4f + seed * 2.3f)
                        + 0.10f * Mathf.Cos(fx * 9f - fy * 7f + seed * 0.6f);
        }

        /// <summary>2x2 twill carbon-fibre weave, dark with a faint diagonal sheen.</summary>
        public static Texture2D Carbon()
        {
            if (carbon != null) return carbon;
            const int S = 64;
            carbon = New(S, S);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int cx = x / 4, cy = y / 4;
                    bool warp = ((cx + cy) / 2) % 2 == 0;
                    float inCell = warp ? (x % 4) / 3f : (y % 4) / 3f;
                    float v = 0.10f + (warp ? 0.07f : 0.025f) + 0.05f * Mathf.Sin(inCell * Mathf.PI);
                    v += 0.012f * Mathf.Sin((x + y) * 0.35f);
                    carbon.SetPixel(x, y, new Color(v, v, v * 1.08f, 1f));
                }
            carbon.Apply();
            return carbon;
        }

        public static Texture2D Asphalt()
        {
            if (asphalt != null) return asphalt;
            const int S = 128;
            asphalt = New(S, S);
            var rnd = new System.Random(11);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float v = 0.62f + (Periodic(x, y, S, 1.3f) - 0.5f) * 0.22f + ((float)rnd.NextDouble() - 0.5f) * 0.16f;
                    if (rnd.NextDouble() < 0.012) v += 0.18f;   // bright aggregate chips
                    asphalt.SetPixel(x, y, new Color(v, v, v * 1.03f, 1f));
                }
            asphalt.Apply();
            return asphalt;
        }

        public static Texture2D Grass()
        {
            if (grass != null) return grass;
            const int S = 128;
            grass = New(S, S);
            var rnd = new System.Random(5);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float n = Periodic(x, y, S, 4.1f);
                    float blade = ((float)rnd.NextDouble() - 0.5f) * 0.22f;
                    float v = 0.72f + (n - 0.5f) * 0.5f + blade;
                    grass.SetPixel(x, y, new Color(v * 0.95f, v, v * 0.9f, 1f));
                }
            grass.Apply();
            return grass;
        }

        public static Texture2D Gravel()
        {
            if (gravel != null) return gravel;
            const int S = 64;
            gravel = New(S, S);
            var rnd = new System.Random(23);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float v = 0.85f + ((float)rnd.NextDouble() - 0.5f) * 0.3f;
                    if (rnd.NextDouble() < 0.08) v -= 0.25f;
                    gravel.SetPixel(x, y, new Color(v, v * 0.97f, v * 0.9f, 1f));
                }
            gravel.Apply();
            return gravel;
        }

        /// <summary>A packed grandstand crowd: rows of coloured shirts and heads.</summary>
        public static Texture2D Crowd()
        {
            if (crowd != null) return crowd;
            const int W = 256, H = 32;
            crowd = New(W, H);
            var px = new Color[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0.08f, 0.08f, 0.1f, 1f);
            var rnd = new System.Random(77);
            Color[] shirts = { new Color(0.9f, 0.15f, 0.15f), new Color(0.15f, 0.4f, 0.9f), new Color(0.95f, 0.85f, 0.2f), new Color(0.2f, 0.75f, 0.35f), new Color(0.95f, 0.95f, 0.95f), new Color(0.9f, 0.4f, 0.1f), new Color(0.6f, 0.2f, 0.8f) };
            Color[] skin = { new Color(0.95f, 0.78f, 0.62f), new Color(0.8f, 0.58f, 0.42f), new Color(0.55f, 0.37f, 0.26f), new Color(0.35f, 0.24f, 0.17f) };
            for (int row = 0; row < 2; row++)
                for (int cx = 4 + row * 5; cx < W; cx += 10)
                {
                    int cy = 8 + row * 12;
                    Color sh = shirts[rnd.Next(shirts.Length)], sk = skin[rnd.Next(skin.Length)];
                    for (int y = -9; y <= 3; y++)
                        for (int x = -4; x <= 4; x++)
                        {
                            int px2 = cx + x, py = cy + y;
                            if (px2 < 0 || px2 >= W || py < 0 || py >= H) continue;
                            bool head = y >= 0 && x * x + (y - 1) * (y - 1) <= 8;
                            bool body = y < 0;
                            if (head) px[py * W + px2] = sk;
                            else if (body) px[py * W + px2] = sh;
                        }
                }
            crowd.SetPixels(px);
            crowd.Apply();
            return crowd;
        }

        /// <summary>Tyre tread seen on the cylinder's side: circumferential grooves (v axis) over dark rubber.</summary>
        public static Texture2D Tread()
        {
            if (tread != null) return tread;
            const int S = 64;
            tread = New(S, S);
            var rnd = new System.Random(3);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int g = y % 16;
                    float groove = (g >= 6 && g <= 9) ? 0.35f : 1f;
                    float v = (0.16f + ((float)rnd.NextDouble() - 0.5f) * 0.05f) * groove;
                    // faint transverse sipes
                    if (x % 8 == 0 && groove > 0.9f) v *= 0.8f;
                    tread.SetPixel(x, y, new Color(v, v, v * 1.05f, 1f));
                }
            tread.Apply();
            return tread;
        }

        /// <summary>Sidewall disc: black rubber, a coloured compound ring, and tick "lettering" around it.</summary>
        public static Texture2D Sidewall(Compound c)
        {
            int k = (int)c;
            if (sidewalls[k] != null) return sidewalls[k];
            const int S = 128;
            var t = New(S, S);
            t.wrapMode = TextureWrapMode.Clamp;
            Color band = c == Compound.Soft ? new Color(1f, 0.18f, 0.14f) : c == Compound.Medium ? new Color(1f, 0.84f, 0.1f)
                       : c == Compound.Inter ? new Color(0.2f, 0.82f, 0.32f) : c == Compound.Wet ? new Color(0.25f, 0.5f, 1f) : new Color(0.94f, 0.94f, 0.96f);
            var rnd = new System.Random(8 + k);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S - 0.5f, dy = (y + 0.5f) / S - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;           // 0 centre .. 1 edge of the disc
                    float ang = Mathf.Atan2(dy, dx);
                    float v = 0.07f + ((float)rnd.NextDouble() - 0.5f) * 0.02f;
                    Color col = new Color(v, v, v * 1.05f, 1f);
                    if (r < 0.15f) col = new Color(0.55f, 0.57f, 0.6f, 1f);                       // hub
                    else if (r < 0.2f) col = new Color(0.14f, 0.14f, 0.16f, 1f);
                    else if (r > 0.58f && r < 0.68f) col = band;                                  // compound ring
                    else if (r > 0.74f && r < 0.84f)
                    {
                        // dashed "lettering"
                        int seg = Mathf.FloorToInt((ang + Mathf.PI) / (Mathf.PI * 2f) * 48f);
                        if (seg % 3 != 2 && (seg % 7) != 0) col = new Color(0.82f, 0.82f, 0.85f, 1f);
                    }
                    else if (r > 0.96f) col = new Color(0.03f, 0.03f, 0.03f, 1f);
                    t.SetPixel(x, y, col);
                }
            t.Apply();
            sidewalls[k] = t;
            return t;
        }
    }
}
