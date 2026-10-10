using UnityEngine;

namespace ApexDrive
{
    public class HumanLook
    {
        public Material suit, boots, helmet, visor, glove, trim;
        public float height = 1f, width = 1f;
    }

    /// <summary>
    /// A person with real proportions and joints (about 1.8 m tall in a race suit and helmet): hips, a chest with shoulders, a neck and
    /// a helmet with a visor, upper and lower arms with gloved hands, thighs, shins and boots. Pose() bends the knees and hips (crouching,
    /// running), leans the body and raises and bends the arms, and keeps the feet on the ground while doing it.
    /// The figure faces +z; its origin is on the ground between the feet.
    /// </summary>
    public class Human
    {
        const float THIGH = 0.46f, SHIN = 0.46f, UARM = 0.30f, FARM = 0.27f, HIPDROP = 0.05f, ANKLE = 0.07f;

        public readonly Transform root;
        public Transform spine, wristL, wristR, head;
        Transform pelvis, shL, shR, elL, elR, hipL, hipR, knL, knR;

        static Transform Child(Transform parent, string name, Vector3 pos)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            return g.transform;
        }

        public Human(Transform parent, HumanLook look, string name)
        {
            root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localScale = new Vector3(look.width, look.height, look.width);

            pelvis = Child(root, "pelvis", new Vector3(0f, 1.04f, 0f));
            Prim.Sphere(pelvis, Vector3.zero, new Vector3(0.35f, 0.22f, 0.26f), look.suit, "hips");

            // ---- torso, neck and head
            spine = Child(pelvis, "spine", new Vector3(0f, 0.06f, 0f));
            Prim.Sphere(spine, new Vector3(0f, 0.12f, 0.01f), new Vector3(0.34f, 0.30f, 0.24f), look.suit, "belly");
            Prim.Sphere(spine, new Vector3(0f, 0.30f, 0f), new Vector3(0.39f, 0.46f, 0.26f), look.suit, "chest");
            Prim.Sphere(spine, new Vector3(0f, 0.12f, 0f), new Vector3(0.352f, 0.05f, 0.252f), look.trim, "belt");
            Prim.Sphere(spine, new Vector3(0f, 0.37f, 0f), new Vector3(0.40f, 0.06f, 0.268f), look.trim, "chestBand");
            Prim.Sphere(spine, new Vector3(0f, 0.50f, 0f), new Vector3(0.47f, 0.15f, 0.21f), look.suit, "shoulders");
            Prim.Capsule(spine, new Vector3(0f, 0.60f, 0.01f), new Vector3(0.11f, 0.055f, 0.11f), Quaternion.identity, look.suit, "neck");
            head = Child(spine, "head", new Vector3(0f, 0.68f, 0.02f));
            Prim.Sphere(head, new Vector3(0f, 0.07f, -0.005f), new Vector3(0.30f, 0.32f, 0.34f), look.helmet, "helmet");
            Prim.Sphere(head, new Vector3(0f, -0.035f, 0.09f), new Vector3(0.21f, 0.11f, 0.16f), look.helmet, "chinGuard");
            Prim.Sphere(head, new Vector3(0f, 0.065f, 0.105f), new Vector3(0.255f, 0.125f, 0.15f), look.visor, "visor");
            Prim.Sphere(head, new Vector3(0f, 0.20f, -0.02f), new Vector3(0.12f, 0.025f, 0.22f), look.trim, "helmetStripe");

            // ---- arms: shoulder, upper arm, elbow, forearm, wrist and a gloved hand
            for (int side = -1; side <= 1; side += 2)
            {
                var sh = Child(spine, side < 0 ? "shoulderL" : "shoulderR", new Vector3(side * 0.215f, 0.50f, 0f));
                Prim.Capsule(sh, new Vector3(0f, -UARM * 0.5f, 0f), new Vector3(0.105f, UARM * 0.5f + 0.025f, 0.105f), Quaternion.identity, look.suit, "upperArm");
                var el = Child(sh, "elbow", new Vector3(0f, -UARM, 0f));
                Prim.Sphere(el, Vector3.zero, Vector3.one * 0.095f, look.suit, "elbowJoint");
                Prim.Capsule(el, new Vector3(0f, -FARM * 0.5f, 0f), new Vector3(0.088f, FARM * 0.5f + 0.02f, 0.088f), Quaternion.identity, look.suit, "forearm");
                Prim.Capsule(el, new Vector3(0f, -FARM * 0.5f, 0f), new Vector3(0.095f, 0.03f, 0.095f), Quaternion.identity, look.trim, "cuffBand");
                var wr = Child(el, "wrist", new Vector3(0f, -FARM, 0f));
                Prim.Sphere(wr, new Vector3(0f, -0.055f, 0f), new Vector3(0.095f, 0.115f, 0.052f), look.glove, "palm");
                Prim.Capsule(wr, new Vector3(0f, -0.125f, 0.004f), new Vector3(0.085f, 0.045f, 0.040f), Quaternion.identity, look.glove, "fingers");
                Prim.Capsule(wr, new Vector3(-side * 0.052f, -0.075f, 0.02f), new Vector3(0.036f, 0.04f, 0.036f), Quaternion.Euler(0f, 0f, side * 25f), look.glove, "thumb");
                if (side < 0) { shL = sh; elL = el; wristL = wr; } else { shR = sh; elR = el; wristR = wr; }
            }

            // ---- legs: hip, thigh, knee (with a pad), shin, ankle and a boot
            for (int side = -1; side <= 1; side += 2)
            {
                var hip = Child(pelvis, side < 0 ? "hipL" : "hipR", new Vector3(side * 0.10f, -HIPDROP, 0f));
                Prim.Capsule(hip, new Vector3(0f, -THIGH * 0.5f, 0f), new Vector3(0.165f, THIGH * 0.5f + 0.03f, 0.165f), Quaternion.identity, look.suit, "thigh");
                var kn = Child(hip, "knee", new Vector3(0f, -THIGH, 0f));
                Prim.Sphere(kn, new Vector3(0f, 0f, 0.05f), new Vector3(0.14f, 0.15f, 0.10f), look.boots, "kneePad");
                Prim.Capsule(kn, new Vector3(0f, -SHIN * 0.5f, 0f), new Vector3(0.118f, SHIN * 0.5f + 0.03f, 0.118f), Quaternion.identity, look.suit, "shin");
                var an = Child(kn, "ankle", new Vector3(0f, -SHIN, 0f));
                Prim.Sphere(an, new Vector3(0f, -0.025f, 0.05f), new Vector3(0.115f, 0.10f, 0.27f), look.boots, "boot");
                if (side < 0) { hipL = hip; knL = kn; } else { hipR = hip; knR = kn; }
            }
            Pose(0f, 0f, 0f, 0f, 8f, 20f, 8f, 20f, 4f);
        }

