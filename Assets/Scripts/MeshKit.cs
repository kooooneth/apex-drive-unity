using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ApexDrive
{
    public static class Mats
    {
        static Shader _shader;
        static bool _urp;

        static void Init()
        {
            if (_shader != null) return;
            _urp = GraphicsSettings.currentRenderPipeline != null;
            _shader = Shader.Find(_urp ? "Universal Render Pipeline/Lit" : "Standard");
            if (_shader == null) _shader = Shader.Find("Standard");
            if (_shader == null) _shader = Shader.Find("Sprites/Default");
        }

        public static Material Make(Color c, float smooth = 0.25f, float metal = 0f)
        {
            Init();
            var m = new Material(_shader);
            m.color = c;
            if (_urp) { m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal); }
            else { m.SetFloat("_Glossiness", smooth); m.SetFloat("_Metallic", metal); }
            return m;
        }

        public static void SetTex(Material m, Texture t, float repeat = 1f)
        {
            Init();
            m.mainTexture = t;
            m.mainTextureScale = new Vector2(repeat, repeat);
            if (_urp) m.SetTexture("_BaseMap", t);
        }

        public static Material Emissive(Color c, float intensity)
        {
            var m = Make(c, 0.4f, 0f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * intensity);
            return m;
        }

        /// <summary>Translucent material (used for the ghost car).</summary>
        public static Material Fade(Color c, float alpha)
        {
            c.a = alpha;
            var m = Make(c, 0.2f, 0f);
            m.SetColor("_BaseColor", c);
            if (_urp)
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            else
            {
                m.SetFloat("_Mode", 3f);
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON"); m.EnableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            m.renderQueue = 3000;
            return m;
        }
    }

    /// <summary>Accumulates unshared quads so RecalculateNormals gives crisp flat shading with correct winding.</summary>
    public class MeshBuilder
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<int> t = new List<int>();
        readonly List<Vector2> uv = new List<Vector2>();

        public int VertexCount { get { return v.Count; } }
        bool planar, planarXY, planarZY; float planarScale = 1f;

        /// <summary>Texture coordinates from world position (x,z - or x,y) so a texture tiles evenly in metres.</summary>
        public void PlanarUV(float scale, bool xy = false) { planar = true; planarXY = xy; planarScale = scale; }

        /// <summary>Texture coordinates from (z, y): for walls that run front-to-back.</summary>
        public void PlanarUVSide(float scale) { planar = true; planarZY = true; planarScale = scale; }

        /// <summary>a-b-c-d go around the quad; 'facing' says which side must be the visible front.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            int s = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, facing) >= 0f)
            {
                t.Add(s); t.Add(s + 1); t.Add(s + 2);
                t.Add(s); t.Add(s + 2); t.Add(s + 3);
            }
            else
            {
                t.Add(s); t.Add(s + 2); t.Add(s + 1);
                t.Add(s); t.Add(s + 3); t.Add(s + 2);
            }
        }

        /// <summary>One triangle visible from both sides (separate vertices per side so normals stay correct).</summary>
        public void DoubleTri(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            Tri(a, b, c, n);
            Tri(a, b, c, -n);
        }

        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
        {
            int s = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0, 1));
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, facing) >= 0f) { t.Add(s); t.Add(s + 1); t.Add(s + 2); }
            else { t.Add(s); t.Add(s + 2); t.Add(s + 1); }
        }

        public GameObject Build(string name, Transform parent, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (v.Count == 0) return go;
            var mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            if (planar)
                for (int i = 0; i < v.Count; i++) uv[i] = (planarZY ? new Vector2(v[i].z, v[i].y) : planarXY ? new Vector2(v[i].x, v[i].y) : new Vector2(v[i].x, v[i].z)) * planarScale;
            mesh.SetUVs(0, uv);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }
    }

    public static class Prim
    {
        public static GameObject Box(Transform parent, Vector3 localPos, Vector3 scale, Material mat, string name = "box")
        {
            var g = PrimitiveMeshes.Create(PrimitiveType.Cube);
            Object.Destroy(g.GetComponent<Collider>());
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            return g;
        }

        public static GameObject Cyl(Transform parent, Vector3 localPos, Vector3 scale, Quaternion rot, Material mat, string name = "cyl")
        {
            var g = PrimitiveMeshes.Create(PrimitiveType.Cylinder);
            Object.Destroy(g.GetComponent<Collider>());
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localRotation = rot;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            return g;
        }

        public static GameObject Capsule(Transform parent, Vector3 localPos, Vector3 scale, Quaternion rot, Material mat, string name = "capsule")
        {
            var g = PrimitiveMeshes.Create(PrimitiveType.Capsule);
            Object.Destroy(g.GetComponent<Collider>());
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localRotation = rot;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            return g;
        }

        public static GameObject Sphere(Transform parent, Vector3 localPos, Vector3 scale, Material mat, string name = "sphere")
        {
            var g = PrimitiveMeshes.Create(PrimitiveType.Sphere);
            Object.Destroy(g.GetComponent<Collider>());
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            return g;
        }
    }
}
