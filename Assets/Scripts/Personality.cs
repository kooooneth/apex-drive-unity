namespace ApexDrive
{
    /// <summary>Each AI rival has a character that changes how they race, so the field isn't five clones.</summary>
    public enum Personality { Balanced, Aggressive, Cautious, TireSaver, WetSpecialist }

    public static class Personalities
    {
        public static readonly string[] Names = { "Balanced", "Aggressive", "Cautious", "Tire Saver", "Wet Specialist" };
        public static readonly string[] Drivers = { "R. Vance", "K. Okoro", "M. Duarte", "S. Lindqvist", "T. Hashimoto" };
        public static readonly string[] Blurbs =
        {
            "Even pace, few surprises.",
            "Quick and hungry - takes risks, defends hard, chews tyres.",
            "Careful and clean - slower, rarely makes mistakes.",
            "Looks after the rubber - stops later, never very fast.",
            "Magic in the rain, a bit off the pace when dry."
        };

        public static float Pace(Personality p, bool raining)
        {
            switch (p)
            {
                case Personality.Aggressive: return raining ? 1.03f : 1.04f;
                case Personality.Cautious: return 0.96f;
                case Personality.TireSaver: return 0.98f;
                case Personality.WetSpecialist: return raining ? 1.07f : 0.98f;
                default: return 1f;
            }
        }

        public static float MistakeMult(Personality p, bool raining)
        {
            switch (p)
            {
                case Personality.Aggressive: return 1.5f;
                case Personality.Cautious: return 0.5f;
                case Personality.WetSpecialist: return raining ? 0.6f : 1.1f;
                default: return 1f;
            }
        }

        public static float WearMult(Personality p)
        {
            switch (p)
            {
                case Personality.Aggressive: return 1.25f;
                case Personality.TireSaver: return 0.65f;
                default: return 1f;
            }
        }
    }
}
