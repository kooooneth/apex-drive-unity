using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The guided tutorial: a solo practice session (no rivals, no rain) where a panel on the left walks you
    /// through the controls one step at a time. Action steps finish by themselves when you do the thing;
    /// info steps wait for Enter. Everything can be skipped.
    /// </summary>
    public class TutorialGuide
    {
        delegate bool Check(TutorialGuide g, PlayerCar p, float dt);
        class Step
        {
            public string title, text, keys;
            public Check done;            // null = an info step that waits for Enter
            public Target[] arrows;       // things on screen this step points at
        }

        // An arrow that points at something on screen. 'cockpit' targets only exist in the cockpit view.
        const int DOWN = 0, LEFT = 1, UP = 2, RIGHT = 3;        // the way the arrow points
        class Target
        {
            public bool cockpit; public int dir; public string label;
            public System.Func<float, float, Vector2> pos;       // (screen width, height) -> where it points, in GUI units
        }
        static Target Tg(bool cockpit, int dir, string label, System.Func<float, float, Vector2> pos)
        {
            return new Target { cockpit = cockpit, dir = dir, label = label, pos = pos };
        }
        // a spot on the cockpit picture, as fractions of the screen (same layout maths as CockpitRig.Sp)
        static Vector2 Cock(float W, float H, float fx, float fy) { return new Vector2(W * 0.5f + (fx - 0.5f) * H * 1.78f, fy * H); }
        // a spot on the steering wheel face, in the wheel's own units (it sits 0.5 m in front, scaled 1.12)
        static Vector2 Whl(float W, float H, float lx, float ly)
        {
            float x = lx * 1.12f, y = -0.27f + ly * 1.12f;
            return Cock(W, H, 0.5f + x / 1.4416f, 0.5f - y / 0.8098f);
        }
        // HUD spots (the HUD is laid out on a 1280 x 720 canvas)
        static Vector2 FromRight(float W, float H, float dx, float dyFromBottom) { return new Vector2(W - dx, H - dyFromBottom); }

        readonly Step[] steps;
        int idx;
        float acc, acc2, advanceTimer;
        bool flag, stepDone;
        public bool finished, completed;

        static GUIStyle sTitle, sBody, sSmall, sKeys, sLabel;

        public TutorialGuide()
        {
            steps = new[]
            {
                new Step { title = "Welcome to the track",
                    text = "This is a solo practice session: no rivals and no rain, just you and the car. A panel like this one will walk you through the controls. Steps tick off by themselves when you do what they ask.",
                    keys = "Enter = continue     Esc = pause" },
                new Step { title = "Accelerate",
                    text = "Hold the throttle and build speed. Watch the shift lights across the top of the steering wheel and the gear number change as you go. Reach 150 km/h.",
                    keys = "Hold  W  or  Up Arrow", done = (g, p, dt) => p.speed >= 42f },
                new Step { title = "Steer",
                    text = "Turn left, then right. The front wheels turn with you. Steering is smooth, so ease into it rather than snapping the wheel at speed.",
                    keys = "A / D   or   Left / Right Arrow",
                    done = (g, p, dt) => { if (p.speed > 8f) { if (p.inLeft) g.acc += dt; if (p.inRight) g.acc2 += dt; } return g.acc > 0.5f && g.acc2 > 0.5f; } },
                new Step { title = "Brake",
                    text = "Brake in a straight line before a corner, not in the middle of it. Hard braking heats the discs (see the brake temperature on the wheel) and past about 800 C they fade. Braking also recharges the ERS battery.",
                    keys = "Hold  S  or  Down Arrow",
                    done = (g, p, dt) => { if (p.inBrake && p.speed > 10f) g.acc += dt; return g.acc > 1.2f; } },
                new Step { title = "Change the camera",
                    text = "Switch between the cockpit view and the chase view. In the chase view you can see the wheels and the whole car.",
                    keys = "Press  V", done = (g, p, dt) => Inp.Pressed(KeyCode.V) },
                new Step { title = "Look around",
                    text = "Hold a look key to turn your head: left, right, or straight behind. Use it to check the mirrors' blind spots before you move over.",
                    keys = "Hold  Z (left)   X (right)   C (behind)",
                    done = (g, p, dt) => { if (Inp.Held(KeyCode.Z) || Inp.Held(KeyCode.X) || Inp.Held(KeyCode.C)) g.acc += dt; return g.acc > 0.5f; } },
                new Step { title = "Engine mode",
                    text = "Cycle through BALANCED, PUSH and SAVE. PUSH gives more power but wears the tyres faster and drains the battery slower than it charges. SAVE does the opposite. The mode shows on the wheel display.",
                    keys = "Press  E", done = (g, p, dt) => Inp.Pressed(KeyCode.E) },
                new Step { title = "ERS overtake boost",
                    text = "Hold Shift with the throttle to spend the battery on extra acceleration and top speed. It recharges when you brake or lift off, so save it for passing and for the exit of slow corners.",
                    keys = "Hold  Shift  +  W   for 1.5 seconds",
                    done = (g, p, dt) => { if (p.ersDeploy) g.acc += dt; return g.acc > 1.5f; } },
                new Step { title = "DRS",
                    text = "In the DRS zone on the straight, if you are within a second or so of the car ahead, the DRS light on the wheel turns on. Hold Q to open the rear wing flap for about +9% top speed. It switches off under braking and during a safety car. (There is no car ahead in practice, so you will meet it in a race.)",
                    keys = "Hold  Q  when the DRS banner appears" },
                new Step { title = "Slipstream and slingshot",
                    text = "Drive right behind another car, in its line, and the air has less drag: your SLIPSTREAM banner shows a TOW percentage that fills the longer you stay there. Then pull out to the side while it is charged and it is released as a SLINGSHOT: extra acceleration and top speed for a couple of seconds, enough to complete the pass. The rivals use it too.",
                    keys = "Stay in line behind a car, then steer out" },
                new Step { title = "Pit stops",
                    text = "Call a stop with B. A guide shows you the way into the pit lane and the box (there are six boxes and the pit assigns you box 3; pass the others on the track side of the lane), and the car slows to the pit limiter. Once stopped the crew changes the tyres: press 1 (soft), 2 (medium), 3 (hard), 4 (inter) or 5 (wet) to pick them. Now press B to call a stop, then B again to cancel it, because we will not pit right now.",
                    keys = "Press  B  then  B  again",
                    done = (g, p, dt) => { if (p.pitRequest) g.flag = true; return g.flag && !p.pitRequest; } },
                new Step { title = "Recover to the track",
                    text = "Spun, stuck or lost? Recover puts you back on the centre of the track and pointed the right way, at a cost of most of your speed.",
                    keys = "Press  R", done = (g, p, dt) => Inp.Pressed(KeyCode.R) },
                new Step { title = "Walls, contact and track limits",
                    text = "Hitting a wall or another car damages the front wing and body, and a broken wing costs you grip until the next pit stop. Keep all four wheels inside the white lines: repeated track-limit offences add time penalties. Yellow flags mean an incident ahead, so be careful in that sector.",
                    keys = "" },
                new Step { title = "Starts, weather and tyres",
                    text = "Rolling starts (Race tab) skip the lights: weave to warm the tyres and the green flag comes at the line. The garage forecast tells you if rain is coming and how hot the track is: a hot track cooks soft tyres, a cold one leaves them slow to warm up. Kerbs shake the car and scrub speed, and are much worse in the wet. Slicks hate water: use intermediates (4) on a damp track and full wets (5) when it is soaked, and expect wet tyres to wear out fast on a dry road.",
                    keys = "Forecast on the Race tab     1-5 = tyres in the pit" },
                new Step { title = "Radio calls and race control",
                    text = "The pit wall asks for decisions: Y to say yes or N to say no (rain coming, a puncture, a rival pitting, a safety car). Ignore it and nothing changes. Safety car, virtual safety car (stay under 137 km/h) and red flags can all come out; after a red flag you get free tyres and a standing restart. Blue flag: let a lapping car through. Causing a collision, ignoring blue flags or overtaking under yellow earns the stewards' warning, then time penalties. Your pit crew can make mistakes too. After a race, open Telemetry to compare laps.",
                    keys = "Y / N = answer the radio" },
                new Step { title = "Your racer, training and new modes",
                    text = "On the Racer tab you name your racer and train three stats: Fitness (the driver tires more slowly), Focus (less tyre wear, steadier over kerbs) and Reflexes (quicker steering). Train with gym exercises (they cost energy) or practice sessions on the circuits. Endurance races run into the night with fuel (F changes the fuel load at a stop), a tiring driver and a relief-driver swap. On the Race tab you can also build your own custom season.",
                    keys = "Racer tab = name + training" },
                new Step { title = "Weekend, setup and conditions",
                    text = "Race weekend (Race tab): three practice sessions, qualifying, then the race. In practice your engineer gives setup feedback on ride height, camber, differential and tyre pressure; the setup locks when qualifying starts. In the wet, spray hides the car ahead and puddles can make a car aquaplane (wets and intermediates resist it). A low sun dazzles you when you drive toward it. Rivals can spin, break down (a recovery truck clears them) and launch off each other's wheels; so can you.",
                    keys = "" },
                new Step { title = "Team, strategy screen and voices",
                    text = "On the Team tab you have a contract with a team (it sets how good your car is), a teammate, R&D points to develop the car and, after a career season, offers from other teams. Press T in a race for the pit-wall screen: every car's tyres, stint age, stops and gap, and a planner that predicts where you rejoin if you pit now. The engineer and the pit crew speak over the radio, and in the replay two commentators call the race (needs the Windows voices; switch it off in the Profile tab). Start type 'Formation' gives a grid walk and a formation lap.",
                    keys = "T = pit wall" },
                new Step { title = "You are ready",
                    text = "That is everything: throttle, brake, steering, views, engine mode, ERS, DRS, slipstream, pit stops and recovery. Head back to the garage to set up your car and start a race. You can replay this tutorial any time from the Race tab.",
                    keys = "Enter = finish" },
            };

            // ---- what each step points at ----
            steps[0].arrows = new[]
            {
                Tg(false, UP, "Lap", (W, H) => new Vector2(W * 0.5f - 120f, 52f)),
                Tg(false, UP, "Position", (W, H) => new Vector2(W * 0.5f + 120f, 52f)),
                Tg(false, DOWN, "Speed (km/h)", (W, H) => FromRight(W, H, 100f, 92f)),
                Tg(true, DOWN, "Gear and lap time", (W, H) => Whl(W, H, 0f, 0.075f)),
            };
            steps[1].arrows = new[]
            {
                Tg(true, DOWN, "Shift lights", (W, H) => Whl(W, H, 0f, 0.14f)),
                Tg(true, RIGHT, "Gear", (W, H) => Whl(W, H, -0.07f, 0.058f)),
                Tg(false, DOWN, "Speed", (W, H) => FromRight(W, H, 100f, 92f)),
            };
            steps[2].arrows = new[]
            {
                Tg(true, RIGHT, "Steering wheel", (W, H) => Whl(W, H, -0.235f, 0f)),
                Tg(true, DOWN, "Front tyre", (W, H) => Cock(W, H, 0.115f, 0.585f)),
                Tg(true, DOWN, "Front tyre", (W, H) => Cock(W, H, 0.885f, 0.585f)),
            };
            steps[3].arrows = new[]
            {
                Tg(true, UP, "Brake temperature", (W, H) => Whl(W, H, -0.058f, 0.07f)),
                Tg(false, RIGHT, "Battery recharges", (W, H) => FromRight(W, H, 330f, 80f)),
            };
            steps[5].arrows = new[]
            {
                Tg(true, DOWN, "Left mirror", (W, H) => Cock(W, H, 0.255f, 0.43f)),
                Tg(true, DOWN, "Right mirror", (W, H) => Cock(W, H, 0.745f, 0.43f)),
            };
            steps[6].arrows = new[] { Tg(true, DOWN, "Engine mode", (W, H) => Whl(W, H, 0f, 0.12f)) };
            steps[7].arrows = new[] { Tg(false, RIGHT, "ERS battery", (W, H) => FromRight(W, H, 330f, 80f)) };
            steps[8].arrows = new[]
            {
                Tg(true, DOWN, "DRS light", (W, H) => Whl(W, H, 0.1f, 0.125f)),
                Tg(false, RIGHT, "DRS banner", (W, H) => new Vector2(W * 0.5f - 95f, 190f)),
            };
            steps[9].arrows = new[] { Tg(false, RIGHT, "Slipstream and tow banner", (W, H) => new Vector2(W * 0.5f - 95f, 271f)) };
            steps[10].arrows = new[]
            {
                Tg(true, DOWN, "Pit light", (W, H) => Whl(W, H, -0.1f, 0.125f)),
                Tg(false, RIGHT, "Tyre wear", (W, H) => FromRight(W, H, 330f, 43f)),
            };
        }

        void Next()
        {
            idx++;
            acc = acc2 = 0f; flag = false; stepDone = false; advanceTimer = 0f;
            if (idx >= steps.Length) { idx = steps.Length - 1; finished = true; completed = true; }
        }

        /// <summary>Called every racing frame.</summary>
        public void Update(PlayerCar p, float dt)
        {
            if (finished) return;
            if (advanceTimer > 0f)
            {
                advanceTimer -= dt;
                if (advanceTimer <= 0f) Next();
                return;
            }
            var s = steps[idx];
            if (s.done == null)
            {
                if (Inp.Pressed(KeyCode.Return) || Inp.Pressed(KeyCode.KeypadEnter)) Next();
            }
            else if (s.done(this, p, dt))
            {
                stepDone = true; advanceTimer = 1.0f;
            }
        }

        static void Styles()
        {
            if (sTitle != null) return;
            sTitle = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold, wordWrap = true };
            sTitle.normal.textColor = Color.white;
            sBody = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            sBody.normal.textColor = new Color(0.9f, 0.92f, 0.96f);
            sSmall = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            sSmall.normal.textColor = new Color(0.2f, 0.9f, 0.8f);
            sKeys = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = true };
            sKeys.normal.textColor = new Color(1f, 0.85f, 0.2f);
            sLabel = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            sLabel.normal.textColor = new Color(1f, 0.9f, 0.25f);
        }

        static bool HasCockpitArrows(Step s)
        {
            if (s.arrows == null) return false;
            foreach (var t in s.arrows) if (t.cockpit) return true;
            return false;
        }

        // Yellow arrows with a label, bobbing toward the thing they point at.
        void DrawArrows(Step s, float W, float H, bool cockpitView)
        {
            if (s.arrows == null) return;
            float bob = (Mathf.Sin(Time.realtimeSinceStartup * 6f) + 1f) * 4f;
            foreach (var t in s.arrows)
            {
                if (t.cockpit && !cockpitView) continue;
                Vector2 target = t.pos(W, H);
                float a = t.dir * 90f * Mathf.Deg2Rad;
                Vector2 away = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));            // from the target back toward the arrow's tail
                Vector2 tip = target + away * (10f + bob);
                var old = GUI.matrix;
                GUIUtility.RotateAroundPivot(t.dir * 90f, tip);
                DrawArrowShape(tip, new Color(0f, 0f, 0f, 0.85f), 4f);
                DrawArrowShape(tip, new Color(1f, 0.88f, 0.2f), 0f);
                GUI.matrix = old;
                GUI.color = Color.white;

                Vector2 size = sLabel.CalcSize(new GUIContent(t.label));
                float lw = size.x + 14f, lh = 24f;
                Vector2 tail = tip + away * 50f;
                Vector2 centre = (t.dir == LEFT || t.dir == RIGHT) ? tail + away * (lw * 0.5f) : tail + away * (lh * 0.5f);
                var r = new Rect(Mathf.Clamp(centre.x - lw * 0.5f, 4f, W - lw - 4f), Mathf.Clamp(centre.y - lh * 0.5f, 4f, H - lh - 4f), lw, lh);
                GUI.color = new Color(0f, 0f, 0f, 0.78f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(r, t.label, sLabel);
            }
        }

        // arrow pointing down with its tip at 'tip' (the caller rotates the GUI for other directions)
        static void DrawArrowShape(Vector2 tip, Color c, float grow)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(tip.x - 3f - grow, tip.y - 50f - grow, 6f + grow * 2f, 40f + grow * 2f), Texture2D.whiteTexture);
            var m = GUI.matrix;
            Vector2 head = new Vector2(tip.x, tip.y - 10f);
            GUIUtility.RotateAroundPivot(45f, head);
            float hs = 14f + grow * 2f;
            GUI.DrawTexture(new Rect(head.x - hs * 0.5f, head.y - hs * 0.5f, hs, hs), Texture2D.whiteTexture);
            GUI.matrix = m;
        }

        public void Draw(float W, float H, bool cockpitView)
        {
            if (finished) return;
            Styles();
            var s = steps[idx];
            if (!stepDone) DrawArrows(s, W, H, cockpitView);
            float x = 16f, y = 120f, w = 430f, pad = 14f, iw = w - pad * 2f;
            float hBody = sBody.CalcHeight(new GUIContent(s.text), iw);
            float hKeys = s.keys.Length > 0 ? sKeys.CalcHeight(new GUIContent(s.keys), iw) + 6f : 0f;
            bool needCockpit = !cockpitView && HasCockpitArrows(s);
            float hNote = needCockpit ? 22f : 0f;
            float total = pad + 18f + 30f + hBody + 8f + hKeys + hNote + 12f + 34f + pad;

            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(x, y, w, total), Texture2D.whiteTexture);
            GUI.color = stepDone ? new Color(0.3f, 0.95f, 0.5f) : new Color(0.2f, 0.9f, 0.8f);
            GUI.DrawTexture(new Rect(x, y, 4f, total), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float cy = y + pad;
            GUI.Label(new Rect(x + pad, cy, iw, 18f), "TUTORIAL   STEP " + (idx + 1) + " / " + steps.Length + (stepDone ? "      DONE!" : ""), sSmall); cy += 18f;
            GUI.Label(new Rect(x + pad, cy, iw, 30f), s.title, sTitle); cy += 30f;
            GUI.Label(new Rect(x + pad, cy, iw, hBody), s.text, sBody); cy += hBody + 8f;
            if (hKeys > 0f) { GUI.Label(new Rect(x + pad, cy, iw, hKeys), s.keys, sKeys); cy += hKeys; }
            if (needCockpit) { GUI.Label(new Rect(x + pad, cy, iw, 20f), "Press V for the cockpit view to see the arrows.", sSmall); cy += hNote; }
            cy += 12f;

            // progress bar
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(new Rect(x + pad, cy + 4f, iw, 6f), Texture2D.whiteTexture);
            GUI.color = new Color(0.2f, 0.9f, 0.8f);
            GUI.DrawTexture(new Rect(x + pad, cy + 4f, iw * (idx + (stepDone ? 1f : 0f)) / steps.Length, 6f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            cy += 16f;

            if (s.done == null)
            {
                if (GUI.Button(new Rect(x + pad, cy, 200f, 26f), idx == steps.Length - 1 ? "FINISH  (Enter)" : "CONTINUE  (Enter)")) Next();
            }
            else if (!stepDone)
            {
                if (GUI.Button(new Rect(x + pad, cy, 120f, 26f), "Skip step")) Next();
            }
            if (GUI.Button(new Rect(x + w - pad - 130f, cy, 130f, 26f), "Exit tutorial")) { finished = true; completed = false; }
        }
    }
}
