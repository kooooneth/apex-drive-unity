using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The browser build runs on whatever graphics the visitor has, so it watches its own frame rate and steps the graphics down until the
    /// game is smooth: soft shadows, then hard shadows over a shorter distance, then smaller shadows.
    /// The page can also set a level by hand: unityInstance.SendMessage("ApexDrive", "SetQuality", "2").
    /// </summary>
    public class WebTuning : MonoBehaviour
    {
        public static int Level;
        float fpsAvg = 60f, slowFor, fastFor, lastChange;
        int floorLevel;                                           // the lowest level worth trying again: set when stepping up made it slow again
        bool steppedUp, manual;                                   // manual: the page chose a level, so the automatic tuning stays out of the way
        const int MAX_LEVEL = 2;

        void Awake() { gameObject.name = "ApexDrive"; }

        void Update()
        {
            if (manual) return;
            if (!Application.isFocused) { slowFor = fastFor = 0f; return; }       // a hidden tab is slow on purpose
            float dt = Mathf.Max(0.0005f, Time.unscaledDeltaTime);
            if (dt > 4f) return;                                      // a long loading hitch is not the frame rate
            fpsAvg += (1f / dt - fpsAvg) * Mathf.Min(1f, dt * 1.5f);
            float since = Time.realtimeSinceStartup - lastChange;
            slowFor = fpsAvg < 30f && since > 4f ? slowFor + dt : 0f;
            fastFor = fpsAvg > 56f && since > 20f ? fastFor + dt : 0f;
            if (slowFor > 3f && Level < MAX_LEVEL)
            {
                if (steppedUp && since < 40f) floorLevel = Level + 1;       // going back up did not hold: stay here
                steppedUp = false;
                SetLevel(Level + 1); slowFor = 0f;
            }
            else if (fastFor > 12f && Level > floorLevel) { steppedUp = true; SetLevel(Level - 1); fastFor = 0f; }
        }

        /// <summary>Called from the page (SendMessage) to test or force a level.</summary>
        public void SetQuality(string level) { int l; if (int.TryParse(level, out l)) { manual = true; SetLevel(Mathf.Clamp(l, 0, MAX_LEVEL)); } }

        public void SetLevel(int level)
        {
            Level = level; lastChange = Time.realtimeSinceStartup; fpsAvg = 60f;
            Apply();
            Debug.Log("[WebTuning] graphics level " + level);
        }

        /// <summary>Applied again whenever the race manager sets up the sun, so a level survives a new race.</summary>
        public static void Apply()
        {
            var sun = FindSun();
            QualitySettings.shadowCascades = Level >= 1 ? 1 : 2;
            QualitySettings.shadowDistance = Level >= 2 ? 70f : Level >= 1 ? 140f : 220f;
            QualitySettings.shadowResolution = Level >= 2 ? ShadowResolution.Low : Level >= 1 ? ShadowResolution.Medium : ShadowResolution.High;
            if (sun != null) sun.shadows = Level >= 1 ? LightShadows.Hard : LightShadows.Soft;       // (switching shadows off altogether loses the road in the browser build)
            QualitySettings.antiAliasing = 0;
        }

        static Light FindSun()
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) return l;
            return null;
        }

    }
}
