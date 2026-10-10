using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// Marshal posts around the lap with real flag duty: green at the start, yellow in a sector where
    /// something happened (and everywhere under the safety car), blue when a lapping car is about to pass,
    /// white after the finish. Flags stay furled when there is nothing to show.
    /// </summary>
    public class Marshals
    {
        class Post { public float dist; public int sector; public Transform flag; public Material mat; public float phase; }
        readonly List<Post> posts = new List<Post>();

        public static Marshals Build(TrackModel m, Transform parent)
        {
            var ms = new Marshals();
            var hut = Mats.Make(new Color(0.23f, 0.25f, 0.29f), 0.2f);
            var roof = Mats.Make(new Color(0.12f, 0.13f, 0.16f), 0.3f);
            var pole = Mats.Make(new Color(0.8f, 0.8f, 0.82f), 0.5f, 0.5f);
            const int count = 10;
            for (int k = 0; k < count; k++)
            {
                float d = (k + 0.5f) / count * m.roadLength;
                float dp = Mathf.Abs(d - m.PitZoneDist); dp = Mathf.Min(dp, m.roadLength - dp);
                if (dp < 60f) continue;
                int side = (k % 2 == 0) ? -1 : 1;
                float off = m.WallOffsetAt(d) + 3.5f;
                Vector2 n = TrackModel.Normal(m.TangentAt(d));
                Vector3 p = TrackModel.W(m.PointAt(d) + n * off * side, m.ElevationAt(d));
                var g = new GameObject("MarshalPost");
                g.transform.SetParent(parent, false);
                g.transform.position = p;
                g.transform.rotation = Quaternion.LookRotation(TrackModel.W(-n * side, 0f));
                Prim.Box(g.transform, new Vector3(0f, 0.8f, 0f), new Vector3(1.6f, 1.6f, 1.4f), hut, "hut");
                Prim.Box(g.transform, new Vector3(0f, 1.65f, 0f), new Vector3(1.9f, 0.12f, 1.7f), roof, "roof");
                Prim.Cyl(g.transform, new Vector3(1.4f, 1.75f, 0f), new Vector3(0.08f, 1.75f, 0.08f), Quaternion.identity, pole, "pole");
                var flagMat = Mats.Make(Color.green, 0.1f);
                var flag = Prim.Box(g.transform, new Vector3(1.4f, 3.2f, 0.45f), new Vector3(0.03f, 0.55f, 0.9f), flagMat, "flag");
                ms.posts.Add(new Post { dist = d, sector = Mathf.Min(2, Mathf.FloorToInt(d / (m.roadLength / 3f))), flag = flag.transform, mat = flagMat, phase = k * 1.7f });
            }
            return ms;
        }

        public void Update(RaceManager rm, float t)
        {
            var p = rm.player;
            if (p == null) return;
            TrackModel tm = rm.track;
            bool start = rm.state == RaceManager.State.Racing && rm.Now < rm.raceStartClock + 10f;
            bool finished = rm.state == RaceManager.State.Finished;

            // blue flag: a car a lap ahead of you is sitting just behind you on the road
            bool blue = rm.blueCar != null;                    // RaceManager works out who the blue flag is for
            int mySector = Mathf.Min(2, Mathf.FloorToInt(TrackModel.Mod(p.dist, tm.roadLength) / (tm.roadLength / 3f)));

            rm.flagBanner = rm.redFlag ? "RED FLAG - RACE SUSPENDED"
                          : rm.rolling ? (rm.formationLap ? "FORMATION LAP - STAY IN ORDER, WARM THE TYRES" : "ROLLING START - HOLD YOUR SLOT, WARM THE TYRES")
                          : rm.safetyCarActive ? "YELLOW FLAG - SAFETY CAR"
                          : rm.vscActive ? "YELLOW FLAG - VIRTUAL SAFETY CAR"
                          : rm.incidentUntil[mySector] > rm.Now ? "YELLOW FLAG - NO OVERTAKING"
                          : blue ? "BLUE FLAG - LET HIM THROUGH"
                          : start ? "GREEN FLAG" : "";

            foreach (var post in posts)
            {
                Color c = Color.green; bool show = false;
                if (finished) { c = Color.white; show = true; }
                else if (rm.redFlag) { c = new Color(1f, 0.1f, 0.1f); show = true; }
                else if (rm.safetyCarActive || rm.vscActive || rm.incidentUntil[post.sector] > rm.Now) { c = new Color(1f, 0.85f, 0f); show = true; }
                else if (blue && TrackModel.Mod(post.dist - p.dist, tm.roadLength) < 350f) { c = new Color(0.15f, 0.4f, 1f); show = true; }
                else if (start) { c = Color.green; show = true; }
                post.mat.color = c;
                post.flag.localScale = show ? new Vector3(0.03f, 0.55f, 0.9f) : new Vector3(0.03f, 0.04f, 0.04f);
                post.flag.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 9f + post.phase) * (show ? 28f : 0f), 0f);
            }
        }
    }
}