        /// <param name="crouch">0 standing .. 1 deep crouch</param>
        /// <param name="lean">forward lean of the body in degrees</param>
        /// <param name="walkAmp">leg swing in degrees (running)</param>
        /// <param name="phase">stride phase</param>
        /// <param name="sL">left shoulder raise forward in degrees; eL: left elbow bend in degrees (sR, eR for the right arm)</param>
        /// <param name="spread">arms out sideways in degrees</param>
        public void Pose(float crouch, float lean, float walkAmp, float phase, float sL, float eL, float sR, float eR, float spread)
        {
            float t = crouch * 72f, k = crouch * 112f;
            float tr = t * Mathf.Deg2Rad, kr = k * Mathf.Deg2Rad;
            // put the pelvis where it keeps the ankles on the ground
            float py = ANKLE + THIGH * Mathf.Cos(tr) + SHIN * Mathf.Cos(tr - kr) + HIPDROP;
            float pz = -(THIGH * Mathf.Sin(tr) + SHIN * Mathf.Sin(tr - kr));
            float swing = walkAmp * Mathf.Sin(phase);
            pelvis.localPosition = new Vector3(0f, py + Mathf.Abs(Mathf.Sin(phase)) * walkAmp * 0.0015f, pz);
            spine.localRotation = Quaternion.Euler(lean, 0f, 0f);
            head.localRotation = Quaternion.Euler(-lean * 0.7f, 0f, 0f);                         // keep looking ahead
            hipL.localRotation = Quaternion.Euler(-(t + swing), 0f, 0f);
            hipR.localRotation = Quaternion.Euler(-(t - swing), 0f, 0f);
            knL.localRotation = Quaternion.Euler(k + Mathf.Max(0f, -swing) * 1.1f, 0f, 0f);
            knR.localRotation = Quaternion.Euler(k + Mathf.Max(0f, swing) * 1.1f, 0f, 0f);
            shL.localRotation = Quaternion.Euler(-sL, 0f, -spread);
            shR.localRotation = Quaternion.Euler(-sR, 0f, spread);
            elL.localRotation = Quaternion.Euler(-eL, 0f, 0f);
            elR.localRotation = Quaternion.Euler(-eR, 0f, 0f);
        }
    }
}
