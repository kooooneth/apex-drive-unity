using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// A different car for every circuit, each with its own saved gear ratio and setup. Switching circuit (in the Race tab, or when a
    /// season round starts) puts that circuit's car in the garage: the sliders show its values, you can tune it, and your changes are
    /// remembered for next time. Every car starts from its own base setup.
    /// </summary>
    public static class CarSetups
    {
        public class Car
        {
            public string name, blurb;
            public float wing, gear, brake, rideHeight, camber, diff, pressure, duct = 50f;
        }

        static readonly Dictionary<string, Car> bases = new Dictionary<string, Car>
        {
            { "harborline", new Car { name = "Harbor HB-1", blurb = "Short gearing for the tight corners: strong acceleration, lots of downforce, a lower top speed.",
                                      wing = 72f, gear = 25f, brake = 62f, rideHeight = 55f, camber = 58f, diff = 45f, pressure = 48f, duct = 55f } },
            { "desert", new Car { name = "Desert DS-2", blurb = "Long gearing and a low wing for the fast straights: high top speed, less grip in the corners.",
                                  wing = 28f, gear = 78f, brake = 58f, rideHeight = 48f, camber = 42f, diff = 58f, pressure = 55f, duct = 30f } },
            { "sprint", new Car { name = "Sprint SP-3", blurb = "A balanced short-circuit car: quick out of the corners with medium gearing.",
                                  wing = 50f, gear = 38f, brake = 60f, rideHeight = 50f, camber = 50f, diff = 52f, pressure = 50f } },
            { "marina", new Car { name = "Marina MR-4", blurb = "A stiff, responsive street car: short gearing, high downforce and strong brakes for the walls and hairpins.",
                                  wing = 78f, gear = 30f, brake = 64f, rideHeight = 54f, camber = 60f, diff = 42f, pressure = 46f, duct = 72f } },
            { "alpine", new Car { name = "Alpine AL-5", blurb = "A hill-climb car: raised ride height for the bumps, medium-short gearing and good traction.",
                                  wing = 64f, gear = 40f, brake = 61f, rideHeight = 60f, camber = 52f, diff = 58f, pressure = 52f, duct = 66f } },
            { "ring", new Car { name = "Ring GR-6", blurb = "A long-distance speed car: the longest gearing and the lowest wing for the fast sweepers.",
                                wing = 18f, gear = 86f, brake = 57f, rideHeight = 46f, camber = 40f, diff = 60f, pressure = 56f, duct = 36f } },
        };

        static readonly Car neutral = new Car { name = "Race car", blurb = "A balanced car.", wing = 50f, gear = 50f, brake = 60f, rideHeight = 50f, camber = 50f, diff = 50f, pressure = 50f };

        static string current;            // the car (named after its circuit) whose values the garage (GameSave) is showing
        public static readonly string[] Ids = { "harborline", "desert", "sprint", "marina", "alpine", "ring" };
        public static string Current { get { return current ?? Ids[0]; } }

        public static Car Base(string id) { Car c; return bases.TryGetValue(id, out c) ? c : neutral; }
        public static string NameOf(string id) { return Base(id).name; }
        public static string BlurbOf(string id) { return Base(id).blurb; }

        static string K(string id, string f) { return "ad_car_" + id + "_" + f; }

        static void Apply(string id)
        {
            var b = Base(id);
            GameSave.wing = PlayerPrefs.GetFloat(K(id, "wing"), b.wing);
            GameSave.gear = PlayerPrefs.GetFloat(K(id, "gear"), b.gear);
            GameSave.brakeBias = PlayerPrefs.GetFloat(K(id, "brake"), b.brake);
            GameSave.rideHeight = PlayerPrefs.GetFloat(K(id, "rh"), b.rideHeight);
            GameSave.camber = PlayerPrefs.GetFloat(K(id, "cam"), b.camber);
            GameSave.diff = PlayerPrefs.GetFloat(K(id, "diff"), b.diff);
            GameSave.pressure = PlayerPrefs.GetFloat(K(id, "prs"), b.pressure);
            GameSave.brakeDuct = PlayerPrefs.GetFloat(K(id, "duct"), b.duct);
            // the livery and tyres: a car without a record keeps the look that is set now
            GameSave.bodyColor = PlayerPrefs.GetInt(K(id, "body"), GameSave.bodyColor);
            GameSave.accentColor = PlayerPrefs.GetInt(K(id, "accent"), GameSave.accentColor);
            GameSave.pattern = PlayerPrefs.GetInt(K(id, "pattern"), GameSave.pattern);
            GameSave.compound = (Compound)PlayerPrefs.GetInt(K(id, "tyre"), (int)GameSave.compound);
        }

        static void Store(string id)
        {
            PlayerPrefs.SetFloat(K(id, "wing"), GameSave.wing);
            PlayerPrefs.SetFloat(K(id, "gear"), GameSave.gear);
            PlayerPrefs.SetFloat(K(id, "brake"), GameSave.brakeBias);
            PlayerPrefs.SetFloat(K(id, "rh"), GameSave.rideHeight);
            PlayerPrefs.SetFloat(K(id, "cam"), GameSave.camber);
            PlayerPrefs.SetFloat(K(id, "diff"), GameSave.diff);
            PlayerPrefs.SetFloat(K(id, "prs"), GameSave.pressure);
            PlayerPrefs.SetFloat(K(id, "duct"), GameSave.brakeDuct);
            PlayerPrefs.SetInt(K(id, "body"), GameSave.bodyColor);
            PlayerPrefs.SetInt(K(id, "accent"), GameSave.accentColor);
            PlayerPrefs.SetInt(K(id, "pattern"), GameSave.pattern);
            PlayerPrefs.SetInt(K(id, "tyre"), (int)GameSave.compound);
        }

        /// <summary>Puts the car of this circuit in the garage, remembering the tuning of the car that was there.</summary>
        public static void Switch(string id)
        {
            if (current == id) return;
            if (current != null) Store(current);
            Apply(id);
            current = id;
        }

        /// <summary>One line with a car's main settings (the live values for the car in the garage, the saved ones for the others).</summary>
        public static string Summary(string id)
        {
            var b = Base(id);
            bool live = id == current;
            float gear = live ? GameSave.gear : PlayerPrefs.GetFloat(K(id, "gear"), b.gear);
            float wing = live ? GameSave.wing : PlayerPrefs.GetFloat(K(id, "wing"), b.wing);
            float brake = live ? GameSave.brakeBias : PlayerPrefs.GetFloat(K(id, "brake"), b.brake);
            int tyre = live ? (int)GameSave.compound : PlayerPrefs.GetInt(K(id, "tyre"), (int)GameSave.compound);
            return "gear " + Mathf.RoundToInt(gear) + "   wing " + Mathf.RoundToInt(wing) + "   brake " + Mathf.RoundToInt(brake) + "   " + Tuning.CompoundNames[Mathf.Clamp(tyre, 0, Tuning.CompoundNames.Length - 1)];
        }

        /// <summary>The setup of a car as the lap simulation needs it (live values for the car in the garage, the saved ones for the others).</summary>
        public static SetupLab.S SetupOf(string id)
        {
            var b = Base(id);
            if (id == current)
                return new SetupLab.S { ride = GameSave.rideHeight, camber = GameSave.camber, diff = GameSave.diff, pressure = GameSave.pressure, wing = GameSave.wing, gear = GameSave.gear, brake = GameSave.brakeBias };
            return new SetupLab.S
            {
                ride = PlayerPrefs.GetFloat(K(id, "rh"), b.rideHeight), camber = PlayerPrefs.GetFloat(K(id, "cam"), b.camber), diff = PlayerPrefs.GetFloat(K(id, "diff"), b.diff),
                pressure = PlayerPrefs.GetFloat(K(id, "prs"), b.pressure), wing = PlayerPrefs.GetFloat(K(id, "wing"), b.wing), gear = PlayerPrefs.GetFloat(K(id, "gear"), b.gear),
                brake = PlayerPrefs.GetFloat(K(id, "brake"), b.brake)
            };
        }

        /// <summary>Remember the tuning of the car that is in the garage now.</summary>
        public static void StoreCurrent() { if (current != null) Store(current); }

        /// <summary>Back to the car's own base setup.</summary>
        public static void ResetToBase(string id)
        {
            var b = Base(id);
            GameSave.wing = b.wing; GameSave.gear = b.gear; GameSave.brakeBias = b.brake;
            GameSave.rideHeight = b.rideHeight; GameSave.camber = b.camber; GameSave.diff = b.diff; GameSave.pressure = b.pressure; GameSave.brakeDuct = b.duct;
            Store(id);
        }
    }
}
