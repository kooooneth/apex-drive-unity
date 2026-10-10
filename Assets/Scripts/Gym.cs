using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The gym: three short exercises that train your racer's stats. Each one costs a point of energy.
    ///   Cardio sprint (Fitness): mash Space for 12 seconds.
    ///   Reaction lights (Reflexes): five start-light sequences - hit Space the moment they go out.
    ///   Focus drill (Focus): memorise a growing sequence of arrows and repeat it.
    /// </summary>
    public class Gym
    {
        public bool active;
        int kind;                                    // 0 cardio, 1 reaction, 2 focus
        enum Phase { Intro, Run, Result }
        Phase phase;
        string resultText = "";
        int xpGain, levelsGained;

        // cardio
        int presses; float timeLeft;
        const float CARDIO_TIME = 12f;

        // reaction lights
        int round, lightsOn; float hold, litTimer, reactClock, roundPause;
        bool lightsOut, waitingResult;
        readonly List<float> times = new List<float>();
        int jumpStarts; string roundMsg = "";
        const int ROUNDS = 5;

        // focus drill
        readonly List<int> seq = new List<int>();
        int showIdx, inputIdx, roundsDone; float showT; bool showing; string focusMsg = "";
        const int FOCUS_ROUNDS = 6;
        static readonly string[] ARROWS = { "^", ">", "v", "<" };

        static readonly string[] Titles = { "CARDIO SPRINT", "REACTION LIGHTS", "FOCUS DRILL" };
        static readonly int[] StatOf = { RacerProfile.FITNESS, RacerProfile.REFLEXES, RacerProfile.FOCUS };
        static readonly string[] Intro =
        {
            "Mash SPACE as fast as you can for 12 seconds. Builds stamina so your driver tires more slowly in long races.",
            "Five start-light sequences. When the lights go out, hit SPACE as fast as you can. Press early and it is a jump start. Sharpens your reactions so the car answers your steering faster.",
            "Watch the arrows light up, then repeat the sequence with the arrow keys (or W A S D). It grows by one every round. Improves concentration: less tyre wear and a steadier car over the kerbs."
        };

        static GUIStyle sTitle, sText, sBig, sSmall;

        public void Open(int k)
        {
            kind = Mathf.Clamp(k, 0, 2);
            phase = Phase.Intro; active = true; resultText = ""; xpGain = 0; levelsGained = 0;
        }

        void Begin()
        {
            if (!RacerProfile.SpendEnergy()) return;
            phase = Phase.Run;
            if (kind == 0) { presses = 0; timeLeft = CARDIO_TIME; }
            else if (kind == 1) { round = 0; times.Clear(); jumpStarts = 0; StartReactionRound(); }
            else { roundsDone = 0; focusMsg = ""; StartFocusRound(); }
        }

        void StartReactionRound()
        {
            lightsOn = 0; litTimer = 0f; hold = 0.5f + Random.value * 1.7f;
            lightsOut = false; waitingResult = false; roundMsg = "";
        }

        void StartFocusRound()
        {
            int len = 3 + roundsDone;
            seq.Clear();
            for (int i = 0; i < len; i++) seq.Add(Random.Range(0, 4));
            showing = true; showIdx = 0; showT = 0.8f; inputIdx = 0; focusMsg = "";
        }

        static int ReadArrow()
        {
            if (Inp.Pressed(KeyCode.UpArrow) || Inp.Pressed(KeyCode.W)) return 0;
            if (Inp.Pressed(KeyCode.RightArrow) || Inp.Pressed(KeyCode.D)) return 1;
            if (Inp.Pressed(KeyCode.DownArrow) || Inp.Pressed(KeyCode.S)) return 2;
            if (Inp.Pressed(KeyCode.LeftArrow) || Inp.Pressed(KeyCode.A)) return 3;
            return -1;
        }

        public void Update(float dt)
        {
            if (!active) return;
            if (Inp.Pressed(KeyCode.Escape)) { active = false; return; }
            if (phase == Phase.Intro) { if (Inp.Pressed(KeyCode.Return)) Begin(); return; }
            if (phase == Phase.Result) return;

            if (kind == 0)
            {
                timeLeft -= dt;
                if (Inp.Pressed(KeyCode.Space)) presses++;
                if (timeLeft <= 0f) Finish(Mathf.Clamp(Mathf.RoundToInt(presses * 0.8f), 5, 80),
                                           presses + " presses in " + CARDIO_TIME.ToString("0") + " seconds.");
            }
            else if (kind == 1) UpdateReaction(dt);
            else UpdateFocus(dt);
        }

        void UpdateReaction(float dt)
        {
            if (waitingResult)
            {
                roundPause -= dt;
                if (roundPause <= 0f)
                {
                    round++;
                    if (round >= ROUNDS) FinishReaction(); else StartReactionRound();
                }
                return;
            }
            bool press = Inp.Pressed(KeyCode.Space);
            if (!lightsOut)
            {
                if (lightsOn < 5) { litTimer += dt; if (litTimer >= 0.6f) { litTimer = 0f; lightsOn++; } }
                else { hold -= dt; if (hold <= 0f) { lightsOut = true; reactClock = 0f; } }
                if (press) { jumpStarts++; times.Add(0.6f); roundMsg = "JUMP START!"; waitingResult = true; roundPause = 1f; }
            }
            else
            {
                reactClock += dt;
                if (press) { times.Add(reactClock); roundMsg = Mathf.RoundToInt(reactClock * 1000f) + " ms"; waitingResult = true; roundPause = 0.9f; }
                else if (reactClock > 1.5f) { times.Add(1.5f); roundMsg = "Too slow"; waitingResult = true; roundPause = 0.9f; }
            }
        }

        void FinishReaction()
        {
            float sum = 0f;
            foreach (float t in times) sum += t;
            float avg = times.Count > 0 ? sum / times.Count : 1f;
            int xp = avg < 0.22f ? 80 : avg < 0.26f ? 62 : avg < 0.32f ? 46 : avg < 0.42f ? 30 : 14;
            xp = Mathf.Max(5, xp - 8 * jumpStarts);
            Finish(xp, "Average reaction " + Mathf.RoundToInt(avg * 1000f) + " ms" + (jumpStarts > 0 ? " (" + jumpStarts + " jump start" + (jumpStarts > 1 ? "s" : "") + ")" : "") + ".");
        }

        void UpdateFocus(float dt)
        {
            if (showing)
            {
                showT -= dt;
                if (showT <= 0f)
                {
                    showIdx++; showT = 0.7f;
                    if (showIdx >= seq.Count) showing = false;
                }
                return;
            }
            int k = ReadArrow();
            if (k < 0) return;
            if (k == seq[inputIdx])
            {
                inputIdx++;
                if (inputIdx >= seq.Count)
                {
                    roundsDone++;
                    if (roundsDone >= FOCUS_ROUNDS) FinishFocus(true); else StartFocusRound();
                }
            }
            else { focusMsg = "Wrong key!"; FinishFocus(false); }
        }

        void FinishFocus(bool perfect)
        {
            int xp = Mathf.Max(4, 10 * roundsDone + (perfect ? 20 : 0));
            Finish(xp, roundsDone + " of " + FOCUS_ROUNDS + " sequences" + (perfect ? " - perfect!" : "."));
        }

        void Finish(int xp, string summary)
        {
            int stat = StatOf[kind];
            xpGain = xp;
            levelsGained = RacerProfile.AddXp(stat, xp);
            RacerProfile.lastTrainDay = RacerProfile.day;
            RacerProfile.Save();
            resultText = summary;
            phase = Phase.Result;
        }

        // ---------------------------------------------------------------- drawing
        static void Fill(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; }

        static void Styles()
        {
            if (sTitle != null) return;
            sTitle = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold };
            sTitle.normal.textColor = Color.white;
            sText = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            sText.normal.textColor = new Color(0.9f, 0.92f, 0.96f);
            sBig = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            sBig.normal.textColor = new Color(0.2f, 0.9f, 0.8f);
            sSmall = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            sSmall.normal.textColor = new Color(0.2f, 0.9f, 0.8f);
        }

        public void Draw(float W, float H)
        {
            if (!active) return;
            Styles();
            float x = 490f, y = 100f, w = Mathf.Min(W - 520f, 720f), h = 470f;
            Fill(new Rect(x, y, w, h), new Color(0f, 0f, 0f, 0.84f));
            Fill(new Rect(x, y, w, 5f), new Color(0.2f, 0.9f, 0.8f));
            GUI.Label(new Rect(x + 24f, y + 16f, w - 48f, 40f), Titles[kind], sTitle);
            GUI.Label(new Rect(x + 24f, y + 56f, w - 48f, 20f), "TRAINS " + RacerProfile.StatNames[StatOf[kind]].ToUpper() + "     energy " + RacerProfile.energy + "/" + RacerProfile.MAX_ENERGY, sSmall);

            if (phase == Phase.Intro)
            {
                GUI.Label(new Rect(x + 24f, y + 90f, w - 48f, 120f), Intro[kind], sText);
                GUI.enabled = RacerProfile.energy > 0;
                GUI.backgroundColor = new Color(0.3f, 0.9f, 0.4f);
                if (GUI.Button(new Rect(x + 24f, y + 240f, 260f, 44f), RacerProfile.energy > 0 ? "START  (uses 1 energy)  [Enter]" : "No energy left")) Begin();
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
                if (RacerProfile.energy <= 0)
                    GUI.Label(new Rect(x + 24f, y + 296f, w - 48f, 60f), "Finish a race (+2 energy) or a practice session (+1) to recover.", sText);
            }
            else if (phase == Phase.Run)
            {
                if (kind == 0) DrawCardio(x, y, w);
                else if (kind == 1) DrawReaction(x, y, w);
                else DrawFocus(x, y, w);
            }
            else
            {
                GUI.Label(new Rect(x + 24f, y + 100f, w - 48f, 30f), resultText, sText);
                GUI.Label(new Rect(x + 24f, y + 150f, w - 48f, 80f), "+" + xpGain + " " + RacerProfile.StatNames[StatOf[kind]] + " XP", sBig);
                if (levelsGained > 0)
                    GUI.Label(new Rect(x + 24f, y + 240f, w - 48f, 30f), "LEVEL UP!  " + RacerProfile.StatNames[StatOf[kind]] + " is now level " + RacerProfile.level[StatOf[kind]], sSmall);
                GUI.enabled = RacerProfile.energy > 0;
                if (GUI.Button(new Rect(x + 24f, y + 300f, 200f, 40f), "TRAIN AGAIN")) phase = Phase.Intro;
                GUI.enabled = true;
            }
            if (GUI.Button(new Rect(x + w - 144f, y + h - 54f, 120f, 36f), phase == Phase.Run ? "QUIT" : "CLOSE")) active = false;
        }

        void DrawCardio(float x, float y, float w)
        {
            GUI.Label(new Rect(x, y + 100f, w, 90f), presses.ToString(), sBig);
            GUI.Label(new Rect(x, y + 190f, w, 24f), "MASH SPACE!", sSmall);
            Fill(new Rect(x + 24f, y + 240f, w - 48f, 18f), new Color(1f, 1f, 1f, 0.15f));
            Fill(new Rect(x + 24f, y + 240f, (w - 48f) * Mathf.Clamp01(timeLeft / CARDIO_TIME), 18f), new Color(0.2f, 0.9f, 0.8f));
            GUI.Label(new Rect(x + 24f, y + 262f, w - 48f, 24f), Mathf.Max(0f, timeLeft).ToString("0.0") + " s", sText);
        }

        void DrawReaction(float x, float y, float w)
        {
            GUI.Label(new Rect(x + 24f, y + 84f, w - 48f, 24f), "Round " + (round + 1) + " of " + ROUNDS, sText);
            float lw = 70f, gap = 18f, total = 5f * lw + 4f * gap, sx = x + (w - total) * 0.5f;
            for (int i = 0; i < 5; i++)
            {
                bool on = !lightsOut && i < lightsOn;
                Fill(new Rect(sx + i * (lw + gap), y + 130f, lw, lw), on ? new Color(1f, 0.15f, 0.12f) : new Color(0.18f, 0.06f, 0.06f));
            }
            string msg = roundMsg;
            if (msg.Length == 0) msg = lightsOut ? "GO!" : lightsOn < 5 ? "Wait for it..." : "Lights are on...";
            GUI.Label(new Rect(x, y + 230f, w, 90f), msg, sBig);
            string avg = "";
            if (times.Count > 0) { float s = 0f; foreach (float t in times) s += t; avg = "Average so far: " + Mathf.RoundToInt(s / times.Count * 1000f) + " ms"; }
            GUI.Label(new Rect(x + 24f, y + 340f, w - 48f, 24f), avg, sText);
        }

        void DrawFocus(float x, float y, float w)
        {
            GUI.Label(new Rect(x + 24f, y + 84f, w - 48f, 24f), "Sequence " + (roundsDone + 1) + " of " + FOCUS_ROUNDS + "   (" + seq.Count + " arrows)", sText);
            string shown = "";
            if (showing && showIdx < seq.Count && showT > 0.2f) shown = ARROWS[seq[showIdx]];
            GUI.Label(new Rect(x, y + 130f, w, 120f), shown.Length > 0 ? shown : (showing ? "." : "?"), sBig);
            string info = showing ? "Watch..." : "Your turn: " + inputIdx + " of " + seq.Count;
            GUI.Label(new Rect(x + 24f, y + 270f, w - 48f, 30f), info, sText);
            if (focusMsg.Length > 0) GUI.Label(new Rect(x + 24f, y + 300f, w - 48f, 30f), focusMsg, sSmall);
        }
    }
}
