using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// Regulation eras: every two seasons of the career the rules change. The cars' balance is reset, so the best setup values move
    /// (and what the wind tunnel told you is forgotten) and a few things change in how the cars behave. Era 0 is the standard rule book.
    /// </summary>
    public static class Regs
    {
        public class Era { public string name, text; public float aeroSens = 1f, ersMul = 1f, fuelMul = 1f, tyreWear = 1f, tyreGrip = 1f, power = 1f; }

        static readonly Era[] eras =
        {
            new Era { name = "Standard rules", text = "The usual rule book. No changes this season." },
            new Era { name = "Ground-effect cars", text = "Floors make the downforce: the ride height matters much more, and bottoming out or porpoising costs more.", aeroSens = 1.6f },
            new Era { name = "Hybrid boost", text = "A bigger electric motor: the ERS gives more power on the straights, so battery management is worth more.", ersMul = 1.5f },
            new Era { name = "Heavy cars", text = "Cars start with more fuel and weigh more: the fuel load costs more lap time and the early laps are slower.", fuelMul = 1.8f },
            new Era { name = "Spec tyres", text = "A new harder-wearing tyre: more grip early but the tyres wear 20 percent faster.", tyreWear = 1.2f, tyreGrip = 1.04f },
            new Era { name = "Engine freeze", text = "Engines are frozen to save cost: a little less power, but cars are more reliable.", power = 0.97f },
        };

        /// <summary>The era number (it changes every two seasons of the career).</summary>
        public static int EraIndex { get { return Mathf.Max(0, (TeamCareer.season - 1) / 2); } }
        public static Era Current { get { return eras[EraIndex % eras.Length]; } }
        public static Era Of(int seasonNumber) { return eras[Mathf.Max(0, (seasonNumber - 1) / 2) % eras.Length]; }

        public static float AeroSens { get { return Current.aeroSens; } }
        public static float ErsMul { get { return Current.ersMul; } }
        public static float FuelMul { get { return Current.fuelMul; } }
        public static float TyreWear { get { return Current.tyreWear; } }
        public static float TyreGrip { get { return Current.tyreGrip; } }
        public static float Power { get { return Current.power; } }

        /// <summary>A short heading for the menus, such as "Season 3 - Ground-effect cars".</summary>
        public static string Heading { get { return "Season " + TeamCareer.season + "  -  " + Current.name; } }

        /// <summary>Years until the next change, for the Team tab.</summary>
        public static int SeasonsUntilChange { get { return 2 - ((TeamCareer.season - 1) % 2); } }
    }
}
