using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The recovery truck: after a car stops on track it leaves the pit exit, drives to the car, hooks it, takes it
    /// away and returns. The safety car stays out until the track is clear.
    /// </summary>
    public class RecoveryTruck
    {
        readonly GameObject go;
        readonly Material lightMat;
        readonly Queue<AICar> queue = new Queue<AICar>();
        AICar target;
        int phase;                 // 0 idle, 1 driving to the car, 2 hooking it, 3 driving away
        float d, timer, side = 1f;

        public bool Busy { get { return phase != 0 || queue.Count > 0; } }

        public RecoveryTruck()
        {
            go = new GameObject("RecoveryTruck");
            var body = Mats.Make(new Color(1f, 0.82f, 0.1f), 0.3f);
            var dark = Mats.Make(new Color(0.08f, 0.09f, 0.11f), 0.2f);
            lightMat = Mats.Emissive(new Color(1f, 0.6f, 0.05f), 1f);
            Prim.Box(go.transform, new Vector3(0f, 1.0f, -0.6f), new Vector3(2.4f, 1.3f, 4.4f), body, "bed");
            Prim.Box(go.transform, new Vector3(0f, 1.5f, 2.0f), new Vector3(2.3f, 1.9f, 1.9f), body, "cab");
            Prim.Box(go.transform, new Vector3(0f, 1.75f, 2.95f), new Vector3(2.0f, 0.8f, 0.05f), dark, "glass");
            Prim.Box(go.transform, new Vector3(0f, 2.65f, 2.0f), new Vector3(1.8f, 0.2f, 0.45f), lightMat, "lightbar");
            Prim.Box(go.transform, new Vector3(0f, 0.4f, 0.4f), new Vector3(2.5f, 0.6f, 7.0f), dark, "chassis");
            Prim.Box(go.transform, new Vector3(0f, 2.2f, -2.2f), new Vector3(0.3f, 2.2f, 0.3f), dark, "boom");
            go.SetActive(false);
        }

        public void Dispatch(AICar car) { queue.Enqueue(car); }

        public void Destroy() { if (go != null) Object.Destroy(go); }

        public void Tick(RaceManager rm, float dt, float now)
        {
            TrackModel tm = rm.track;
            if (phase == 0)
            {
                if (queue.Count == 0) return;
                target = queue.Dequeue();
                d = tm.PitZoneDist + TrackModel.PIT_EXIT;            // out of the pit exit
                side = target.lateral >= 0f ? 1f : -1f;
                phase = 1; timer = 0f;
                go.SetActive(true);
            }
            float L = tm.roadLength;
            if (phase == 1)
            {
                float remaining = Mathf.Repeat(target.dist - d, L);
                if (remaining > L * 0.9f) remaining = 0f;
                d += Mathf.Min(24f, 8f + remaining * 0.5f) * dt;
                if (remaining < 7f) { phase = 2; timer = 0f; }
            }
            else if (phase == 2)
            {
                timer += dt;
                if (timer > 5f) { target.Remove(); target = null; phase = 3; timer = 0f; }
            }
            else
            {
                d += 16f * dt; timer += dt;
                float toPit = Mathf.Repeat(tm.PitZoneDist + TrackModel.PIT_ENTRY - d, L);
                if (timer > 14f || toPit < 8f) { phase = 0; go.SetActive(false); }
            }

            Vector2 p = tm.PointAt(d), t = tm.TangentAt(d), n = TrackModel.Normal(t);
            go.transform.position = TrackModel.W(p + n * (side * 9.4f), tm.ElevationAt(d));
            go.transform.rotation = Quaternion.Euler(0f, (Mathf.PI - Mathf.Atan2(t.x, t.y)) * Mathf.Rad2Deg, 0f);
            bool on = Mathf.FloorToInt(now * 4f) % 2 == 0;
            lightMat.SetColor("_EmissionColor", on ? new Color(1f, 0.6f, 0.05f) * 1.5f : new Color(0.1f, 0.05f, 0f));
        }
    }
}
