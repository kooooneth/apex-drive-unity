using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// Where the sun really is: each circuit has a latitude, each race a day of the year and a local start time, and the standard solar
    /// formulas give the sun's height and compass direction. During a race the sun creeps across the sky, so shadows lengthen and turn.
    /// </summary>
    public static class SunPath
    {
        public static float Latitude(string trackId)
        {
            switch (trackId)
            {
                case "desert": return 25f;
                case "sprint": return 40f;
                case "marina": return 43f;
                case "alpine": return 46f;
                case "ring": return 50f;
                default: return 51f;                    // harborline
            }
        }

        /// <param name="latitude">degrees north</param>
        /// <param name="dayOfYear">1..365</param>
        /// <param name="hour">local solar time, 12 = noon</param>
        /// <param name="elevation">degrees above the horizon</param>
        /// <param name="azimuth">compass bearing of the sun, 0 = north, 90 = east</param>
        public static void Compute(float latitude, float dayOfYear, float hour, out float elevation, out float azimuth)
        {
            float lat = latitude * Mathf.Deg2Rad;
            float decl = 23.44f * Mathf.Deg2Rad * Mathf.Sin(2f * Mathf.PI * (284f + dayOfYear) / 365f);
            float H = (hour - 12f) * 15f * Mathf.Deg2Rad;                         // hour angle: negative in the morning
            float sinE = Mathf.Sin(lat) * Mathf.Sin(decl) + Mathf.Cos(lat) * Mathf.Cos(decl) * Mathf.Cos(H);
            float e = Mathf.Asin(Mathf.Clamp(sinE, -1f, 1f));
            float cosE = Mathf.Max(0.0001f, Mathf.Cos(e));
            float sinA = -Mathf.Cos(decl) * Mathf.Sin(H) / cosE;
            float cosA = (Mathf.Sin(decl) - Mathf.Sin(e) * Mathf.Sin(lat)) / (cosE * Mathf.Max(0.0001f, Mathf.Cos(lat)));
            float a = Mathf.Atan2(sinA, Mathf.Clamp(cosA, -1f, 1f)) * Mathf.Rad2Deg;       // south-based ... converted below
            elevation = e * Mathf.Rad2Deg;
            // atan2(sin, cos) here is measured from north because cosA uses the north-pointing formula
            azimuth = Mathf.Repeat(a, 360f);
            if (H > 0f && azimuth < 180f) azimuth = 360f - azimuth;                  // afternoon sun is always in the west half of the sky
            if (H < 0f && azimuth > 180f) azimuth = 360f - azimuth;                  // morning sun in the east half
        }

        public static string Compass(float bearing)
        {
            string[] n = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            return n[Mathf.RoundToInt(Mathf.Repeat(bearing, 360f) / 45f) % 8];
        }

        /// <summary>The clock time as text, e.g. 16:20.</summary>
        public static string Clock(float hour)
        {
            int h = Mathf.FloorToInt(hour), m = Mathf.FloorToInt((hour - h) * 60f);
            return h.ToString("00") + ":" + m.ToString("00");
        }
    }
}
