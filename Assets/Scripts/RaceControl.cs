using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    public enum Caution { None, Safety, Virtual, Red }

    /// <summary>Who has which pit box. A car claims a box on the way in; nobody else is sent to it until it leaves.</summary>
    public class PitBoxes
    {
        public readonly Racer[] owner = new Racer[TrackModel.PIT_BOX_COUNT];
        public const int PLAYER_BOX = 2;                              // the player's team box: nobody else is sent there

        public void Reset() { for (int i = 0; i < owner.Length; i++) owner[i] = null; }
        public void Release(Racer r) { for (int i = 0; i < owner.Length; i++) if (owner[i] == r) owner[i] = null; }
        public bool IsMine(int i, Racer r) { return i >= 0 && i < owner.Length && owner[i] == r; }

        /// <summary>
        /// Picks a free box the car can still stop in: the preferred one if possible, otherwise the nearest one ahead.
        /// 'ahead' gives the metres from the car to a box centre. Returns -1 if every box ahead is taken or too close.
        /// </summary>
        public int Claim(Racer r, int preferred, System.Func<int, float> ahead, float speed)
        {
            Release(r);
            float need = speed * speed / 28f + 3f;                        // braking distance at about 14 m/s^2
            int best = -1; float bestScore = float.MaxValue;
            for (int i = 0; i < owner.Length; i++)
            {
                if (owner[i] != null && owner[i] != r) continue;
                if (i == PLAYER_BOX && !r.isPlayer) continue;
                float a = ahead(i);
                if (a < need) continue;
                float score = a - (i == preferred ? 1000f : 0f);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0) owner[best] = r;
            return best;
        }
    }

    /// <summary>
    /// The weather for one race, decided before the start so the garage can show an honest forecast: how the
    /// race starts and whether the rain arrives or leaves part-way through.
    /// </summary>
    public class WeatherPlan
    {
        public bool wetStart, fog;
        public float changeFrac = -1f;          // fraction of the race when the weather changes, -1 = it does not
        public bool changeToRain;
        public float sunElev = 52f, sunAz = -35f;   // where the sun is (degrees above the horizon, compass bearing): low sun means glare
        public float startHour = 13f, latitude = 51f, dayOfYear = 150f;      // the local time at the start and where / when the race is
        public float windSpeed, windDir, windGust;                          // metres per second, the heading it blows towards (radians), and how gusty it is
        public float dryTemp, wetTemp;           // track temperature (C) when it is dry / when it is raining

        public float StartTemp { get { return wetStart ? wetTemp : fog ? dryTemp - 4f : dryTemp; } }

        /// <param name="odds">0 = dry (no rain), 1 = the circuit's normal odds, 2 = wet (rain very likely)</param>
        public static WeatherPlan Roll(TrackDef def, int odds = 1)
        {
            var p = new WeatherPlan();
            float rc = odds == 0 ? 0f : odds == 2 ? Mathf.Max(def.rainChance, 0.45f) * 1.6f : def.rainChance;
            p.latitude = SunPath.Latitude(def.id); p.dayOfYear = 80f + Random.value * 200f;
            for (int tries = 0; tries < 8; tries++)
            {
                p.startHour = 9.5f + Random.value * 8.5f;
                SunPath.Compute(p.latitude, p.dayOfYear, p.startHour, out p.sunElev, out p.sunAz);
                if (p.sunElev > 9f) break;
            }
            float wr = Random.value;
            p.windSpeed = 0.6f + wr * wr * 11f; p.windDir = Random.value * 6.2832f; p.windGust = 0.15f + Random.value * 0.8f;
            p.dryTemp = 24f + Random.value * 22f;
            p.wetTemp = p.dryTemp - 11f;
            p.wetStart = Random.value < rc * 0.6f;
            p.fog = !p.wetStart && odds != 0 && Random.value < def.fogChance;
            if (p.wetStart)
            {
                if (Random.value < 0.45f) { p.changeFrac = 0.45f + Random.value * 0.35f; p.changeToRain = false; }
            }
            else if (Random.value < rc * 1.3f + 0.03f)
            {
                p.changeFrac = 0.25f + Random.value * 0.45f; p.changeToRain = true;
            }
            return p;
        }

        public bool Changes { get { return changeFrac >= 0f; } }

        public string Describe(int laps)
        {
            string start = wetStart ? "Wet start" : p_fog();
            string sun = " Starts about " + SunPath.Clock(startHour) + (sunElev < 28f ? ", low sun in the " + SunPath.Compass(sunAz) + " - expect glare." : ".");
            string temp = sun + " Track about " + Mathf.RoundToInt(StartTemp) + " C" + (StartTemp > 40f ? " - hot, the tyres will run hot." : StartTemp < 20f ? " - cold, the tyres will be slow to warm." : ".");
            string windText = " " + WindField.Make(windSpeed, windDir, windGust, 0f).Describe();
            if (!Changes) return start + (wetStart ? ", staying wet." : ", staying dry.") + temp + windText;
            int lap = Mathf.Clamp(Mathf.FloorToInt(changeFrac * laps) + 1, 1, laps);
            string when = laps <= 1 ? "part-way round" : "around lap " + lap + " of " + laps;
            return start + ", " + (changeToRain ? "rain expected " : "drying out ") + when + "." + temp + windText;
        }

        string p_fog() { return fog ? "Dry but foggy start" : "Dry start"; }

        public Color DescribeColor()
        {
            if (wetStart || (Changes && changeToRain)) return new Color(0.55f, 0.75f, 1f);
            return new Color(0.7f, 0.9f, 0.6f);
        }
    }

    /// <summary>The stewards: warnings first, then time penalties, with a log shown on the results screen.</summary>
    public class Stewards
    {
        public readonly List<string> log = new List<string>();
        readonly Dictionary<string, int> offences = new Dictionary<string, int>();
        float lastReportAt = -99f;

        public void Reset() { log.Clear(); offences.Clear(); lastReportAt = -99f; }

        /// <summary>First offence of a kind is a warning, repeats cost 'seconds'. 'immediate' skips the warning for serious ones.</summary>
        public void Offend(RaceManager rm, PlayerCar p, string kind, string reason, float seconds, bool immediate = false)
        {
            if (rm.mode == RaceManager.Mode.Tutorial) return;
            if (rm.Now - lastReportAt < 2.5f) return;
            lastReportAt = rm.Now;
            int n;
            offences.TryGetValue(kind, out n);
            n++; offences[kind] = n;
            if (n == 1 && !immediate)
            {
                log.Add("Warning: " + reason);
                rm.Commentate("The stewards are taking a look at " + rm.NameOf(p) + " for " + reason + ".", 1);
                rm.Radio("Stewards: warning - " + reason);
                rm.Flash("STEWARDS WARNING");
            }
            else
            {
                p.penaltySeconds += seconds;
                log.Add("+" + seconds.ToString("0") + "s: " + reason);
                rm.Radio("Stewards: +" + seconds.ToString("0") + " seconds - " + reason);
                rm.Flash("+" + seconds.ToString("0") + "s PENALTY");
                rm.Commentate(rm.NameOf(p) + " is handed a " + seconds.ToString("0") + " second time penalty for " + reason + ".", 2);
            }
        }

        public void Penalty(RaceManager rm, PlayerCar p, string reason, float seconds)
        {
            if (rm.mode == RaceManager.Mode.Tutorial) return;
            p.penaltySeconds += seconds;
            log.Add("+" + seconds.ToString("0") + "s: " + reason);
            rm.Radio("Stewards: +" + seconds.ToString("0") + " seconds - " + reason);
            rm.Flash("+" + seconds.ToString("0") + "s PENALTY");
            lastReportAt = rm.Now;
        }
    }

    /// <summary>One question from the pit wall with a Yes / No answer (keys Y and N, or the buttons).</summary>
    public class RadioCall
    {
        public string key, text, yesLabel, noLabel;
        public System.Action yes, no, expire;
        public float expires, asked;
    }

    public class CallCenter
    {
        public RadioCall active;
        float nextAllowed = 25f;
        readonly HashSet<string> done = new HashSet<string>();
        static GUIStyle sHead, sBody;

        public void Reset() { active = null; nextAllowed = 25f; done.Clear(); }

        public bool Busy { get { return active != null; } }

        /// <summary>Asks the driver a strategy question. Returns false if one is already open or it is too soon.</summary>
        public bool Ask(RaceManager rm, string key, string text, string yesLabel, string noLabel, System.Action yes, System.Action no, bool repeatable = false, float ttl = 10f, System.Action expire = null)
        {
            float now = rm.Now;
            if (active != null || now < nextAllowed) return false;
            if (!repeatable && done.Contains(key)) return false;
            done.Add(key);
            active = new RadioCall { key = key, text = text, yesLabel = yesLabel, noLabel = noLabel, yes = yes, no = no, expire = expire, asked = now, expires = now + ttl };
            rm.Radio("Radio: " + text, 3);
            return true;
        }

        public void Answer(RaceManager rm, bool yesAnswer)
        {
            if (active == null) return;
            var c = active; active = null;
            nextAllowed = rm.Now + 30f;
            if (yesAnswer) { if (c.yes != null) c.yes(); }
            else { if (c.no != null) c.no(); }
        }

        public void Update(RaceManager rm)
        {
            if (active == null) return;
            if (rm.Now >= active.expires) { var gone = active; active = null; nextAllowed = rm.Now + 20f; if (gone.expire != null) gone.expire(); return; }
            if (Inp.Pressed(KeyCode.Y)) Answer(rm, true);
            else if (Inp.Pressed(KeyCode.N)) Answer(rm, false);
        }

        public void Draw(RaceManager rm, float W, float H)
        {
            if (active == null) return;
            if (sHead == null)
            {
                sHead = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
                sHead.normal.textColor = new Color(0.2f, 0.9f, 0.8f);
                sBody = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = true };
                sBody.normal.textColor = Color.white;
            }
            float w = 560f, x = W * 0.5f - w * 0.5f;
            float hText = sBody.CalcHeight(new GUIContent(active.text), w - 28f);
            float h = 12f + 16f + hText + 8f + 32f + 14f;
            float y = H - 150f - h;
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = new Color(0.2f, 0.9f, 0.8f);
            GUI.DrawTexture(new Rect(x, y, 4f, h), Texture2D.whiteTexture);
            float left = Mathf.Clamp01((active.expires - rm.Now) / Mathf.Max(0.1f, active.expires - active.asked));
            GUI.DrawTexture(new Rect(x + 4f, y + h - 3f, (w - 4f) * left, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 14f, y + 8f, w - 28f, 16f), "TEAM RADIO  -  your call", sHead);
            GUI.Label(new Rect(x + 14f, y + 24f, w - 28f, hText), active.text, sBody);
            float by = y + 24f + hText + 8f;
            GUI.backgroundColor = new Color(0.3f, 0.9f, 0.4f);
            if (GUI.Button(new Rect(x + 14f, by, 260f, 30f), "[Y]  " + active.yesLabel)) { Answer(rm, true); GUI.backgroundColor = Color.white; return; }
            GUI.backgroundColor = new Color(1f, 0.45f, 0.35f);
            if (GUI.Button(new Rect(x + 286f, by, 260f, 30f), "[N]  " + active.noLabel)) { Answer(rm, false); GUI.backgroundColor = Color.white; return; }
            GUI.backgroundColor = Color.white;
        }
    }
}
