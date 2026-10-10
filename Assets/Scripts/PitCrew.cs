using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// The pit crew, modelled on overhead F1 pit-stop photos: a dozen mechanics (real people with joints, see Human) waiting in lines
    /// on the garage side of the box, who run in when you stop - four wheel guns, four tyre men, front and rear jacks, two front-wing
    /// men and the lollipop man - crouch at the car while the wheels are swapped, then run back.
    /// </summary>
    public class PitCrew
    {
        enum Role { Gun, Tyre, Jack, Wing, Lolli }

        class Man
        {
            public Human h;
            public Role role;
            public Vector2 idle, work, look;      // places in car space (x across, y along) and what the man faces
            public float seed;
            public float[] wp;                    // work pose: crouch, lean, sL, eL, sR, eR
        }

        readonly List<Man> men = new List<Man>();
        readonly Transform root;
        readonly Transform[] props = new Transform[4];
        readonly Material propSide, lolliMat;
        Vector2 C, F, R; float y0, heading, os;
        Compound lastCompound = Compound.Medium;
        static readonly float[] WheelX = { -1.05f, 1.05f, -1.05f, 1.05f };
        static readonly float[] WheelZ = { 1.5f, 1.5f, -1.4f, -1.4f };
        const float WORK_X = 1.62f;               // how far from the car's centre line the wheel men kneel

        static float Sm(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / Mathf.Max(0.0001f, b - a));
            return t * t * (3f - 2f * t);
        }

        Vector3 Pt(float x, float y, float z) { return TrackModel.W(C + R * x + F * z, y0 + y); }

        public PitCrew(TrackModel tm, int box, Color suit, Color helmetColor)
        {
            tm.PitBoxFrame(box, out C, out y0, out heading, out F, out R, out os);
            root = new GameObject("PitCrew").transform;
            var look = new HumanLook
            {
                suit = Mats.Make(suit, 0.3f),
                boots = Mats.Make(new Color(0.07f, 0.07f, 0.08f), 0.25f),
                helmet = Mats.Make(helmetColor, 0.75f, 0.2f),
                visor = Mats.Make(new Color(0.03f, 0.04f, 0.05f), 0.95f, 0.8f),
                glove = Mats.Make(new Color(0.09f, 0.09f, 0.10f), 0.25f),
                trim = Mats.Make(new Color(0.93f, 0.93f, 0.95f), 0.25f)
            };

            float[] row1 = { -5.2f, -4.0f, -2.8f, -1.6f, -0.4f, 0.8f, 2.0f, 3.2f, 4.4f };
            float[] row2 = { -4.6f, -2.6f, -0.6f, 1.4f, 3.4f };
            int idx = 0;
            System.Func<Vector2> nextIdle = () =>
            {
                Vector2 v = idx < row1.Length ? new Vector2(os * 3.6f, row1[idx]) : new Vector2(os * 5.0f, row2[Mathf.Min(row2.Length - 1, idx - row1.Length)]);
                idx++;
                return v;
            };

            for (int w = 0; w < 4; w++)   // wheel gun men
            {
                float sx = Mathf.Sign(WheelX[w]);
                men.Add(Make(look, Role.Gun, nextIdle(), new Vector2(sx * WORK_X, WheelZ[w]), new Vector2(WheelX[w] * 0.9f, WheelZ[w]), new[] { 0.85f, 28f, 45f, 30f, 45f, 30f }));
            }
            for (int w = 0; w < 4; w++)   // tyre men (carry the old / new wheel)
            {
                float sx = Mathf.Sign(WheelX[w]);
                men.Add(Make(look, Role.Tyre, nextIdle(), new Vector2(sx * WORK_X, WheelZ[w] + (WheelZ[w] > 0f ? -0.8f : 0.8f)), new Vector2(WheelX[w] * 0.9f, WheelZ[w]), new[] { 0.55f, 22f, 55f, 35f, 55f, 35f }));
            }
            men.Add(Make(look, Role.Jack, nextIdle(), new Vector2(0f, 3.7f), new Vector2(0f, 2.9f), new[] { 0.6f, 24f, 50f, 40f, 50f, 40f }));     // front jack
            men.Add(Make(look, Role.Jack, nextIdle(), new Vector2(0f, -2.8f), new Vector2(0f, -2.1f), new[] { 0.6f, 24f, 50f, 40f, 50f, 40f }));    // rear jack
            men.Add(Make(look, Role.Wing, nextIdle(), new Vector2(-1.2f, 3.5f), new Vector2(-0.5f, 2.9f), new[] { 0f, 18f, 55f, 20f, 55f, 20f }));  // front wing
            men.Add(Make(look, Role.Wing, nextIdle(), new Vector2(1.2f, 3.5f), new Vector2(0.5f, 2.9f), new[] { 0f, 18f, 55f, 20f, 55f, 20f }));
            var lolli = Make(look, Role.Lolli, nextIdle(), new Vector2(0f, 5.4f), new Vector2(0f, 0.5f), new[] { 0f, 6f, 50f, 45f, 50f, 45f });     // lollipop
            men.Add(lolli);

            // the lollipop board: red = hold the car, green = go
            lolliMat = Mats.Make(new Color(0.9f, 0.1f, 0.1f), 0.2f);
            Prim.Cyl(lolli.h.root, new Vector3(0.215f, 1.0f, 0.50f), new Vector3(0.04f, 0.95f, 0.04f), Quaternion.identity, look.boots, "pole");
            Prim.Cyl(lolli.h.root, new Vector3(0.215f, 1.98f, 0.50f), new Vector3(0.55f, 0.015f, 0.55f), Quaternion.Euler(90f, 0f, 0f), lolliMat, "disc");

            // the wheel guns
            var gunMat = Mats.Make(new Color(0.55f, 0.57f, 0.62f), 0.5f, 0.6f);
            foreach (var m in men)
            {
                if (m.role != Role.Gun) continue;
                Prim.Cyl(m.h.wristR, new Vector3(0f, -0.20f, 0f), new Vector3(0.10f, 0.16f, 0.10f), Quaternion.identity, gunMat, "gun");
                Prim.Cyl(m.h.wristR, new Vector3(0f, -0.40f, 0f), new Vector3(0.07f, 0.045f, 0.07f), Quaternion.identity, look.boots, "socket");
                Prim.Box(m.h.wristR, new Vector3(0f, -0.07f, 0.075f), new Vector3(0.04f, 0.11f, 0.04f), look.boots, "handle");
            }

            // the wheels being carried off and on
            var tread = Mats.Make(Color.white, 0.1f); Mats.SetTex(tread, Tex.Tread());
            propSide = Mats.Make(Color.white, 0.1f); Mats.SetTex(propSide, Tex.Sidewall(Compound.Medium));
            for (int w = 0; w < 4; w++)
            {
                var g = new GameObject("tyreProp"); g.transform.SetParent(root, false);
                Prim.Cyl(g.transform, Vector3.zero, new Vector3(0.76f, 0.17f, 0.76f), Quaternion.Euler(0, 0, 90), tread, "tyre");
                Prim.Cyl(g.transform, new Vector3(0.172f, 0, 0), new Vector3(0.74f, 0.004f, 0.74f), Quaternion.Euler(0, 0, 90), propSide, "sideA");
                Prim.Cyl(g.transform, new Vector3(-0.172f, 0, 0), new Vector3(0.74f, 0.004f, 0.74f), Quaternion.Euler(0, 0, 90), propSide, "sideB");
                g.SetActive(false);
                props[w] = g.transform;
            }
            Update(null, 0f);
        }

        int madeCount;
        Man Make(HumanLook look, Role role, Vector2 idle, Vector2 work, Vector2 faces, float[] workPose)
        {
            // every mechanic is a little different in height and build
            float k = (madeCount * 37 % 11) / 11f;
            var variant = new HumanLook { suit = look.suit, boots = look.boots, helmet = look.helmet, visor = look.visor, glove = look.glove, trim = look.trim, height = 0.95f + 0.08f * k, width = 0.96f + 0.08f * ((madeCount * 53 % 7) / 7f) };
            var m = new Man { role = role, idle = idle, work = work, look = faces, wp = workPose, seed = madeCount * 1.7f };
            m.h = new Human(root, variant, "Mechanic");
            madeCount++;
            return m;
        }

        public void Update(Racer p, float now)
        {
            bool active = p != null && p.pitStopActive;
            float P = active ? Mathf.Clamp01((now - p.pitStopStart) / Mathf.Max(0.1f, p.pitStopEnd - p.pitStopStart)) : 0f;
            if (!active && p != null) lastCompound = p.compound;
            float s = Sm(0f, 0.2f, P) * (1f - Sm(0.8f, 1f, P));
            Vector3 carCentre = TrackModel.W(C, y0);

            // far-away boxes are not worth drawing
            var cam = Camera.main;
            bool near = cam == null || (cam.transform.position - carCentre).sqrMagnitude < 170f * 170f;
            foreach (var m in men) if (m.h.root.gameObject.activeSelf != near) m.h.root.gameObject.SetActive(near);
            if (!near) return;

            float workW = Sm(0.7f, 1f, s), idleW = 1f - Sm(0.02f, 0.10f, s), runW = Mathf.Max(0f, 1f - idleW - workW);
            foreach (var m in men)
            {
                Vector2 l = Vector2.Lerp(m.idle, m.work, s);
                m.h.root.position = Pt(l.x, 0f, l.y);
                Vector3 to = Pt(m.look.x, 0f, m.look.y) - m.h.root.position; to.y = 0f;
                if (s > 0.02f && s < 0.7f) to = carCentre - m.h.root.position;               // while running they look at the car
                if (to.sqrMagnitude > 0.01f) m.h.root.rotation = Quaternion.LookRotation(to);

                float phase = now * 10f + m.seed;
                float sw = Mathf.Sin(phase);
                // idle: standing relaxed with the hands in front
                float iCr = 0f, iLean = 3f, iSL = 12f, iEL = 30f, iSR = 12f, iER = 30f;
                // running: a stride with the arms pumping
                float rCr = 0.06f, rLean = 14f, rSL = 30f - 38f * sw, rEL = 80f, rSR = 30f + 38f * sw, rER = 80f;
                // work: the pose of the job, with the guns buzzing and the jacks pumping
                float wCr = m.wp[0], wLean = m.wp[1], wSL = m.wp[2], wEL = m.wp[3], wSR = m.wp[4], wER = m.wp[5];
                if (m.role == Role.Gun && P > 0.28f && P < 0.66f) { float j = Mathf.Sin(now * 48f) * 3f; wSL += j; wSR += j; }
                if (m.role == Role.Jack && (P < 0.28f || P > 0.66f)) { float j = Mathf.Sin(now * 7f) * 12f; wSL += j; wSR += j; wLean += j * 0.4f; }
                if (m.role == Role.Tyre && P > 0.34f && P < 0.6f) { wSL += 10f; wSR += 10f; }

                float crouch = iCr * idleW + rCr * runW + wCr * workW;
                float lean = iLean * idleW + rLean * runW + wLean * workW;
                float sL = iSL * idleW + rSL * runW + wSL * workW, eL = iEL * idleW + rEL * runW + wEL * workW;
                float sR = iSR * idleW + rSR * runW + wSR * workW, eR = iER * idleW + rER * runW + wER * workW;
                m.h.Pose(crouch, lean, 40f * runW, phase, sL, eL, sR, eR, 4f);
            }

            lolliMat.color = P < 0.78f ? new Color(0.9f, 0.1f, 0.1f) : new Color(0.1f, 0.85f, 0.2f);

            // tyres: old ones come off 0.34-0.47, new ones go on 0.5-0.6
            bool propsOn = active && P > 0.34f && P < 0.6f;
            if (propsOn) propSide.mainTexture = Tex.Sidewall(P < 0.47f ? lastCompound : p.compound);
            float outT = P < 0.47f ? Sm(0.34f, 0.44f, P) : 1f - Sm(0.5f, 0.6f, P);
            Quaternion rot = Quaternion.Euler(0f, (Mathf.PI - heading) * Mathf.Rad2Deg, 0f) * Quaternion.identity;
            for (int w = 0; w < 4; w++)
            {
                props[w].gameObject.SetActive(propsOn);
                if (!propsOn) continue;
                float sx = Mathf.Sign(WheelX[w]);
                props[w].position = Pt(sx * (1.05f + 0.95f * outT), 0.38f + 0.12f * outT, WheelZ[w]);
                props[w].rotation = rot;
            }
        }

        public void Destroy() { if (root != null) Object.Destroy(root.gameObject); }
    }
}
