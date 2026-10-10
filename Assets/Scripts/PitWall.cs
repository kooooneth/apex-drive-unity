using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The pit-wall strategy screen (press T): a live timing tower with every car's tyres, stint age, stops and gap, plus a
    /// "what if I pit now?" planner that predicts where you would rejoin.
    /// </summary>
    public static class PitWall
    {
        static GUIStyle sTitle, sRow, sSmall;
        static readonly string[] TyreLetter = { "S", "M", "H", "I", "W" };
        static readonly Color[] TyreColor =
        {
            new Color(1f, 0.25f, 0.2f), new Color(1f, 0.85f, 0.15f), new Color(0.93f, 0.93f, 0.96f), new Color(0.25f, 0.85f, 0.4f), new Color(0.3f, 0.55f, 1f)
        };

        static void Fill(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; }

        static void Styles()
        {
            if (sTitle != null) return;
            sTitle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            sTitle.normal.textColor = Color.white;
            sRow = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            sRow.normal.textColor = Color.white;
            sSmall = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            sSmall.normal.textColor = new Color(0.8f, 0.85f, 0.95f);
        }

        static void Text(float x, float y, float w, string s, GUIStyle st, Color c)
        {
            var old = st.normal.textColor; st.normal.textColor = c;
            GUI.Label(new Rect(x, y, w, 20f), s, st);
            st.normal.textColor = old;
        }

        public static void Draw(RaceManager rm, float W, float H)
        {
            var p = rm.player;
            if (p == null) return;
            Styles();
            TrackModel tm = rm.track;
            var order = new List<Racer>();
            foreach (var r in rm.racers) if (!r.gone) order.Add(r);
            order.Sort((a, b) => b.dist.CompareTo(a.dist));

            float x = W - 480f, y = 84f, w = 462f, rowH = 21f;
            float h = 62f + order.Count * rowH + 130f;
            Fill(new Rect(x, y, w, h), new Color(0f, 0f, 0f, 0.8f));
            Fill(new Rect(x, y, 4f, h), new Color(0.2f, 0.9f, 0.8f));
            Text(x + 12f, y + 6f, w, "PIT WALL     [T] close", sTitle, Color.white);
            var grey = new Color(0.6f, 0.65f, 0.75f);
            float cy = y + 32f;
            Text(x + 12f, cy, 30f, "P", sSmall, grey); Text(x + 38f, cy, 150f, "DRIVER", sSmall, grey);
            Text(x + 196f, cy, 60f, "TYRE", sSmall, grey); Text(x + 250f, cy, 50f, "LAPS", sSmall, grey);
            Text(x + 296f, cy, 50f, "STOPS", sSmall, grey); Text(x + 346f, cy, 60f, "GAP", sSmall, grey); Text(x + 404f, cy, 60f, "PITS IN", sSmall, grey);
            cy += 20f;

            Racer leader = order.Count > 0 ? order[0] : p;
            int myRank = 1;
            for (int i = 0; i < order.Count; i++)
            {
                var r = order[i];
                if (r == p) myRank = i + 1;
                if (r == p) Fill(new Rect(x + 6f, cy, w - 12f, rowH - 1f), new Color(0.2f, 0.9f, 0.8f, 0.2f));
                Color txt = r.dnf ? new Color(0.6f, 0.4f, 0.4f) : Color.white;
                Text(x + 12f, cy, 30f, (i + 1).ToString(), sRow, txt);
                Text(x + 38f, cy, 160f, rm.NameOf(r), sRow, txt);
                int ci = Mathf.Clamp((int)r.compound, 0, 4);
                Fill(new Rect(x + 198f, cy + 3f, 16f, 15f), TyreColor[ci]);
                Text(x + 202f, cy, 20f, TyreLetter[ci], sSmall, new Color(0.05f, 0.05f, 0.05f));
                Text(x + 218f, cy, 40f, Mathf.RoundToInt(r.tireWear) + "%", sSmall, txt);
                Text(x + 252f, cy, 40f, Mathf.Max(0, r.lap - r.stintStartLap).ToString(), sRow, txt);
                Text(x + 304f, cy, 40f, r.pitCount.ToString(), sRow, txt);
                string gap = r.dnf ? "DNF" : r == leader ? "LEADER" : "+" + ((leader.dist - r.dist) / Mathf.Max(30f, r.speed)).ToString("0.0");
                Text(x + 346f, cy, 60f, gap, sRow, txt);
                var a = r as AICar;
                if (a != null && !a.dnf)
                {
                    float perLap = tm.roadLength * 0.014f * Tuning.TireWear[(int)a.compound] * Tuning.WetWear(a.compound, rm.roadWetness);
                    float laps = perLap > 0.1f ? Mathf.Max(0f, (a.tireWear - a.pitThreshold) / perLap) : 99f;
                    Text(x + 408f, cy, 54f, a.pitStopActive ? "in pits" : "~" + laps.ToString("0.0") + "L", sSmall, grey);
                }
                cy += rowH;
            }

            // ---- the planner ----
            cy += 6f;
            Fill(new Rect(x + 8f, cy, w - 16f, 1f), new Color(1f, 1f, 1f, 0.2f));
            cy += 6f;
            float lapsLeft = p.wearPerLap > 0.5f ? Mathf.Max(0f, (p.tireWear - 15f) / p.wearPerLap) : -1f;
            Text(x + 12f, cy, w - 24f, "YOUR TYRES: " + Tuning.CompoundNames[(int)p.compound] + "  " + Mathf.RoundToInt(p.tireWear) + "%"
                 + (lapsLeft >= 0f ? "   about " + lapsLeft.ToString("0.0") + " laps left" : "   (learning the wear rate)"), sRow, Color.white);
            cy += 22f;

            // what if you came in now? You lose roughly the time of the pit lane plus the stop; everyone within that gap passes you.
            float loss = 2.8f + 3.0f + (rm.fuelOn ? Mathf.Max(0f, (p.fuelTarget - p.fuel) / 14f) : 0f) + p.RepairSeconds();
            int behindWithin = 0; Racer nearestBehind = null; float nearestT = 1e9f;
            foreach (var r in order)
            {
                if (r == p || r.dnf || r.pitStopActive) continue;
                float t = (p.dist - r.dist) / Mathf.Max(30f, r.speed);        // seconds the car is behind you (negative = ahead)
                if (t > 0f && t < loss) behindWithin++;
                if (t > loss && t < nearestT) { nearestT = t; nearestBehind = r; }
            }
            int predicted = Mathf.Min(order.Count, myRank + behindWithin);
            Text(x + 12f, cy, w - 24f, "IF YOU PIT NOW: lose about " + loss.ToString("0.0") + " s and rejoin P" + predicted
                 + (predicted > myRank ? "  (" + (predicted - myRank) + " place" + (predicted - myRank == 1 ? "" : "s") + " down)" : "  (no places lost)"), sRow, new Color(1f, 0.85f, 0.3f));
            cy += 22f;
            string traffic = nearestBehind != null && nearestT - loss < 2.5f ? "Traffic: " + rm.NameOf(nearestBehind) + " would be just behind you." : "You would rejoin in clear air.";
            // undercut: the car just ahead on much older tyres is a target
            Racer ahead = null;
            for (int i = 0; i < order.Count; i++) if (order[i] == p && i > 0) ahead = order[i - 1];
            if (ahead != null && !ahead.dnf)
            {
                int theirs = ahead.lap - ahead.stintStartLap, mine = p.lap - p.stintStartLap;
                float gapAhead = (ahead.dist - p.dist) / Mathf.Max(30f, p.speed);
                if (theirs >= mine + 2 && gapAhead < 4f) traffic += "   Undercut chance: " + rm.NameOf(ahead) + " is on old tyres.";
            }
            GUI.Label(new Rect(x + 12f, cy, w - 24f, 42f), traffic, sSmall);
        }
    }
}
