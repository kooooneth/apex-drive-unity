using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// Onboard F1 cockpit modelled on real halo-cam footage: a thick halo band across the top with the
    /// centre strut running down to the nose, painted cockpit sides, red mirror housings, the front tyres,
    /// a painted hood and a detailed steering wheel with rev lights, display, buttons and gloved hands.
    ///
    /// Placement trick: every big shape is positioned with Sp(fx, fy, z) - "where on the screen should this
    /// sit (as fractions of width/height) at this depth" - so the layout matches the reference photos by
    /// construction. The camera is 78 degrees vertical at 16:9.
    /// </summary>
    public class CockpitRig
    {
        public const int LAYER = 30;               // drawn by a dedicated overlay camera so world geometry can never clip it
        public readonly GameObject root;

        static Font font;
        Material carbon, bodyPaint, accent, glassMat;

        Transform steeringWheel;
        TextMesh gearText, brakeText, speedText, modeText, tTL, tTR, tBL, tBR;
        readonly List<Material> ledMats = new List<Material>();
        Material nLightMat, statusMat, drsMat;
        class FrontWheel { public Transform pivot, spin; public Material disc; }
        readonly List<FrontWheel> fronts = new List<FrontWheel>();
        float spinAngle;
        readonly List<Camera> mirrorCams = new List<Camera>();
        readonly List<int> mirrorSides = new List<int>();
        string lastGear = "";

        const float TH = 0.8098f;      // tan(78 deg / 2)
        const float ASPECT = 1.78f;

        // ---------- placement helpers ----------
        /// <summary>Unity camera-space point that lands at screen fraction (fx, fy) (0,0 = top-left) at depth z.</summary>
        static Vector3 Sp(float fx, float fy, float z)
        {
            return new Vector3((2f * fx - 1f) * TH * ASPECT * z, (1f - 2f * fy) * TH * z, z);
        }

        // web-space (forward = -z) point used by the steering wheel code
        static Vector3 P(float x, float y, float z) { return new Vector3(x, y, -z); }

        GameObject UBox(Transform parent, Vector3 pos, Vector3 size, Material m, Quaternion rot)
        {
            var g = Prim.Box(parent, pos, size, m);
            g.transform.localRotation = rot;
            return g;
        }

        GameObject URod(Transform parent, Vector3 a, Vector3 b, float radius, Material m)
        {
            Vector3 d = b - a;
            return Prim.Cyl(parent, (a + b) * 0.5f, new Vector3(radius * 2f, d.magnitude / 2f, radius * 2f), Quaternion.FromToRotation(Vector3.up, d), m);
        }

        void Poly(Transform parent, string name, Material m, params Vector3[] pts)
        {
            var mb = new MeshBuilder();
            for (int i = 1; i < pts.Length - 1; i++) mb.DoubleTri(pts[0], pts[i], pts[i + 1]);
            mb.PlanarUV(9f, true);
            mb.Build(name, parent, m);
        }

        GameObject Box(Transform parent, float x, float y, float z, float sx, float sy, float sz, Material m, float yawDeg = 0f, float zRotDeg = 0f, float xRotDeg = 0f)
        {
            var g = Prim.Box(parent, P(x, y, z), new Vector3(sx, sy, sz), m);
            g.transform.localRotation = Quaternion.Euler(xRotDeg, yawDeg, zRotDeg);
            return g;
        }

        // cylinder with its axis along 'x', 'y' or 'z'
        GameObject Cyl(Transform parent, float x, float y, float z, float radius, float length, char axis, Material m)
        {
            Quaternion q = axis == 'z' ? Quaternion.Euler(90, 0, 0) : axis == 'x' ? Quaternion.Euler(0, 0, 90) : Quaternion.identity;
            return Prim.Cyl(parent, P(x, y, z), new Vector3(radius * 2f, length / 2f, radius * 2f), q, m);
        }

        GameObject Rod(Transform parent, Vector3 a3, Vector3 b3, float radius, Material m)
        {
            return URod(parent, P(a3.x, a3.y, a3.z), P(b3.x, b3.y, b3.z), radius, m);
        }

        TextMesh Text(Transform parent, float x, float y, float z, string s, float size, Color c)
        {
            if (font == null) font = FontKit.Make(new[] { "Consolas", "Courier New", "Arial", "Liberation Mono" }, 64);
            var go = new GameObject("text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = P(x, y, z);
            var tm = go.AddComponent<TextMesh>();
            tm.font = font; tm.text = s; tm.fontSize = 64; tm.characterSize = size;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = c;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return tm;
        }

        static Material Glow(Color c, float k) { return Mats.Emissive(c, k); }
        static void SetGlow(Material m, Color c, float k) { m.color = c; m.SetColor("_EmissionColor", c * k); }

        // ---------- build ----------
        Color bodyCol;
        readonly List<GameObject> mirrorGlass = new List<GameObject>();

        public CockpitRig(Transform parent, Color body, Color accentColor)
        {
            bodyCol = body;
            root = new GameObject("CockpitRig");
            root.transform.SetParent(parent, false);
            Transform r = root.transform;

            carbon = Mats.Make(Color.white, 0.5f, 0.35f);
            Mats.SetTex(carbon, Tex.Carbon(), 3f);
            bodyPaint = Mats.Make(body, 0.75f, 0.45f);
            accent = Mats.Make(accentColor, 0.4f, 0.4f);
            glassMat = Mats.Make(new Color(0.06f, 0.08f, 0.10f), 0.95f, 0.9f);

            Halo(r);
            BodyWork(r);
            Mirrors(r);
            Wheel(r);
            FrontWheels(r);
            foreach (var t in root.GetComponentsInChildren<Transform>()) t.gameObject.layer = LAYER;
        }

        // thick halo: in the reference photos it is not a thin bar but a big dark wedge hanging from the top
        // of the picture (bottom edge around 20-27% down at the sides), dipping into a V where the centre
        // strut starts (about 40% down) and running on down to the nose (about 57% down)
        void Halo(Transform r)
        {
            float[,] edge = { { 0f, 0.18f }, { 0.12f, 0.20f }, { 0.30f, 0.25f }, { 0.43f, 0.33f }, { 0.5f, 0.40f }, { 0.57f, 0.33f }, { 0.70f, 0.25f }, { 0.88f, 0.20f }, { 1f, 0.18f } };
            var mb = new MeshBuilder();
            for (int i = 0; i < edge.GetLength(0) - 1; i++)
            {
                Vector3 b0 = Sp(edge[i, 0], edge[i, 1], 0.75f), b1 = Sp(edge[i + 1, 0], edge[i + 1, 1], 0.75f);
                Vector3 t0 = Sp(edge[i, 0], -0.15f, 0.75f), t1 = Sp(edge[i + 1, 0], -0.15f, 0.75f);
                mb.DoubleTri(t0, t1, b1);
                mb.DoubleTri(t0, b1, b0);
            }
            mb.PlanarUV(9f, true);
            mb.Build("Halo", r, carbon);
            for (int i = 0; i < edge.GetLength(0) - 1; i++)
                URod(r, Sp(edge[i, 0], edge[i, 1] - 0.004f, 0.74f), Sp(edge[i + 1, 0], edge[i + 1, 1] - 0.004f, 0.74f), 0.012f, accent);
            // centre strut: V apex -> nose
            URod(r, Sp(0.5f, 0.40f, 0.75f), Sp(0.5f, 0.57f, 2.4f), 0.04f, carbon);
            UBox(r, Sp(0.5f, 0.30f, 0.72f), new Vector3(0.07f, 0.07f, 0.08f), carbon, Quaternion.identity);
            Prim.Sphere(r, Sp(0.5f, 0.30f, 0.68f), Vector3.one * 0.035f, glassMat, "lens");
        }

        // painted tub sides, hood, and the black carbon surround behind the wheel
        void BodyWork(Transform r)
        {
            Sides(r);
            // bodywork in front of the front tyres, so only their tops peek out above it
            foreach (int s in new[] { -1, 1 })
            {
                float sgn = s;
                System.Func<float, float> fxf = x => sgn < 0f ? x : 1f - x;
                Poly(r, "Fairing" + (s < 0 ? "L" : "R"), bodyPaint,
                    Sp(fxf(0f), 0.715f, 0.9f), Sp(fxf(0.15f), 0.70f, 0.9f), Sp(fxf(0.30f), 0.675f, 0.9f), Sp(fxf(0.385f), 0.655f, 0.9f),
                    Sp(fxf(0.385f), 1.0f, 0.9f), Sp(fxf(0f), 1.0f, 0.9f));
            }
            // hood running from the wheel out to the nose
            Poly(r, "Hood", bodyPaint,
                Sp(0.47f, 0.575f, 2.4f), Sp(0.53f, 0.575f, 2.4f), Sp(0.66f, 0.68f, 1.3f),
                Sp(0.69f, 0.78f, 0.9f), Sp(0.31f, 0.78f, 0.9f), Sp(0.34f, 0.68f, 1.3f));
            URod(r, Sp(0.5f, 0.575f, 2.4f), Sp(0.5f, 0.78f, 0.9f), 0.014f, accent);
            // black carbon dash behind the wheel
            Poly(r, "DashSurround", carbon,
                Sp(0.30f, 0.74f, 0.95f), Sp(0.70f, 0.74f, 0.95f), Sp(0.75f, 1.0f, 0.8f), Sp(0.25f, 1.0f, 0.8f));
        }

        // The cockpit tub: a curved wall on each side that flares out from the floor, rises past your shoulders
        // and curls back in at the top lip, running forward and narrowing / dropping toward the nose - so the
        // painted body wraps around you instead of sitting as two flat patches.
        void Sides(Transform r)
        {
            // cross-section (distance from centre line, height) from the floor up to the curled lip
            float[,] prof = { { 0.28f, -0.95f }, { 0.42f, -0.90f }, { 0.55f, -0.79f }, { 0.61f, -0.60f }, { 0.60f, -0.42f }, { 0.55f, -0.30f }, { 0.48f, -0.23f } };
            float[] zs = { 0.45f, 0.8f, 1.25f, 1.75f, 2.3f, 2.9f };
            int np = prof.GetLength(0);
            foreach (int side in new[] { -1, 1 })
            {
                var mb = new MeshBuilder();
                var mbCarbon = new MeshBuilder();
                var grid = new Vector3[zs.Length, np];
                for (int i = 0; i < zs.Length; i++)
                {
                    float k = (zs[i] - zs[0]) / (zs[zs.Length - 1] - zs[0]);           // 0 at the driver, 1 at the nose
                    float narrow = 1f - 0.30f * k, drop = 0.55f * k;                   // the tub narrows and slopes down forward
                    for (int j = 0; j < np; j++)
                        grid[i, j] = new Vector3(side * prof[j, 0] * narrow, -0.95f + (prof[j, 1] + 0.95f) * (1f - 0.4f * k) - 0.0f * drop, zs[i]);
                }
                for (int i = 0; i < zs.Length - 1; i++)
                    for (int j = 0; j < np - 1; j++)
                    {
                        var target = j < 3 ? mbCarbon : mb;       // lower inside face is bare carbon, the top band is painted
                        target.DoubleTri(grid[i, j], grid[i, j + 1], grid[i + 1, j + 1]);
                        target.DoubleTri(grid[i, j], grid[i + 1, j + 1], grid[i + 1, j]);
                    }
                mbCarbon.PlanarUVSide(2f);
                mbCarbon.Build("CockpitSideCarbon" + (side < 0 ? "L" : "R"), r, carbon);
                mb.Build("CockpitSide" + (side < 0 ? "L" : "R"), r, bodyPaint);
                // carbon rim along the curled lip
                for (int i = 0; i < zs.Length - 1; i++) URod(r, grid[i, np - 1], grid[i + 1, np - 1], 0.022f, carbon);
            }
        }

        void Tub(int side, Transform r)
        {
            System.Func<float, float> fx = x => side < 0 ? x : 1f - x;
            Poly(r, "Tub" + (side < 0 ? "L" : "R"), bodyPaint,
                Sp(fx(0.0f), 0.61f, 1.8f), Sp(fx(0.18f), 0.61f, 1.5f), Sp(fx(0.30f), 0.63f, 1.2f), Sp(fx(0.34f), 0.72f, 0.9f), Sp(fx(0.31f), 1.0f, 0.7f), Sp(fx(0.0f), 1.0f, 0.7f));
            URod(r, Sp(fx(0.18f), 0.61f, 1.5f), Sp(fx(0.30f), 0.63f, 1.2f), 0.016f, carbon);
            URod(r, Sp(fx(0.30f), 0.63f, 1.2f), Sp(fx(0.34f), 0.72f, 0.9f), 0.016f, carbon);
        }

        void Mirrors(Transform r)
        {
            foreach (int side in new[] { -1, 1 })
            {
                float fx = 0.5f + side * 0.245f;
                Vector3 c = Sp(fx, 0.47f, 1.5f);
                UBox(r, c, new Vector3(0.34f, 0.18f, 0.09f), bodyPaint, Quaternion.identity);
                // the glass is a live picture: a small camera behind the car renders into a texture shown here (flipped, like a real mirror)
                var rt = new RenderTexture(256, 128, 16);
                var gm = Mats.Make(Color.white, 0.1f);
                gm.mainTexture = rt; gm.mainTextureScale = new Vector2(-1f, 1f); gm.mainTextureOffset = new Vector2(1f, 0f);
                gm.EnableKeyword("_EMISSION"); gm.SetTexture("_EmissionMap", rt); gm.SetColor("_EmissionColor", new Color(0.8f, 0.8f, 0.8f));
                mirrorGlass.Add(UBox(r, c + new Vector3(0f, 0f, -0.048f), new Vector3(0.28f, 0.13f, 0.012f), gm, Quaternion.identity));
                var cgo = new GameObject("MirrorCam"); cgo.transform.SetParent(r, false);
                var mc = cgo.AddComponent<Camera>();
                mc.targetTexture = rt; mc.fieldOfView = 52f; mc.nearClipPlane = 0.3f; mc.farClipPlane = 900f;
                mc.clearFlags = CameraClearFlags.SolidColor; mc.cullingMask = ~(1 << LAYER); mc.depth = -2f;
                mirrorCams.Add(mc); mirrorSides.Add(side);
                UBox(r, c + new Vector3(0f, 0.095f, 0f), new Vector3(0.34f, 0.014f, 0.092f), accent, Quaternion.identity);
                UBox(r, c + new Vector3(side * 0.19f, 0f, 0f), new Vector3(0.014f, 0.1f, 0.12f), carbon, Quaternion.identity);
                URod(r, Sp(0.5f + side * 0.20f, 0.64f, 1.1f), c + new Vector3(-side * 0.1f, -0.06f, 0f), 0.02f, carbon);
            }
        }

        void Wheel(Transform r)
        {
            var g = new GameObject("SteeringWheel");
            g.transform.SetParent(r, false);
            g.transform.localPosition = P(0f, -0.27f, -0.5f);
            g.transform.localScale = Vector3.one * 1.0f;
            steeringWheel = g.transform;
            Transform w = g.transform;

            // a solid carbon slab like a real F1 wheel: a rounded outline (the bevelled edge), a flush face plate and two thick
            // padded grips down the sides that the gloves wrap round
            Vector3[] pts =
            {
                new Vector3(-0.17f, 0.145f, 0.004f), new Vector3(0.17f, 0.145f, 0.004f), new Vector3(0.225f, 0.085f, 0.004f), new Vector3(0.232f, -0.02f, 0.004f),
                new Vector3(0.205f, -0.13f, 0.004f), new Vector3(-0.205f, -0.13f, 0.004f), new Vector3(-0.232f, -0.02f, 0.004f), new Vector3(-0.225f, 0.085f, 0.004f)
            };
            for (int i = 0; i < pts.Length; i++)
            {
                Rod(w, pts[i], pts[(i + 1) % pts.Length], 0.019f, carbon);
                Prim.Sphere(w, P(pts[i].x, pts[i].y, pts[i].z), Vector3.one * 0.038f, carbon, "joint");
            }
            // face plate behind the display so the wheel reads as a solid black block with a bright screen
            Box(w, 0f, 0.005f, 0.010f, 0.43f, 0.27f, 0.014f, carbon);
            var rubber = Mats.Make(new Color(0.12f, 0.12f, 0.14f), 0.15f);
            foreach (int side in new[] { -1, 1 })
                Tube(w, "grip", rubber, new[] { new Vector3(side * 0.230f, 0.062f, 0.006f), new Vector3(side * 0.231f, -0.02f, 0.012f), new Vector3(side * 0.226f, -0.108f, 0.016f) }, 0.031f, 0.030f, 4, 12);
            Box(w, 0f, -0.08f, 0f, 0.022f, 0.16f, 0.02f, carbon);
            Box(w, 0f, 0.135f, 0f, 0.02f, 0.04f, 0.02f, accent);
            foreach (int side in new[] { -1, 1 })
            {
                Box(w, side * 0.2f, 0.02f, -0.025f, 0.06f, 0.012f, 0.03f, carbon, 0f, 0f, -8.6f);
                Prim.Sphere(w, P(side * 0.21f, 0.05f, 0.012f), Vector3.one * 0.028f, Mats.Make(new Color(0.17f, 0.18f, 0.21f), 0.3f), "thumb");
            }

            // rev lights across the top of the rim
            for (int i = 0; i < 9; i++)
            {
                var m = Glow(new Color(0.11f, 0.13f, 0.19f), 0f);
                ledMats.Add(m);
                Cyl(w, -0.14f + (i / 8f) * 0.28f, 0.128f, 0.021f, 0.009f, 0.004f, 'z', m);
            }

            // scattered buttons (the two top ones double as the pit status light and the DRS indicator)
            Color[] bc = { new Color(1f, 0.29f, 0.18f), new Color(1f, 0.69f, 0.13f), new Color(0.09f, 0.85f, 0.77f), new Color(0.24f, 0.86f, 0.52f), new Color(1f, 0.37f, 0.63f), new Color(0.54f, 0.36f, 1f), new Color(0.93f, 0.94f, 0.97f), new Color(1f, 0.29f, 0.18f) };
            float[,] lay = { { -0.185f, 0.075f }, { -0.185f, 0.03f }, { -0.185f, -0.02f }, { -0.185f, -0.065f }, { 0.185f, 0.075f }, { 0.185f, 0.03f }, { 0.185f, -0.02f }, { 0.185f, -0.065f } };
            for (int i = 0; i < 8; i++) Cyl(w, lay[i, 0], lay[i, 1], 0.018f, 0.011f, 0.012f, 'z', Mats.Make(bc[i], 0.4f, 0.3f));
            statusMat = Glow(new Color(0.16f, 0.04f, 0.02f), 0f);
            Cyl(w, -0.1f, 0.11f, 0.018f, 0.013f, 0.012f, 'z', statusMat);
            drsMat = Glow(new Color(0.04f, 0.04f, 0.04f), 0f);
            Cyl(w, 0.1f, 0.11f, 0.018f, 0.013f, 0.012f, 'z', drsMat);
            Cyl(w, 0.155f, -0.11f, 0.018f, 0.02f, 0.013f, 'z', Mats.Make(new Color(1f, 0.69f, 0.13f), 0.35f, 0.4f));

            // row of coloured rotary dials under the screen, each with a dark pointer
            Color[] dc = { new Color(1f, 0.78f, 0.12f), new Color(0.95f, 0.2f, 0.16f), new Color(0.28f, 0.55f, 1f), new Color(0.3f, 0.85f, 0.4f), new Color(0.93f, 0.94f, 0.97f) };
            float[,] dp = { { -0.105f, -0.085f }, { -0.052f, -0.122f }, { 0f, -0.088f }, { 0.052f, -0.122f }, { 0.105f, -0.085f } };
            for (int i = 0; i < 5; i++)
            {
                Cyl(w, dp[i, 0], dp[i, 1], 0.016f, 0.027f, 0.008f, 'z', Mats.Make(dc[i], 0.35f, 0.3f));
                Cyl(w, dp[i, 0], dp[i, 1], 0.0205f, 0.012f, 0.006f, 'z', carbon);
                Box(w, dp[i, 0], dp[i, 1], 0.0235f, 0.006f, 0.026f, 0.004f, Mats.Make(new Color(0.05f, 0.05f, 0.06f), 0.2f), 0f, i * 55f - 110f, 0f);
            }

            // neutral light + gear display
            nLightMat = Glow(new Color(0.24f, 0.86f, 0.52f), 0.8f);
            Cyl(w, -0.14f, 0.085f, 0.019f, 0.022f, 0.004f, 'z', nLightMat);
            Text(w, -0.14f, 0.085f, 0.0235f, "N", 0.0045f, new Color(0.04f, 0.1f, 0.06f));

            Box(w, 0f, 0.07f, 0.017f, 0.16f, 0.095f, 0.004f, Mats.Make(new Color(0.03f, 0.045f, 0.07f), 0.2f));
            Box(w, 0f, 0.1175f, 0.0165f, 0.162f, 0.003f, 0.004f, Glow(new Color(1f, 0.69f, 0.13f), 0.5f));
            Box(w, 0f, 0.0225f, 0.0165f, 0.162f, 0.003f, 0.004f, Glow(new Color(1f, 0.69f, 0.13f), 0.5f));
            float z = 0.0215f;
            var white = new Color(0.93f, 0.94f, 0.97f); var grey = new Color(0.55f, 0.58f, 0.65f);
            modeText = Text(w, 0f, 0.106f, z, "BAL", 0.0035f, new Color(0.24f, 0.86f, 0.52f));
            brakeText = Text(w, -0.058f, 0.085f, z, "0%", 0.0042f, white);
            speedText = Text(w, 0.058f, 0.085f, z, "0", 0.0042f, white);
            gearText = Text(w, 0f, 0.058f, z, "N", 0.012f, white);
            tTL = Text(w, -0.058f, 0.032f, z, "90", 0.003f, grey);
            tTR = Text(w, 0.058f, 0.032f, z, "90", 0.003f, grey);
            tBL = Text(w, -0.058f, 0.0195f, z, "90", 0.003f, grey);
            tBR = Text(w, 0.058f, 0.0195f, z, "90", 0.003f, grey);

            // gloved hands (designed against the real cockpit camera and F1 onboard photos): chunky dark knit gloves, four fingers
            // curled over the front of the grip, a padded back of the hand with a white logo patch, a thumb reaching towards the
            // buttons and a gauntlet cuff. The hands turn with the wheel; the forearms do not (see UpdateArms)
            var knit = KnitTex();
            var glove = Mats.Make(new Color(0.40f, 0.41f, 0.47f), 0.18f); Mats.SetTex(glove, knit, 5f);
            var guard = Mats.Make(new Color(0.34f, 0.35f, 0.42f), 0.25f);
            var logo = Mats.Make(new Color(0.92f, 0.92f, 0.95f), 0.2f);
            cuffMat = glove; cuffRingMat = logo;
            sleeveMat = Mats.Make(Color.Lerp(bodyCol, Color.black, 0.35f), 0.2f);
            const float y0 = -0.02f, SC = 1.12f;
            float[] fingerOff = { 0.045f, 0.015f, -0.015f, -0.045f };      // the fingers sit side by side, touching
            float[] fingerR = { 0.0165f, 0.0168f, 0.0165f, 0.0150f };
            const float WRAP = 0.040f;                                   // distance of the fingers from the middle of the grip
            foreach (int side in new[] { -1, 1 })
            {
                float gx = side * 0.230f;
                for (int f = 0; f < 4; f++)
                {
                    float[] angs = f < 3 ? new[] { 12f, -25f, -62f, -100f, -140f } : new[] { 12f, -25f, -62f, -100f };      // the fingers wrap round the BACK of the grip; the little finger is shorter
                    var ctrl = new Vector3[angs.Length];
                    for (int k = 0; k < angs.Length; k++)
                    {
                        float ar = angs[k] * Mathf.Deg2Rad;
                        ctrl[k] = new Vector3(gx + side * WRAP * Mathf.Cos(ar), y0 + fingerOff[f], WRAP * Mathf.Sin(ar) + 0.012f);
                    }
                    Tube(w, "finger", glove, ctrl, fingerR[f], fingerR[f] * 0.85f, 5, 10);
                }
                // the back of the hand narrowing into the wrist, with a padded knuckle guard and a white logo patch
                Prim.Sphere(w, P(gx + side * 0.056f, y0, 0.032f), new Vector3(0.050f, 0.152f, 0.074f), glove, "hand");
                Prim.Sphere(w, P(gx + side * 0.060f, y0 - 0.050f, 0.046f), new Vector3(0.056f, 0.076f, 0.064f), glove, "heel");
                Prim.Sphere(w, P(gx + side * 0.054f, y0, 0.062f), new Vector3(0.022f, 0.120f, 0.018f), guard, "knuckleGuard");
                Prim.Sphere(w, P(gx + side * 0.070f, y0 - 0.004f, 0.058f), new Vector3(0.015f, 0.060f, 0.022f), logo, "logo");
                // the thumb lies along the FRONT of the grip, pointing up and a little inwards with a slight bend at the joint
                var thumb = new[]
                {
                    new Vector3(gx + side * 0.040f, y0 + 0.040f, 0.050f),
                    new Vector3(gx + side * 0.024f, y0 + 0.070f, 0.058f),
                    new Vector3(gx + side * 0.010f, y0 + 0.098f, 0.056f)
                };
                Tube(w, "thumb", glove, thumb, 0.0185f, 0.0145f, 4, 10);

                // where the wrist is (it moves with the wheel) and the parts of the arm that are placed every frame
                int i = side < 0 ? 0 : 1;
                var anchor = new GameObject("wrist"); anchor.transform.SetParent(w, false);
                anchor.transform.localPosition = P(gx + side * 0.066f, y0 - 0.060f, 0.062f);
                wristAnchors[i] = anchor.transform;
                cuffObj[i] = Prim.Cyl(r, Vector3.zero, Vector3.one, Quaternion.identity, cuffMat, "cuff").transform;
                stripeObj[i] = Prim.Cyl(r, Vector3.zero, Vector3.one, Quaternion.identity, cuffRingMat, "cuffRing").transform;
                sleeveObj[i] = Prim.Cyl(r, Vector3.zero, Vector3.one, Quaternion.identity, sleeveMat, "sleeve").transform;
            }
        }

        // a fine woven pattern so the gloves look like fabric
        static Texture2D knitTex;
        static Texture2D KnitTex()
        {
            if (knitTex != null) return knitTex;
            const int S = 32;
            knitTex = new Texture2D(S, S, TextureFormat.RGBA32, true);
            knitTex.wrapMode = TextureWrapMode.Repeat;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float wv = 0.5f + 0.5f * Mathf.Sin(x * Mathf.PI / 2f), wh = 0.5f + 0.5f * Mathf.Sin((y + (x % 8 < 4 ? 0 : 2)) * Mathf.PI / 2f);
                    float v = 0.70f + 0.30f * wv * wh;
                    knitTex.SetPixel(x, y, new Color(v, v, v, 1f));
                }
            knitTex.Apply();
            return knitTex;
        }

        // the forearms: a fixed elbow low and outside the cockpit, a wrist that follows the wheel, so the arm swings naturally
        readonly Transform[] wristAnchors = new Transform[2], cuffObj = new Transform[2], stripeObj = new Transform[2], sleeveObj = new Transform[2];
        Material cuffMat, cuffRingMat, sleeveMat;

        static void Place(Transform t, Vector3 a, Vector3 b, float radius)
        {
            Vector3 d = b - a;
            t.localPosition = (a + b) * 0.5f;
            t.localRotation = Quaternion.FromToRotation(Vector3.up, d);
            t.localScale = new Vector3(radius * 2f, d.magnitude * 0.5f, radius * 2f);
        }

        void UpdateArms()
        {
            for (int i = 0; i < 2; i++)
            {
                if (wristAnchors[i] == null) continue;
                int side = i == 0 ? -1 : 1;
                Vector3 wr = root.transform.InverseTransformPoint(wristAnchors[i].position);
                Vector3 elbow = new Vector3(side * 0.36f, -0.62f, -0.10f);
                Vector3 dir = (elbow - wr).normalized;
                Place(cuffObj[i], wr - dir * 0.01f, wr + dir * 0.07f, 0.037f);
                Place(stripeObj[i], wr + dir * 0.058f, wr + dir * 0.074f, 0.0395f);
                Place(sleeveObj[i], wr + dir * 0.07f, elbow, 0.047f);
            }
        }

        // ---- smooth swept tube (a finger, a thumb, a wrist) along a curve, with rounded ends; coordinates in the wheel's own P-space ----
        GameObject Tube(Transform parent, string name, Material m, Vector3[] ctrlP, float r0, float r1, int perSeg, int sides)
        {
            // control points -> smooth Catmull-Rom path in Unity space
            var c = new Vector3[ctrlP.Length];
            for (int i = 0; i < c.Length; i++) c[i] = P(ctrlP[i].x, ctrlP[i].y, ctrlP[i].z);
            var path = new List<Vector3>();
            for (int i = 0; i < c.Length - 1; i++)
            {
                Vector3 p0 = c[Mathf.Max(0, i - 1)], p1 = c[i], p2 = c[i + 1], p3 = c[Mathf.Min(c.Length - 1, i + 2)];
                for (int k = 0; k < perSeg; k++)
                {
                    float u = (float)k / perSeg, u2 = u * u, u3 = u2 * u;
                    path.Add(0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3));
                }
            }
            path.Add(c[c.Length - 1]);
            int n = path.Count;

            // rings: a rounded cap at each end and the tapering tube between
            var centers = new List<Vector3>(); var radii = new List<float>(); var tans = new List<Vector3>();
            Vector3 tStart = (path[1] - path[0]).normalized, tEnd = (path[n - 1] - path[n - 2]).normalized;
            for (int j = 0; j < 3; j++)
            {
                float a = (90f - j * 30f) * Mathf.Deg2Rad;
                centers.Add(path[0] - tStart * r0 * Mathf.Sin(a)); radii.Add(Mathf.Max(0.0005f, r0 * Mathf.Cos(a))); tans.Add(tStart);
            }
            for (int i = 0; i < n; i++)
            {
                Vector3 tg = (path[Mathf.Min(n - 1, i + 1)] - path[Mathf.Max(0, i - 1)]).normalized;
                centers.Add(path[i]); radii.Add(Mathf.Lerp(r0, r1, (float)i / (n - 1))); tans.Add(tg);
            }
            for (int j = 1; j <= 3; j++)
            {
                float a = j * 30f * Mathf.Deg2Rad;
                centers.Add(path[n - 1] + tEnd * r1 * Mathf.Sin(a)); radii.Add(Mathf.Max(0.0005f, r1 * Mathf.Cos(a))); tans.Add(tEnd);
            }

            int rings = centers.Count;
            var verts = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
            Vector3 normal = Vector3.Cross(tans[0], Mathf.Abs(Vector3.Dot(tans[0], Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized;
            for (int i = 0; i < rings; i++)
            {
                Vector3 tg = tans[i];
                normal = (normal - tg * Vector3.Dot(normal, tg)).normalized;           // keep the ring's orientation from twisting
                Vector3 bin = Vector3.Cross(tg, normal);
                for (int j = 0; j < sides; j++)
                {
                    float ang = Mathf.PI * 2f * j / sides;
                    verts.Add(centers[i] + (normal * Mathf.Cos(ang) + bin * Mathf.Sin(ang)) * radii[i]);
                    uvs.Add(new Vector2((float)j / sides * 2f, i * 0.5f));
                }
            }
            for (int i = 0; i < rings - 1; i++)
                for (int j = 0; j < sides; j++)
                {
                    int j2 = (j + 1) % sides;
                    int a0 = i * sides + j, a1 = i * sides + j2, b0 = (i + 1) * sides + j, b1 = (i + 1) * sides + j2;
                    AddOutward(tris, verts, a0, a1, b0, centers[i]);
                    AddOutward(tris, verts, a1, b1, b0, centers[i]);
                }
            var mesh = new Mesh();
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        static void AddOutward(List<int> tris, List<Vector3> v, int a, int b, int c, Vector3 inside)
        {
            Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            Vector3 cen = (v[a] + v[b] + v[c]) / 3f;
            if (Vector3.Dot(n, cen - inside) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
            else { tris.Add(a); tris.Add(c); tris.Add(b); }
        }

        void FrontWheels(Transform r)
        {
            var tire = Mats.Make(Color.white, 0.1f);
            Mats.SetTex(tire, Tex.Tread());
            var sidewall = Mats.Make(Color.white, 0.1f);
            Mats.SetTex(sidewall, Tex.Sidewall(GameSave.compound));
            var rim = Mats.Make(new Color(0.55f, 0.58f, 0.62f), 0.3f, 0.8f);
            const float R = 0.34f, W = 0.42f;
            foreach (int side in new[] { -1, 1 })
            {
                var pivot = new GameObject("fwPivot"); pivot.transform.SetParent(r, false);
                pivot.transform.localPosition = Sp(0.5f + side * 0.385f, 0.716f, 1.6f);
                var orient = new GameObject("fwOrient"); orient.transform.SetParent(pivot.transform, false);
                orient.transform.localRotation = Quaternion.Euler(0, 0, 90);
                var spin = new GameObject("fwSpin"); spin.transform.SetParent(orient.transform, false);
                Prim.Cyl(spin.transform, Vector3.zero, new Vector3(R * 2f, W / 2f, R * 2f), Quaternion.identity, tire, "tire");
                Prim.Cyl(spin.transform, new Vector3(0f, W / 2f + 0.004f, 0f), new Vector3(R * 1.96f, 0.004f, R * 1.96f), Quaternion.identity, sidewall, "sideA");
                Prim.Cyl(spin.transform, new Vector3(0f, -W / 2f - 0.004f, 0f), new Vector3(R * 1.96f, 0.004f, R * 1.96f), Quaternion.identity, sidewall, "sideB");
                Prim.Cyl(spin.transform, Vector3.zero, new Vector3(R * 1.24f, W * 0.4f + 0.005f, R * 1.24f), Quaternion.identity, rim, "rim");
                Prim.Cyl(spin.transform, Vector3.zero, new Vector3(R * 0.28f, W * 0.43f + 0.01f, R * 0.28f), Quaternion.identity, rim, "hub");
                var disc = Mats.Make(new Color(0.16f, 0.08f, 0.06f), 0.55f, 0.6f);
                disc.EnableKeyword("_EMISSION");
                Prim.Cyl(spin.transform, Vector3.zero, new Vector3(R * 0.96f, W * 0.25f + 0.015f, R * 0.96f), Quaternion.identity, disc, "disc");
                fronts.Add(new FrontWheel { pivot = pivot.transform, spin = spin.transform, disc = disc });
            }
        }

        /// <summary>Points each mirror camera backwards from the car's rear corners.</summary>
        /// <summary>A mirror knocked off: its glass disappears and its camera stops.</summary>
        public void SetMirrorLost(int side, bool lost)
        {
            for (int i = 0; i < mirrorSides.Count; i++)
                if (mirrorSides[i] == side) { mirrorCams[i].enabled = !lost; if (i < mirrorGlass.Count) mirrorGlass[i].SetActive(!lost); }
        }

        public void UpdateMirrors(Vector3 carWorldPos, float carYawDeg, Color sky)
        {
            float yaw = carYawDeg * Mathf.Deg2Rad;
            Vector3 right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw)), fwd = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            for (int i = 0; i < mirrorCams.Count; i++)
            {
                int side = mirrorSides[i];
                mirrorCams[i].transform.position = carWorldPos + Vector3.up * 0.95f + right * side * 0.95f + fwd * 0.3f;
                mirrorCams[i].transform.rotation = Quaternion.Euler(0f, carYawDeg + 180f - side * 12f, 0f);
                mirrorCams[i].backgroundColor = sky;
            }
        }

        static void TyreText(TextMesh t, float temp, PlayerCar p)
        {
            t.text = Mathf.RoundToInt(temp).ToString();
            t.color = temp < p.TempLowForCompound - 5f ? new Color(0.45f, 0.65f, 1f) : temp > p.TempHighForCompound + 5f ? new Color(1f, 0.38f, 0.28f) : new Color(0.45f, 0.9f, 0.55f);
        }

        // ---------- per-frame ----------
        static readonly Color LedOff = new Color(0.11f, 0.13f, 0.19f);

        public void Refresh(PlayerCar p, float dt, float maxSpeed)
        {
            steeringWheel.localRotation = Quaternion.Euler(0f, 0f, p.steerSmooth * 70f);
            UpdateArms();

            spinAngle += (p.speed / 0.45f) * dt * Mathf.Rad2Deg;
            float glowTarget = Mathf.Clamp01((p.brakeTemp - 450f) / 650f);   // discs glow with their real temperature
            float steerDeg = -p.steerSmooth * 0.45f * Mathf.Rad2Deg;
            foreach (var f in fronts)
            {
                f.pivot.localRotation = Quaternion.Euler(0f, steerDeg, 0f);
                f.spin.localRotation = Quaternion.Euler(0f, spinAngle, 0f);
                Color e = new Color(1f, 0.33f, 0.13f) * glowTarget * 1.5f;
                f.disc.SetColor("_EmissionColor", e);
            }

            string gear = p.speed <= 0.3f ? "N" : p.dyn.gear.ToString();
            int lit = Mathf.RoundToInt(Mathf.Clamp01((p.dyn.rpm - 8600f) / 4200f) * ledMats.Count);
            for (int i = 0; i < ledMats.Count; i++)
            {
                bool on = i < lit;
                Color c = on ? (i < 3 ? new Color(0.24f, 0.86f, 0.52f) : i < 6 ? new Color(1f, 0.69f, 0.13f) : new Color(1f, 0.29f, 0.18f)) : LedOff;
                SetGlow(ledMats[i], c, on ? 1f : 0f);
            }
            if (gear != lastGear)
            {
                lastGear = gear;
                gearText.text = gear;
                SetGlow(nLightMat, gear == "N" ? new Color(0.24f, 0.86f, 0.52f) : new Color(0.11f, 0.23f, 0.16f), gear == "N" ? 0.8f : 0f);
            }
            brakeText.text = Mathf.RoundToInt(p.brakeTemp) + "C";
            speedText.text = Mathf.Max(0, Mathf.RoundToInt(p.speed * 3.6f)).ToString();
            modeText.text = p.engineMode == EngineMode.Push ? "PUSH" : p.engineMode == EngineMode.Save ? "SAVE" : "BAL";
            // the real surface temperature of each tyre: blue when cold, green in its window, red when it is cooking
            TyreText(tTL, p.dyn.surf[0], p); TyreText(tTR, p.dyn.surf[1], p);
            TyreText(tBL, p.dyn.surf[2], p); TyreText(tBR, p.dyn.surf[3], p);

            if (p.pitStopActive) SetGlow(statusMat, new Color(1f, 0.29f, 0.18f), 0.6f + Mathf.Sin(Time.time * 20f) * 0.4f);
            else SetGlow(statusMat, new Color(0.16f, 0.04f, 0.02f), 0f);
            SetGlow(drsMat, p.drsActive ? new Color(0.09f, 0.85f, 0.77f) : p.drsAvailable ? new Color(0.05f, 0.37f, 0.34f) : new Color(0.04f, 0.04f, 0.04f),
                    p.drsActive ? 1f : p.drsAvailable ? 0.6f : 0f);
        }
    }
}
