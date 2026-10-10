using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The F1 car, built from primitives: monocoque and nose cone, multi-element front and rear wings with
    /// end plates, halo, driver helmet, engine cover with shark fin, sidepods with inlets, bargeboards,
    /// floor and diffuser, suspension arms, mirrors, exhaust, rear light and sponsor / number decals.
    /// Car forward is local +Z.
    /// </summary>
    public static class CarVisual
    {
        static Texture2D checker;
        static Font font;

        static Texture2D CheckerTex()
        {
            if (checker != null) return checker;
            checker = new Texture2D(32, 8, TextureFormat.RGBA32, false);
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 32; x++)
                    checker.SetPixel(x, y, (((x / 4) + (y / 4)) % 2 == 0) ? new Color(0.95f, 0.95f, 0.97f) : new Color(0.04f, 0.04f, 0.06f));
            checker.filterMode = FilterMode.Point;
            checker.Apply();
            return checker;
        }

        static GameObject Box(Transform p, float x, float y, float z, float sx, float sy, float sz, Material m, float rx = 0f, float ry = 0f, float rz = 0f)
        {
            var g = Prim.Box(p, new Vector3(x, y, z), new Vector3(sx, sy, sz), m);
            g.transform.localRotation = Quaternion.Euler(rx, ry, rz);
            return g;
        }

        static GameObject Rod(Transform p, Vector3 a, Vector3 b, float r, Material m)
        {
            Vector3 d = b - a;
            return Prim.Cyl(p, (a + b) * 0.5f, new Vector3(r * 2f, d.magnitude / 2f, r * 2f), Quaternion.FromToRotation(Vector3.up, d), m);
        }

        static void Decal(Transform p, string text, Vector3 pos, float yawDeg, float size, Color c)
        {
            if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "Liberation Sans" }, 64);
            var go = new GameObject("decal");
            go.transform.SetParent(p, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.font = font; tm.text = text; tm.fontSize = 64; tm.characterSize = size;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = c;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        public static GameObject Build(Color body, Color accent, int pattern = 0, bool ghost = false, Compound compound = Compound.Medium, int number = 0, Color helmetColor = default(Color))
        {
            var root = new GameObject(ghost ? "GhostCar" : "Car");
            Transform r = root.transform;
            Material bodyMat = ghost ? Mats.Fade(new Color(0.93f, 0.94f, 0.97f), 0.3f) : Mats.Make(body, 0.65f, 0.25f);
            Material accentMat = ghost ? bodyMat : Mats.Make(accent, 0.45f, 0.2f);
            Material dark = bodyMat;
            Material tire = bodyMat, sidewall = bodyMat, glass = bodyMat, silver = bodyMat, rearLight = bodyMat, helmet = bodyMat;
            if (!ghost)
            {
                dark = Mats.Make(Color.white, 0.4f, 0.3f);
                Mats.SetTex(dark, Tex.Carbon(), 2f);
                tire = Mats.Make(Color.white, 0.1f);
                Mats.SetTex(tire, Tex.Tread());
                sidewall = Mats.Make(Color.white, 0.1f);
                Mats.SetTex(sidewall, Tex.Sidewall(compound));
                glass = Mats.Make(new Color(0.05f, 0.07f, 0.1f), 0.95f, 0.9f);
                silver = Mats.Make(new Color(0.72f, 0.74f, 0.78f), 0.8f, 0.9f);
                rearLight = Mats.Emissive(new Color(1f, 0.1f, 0.05f), 1.2f);
                helmet = Mats.Make(helmetColor.a > 0f ? helmetColor : accent, 0.8f, 0.2f);
            }

            // ---- core: monocoque, nose, cockpit, airbox (unchanged proportions, wheels sit around them)
            Box(r, 0, 0.45f, 0, 0.9f, 0.35f, 3.0f, bodyMat);
            Box(r, 0, 0.38f, 2.0f, 0.45f, 0.22f, 1.7f, bodyMat);
            Box(r, 0, 0.62f, -0.2f, 0.55f, 0.3f, 0.9f, dark);
            Box(r, 0, 0.95f, -0.7f, 0.45f, 0.35f, 0.7f, accentMat);
            Box(r, 0.85f, 0.4f, -0.1f, 0.6f, 0.3f, 1.4f, accentMat);
            Box(r, -0.85f, 0.4f, -0.1f, 0.6f, 0.3f, 1.4f, accentMat);
            // main front and rear wing planes keep their names: front-wing damage rescales "frontWing"
            Box(r, 0, 0.18f, 3.0f, 1.9f, 0.05f, 0.45f, accentMat).name = "frontWing";
            Box(r, 0, 1.05f, -1.75f, 1.5f, 0.05f, 0.45f, accentMat).name = "rearWing";
            Box(r, 0.6f, 0.75f, -1.75f, 0.05f, 0.6f, 0.4f, dark);
            Box(r, -0.6f, 0.75f, -1.75f, 0.05f, 0.6f, 0.4f, dark);

            if (!ghost)
            {
                // ---- nose cone and front wing assembly
                Box(r, 0, 0.32f, 3.1f, 0.24f, 0.15f, 0.6f, bodyMat);                              // nose tip
                Box(r, 0.12f, 0.27f, 2.88f, 0.04f, 0.14f, 0.3f, dark);                             // wing pylons
                Box(r, -0.12f, 0.27f, 2.88f, 0.04f, 0.14f, 0.3f, dark);
                Box(r, 0, 0.27f, 2.9f, 1.7f, 0.025f, 0.2f, accentMat, -8f);                       // upper flap
                Box(r, 0, 0.34f, 2.85f, 1.2f, 0.02f, 0.14f, dark, -14f);                          // third element
                foreach (int s in new[] { -1, 1 })
                {
                    Box(r, s * 0.95f, 0.26f, 3.0f, 0.03f, 0.27f, 0.55f, accentMat).name = "endplate" + (s < 0 ? "L" : "R");    // end plates
                    Box(r, s * 0.65f, 0.25f, 2.95f, 0.025f, 0.11f, 0.26f, dark);                 // cascade winglets
                    Box(r, s * 0.4f, 0.24f, 2.95f, 0.025f, 0.09f, 0.22f, dark);
                }

                // ---- halo, driver and cockpit rim
                Box(r, 0, 0.74f, -0.2f, 0.66f, 0.05f, 1.05f, dark);                                // cockpit rim
                foreach (int s in new[] { -1, 1 })
                {
                    Vector3 a = new Vector3(s * 0.33f, 0.70f, -0.6f), b = new Vector3(s * 0.37f, 0.94f, -0.25f);
                    Vector3 c = new Vector3(s * 0.22f, 1.06f, 0.15f), d = new Vector3(0f, 1.08f, 0.45f);
                    Rod(r, a, b, 0.028f, dark); Rod(r, b, c, 0.028f, dark); Rod(r, c, d, 0.028f, dark);
                }
                Rod(r, new Vector3(0f, 1.08f, 0.45f), new Vector3(0f, 0.68f, 0.65f), 0.03f, dark);   // centre pylon
                Prim.Sphere(r, new Vector3(0f, 0.86f, -0.2f), Vector3.one * 0.3f, helmet, "helmet");
                Box(r, 0, 0.87f, -0.06f, 0.22f, 0.08f, 0.06f, glass);                               // visor

                // ---- engine cover, shark fin, T-cam, rear crash structure
                Box(r, 0, 0.74f, -1.0f, 0.5f, 0.32f, 1.5f, bodyMat);
                Box(r, 0, 1.08f, -1.15f, 0.03f, 0.42f, 1.2f, bodyMat);
                Box(r, 0, 1.22f, -0.65f, 0.1f, 0.1f, 0.14f, dark);
                Rod(r, new Vector3(0f, 1.27f, -0.65f), new Vector3(0f, 1.42f, -0.62f), 0.008f, silver);
                Box(r, 0, 0.5f, -1.95f, 0.3f, 0.25f, 0.6f, bodyMat);

                // ---- sidepods: inlets, coke-bottle rear, bargeboards, floor, diffuser
                foreach (int s in new[] { -1, 1 })
                {
                    Box(r, s * 0.85f, 0.44f, 0.63f, 0.5f, 0.22f, 0.04f, glass);                  // inlet
                    Box(r, s * 0.75f, 0.34f, -1.0f, 0.4f, 0.22f, 0.9f, bodyMat);                  // tapered rear
                    Box(r, s * 0.58f, 0.30f, 0.75f, 0.025f, 0.26f, 0.55f, dark).name = "barge" + (s < 0 ? "L" : "R");           // bargeboards
                    Box(r, s * 0.66f, 0.28f, 0.95f, 0.025f, 0.2f, 0.45f, accentMat).name = "barge" + (s < 0 ? "L" : "R");
                    // mirrors
                    Rod(r, new Vector3(s * 0.5f, 0.72f, 0.35f), new Vector3(s * 0.68f, 0.8f, 0.42f), 0.012f, dark).name = "mirror" + (s < 0 ? "L" : "R");
                    Box(r, s * 0.72f, 0.82f, 0.42f, 0.12f, 0.07f, 0.05f, accentMat).name = "mirror" + (s < 0 ? "L" : "R");
                }
                Box(r, 0, 0.12f, -0.05f, 1.55f, 0.035f, 3.6f, dark);                              // floor
                Box(r, 0, 0.2f, -1.95f, 1.2f, 0.03f, 0.9f, dark, -14f);                           // diffuser

                // ---- rear wing: upper flap, end plates, DRS pod, beam wing, rain light and exhaust
                var drsFlapBox = Box(r, 0, 1.17f, -1.82f, 1.5f, 0.03f, 0.22f, accentMat, 10f);
                drsFlapBox.name = "drsFlap";
                root.AddComponent<DrsFlap>().flap = drsFlapBox.transform;
                foreach (int s in new[] { -1, 1 }) Box(r, s * 0.77f, 0.92f, -1.78f, 0.03f, 0.52f, 0.6f, accentMat);
                Box(r, 0, 1.12f, -1.75f, 0.06f, 0.08f, 0.1f, dark);
                Box(r, 0, 0.62f, -2.15f, 1.2f, 0.03f, 0.2f, dark);
                Box(r, 0, 0.78f, -2.27f, 0.14f, 0.1f, 0.02f, rearLight);
                Prim.Cyl(r, new Vector3(0f, 0.62f, -2.1f), new Vector3(0.1f, 0.15f, 0.1f), Quaternion.Euler(90, 0, 0), silver, "exhaust");

                // ---- suspension: wishbones and pushrods to each wheel hub
                float[] zs2 = { 1.5f, -1.4f };
                foreach (float z in zs2)
                    foreach (int s in new[] { -1, 1 })
                    {
                        Vector3 hubUp = new Vector3(s * 0.9f, 0.5f, z), hubLow = new Vector3(s * 0.9f, 0.28f, z);
                        Rod(r, new Vector3(s * 0.3f, 0.52f, z - 0.25f), hubUp, 0.013f, dark);
                        Rod(r, new Vector3(s * 0.3f, 0.52f, z + 0.25f), hubUp, 0.013f, dark);
                        Rod(r, new Vector3(s * 0.3f, 0.26f, z - 0.25f), hubLow, 0.013f, dark);
                        Rod(r, new Vector3(s * 0.3f, 0.26f, z + 0.25f), hubLow, 0.013f, dark);
                        Rod(r, new Vector3(s * 0.28f, 0.66f, z), hubLow, 0.012f, silver);
                    }

                // ---- sponsor and number decals (readable from the outside of each side)
                Color white = new Color(0.95f, 0.96f, 0.98f);
                if (number > 0)
                {
                    Decal(r, number.ToString(), new Vector3(0.26f, 0.74f, -1.0f), -90f, 0.045f, white);
                    Decal(r, number.ToString(), new Vector3(-0.26f, 0.74f, -1.0f), 90f, 0.045f, white);
                    Decal(r, number.ToString(), new Vector3(0f, 0.545f, 2.2f), 180f, 0.05f, white);
                }
            }

            // ---- livery patterns
            if (!ghost && pattern == 1)
            {
                Box(r, 0, 0.635f, 0.6f, 0.18f, 0.01f, 2.0f, accentMat);
                Box(r, 0, 0.505f, 2.0f, 0.18f, 0.01f, 1.7f, accentMat);
            }
            else if (!ghost && pattern == 2)
            {
                var mat = Mats.Make(Color.white, 0.2f);
                mat.mainTexture = CheckerTex();
                Box(r, 0, 0.505f, 2.0f, 0.46f, 0.012f, 1.7f, mat);
                Box(r, 0, 0.635f, 0.6f, 0.46f, 0.012f, 1.0f, mat);
            }

            // ---- wheels: pivot (steering, front only) -> spin node (rolls about the axle) -> tyre/rim/marker bars
            var rig = root.AddComponent<WheelRig>();
            var spins = new System.Collections.Generic.List<Transform>();
            var fronts = new System.Collections.Generic.List<Transform>();
            Quaternion onSide = Quaternion.Euler(0, 0, 90);
            float[] zs = { 1.5f, -1.4f };
            foreach (float z in zs)
                foreach (float x in new[] { -1.05f, 1.05f })
                {
                    var pivot = new GameObject("wheelPivot");
                    pivot.transform.SetParent(r, false);
                    pivot.transform.localPosition = new Vector3(x, 0.38f, z);
                    var spin = new GameObject("wheelSpin");
                    spin.transform.SetParent(pivot.transform, false);
                    Prim.Cyl(spin.transform, Vector3.zero, new Vector3(0.76f, 0.17f, 0.76f), onSide, tire, "tire");
                    Prim.Cyl(spin.transform, new Vector3(0.172f, 0f, 0f), new Vector3(0.74f, 0.004f, 0.74f), onSide, sidewall, "sideA");
                    Prim.Cyl(spin.transform, new Vector3(-0.172f, 0f, 0f), new Vector3(0.74f, 0.004f, 0.74f), onSide, sidewall, "sideB");
                    Prim.Cyl(spin.transform, new Vector3(Mathf.Sign(x) * 0.02f, 0f, 0f), new Vector3(0.44f, 0.175f, 0.44f), onSide, ghost ? bodyMat : silver, "rim");
                    // two crossed bars on the outer face so you can actually see the wheel roll
                    float face = Mathf.Sign(x) * 0.185f;
                    Prim.Box(spin.transform, new Vector3(face, 0f, 0f), new Vector3(0.015f, 0.4f, 0.07f), accentMat, "bar1");
                    Prim.Box(spin.transform, new Vector3(face, 0f, 0f), new Vector3(0.015f, 0.07f, 0.4f), accentMat, "bar2");
                    spins.Add(spin.transform);
                    if (z > 0f) fronts.Add(pivot.transform);
                }
            rig.spins = spins.ToArray();
            rig.frontPivots = fronts.ToArray();
            rig.sidewallMat = sidewall;
            if (!ghost) Scenery.AddContactShadow(root);
            return root;
        }
    }
}
