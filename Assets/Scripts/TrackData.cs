using UnityEngine;

namespace ApexDrive
{
    public class TrackDef
    {
        public string id, name, sub;
        public Vector2[] points;      // x,z in "web space" (same coordinates as the HTML game)
        public float pitFrac;
        public float[] elevation;     // null on flat tracks
        public float rainChance, fogChance;
        public bool tightWalls;       // a street circuit: the walls stand just beyond the kerbs
    }

    public static class Tracks
    {
        static Vector2[] P(params float[] v)
        {
            var r = new Vector2[v.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = new Vector2(v[i * 2], v[i * 2 + 1]);
            return r;
        }

        public static readonly TrackDef[] All =
        {
            new TrackDef {
                id = "harborline", name = "Harborline Circuit", sub = "Technical - flowing harbour loop",
                points = P(-205,88, -221,12, -191,-50, -145,-75, -102,-94, -73,-144, -23,-172, 20,-162, 53,-125, 99,-112, 152,-106, 205,-81, 214,-31, 178,6, 125,12, 86,44, 46,94, 7,150, -46,175, -106,175, -152,150, -185,119),
                pitFrac = 0.96f, elevation = null, rainChance = 0.28f, fogChance = 0.38f
            },
            new TrackDef {
                id = "desert", name = "Desert Straight", sub = "Fast - sweeping corners, rolling elevation",
                points = P(0,0, 150,18, 262,88, 300,202, 218,302, 78,322, -62,280, -162,178, -180,58, -98,-22),
                pitFrac = 0.78f, elevation = new float[] { 0, 6, 13, 7, -3, -9, -4, 8, 15, 5 },
                rainChance = 0.04f, fogChance = 0.06f
            },
            new TrackDef {
                id = "sprint", name = "Sprint Alley", sub = "Short - quick laps",
                points = P(0,0, 80,0, 130,40, 130,90, 80,130, 0,130, -50,90, -50,40),
                pitFrac = 0.35f, elevation = null, rainChance = 0.35f, fogChance = 0.28f
            },
                        new TrackDef {
                id = "marina", name = "Marina Streets", sub = "Street circuit - walls right beside the road",
                points = P(0,-150, 90,-150, 130,-120, 140,-60, 110,-20, 60,-10, 40,30, 60,70, 120,90, 150,140, 120,190, 40,200, -40,190, -80,150, -70,90, -110,60, -140,0, -120,-60, -70,-100, -30,-140),
                pitFrac = 0.9f, elevation = null, rainChance = 0.25f, fogChance = 0.2f, tightWalls = true
            },
            new TrackDef {
                id = "alpine", name = "Alpine Pass", sub = "Mountain - big climbs, drops and hairpins",
                points = P(0,0, 120,-30, 240,-20, 330,30, 340,100, 280,140, 200,120, 150,150, 160,210, 230,250, 260,320, 190,370, 90,350, 30,290, -60,280, -140,230, -150,150, -100,80, -60,30),
                pitFrac = 0.97f, elevation = new float[] { 0, 5, 12, 20, 28, 34, 40, 44, 40, 34, 26, 18, 10, 6, 2, -2, -4, -2, 0 },
                rainChance = 0.3f, fogChance = 0.45f
            },
            new TrackDef {
                id = "ring", name = "Grand Ring", sub = "Long and fast - an endurance circuit",
                points = P(472,0, 445,96, 319,160, 202,208, 69,232, -65,221, -212,218, -379,189, -431,93, -368,0, -345,-75, -324,-162, -218,-225, -77,-260, 80,-271, 208,-215, 265,-132, 358,-78),
                pitFrac = 0.97f, elevation = new float[] { 0, 2, 4, 6, 5, 3, 1, 0, -1, -2, -1, 0, 1, 3, 4, 3, 1, 0 },
                rainChance = 0.3f, fogChance = 0.15f
            },
        };
    }
}
