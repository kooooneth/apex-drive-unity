using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The wind for a race: a mean speed and direction (from the weather plan) plus gusts that come and go. A headwind adds drag (lower top
    /// speed) and downforce (more grip), a tailwind does the opposite, and a crosswind shoves the car sideways. Works in web space, like the track.
    /// </summary>
    public class WindField
    {
        public float mean;                 // m/s
        public float dir;                  // radians: the direction the wind blows TOWARDS, as a heading (sin, cos) in web space
        public float gustiness = 0.5f;     // 0..1
        float seed;

        public static WindField Make(float meanSpeed, float direction, float gusty, float seed)
        {
            return new WindField { mean = meanSpeed, dir = direction, gustiness = gusty, seed = seed };
        }

        public static readonly string[] Compass = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

        /// <summary>Gust multiplier: slow swells plus quicker bursts.</summary>
        public float GustAt(float t)
        {
            float slow = Mathf.PerlinNoise(seed, t * 0.08f);
            float fast = Mathf.PerlinNoise(seed + 9.1f, t * 0.45f);
            float g = (slow - 0.5f) * 1.1f + (fast - 0.5f) * 0.9f;
            return Mathf.Max(0.15f, 1f + g * gustiness * 1.6f);
        }

        public float SpeedAt(float t) { return mean * GustAt(t); }

        /// <summary>The wind as a vector (blowing towards) in web space.</summary>
        public Vector2 VectorAt(float t) { float s = SpeedAt(t); return new Vector2(Mathf.Sin(dir), Mathf.Cos(dir)) * s; }

        /// <summary>Wind relative to a car pointing along fwd (unit vector, web space): head is positive for a headwind, cross positive pushes the car to its right.</summary>
        public void Relative(Vector2 fwd, float t, out float head, out float cross)
        {
            Vector2 w = VectorAt(t);
            Vector2 right = new Vector2(fwd.y, -fwd.x);                     // for a heading h: fwd = (sin h, cos h), right = (cos h, -sin h)
            head = -Vector2.Dot(w, fwd);
            cross = Vector2.Dot(w, right);
        }

        /// <summary>Drag and downforce share the same airspeed: top speed falls with a headwind, grip in corners rises with it.</summary>
        public static float TopSpeedMul(float head) { return 1f - Mathf.Clamp(0.0042f * head, -0.06f, 0.07f); }
        public static float GripMul(float head) { return 1f + Mathf.Clamp(0.0035f * head, -0.05f, 0.05f); }

        public string Describe()
        {
            // web space has +y pointing south, so a heading of dir blows towards the compass bearing 180 - dir; the name is where the wind comes FROM
            float fromBearing = Mathf.Repeat(180f - dir * Mathf.Rad2Deg + 180f, 360f);
            string from = Compass[Mathf.RoundToInt(fromBearing / 45f) % 8];
            string strength = mean < 2f ? "Calm" : mean < 5f ? "Light wind" : mean < 8f ? "Breezy" : mean < 11f ? "Windy" : "Very windy";
            return strength + " " + Mathf.RoundToInt(mean) + " m/s from the " + from + (gustiness > 0.6f && mean >= 4f ? ", gusting" : "") + ".";
        }
    }
}
