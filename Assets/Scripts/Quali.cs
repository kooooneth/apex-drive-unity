using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// Three-part knockout qualifying: Q1 with all six cars (slowest two go out), Q2 with four (slowest one
    /// goes out), Q3 with the last three for pole. You drive one flying lap per session; the rivals' lap times
    /// come from their pace ratings. Whoever is knocked out is classified behind everyone still running.
    /// </summary>
    public class QualiSession
    {
        public class Entry
        {
            public string name; public bool isPlayer; public float rating;
            public float time = -1f;        // best time in the current session
            public bool alive = true;       // still running
            public int outIn;               // session they were knocked out in (0 = still in)
        }

        public readonly List<Entry> entries = new List<Entry>();
        public int session = 1;                       // 1..3, the session being driven now
        public bool done;                             // classification final
        public int playerSlot = -1;                   // grid slot (0 = pole) once done
        public readonly List<Entry> classification = new List<Entry>();
        public readonly List<Entry> lastResult = new List<Entry>();   // the session that just finished, in order
        public int lastSession;                       // which session lastResult is
        public bool playerAdvanced;
        float reference, difficulty;

        static readonly int[] KEEP = { 4, 3 };       // cars that go through from Q1 and Q2

        public static QualiSession Begin(float referenceLap, float aiDifficulty)
        {
            var q = new QualiSession { reference = referenceLap, difficulty = Mathf.Max(0.5f, aiDifficulty) };
            q.entries.Add(new Entry { name = RacerProfile.DisplayName, isPlayer = true });
            var ratings = new List<float>();
            for (int i = 0; i < 5; i++) ratings.Add(4.5f + Random.value * 3.5f);
            for (int i = 0; i < 5; i++) q.entries.Add(new Entry { name = Personalities.Drivers[i % 5], rating = ratings[i] });
            q.PrepareSession();
            return q;
        }

        float AiTime(Entry e)
        {
            float t = reference * (1.2f - 0.034f * e.rating) / difficulty;
            t *= 1f - 0.004f * (session - 1);                                   // the track gets quicker through the day
            t *= 1f + (Random.value - 0.5f) * 0.02f;
            return t;
        }

        void PrepareSession()
        {
            foreach (var e in entries) { e.time = -1f; }
            foreach (var e in entries) if (e.alive && !e.isPlayer) e.time = AiTime(e);
        }

        public string SessionName { get { return "Q" + session; } }

        public int CarsThrough { get { return session == 1 ? KEEP[0] : session == 2 ? KEEP[1] : 3; } }

        /// <summary>The time you must beat to get through this session.</summary>
        public float CutoffTime()
        {
            var times = new List<float>();
            foreach (var e in entries) if (e.alive && !e.isPlayer && e.time > 0f) times.Add(e.time);
            times.Sort();
            int through = CarsThrough;
            if (session == 3) return times.Count > 0 ? times[0] : -1f;          // pole: beat the quickest rival
            // the player takes one of the 'through' places, so the benchmark is the (through)th quickest rival
            int idx = Mathf.Clamp(through - 1, 0, times.Count - 1);
            return times.Count > 0 ? times[idx] : -1f;
        }

        /// <summary>The player has set a lap; work out who goes through and move on.</summary>
        public void PlayerLap(float lapTime)
        {
            foreach (var e in entries) if (e.isPlayer) e.time = lapTime;
            var running = new List<Entry>();
            foreach (var e in entries) if (e.alive) running.Add(e);
            running.Sort((a, b) => a.time.CompareTo(b.time));
            lastSession = session;

            int through = session == 3 ? running.Count : Mathf.Min(CarsThrough, running.Count);
            for (int i = through; i < running.Count; i++) { running[i].alive = false; running[i].outIn = session; }
            playerAdvanced = false;
            for (int i = 0; i < through; i++) if (running[i].isPlayer) playerAdvanced = true;
            lastResult.Clear();
            foreach (var e in running) lastResult.Add(new Entry { name = e.name, isPlayer = e.isPlayer, rating = e.rating, time = e.time, alive = e.alive, outIn = e.outIn });

            if (session < 3 && playerAdvanced)
            {
                session++;
                PrepareSession();
                return;
            }
            // either pole shootout finished, or the player went out: simulate whatever is left and classify
            while (session < 3)
            {
                session++;
                var rest = new List<Entry>();
                foreach (var e in entries) if (e.alive) { e.time = AiTime(e); rest.Add(e); }
                rest.Sort((a, b) => a.time.CompareTo(b.time));
                int keep = session == 3 ? rest.Count : Mathf.Min(KEEP[1], rest.Count);
                for (int i = keep; i < rest.Count; i++) { rest[i].alive = false; rest[i].outIn = session; }
            }
            Classify();
        }

        void Classify()
        {
            classification.Clear();
            var q3 = new List<Entry>(); var q2 = new List<Entry>(); var q1 = new List<Entry>();
            foreach (var e in entries)
            {
                if (e.alive || e.outIn == 3) q3.Add(e); else if (e.outIn == 2) q2.Add(e); else q1.Add(e);
            }
            System.Comparison<Entry> byTime = (a, b) => a.time.CompareTo(b.time);
            q3.Sort(byTime); q2.Sort(byTime); q1.Sort(byTime);
            classification.AddRange(q3); classification.AddRange(q2); classification.AddRange(q1);
            for (int i = 0; i < classification.Count; i++) if (classification[i].isPlayer) playerSlot = Mathf.Clamp(i, 0, 5);
            done = true;
        }

        public string StatusOf(Entry e)
        {
            if (e.outIn == lastSession && lastSession > 0) return "OUT";
            return "through";
        }
    }
}
