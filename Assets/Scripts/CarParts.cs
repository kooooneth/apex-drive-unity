using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The parts of the car that can be knocked off: the mirrors, the front-wing end plates and the bargeboards (each left and right).
    /// A part that breaks disappears from the car, flies off as debris and costs the car something (see PlayerCar.BreakPart).
    /// </summary>
    public class CarParts
    {
        readonly Dictionary<string, List<GameObject>> map = new Dictionary<string, List<GameObject>>();
        readonly HashSet<string> lost = new HashSet<string>();

        public static readonly string[] Keys = { "mirrorL", "mirrorR", "endplateL", "endplateR", "bargeL", "bargeR" };

        public CarParts(GameObject car)
        {
            foreach (var t in car.GetComponentsInChildren<Transform>())
            {
                string n = t.name;
                foreach (var k in Keys)
                    if (n == k)
                    {
                        List<GameObject> l;
                        if (!map.TryGetValue(k, out l)) { l = new List<GameObject>(); map[k] = l; }
                        l.Add(t.gameObject);
                    }
            }
        }

        public bool Has(string key) { return map.ContainsKey(key); }

        public List<string> LostKeys() { return new List<string>(lost); }

        /// <summary>Puts the car back to a state with exactly these parts missing (no debris: used by a rewind).</summary>
        public void SetLost(List<string> keys)
        {
            RestoreAll();
            foreach (var k in keys)
            {
                List<GameObject> l;
                if (!map.TryGetValue(k, out l)) continue;
                lost.Add(k);
                foreach (var g in l) if (g != null) g.SetActive(false);
            }
        }
        public bool IsLost(string key) { return lost.Contains(key); }

        /// <summary>How many parts of a kind (such as "barge") are gone.</summary>
        public int LostCount(string prefix) { int n = 0; foreach (var k in lost) if (k.StartsWith(prefix)) n++; return n; }
        public int TotalLost { get { return lost.Count; } }

        /// <summary>Removes the part from the car. Returns false if it was not there or already gone.</summary>
        public bool Break(string key, RaceManager rm, Vector3 worldPos, Vector3 outward)
        {
            List<GameObject> l;
            if (lost.Contains(key) || !map.TryGetValue(key, out l)) return false;
            lost.Add(key);
            foreach (var g in l) if (g != null) g.SetActive(false);
            if (rm != null) rm.SpawnWingDebris(worldPos, outward, 1);
            return true;
        }

        public void RestoreAll()
        {
            foreach (var kv in map) foreach (var g in kv.Value) if (g != null) g.SetActive(true);
            lost.Clear();
        }

        public static string Describe(string key)
        {
            return key == "mirrorL" ? "left mirror" : key == "mirrorR" ? "right mirror"
                 : key == "endplateL" ? "left front-wing end plate" : key == "endplateR" ? "right front-wing end plate"
                 : key == "bargeL" ? "left bargeboard" : "right bargeboard";
        }
    }
}
