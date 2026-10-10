using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The race commentary is written down while you race (overtakes, spins, pit stops, cautions...) with the time each
    /// thing happened. Nothing is said during the race; when you watch the replay the two commentators speak each line as
    /// the replay reaches it, with subtitles.
    /// </summary>
    public class CommentaryLog
    {
        public class Ev { public float t; public string text; public int prio; }
        public readonly List<Ev> events = new List<Ev>();
        float lastAdded = -99f;

        public void Reset() { events.Clear(); lastAdded = -99f; }

        /// <param name="prio">2 = kept unless it comes right after another line, 1 = only when things are quiet</param>
        public void Add(float t, string text, int prio)
        {
            // the commentators speak about every two seconds, so events only need a short gap between them
            if (t - lastAdded < (prio < 2 ? 2.2f : 1.6f)) return;
            events.Add(new Ev { t = t, text = text, prio = prio });
            lastAdded = t;
        }
    }

    public class CommentaryPlayer
    {
        int idx, turn;
        float lastT, nextFiller;
        public string subtitle = "", speaker = "";
        public float subtitleUntil;

        // short lines (about two seconds each) for the moments between events, so the two commentators keep talking
        static readonly string[] Fillers =
        {
            "The tyres are working hard now.",
            "It is all about tyre management.",
            "A clean race so far.",
            "They are fighting for every tenth.",
            "Great speed down the straight.",
            "The pit wall is watching the gaps.",
            "Look at the pace through these corners.",
            "That was a late brake.",
            "He is right on the limit there.",
            "The gap is coming down.",
            "Smooth through the esses.",
            "A big lunge into the corner.",
            "He is taking the long way round.",
            "They are bunched up here.",
            "Good traction out of the slow corner.",
            "The car looks planted.",
            "Real pressure from behind.",
            "He has the line on the exit.",
            "Fantastic car control.",
            "A tidy lap from the leader.",
            "The crowd loves this.",
            "You can see the sparks flying.",
            "He is lifting early for the corner.",
            "What a battle this is.",
            "They are not giving an inch.",
            "Watch the kerbs there.",
            "The rear is stepping out a little.",
            "He is trying everything.",
            "Brilliant defending.",
            "The tow is helping on the straight.",
            "A touch of understeer there.",
            "He will want to get the DRS.",
            "Rubber is going down on the track.",
            "The engine note sounds strong.",
            "That is a quick sector.",
            "They are lapping consistently.",
            "Nobody wants to back out.",
            "Superb exit from the corner.",
            "The leader is pulling away.",
            "It is wheel to wheel."
        };
        readonly List<int> bag = new List<int>();
        int lastFiller = -1;

        string NextFiller()
        {
            if (bag.Count == 0)
            {
                for (int i = 0; i < Fillers.Length; i++) bag.Add(i);
                for (int i = bag.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); int tmp = bag[i]; bag[i] = bag[j]; bag[j] = tmp; }   // a shuffled bag: no repeats until all are used
                if (bag.Count > 1 && bag[bag.Count - 1] == lastFiller) { int tmp = bag[0]; bag[0] = bag[bag.Count - 1]; bag[bag.Count - 1] = tmp; }
            }
            int k = bag[bag.Count - 1]; bag.RemoveAt(bag.Count - 1);
            lastFiller = k;
            return Fillers[k];
        }

        static int FirstAfter(CommentaryLog log, float t)
        {
            int i = 0;
            while (i < log.events.Count && log.events[i].t <= t) i++;
            return i;
        }

        public void Begin(CommentaryLog log, float t)
        {
            idx = FirstAfter(log, t); lastT = t; nextFiller = t + 1.5f; turn = 0;
            subtitle = ""; subtitleUntil = 0f;
        }

        void Speak(string text)
        {
            // two commentators trade lines: the play-by-play man and a woman adding colour
            bool man = turn % 2 == 0;
            turn++;
            speaker = man ? "COMMENTATOR 1" : "COMMENTATOR 2";
            subtitle = text;
            subtitleUntil = Time.time + 3.5f;
            Voice.Say(man ? Voice.PLAYBYPLAY : Voice.WOMAN, 0, text);
        }

        /// <summary>Call every replay frame with the replay time.</summary>
        public void Tick(CommentaryLog log, float t, bool playing)
        {
            // a jump in time (scrubbing) moves the position without speaking
            if (t < lastT - 0.2f || t > lastT + 1.5f) idx = FirstAfter(log, t);
            lastT = t;
            if (!playing) return;
            while (idx < log.events.Count && log.events[idx].t <= t)
            {
                Speak(log.events[idx].text);
                idx++;
                nextFiller = t + 2.2f + Random.value * 0.8f;
            }
            if (t > nextFiller)
            {
                Speak(NextFiller());
                nextFiller = t + 2.3f + Random.value * 0.9f;
            }
        }
    }
}
