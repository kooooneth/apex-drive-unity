using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// GameObject.CreatePrimitive, without the collider. In the browser build the physics classes are not available, so CreatePrimitive
    /// (which adds a collider) fails; there the same shapes are made from code, with the same sizes as the built-in ones
    /// (cube 1 m, sphere diameter 1 m, cylinder and capsule 2 m tall and 1 m wide, quad 1 m facing -Z).
    /// </summary>
    public static class PrimitiveMeshes
    {
        static readonly Dictionary<PrimitiveType, Mesh> cache = new Dictionary<PrimitiveType, Mesh>();

        public static GameObject Create(PrimitiveType type)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var go = new GameObject(type.ToString());
            go.AddComponent<MeshFilter>().sharedMesh = Get(type);
            go.AddComponent<MeshRenderer>();
            return go;
#else
            return GameObject.CreatePrimitive(type);
#endif
        }

        static Mesh Get(PrimitiveType type)
        {
            Mesh m;
            if (cache.TryGetValue(type, out m) && m != null) return m;
            switch (type)
            {
                case PrimitiveType.Cube: m = Cube(); break;
                case PrimitiveType.Quad: m = Quad(); break;
                case PrimitiveType.Sphere: m = Revolve(0.5f, 0f, 16, 24); break;
                case PrimitiveType.Capsule: m = Revolve(0.5f, 0.5f, 16, 24); break;
                default: m = Cylinder(); break;
            }
            m.name = "Prim" + type;
            cache[type] = m;
            return m;
        }

        static Mesh Build(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)
        {
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh Quad()
        {
            var v = new List<Vector3> { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            var n = new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            var uv = new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            return Build(v, n, uv, new List<int> { 0, 2, 1, 2, 3, 1 });
        }

        static Mesh Cube()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var nm in normals)
            {
                Vector3 a = Mathf.Abs(nm.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 u = Vector3.Cross(nm, a).normalized, w = Vector3.Cross(nm, u);       // two axes on the face
                int s = v.Count;
                v.Add(nm * 0.5f - u * 0.5f - w * 0.5f); v.Add(nm * 0.5f + u * 0.5f - w * 0.5f); v.Add(nm * 0.5f + u * 0.5f + w * 0.5f); v.Add(nm * 0.5f - u * 0.5f + w * 0.5f);
                for (int i = 0; i < 4; i++) n.Add(nm);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                // wind the triangles so they face along the normal
                Vector3 face = Vector3.Cross(v[s + 1] - v[s], v[s + 2] - v[s]);
                if (Vector3.Dot(face, nm) > 0f) { t.Add(s); t.Add(s + 1); t.Add(s + 2); t.Add(s); t.Add(s + 2); t.Add(s + 3); }
                else { t.Add(s); t.Add(s + 2); t.Add(s + 1); t.Add(s); t.Add(s + 3); t.Add(s + 2); }
            }
            return Build(v, n, uv, t);
        }

        /// <summary>A surface of revolution: a sphere (halfLength 0) or a capsule (a cylinder of 2 x halfLength between two hemispheres).</summary>
        static Mesh Revolve(float radius, float halfLength, int rings, int segs)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float theta = Mathf.PI * i / rings;                              // 0 at the top pole
                float y = Mathf.Cos(theta) * radius + (i <= rings / 2 ? halfLength : -halfLength);
                float r = Mathf.Sin(theta) * radius;
                for (int j = 0; j <= segs; j++)
                {
                    float phi = 2f * Mathf.PI * j / segs;
                    var dir = new Vector3(Mathf.Cos(phi) * Mathf.Sin(theta), Mathf.Cos(theta), Mathf.Sin(phi) * Mathf.Sin(theta));
                    v.Add(new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r)); n.Add(dir);
                    uv.Add(new Vector2((float)j / segs, 1f - (float)i / rings));
                }
            }
            int row = segs + 1;
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < segs; j++)
                {
                    int a = i * row + j, b = a + 1, c = a + row, d = c + 1;
                    t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c);
                }
            // flip the winding if the first triangle faces inwards
            int k0 = (rings / 2) * row + 1;                                      // a vertex on the equator, away from the poles
            Vector3 f0 = Vector3.Cross(v[k0 + 1] - v[k0], v[k0 + row] - v[k0]);
            if (Vector3.Dot(f0, n[k0]) < 0f) for (int k = 0; k < t.Count; k += 3) { int tmp = t[k + 1]; t[k + 1] = t[k + 2]; t[k + 2] = tmp; }
            return Build(v, n, uv, t);
        }

        static Mesh Cylinder()
        {
            const int segs = 24;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= segs; j++)
            {
                float phi = 2f * Mathf.PI * j / segs; float c = Mathf.Cos(phi), s = Mathf.Sin(phi);
                v.Add(new Vector3(c * 0.5f, 1f, s * 0.5f)); n.Add(new Vector3(c, 0f, s)); uv.Add(new Vector2((float)j / segs, 1f));
                v.Add(new Vector3(c * 0.5f, -1f, s * 0.5f)); n.Add(new Vector3(c, 0f, s)); uv.Add(new Vector2((float)j / segs, 0f));
            }
            for (int j = 0; j < segs; j++)
            {
                int a = j * 2, b = a + 1, c2 = a + 2, d = a + 3;
                t.Add(a); t.Add(c2); t.Add(b); t.Add(c2); t.Add(d); t.Add(b);
            }
            // check the side faces point outwards, else flip
            Vector3 fs = Vector3.Cross(v[2] - v[0], v[1] - v[0]);
            if (Vector3.Dot(fs, n[0] + n[2]) < 0f) for (int k = 0; k < t.Count; k += 3) { int tmp = t[k + 1]; t[k + 1] = t[k + 2]; t[k + 2] = tmp; }
            foreach (float y in new[] { 1f, -1f })
            {
                int centre = v.Count; v.Add(new Vector3(0f, y, 0f)); n.Add(new Vector3(0f, y, 0f)); uv.Add(new Vector2(0.5f, 0.5f));
                int ring = v.Count;
                for (int j = 0; j <= segs; j++)
                {
                    float phi = 2f * Mathf.PI * j / segs;
                    v.Add(new Vector3(Mathf.Cos(phi) * 0.5f, y, Mathf.Sin(phi) * 0.5f)); n.Add(new Vector3(0f, y, 0f)); uv.Add(new Vector2(Mathf.Cos(phi) * 0.5f + 0.5f, Mathf.Sin(phi) * 0.5f + 0.5f));
                }
                for (int j = 0; j < segs; j++)
                {
                    // wind each cap so it faces away from the middle of the cylinder
                    Vector3 face = Vector3.Cross(v[ring + j] - v[centre], v[ring + j + 1] - v[centre]);
                    bool up = Vector3.Dot(face, new Vector3(0f, y, 0f)) > 0f;
                    if (up) { t.Add(centre); t.Add(ring + j); t.Add(ring + j + 1); }
                    else { t.Add(centre); t.Add(ring + j + 1); t.Add(ring + j); }
                }
            }
            return Build(v, n, uv, t);
        }
    }
}
