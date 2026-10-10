using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>Everything about your car that is put back by a rewind.</summary>
    public class PlayerSnap
    {
        public Vector3 pos; public float heading, speed, dist, steer, pitch, lapStart, tireWear, tireTemp, fuel, ers;
        public float damage, wing, floor, engine; public int lap, lost; public bool punctured; public int punctureWheel;
        public List<string> parts = new List<string>();
    }

    /// <summary>
    /// A limited number of rewinds per race: undo a crash or a mistake. The whole field goes back a few seconds with you (cars that are
    /// pitting or have broken down are left alone), the damage is undone and the lost time does not count against the race clock.
    /// </summary>
    public class Rewinder
    {
        class AiSnap { public AICar car; public float dist, speed, lateral, tire; public int lap, pitCount; public bool pitting; }
        class Snap { public float t; public PlayerSnap me; public List<AiSnap> ai = new List<AiSnap>(); }

        const float KEEP = 12f, EVERY = 0.25f, BACK = 6f;
        readonly List<Snap> snaps = new List<Snap>();
        float nextRecord;
        public int left = 3;
        public float flashUntil;

        public void Reset(int charges) { snaps.Clear(); left = charges; nextRecord = 0f; flashUntil = 0f; }

        public void Record(RaceManager rm, PlayerCar p, float clock)
        {
            if (clock < nextRecord || p == null || p.pitStopActive) return;
            nextRecord = clock + EVERY;
            var s = new Snap { t = clock, me = p.Capture() };
            foreach (var r in rm.racers)
            {
                var a = r as AICar;
                if (a == null || a.gone) continue;
                s.ai.Add(new AiSnap { car = a, dist = a.dist, speed = a.speed, lateral = a.lateral, tire = a.tireWear, lap = a.lap, pitCount = a.pitCount, pitting = a.pitStopActive || a.retired });
            }
            snaps.Add(s);
            while (snaps.Count > 0 && clock - snaps[0].t > KEEP) snaps.RemoveAt(0);
        }

        public bool CanRewind(RaceManager rm, PlayerCar p) { return left > 0 && snaps.Count > 2 && p != null && !p.pitStopActive && !rm.redFlag; }

        /// <summary>Goes back about six seconds. Returns how many seconds were undone (0 if it could not).</summary>
        public float Rewind(RaceManager rm, PlayerCar p, float clock)
        {
            if (!CanRewind(rm, p)) return 0f;
            Snap pick = snaps[0];
            for (int i = snaps.Count - 1; i >= 0; i--) if (clock - snaps[i].t >= BACK) { pick = snaps[i]; break; }
            float elapsed = clock - pick.t;
            p.Restore(pick.me, elapsed);
            foreach (var a in pick.ai)
            {
                var c = a.car;
                if (c == null || c.gone || c.retired || c.pitStopActive || a.pitting || c.pitCount != a.pitCount) continue;     // leave cars that stopped or pitted alone
                c.RewindTo(a.dist, a.speed, a.lateral, a.tire, a.lap);
            }
            // drop the snapshots after the one we went back to: that time is being replayed
            int idx = snaps.IndexOf(pick);
            if (idx >= 0 && idx + 1 < snaps.Count) snaps.RemoveRange(idx + 1, snaps.Count - idx - 1);
            left--;
            nextRecord = clock + EVERY;
            flashUntil = Time.time + 0.7f;
            return elapsed;
        }
    }
}
