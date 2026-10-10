using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>Grandstand crowd: rows that bob and cheer, waving flags, and winner's fireworks.</summary>
    public static class Crowd
    {
        class Row { public Transform t; public float baseY, phase; }
        class FlagBit { public Transform t; public float phase; }
        static readonly List<Row> rows = new List<Row>();
        static readonly List<FlagBit> flags = new List<FlagBit>();
        static readonly List<Vector3> standCenters = new List<Vector3>();
        static ParticleSystem fw;
        static float nextBurst;

        public static void Reset()
        {
            rows.Clear(); flags.Clear(); standCenters.Clear();
            if (fw != null) { Object.Destroy(fw.gameObject); fw = null; }
        }

        public static void AddRow(Transform t) { rows.Add(new Row { t = t, baseY = t.localPosition.y, phase = Random.value * 6.28f }); }
        public static float WindStrength = 3f;
        public static void AddFlag(Transform t) { flags.Add(new FlagBit { t = t, phase = Random.value * 6.28f }); }
        public static void AddStand(Vector3 center) { standCenters.Add(center); }

        static void EnsureFireworks()
        {
            if (fw != null) return;
            var go = new GameObject("Fireworks");
            fw = go.AddComponent<ParticleSystem>();
            var main = fw.main;
            main.loop = true; main.startLifetime = 1.8f; main.startSpeed = 0f; main.startSize = 0.7f;
            main.maxParticles = 3000; main.gravityModifier = 0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = fw.emission; em.rateOverTime = 0f;
            var sh = fw.shape; sh.enabled = false;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            fw.Play();
        }

        static void Burst(Vector3 center)
        {
            EnsureFireworks();
            Color c = Color.HSVToRGB(Random.value, 0.85f, 1f);
            for (int i = 0; i < 90; i++)
            {
                float th = Random.value * Mathf.PI * 2f, ph = Mathf.Acos(2f * Random.value - 1f), sp = Random.Range(8f, 15f);
                var ep = new ParticleSystem.EmitParams();
                ep.position = center;
                ep.velocity = new Vector3(Mathf.Sin(ph) * Mathf.Cos(th), Mathf.Cos(ph), Mathf.Sin(ph) * Mathf.Sin(th)) * sp;
                ep.startColor = c;
                ep.startLifetime = Random.Range(1.2f, 2.0f);
                ep.startSize = 0.6f;
                fw.Emit(ep, 1);
            }
        }

        public static void Tick(float dt, bool excited, bool fireworks)
        {
            float t = Time.time;
            foreach (var r in rows)
            {
                float bob = Mathf.Abs(Mathf.Sin(t * (excited ? 6.5f : 2.2f) + r.phase)) * (excited ? 0.09f : 0.025f);
                r.t.localPosition = new Vector3(r.t.localPosition.x, r.baseY + bob, r.t.localPosition.z);
            }
            foreach (var f in flags) f.t.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * (3.5f + 0.4f * WindStrength) + f.phase) * (14f + 3.5f * Mathf.Min(WindStrength, 10f)), 0f);
            if (fireworks && standCenters.Count > 0 && Time.time >= nextBurst)
            {
                nextBurst = Time.time + Random.Range(0.35f, 0.8f);
                Vector3 c = standCenters[Random.Range(0, standCenters.Count)];
                Burst(c + new Vector3(Random.Range(-25f, 25f), Random.Range(22f, 40f), Random.Range(-25f, 25f)));
            }
        }
    }
}
