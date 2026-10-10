using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// Night racing: floodlight poles along the track with glowing lamp heads, soft pools of light on the road and a few real lights that
    /// follow the car, and a sky full of stars. Everything stays off in the daytime.
    /// </summary>
    public class Floodlights
    {
        readonly List<Vector3> lamps = new List<Vector3>();
        readonly List<Material> poolMats = new List<Material>();
        Material lampMat;
        readonly Light[] lights = new Light[4];
        float night;

        static bool NearPit(TrackModel m, float d, float range)
        {
            float dd = Mathf.Abs(d - m.PitZoneDist);
            dd = Mathf.Min(dd, m.roadLength - dd);
            return dd < range;
        }

        public static Floodlights Build(TrackModel m, Transform parent)
        {
            var f = new Floodlights();
            var root = new GameObject("Floodlights");
            root.transform.SetParent(parent, false);
            var pole = Mats.Make(new Color(0.22f, 0.24f, 0.27f), 0.2f, 0.4f);
            f.lampMat = Mats.Emissive(new Color(1f, 0.93f, 0.78f), 0f);
            var soft = Effects.Soft();

            float spacing = 62f; int count = Mathf.Max(8, Mathf.RoundToInt(m.roadLength / spacing));
            for (int i = 0; i < count; i++)
            {
                float d = m.roadLength * i / count;
                if (NearPit(m, d, 50f)) continue;
                int side = (i % 2 == 0) ? 1 : -1;
                Vector2 n = TrackModel.Normal(m.TangentAt(d));
                float e = m.ElevationAt(d);
                float off = m.WallOffsetAt(d) + 3.5f;
                Vector2 basePos = m.PointAt(d) + n * side * off;
                Vector3 b = TrackModel.W(basePos, e);
                Vector3 inward = TrackModel.W(-n * side, 0f);
                Prim.Cyl(root.transform, b + Vector3.up * 5.5f, new Vector3(0.28f, 5.5f, 0.28f), Quaternion.identity, pole, "pole");
                Vector3 head = b + Vector3.up * 11.2f + inward * 1.8f;
                var arm = Prim.Box(root.transform, b + Vector3.up * 11.0f + inward * 0.9f, new Vector3(0.18f, 0.18f, 2.0f), pole, "arm");
                arm.transform.rotation = Quaternion.LookRotation(inward);
                var lamp = Prim.Box(root.transform, head, new Vector3(1.3f, 0.22f, 0.7f), f.lampMat, "lamp");
                lamp.transform.rotation = Quaternion.LookRotation(inward);
                f.lamps.Add(head);

                // a soft pool of light on the road under the lamp
                var pm = Mats.Fade(new Color(1f, 0.93f, 0.75f), 0f);
                Mats.SetTex(pm, soft);
                f.poolMats.Add(pm);
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(q.GetComponent<Collider>());
                q.name = "lightPool";
                q.transform.SetParent(root.transform, false);
                Vector2 poolPos = m.PointAt(d) + n * side * (off * 0.5f);
                q.transform.position = TrackModel.W(poolPos, e + 0.06f);
                q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                q.transform.localScale = new Vector3(46f, 46f, 1f);
                var qr = q.GetComponent<Renderer>();
                qr.sharedMaterial = pm; qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; qr.receiveShadows = false;
            }

            for (int i = 0; i < f.lights.Length; i++)
            {
                var g = new GameObject("FloodLight"); g.transform.SetParent(root.transform, false);
                var l = g.AddComponent<Light>();
                l.type = LightType.Point; l.range = 62f; l.intensity = 0f; l.color = new Color(1f, 0.93f, 0.8f); l.shadows = LightShadows.None;
                l.cullingMask = ~(1 << CockpitRig.LAYER); l.enabled = false;
                f.lights[i] = l;
            }
            f.SetNight(0f);
            return f;
        }

        public void SetNight(float n)
        {
            night = n;
            if (lampMat != null)
            {
                Color c = new Color(1f, 0.93f, 0.78f);
                lampMat.color = Color.Lerp(new Color(0.3f, 0.3f, 0.32f), c, n);
                lampMat.SetColor("_EmissionColor", c * (n * 2.4f));
            }
            foreach (var pm in poolMats)
            {
                Color c = pm.color; c.a = 0.30f * n; pm.color = c;
                pm.SetColor("_BaseColor", c);
            }
            foreach (var l in lights) if (l != null) l.enabled = n > 0.05f;
        }

        /// <summary>Moves the real lights to the lamps nearest the car.</summary>
        public void Tick(Vector3 car)
        {
            if (night < 0.05f || lamps.Count == 0) return;
            for (int k = 0; k < lights.Length; k++)
            {
                int best = -1; float bd = float.MaxValue;
                for (int i = 0; i < lamps.Count; i++)
                {
                    float d = (lamps[i] - car).sqrMagnitude;
                    bool taken = false;
                    for (int j = 0; j < k; j++) if ((lights[j].transform.position - lamps[i]).sqrMagnitude < 0.01f) taken = true;
                    if (!taken && d < bd) { bd = d; best = i; }
                }
                if (best >= 0) { lights[k].transform.position = lamps[best] + Vector3.down * 3f; lights[k].intensity = 2.3f * night; }
            }
        }
    }

    /// <summary>A dome of stars that follows the camera and fades in at night.</summary>
    public class Stars
    {
        readonly GameObject go;
        readonly Material mat;

        public Stars()
        {
            go = new GameObject("Stars");
            var shader = Shader.Find("Sprites/Default");
            mat = new Material(shader != null ? shader : Shader.Find("Standard"));
            mat.color = new Color(1f, 1f, 1f, 0f);
            var rng = new System.Random(77);
            var verts = new List<Vector3>(); var tris = new List<int>(); var cols = new List<Color>();
            for (int i = 0; i < 420; i++)
            {
                // a random direction in the upper hemisphere
                float az = (float)rng.NextDouble() * Mathf.PI * 2f, el = Mathf.Asin(0.08f + 0.92f * (float)rng.NextDouble());
                Vector3 dir = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az));
                Vector3 c = dir * 1700f;
                Vector3 right = Vector3.Cross(Vector3.up, dir).normalized, up = Vector3.Cross(dir, right).normalized;
                float s = 3.5f + (float)rng.NextDouble() * 5.5f;
                float br = 0.45f + 0.55f * (float)rng.NextDouble();
                int b = verts.Count;
                verts.Add(c - right * s - up * s); verts.Add(c + right * s - up * s); verts.Add(c + right * s + up * s); verts.Add(c - right * s + up * s);
                for (int k = 0; k < 4; k++) cols.Add(new Color(br, br, br * 1.05f, 1f));
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);       // both sides
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            go.SetActive(false);
        }

        public void SetNight(float n)
        {
            go.SetActive(n > 0.15f);
            Color c = mat.color; c.a = Mathf.Clamp01((n - 0.15f) / 0.6f); mat.color = c;
        }

        public void Follow(Vector3 camPos) { go.transform.position = camPos; }
    }
}
