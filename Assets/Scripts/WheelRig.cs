using UnityEngine;

namespace ApexDrive
{
    /// <summary>Spins every wheel with the car's speed and steers the front pair (visible in chase view).</summary>
    public class WheelRig : MonoBehaviour
    {
        public Transform[] spins;
        public Transform[] frontPivots;
        public Material sidewallMat;
        float angle;
        int lost = -1;

        /// <summary>A wheel that has been torn off stays hidden (-1 puts it back).</summary>
        public void LoseWheel(int i) { lost = i; SetWheelsVisible(true); }

        /// <summary>Hide or show all four wheels (they are off the car mid pit stop).</summary>
        public void SetWheelsVisible(bool v)
        {
            if (spins == null) return;
            for (int i = 0; i < spins.Length; i++) spins[i].localScale = v && i != lost ? Vector3.one : Vector3.one * 0.001f;
        }

        public void Animate(float speed, float steerRad, float dt)
        {
            angle = Mathf.Repeat(angle + speed / 0.38f * dt * Mathf.Rad2Deg, 360f);
            if (spins != null) foreach (var s in spins) s.localRotation = Quaternion.Euler(angle, 0f, 0f);
            if (frontPivots != null) foreach (var p in frontPivots) p.localRotation = Quaternion.Euler(0f, -steerRad * Mathf.Rad2Deg, 0f);
        }
    }
}
