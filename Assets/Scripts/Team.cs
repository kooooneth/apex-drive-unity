using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    public class TeamDef { public string name; public Color color; public float baseRating; public int baseSalary; }

    /// <summary>
    /// Your team and contract, the rival teams, the R&amp;D tree and the season's objectives. Team 0 is a backmarker you start with;
    /// do well in a career season and the better teams make offers. Everything saves in PlayerPrefs.
    /// </summary>
    public static class TeamCareer
    {
        public static readonly TeamDef[] Teams =
        {
            new TeamDef { name = "Backmarker Racing", color = new Color(0.9f, 0.55f, 0.15f), baseRating = 0.30f, baseSalary = 250 },
            new TeamDef { name = "Meridian GP", color = new Color(0.24f, 0.55f, 1f), baseRating = 0.55f, baseSalary = 600 },
            new TeamDef { name = "Titan Works", color = new Color(0.82f, 0.84f, 0.9f), baseRating = 0.80f, baseSalary = 1200 },
        };
        public static readonly string[] ObjectiveText =
        {
            "Finish the season in the top 3 of the championship",
            "Beat your teammate in the championship",
            "Win at least one race this season"
        };

        public static int team, seasonsLeft = 2, salary = 250, objective, objectiveReward = 400, season = 1, rapport = 50;
        public static int rdPoints;
        public static readonly int[,] rdLevel = new int[3, 3];       // [team, branch]: 0 aero, 1 engine, 2 tyres, each 0..3
        public static readonly float[] dev = new float[3];             // how far each team has developed its car this career
        public static int seasonWins, seasonPodiums;

        public class Offer { public int team, salary, seasons, objective; }
        public static readonly List<Offer> offers = new List<Offer>();

        public static readonly string[] BranchNames = { "Aerodynamics", "Engine", "Tyre management" };
        public static readonly string[] BranchEffect = { "+1.2% top speed per level", "+2% acceleration per level", "-4% tyre wear per level" };
        public static readonly int[] RdCost = { 4, 7, 11 };

        public static float Rating(int t) { return Mathf.Clamp01(Teams[t].baseRating + dev[t]); }
        public static float PlayerRating { get { return Rating(team); } }
        /// <summary>-0.5 .. +0.5: how good your team's car is compared with an average one.</summary>
        public static float Perf { get { return PlayerRating - 0.5f; } }
        public static int RdLevel(int branch) { return rdLevel[team, branch]; }

        /// <summary>Which team an AI driver is on this race. AI 0 is your teammate; the other four make up the two rival teams.</summary>
        public static int TeamOfAi(int id)
        {
            if (id == 0) return team;
            var others = new List<int>();
            for (int t = 0; t < Teams.Length; t++) if (t != team) others.Add(t);
            return id <= 2 ? others[0] : others[1];
        }

        public static void Load()
        {
            team = Mathf.Clamp(PlayerPrefs.GetInt("ad_t_team", 0), 0, 2);
            seasonsLeft = PlayerPrefs.GetInt("ad_t_left", 2); salary = PlayerPrefs.GetInt("ad_t_sal", 250);
            objective = Mathf.Clamp(PlayerPrefs.GetInt("ad_t_obj", 0), 0, 2); objectiveReward = PlayerPrefs.GetInt("ad_t_rew", 400);
            season = PlayerPrefs.GetInt("ad_t_season", 1); rapport = PlayerPrefs.GetInt("ad_t_rap", 50);
            rdPoints = PlayerPrefs.GetInt("ad_t_rd", 0);
            for (int t = 0; t < 3; t++)
            {
                dev[t] = PlayerPrefs.GetFloat("ad_t_dev" + t, 0f);
                for (int b = 0; b < 3; b++) rdLevel[t, b] = Mathf.Clamp(PlayerPrefs.GetInt("ad_t_rd" + t + "_" + b, 0), 0, 3);
            }
            seasonWins = PlayerPrefs.GetInt("ad_t_wins", 0); seasonPodiums = PlayerPrefs.GetInt("ad_t_pods", 0);
        }

        public static void Save()
        {
            PlayerPrefs.SetInt("ad_t_team", team); PlayerPrefs.SetInt("ad_t_left", seasonsLeft); PlayerPrefs.SetInt("ad_t_sal", salary);
            PlayerPrefs.SetInt("ad_t_obj", objective); PlayerPrefs.SetInt("ad_t_rew", objectiveReward);
            PlayerPrefs.SetInt("ad_t_season", season); PlayerPrefs.SetInt("ad_t_rap", rapport); PlayerPrefs.SetInt("ad_t_rd", rdPoints);
            for (int t = 0; t < 3; t++)
            {
                PlayerPrefs.SetFloat("ad_t_dev" + t, dev[t]);
                for (int b = 0; b < 3; b++) PlayerPrefs.SetInt("ad_t_rd" + t + "_" + b, rdLevel[t, b]);
            }
            PlayerPrefs.SetInt("ad_t_wins", seasonWins); PlayerPrefs.SetInt("ad_t_pods", seasonPodiums);
            PlayerPrefs.Save();
        }

        public static void BeginSeason()
        {
            seasonWins = 0; seasonPodiums = 0;
            offers.Clear();
            Save();
        }

        /// <summary>A career-season race is over: salary, R&amp;D points and season tallies.</summary>
        public static int OnCareerRace(int rank)
        {
            if (rank == 1) seasonWins++;
            if (rank <= 3) seasonPodiums++;
            Save();
            return salary;
        }

        public static void AddRd(int points) { rdPoints += Mathf.Max(0, points); Save(); }

        public static bool CanDevelop(int branch) { return rdLevel[team, branch] < 3 && rdPoints >= RdCost[rdLevel[team, branch]]; }

        public static void Develop(int branch)
        {
            if (!CanDevelop(branch)) return;
            rdPoints -= RdCost[rdLevel[team, branch]];
            rdLevel[team, branch]++;
            Save();
        }

        /// <summary>The career season has ended. Returns lines describing what happened (bonus, offers).</summary>
        public static List<string> EndSeason(int playerRank, int playerPts, int matePts)
        {
            var notes = new List<string>();
            bool met = objective == 0 ? playerRank <= 3 : objective == 1 ? playerPts > matePts : seasonWins > 0;
            if (met)
            {
                GameSave.money += objectiveReward; rapport = Mathf.Min(100, rapport + 10);
                notes.Add("Contract objective met: " + ObjectiveText[objective] + ".  Bonus $" + objectiveReward);
            }
            else
            {
                rapport = Mathf.Max(0, rapport - 10);
                notes.Add("Contract objective missed: " + ObjectiveText[objective] + ".");
            }
            seasonsLeft--;
            int oldEra = Regs.EraIndex;
            season++;
            if (Regs.EraIndex != oldEra) notes.Add("NEW REGULATIONS: " + Regs.Current.name + ". " + Regs.Current.text + " Your setups need re-tuning.");
            // the rival teams keep developing their cars: the good ones faster
            for (int t = 0; t < 3; t++) dev[t] += 0.01f + 0.04f * Teams[t].baseRating;
            GameSave.partsUsed = 0;                                 // a new season: engine and gearbox allowance resets

            offers.Clear();
            if (seasonsLeft <= 0 || playerRank <= 2)
            {
                for (int t = 0; t < 3; t++)
                {
                    if (t == team) continue;
                    // good results attract the better teams; poor results only the weaker ones
                    bool better = Rating(t) > PlayerRating;
                    if (better && playerRank > 2) continue;
                    offers.Add(new Offer
                    {
                        team = t,
                        salary = Mathf.RoundToInt(Teams[t].baseSalary * (0.8f + 0.08f * (6 - playerRank))),
                        seasons = 2,
                        objective = (t + season) % 3
                    });
                }
            }
            if (seasonsLeft <= 0 && offers.Count == 0)
            {
                seasonsLeft = 1; salary = Mathf.Max(150, Mathf.RoundToInt(salary * 0.9f));
                notes.Add("Your contract was renewed for one more season.");
            }
            else if (offers.Count > 0) notes.Add("You have contract offers - see the TEAM tab!");
            seasonWins = 0; seasonPodiums = 0;
            Save();
            return notes;
        }

        public static void Accept(Offer o)
        {
            team = o.team; salary = o.salary; seasonsLeft = o.seasons; objective = o.objective;
            objectiveReward = 300 + 150 * team;
            rapport = 50;
            offers.Clear();
            Save();
        }

        public static void Decline()
        {
            offers.Clear();
            if (seasonsLeft <= 0) { seasonsLeft = 1; salary = Mathf.Max(150, Mathf.RoundToInt(salary * 0.9f)); }
            Save();
        }
    }
}
