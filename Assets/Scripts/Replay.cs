using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>Records every car's position through the race so it can be replayed afterwards with a drone camera.</summary>
    public class ReplayRecorder
    {
        public struct Frame { public float t; public Vector3[] pos; public float[] yaw; }

        public readonly List<Frame> frames = new List<Frame>();
        float lastT = -1f;
        public float Duration { get { return frames.Count > 0 ? frames[frames.Count - 1].t : 0f; } }

        public void Reset() { frames.Clear(); lastT = -1f; }

        public void Record(float t, List<Racer> racers)
        {
            if (t - lastT < 0.05f || frames.Count > 40000) return;
            lastT = t;
            var f = new Frame { t = t, pos = new Vector3[racers.Count], yaw = new float[racers.Count] };
            for (int i = 0; i < racers.Count; i++)
            {
                f.pos[i] = racers[i].go.transform.position;
                f.yaw[i] = racers[i].go.transform.eulerAngles.y;
            }
            frames.Add(f);
        }

        /// <summary>Interpolated pose of car i at replay time t.</summary>
        public bool Sample(float t, int i, ref int hint, out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero; yaw = 0f;
            if (frames.Count < 2) return false;
            if (hint >= frames.Count - 1 || frames[hint].t > t) hint = 0;
            while (hint < frames.Count - 2 && frames[hint + 1].t <= t) hint++;
            Frame a = frames[hint], b = frames[hint + 1];
            float k = Mathf.Clamp01((t - a.t) / Mathf.Max(0.0001f, b.t - a.t));
            pos = Vector3.Lerp(a.pos[i], b.pos[i], k);
            yaw = Mathf.LerpAngle(a.yaw[i], b.yaw[i], k);
            return true;
        }
    }
}
