using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>Bits of broken bodywork that fly off, tumble and settle.</summary>
    public class Debris
    {
        class Piece { public Transform t; public Vector3 vel, spin; public float life; public float floorY; public bool wheel; }
        readonly List<Piece> pieces = new List<Piece>();
        Material mat;

        public void Spawn(Vector3 pos, Vector3 side, int stage)
        {
            if (mat == null) mat = Mats.Make(new Color(0.1f, 0.1f, 0.12f), 0.3f, 0.3f);
            int n = stage >= 2 ? 6 : 3;
            for (int i = 0; i < n; i++)
            {
                var g = Prim.Box(null, pos, new Vector3(0.25f + Random.value * 0.5f, 0.03f, 0.1f + Random.value * 0.2f), mat, "debris");
                g.transform.position = pos;
                var p = new Piece
                {
                    t = g.transform,
                    vel = side * (Random.Range(-4f, 4f)) + Vector3.up * Random.Range(2f, 5f) + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f)),
                    spin = new Vector3(Random.Range(-360f, 360f), Random.Range(-360f, 360f), Random.Range(-360f, 360f)),
                    life = 9f, floorY = pos.y - 0.25f
                };
                pieces.Add(p);
            }
        }

        /// <summary>A tyre that bounces down the track after its tether snaps.</summary>
        public void SpawnWheel(Vector3 pos, Vector3 vel)
        {
            var tread = Mats.Make(new Color(0.07f, 0.07f, 0.08f), 0.1f);
            var g = Prim.Cyl(null, pos, new Vector3(0.76f, 0.17f, 0.76f), Quaternion.Euler(0f, 0f, 90f), tread, "lostWheel");
            g.transform.position = pos;
            pieces.Add(new Piece { t = g.transform, vel = vel, spin = new Vector3(0f, 0f, 540f), life = 14f, floorY = pos.y - 0.2f, wheel = true });
        }

        public void Tick(float dt)
        {
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                var p = pieces[i];
                p.life -= dt;
                if (p.life <= 0f) { Object.Destroy(p.t.gameObject); pieces.RemoveAt(i); continue; }
                if (p.wheel)
                {
                    p.vel += Vector3.down * 20f * dt;
                    p.t.position += p.vel * dt;
                    p.t.Rotate(p.spin * dt);
                    if (p.t.position.y < p.floorY)
                    {
                        p.t.position = new Vector3(p.t.position.x, p.floorY, p.t.position.z);
                        p.vel = new Vector3(p.vel.x * 0.9f, Mathf.Abs(p.vel.y) * 0.4f, p.vel.z * 0.9f);        // bounce, losing speed
                        if (p.vel.y < 1f) p.vel.y = 0f;
                    }
                    continue;
                }
                if (p.t.position.y > p.floorY || p.vel.y > 0f)
                {
                    p.vel += Vector3.down * 20f * dt;
                    p.t.position += p.vel * dt;
                    p.t.Rotate(p.spin * dt);
                    if (p.t.position.y < p.floorY) { p.t.position = new Vector3(p.t.position.x, p.floorY, p.t.position.z); p.vel = Vector3.zero; }
                }
            }
        }

        public void Clear()
        {
            foreach (var p in pieces) if (p.t != null) Object.Destroy(p.t.gameObject);
            pieces.Clear();
        }
    }
}
