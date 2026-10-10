using UnityEngine;

namespace ApexDrive
{
    /// <summary>Particle effects built in code: rain spray behind the cars, smoke from broken cars, and a soft glow texture.</summary>
    public static class Effects
    {
        static Texture2D soft;
        static Material particleMat;

        /// <summary>A white disc that fades to transparent at the edge.</summary>
        public static Texture2D Soft()
        {
            if (soft != null) return soft;
            const int S = 32;
            soft = new Texture2D(S, S, TextureFormat.RGBA32, false);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    soft.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            soft.wrapMode = TextureWrapMode.Clamp;
            soft.Apply();
            return soft;
        }

        static Material ParticleMat()
        {
            if (particleMat != null) return particleMat;
            particleMat = new Material(Shader.Find("Sprites/Default"));
            particleMat.mainTexture = Soft();
            return particleMat;
        }

        static ParticleSystem Make(Transform parent, Vector3 localPos, Quaternion localRot, float lifeMin, float lifeMax, float speedMin, float speedMax,
                                   float sizeMin, float sizeMax, float growth, float angle, float radius, float gravity, Color col, int max)
        {
            var go = new GameObject("Particles");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.maxParticles = max;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.startColor = col;
            var shape = ps.shape;
            shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = angle; shape.radius = radius;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            var colLife = ps.colorOverLifetime; colLife.enabled = true; colLife.color = new ParticleSystem.MinMaxGradient(grad);
            var sizeLife = ps.sizeOverLifetime; sizeLife.enabled = true;
            sizeLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, growth)));
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = ParticleMat();
            var em = ps.emission; em.rateOverTime = 0f;
            return ps;
        }

        /// <summary>The rooster tail of spray behind a car in the wet. Set its emission rate from speed and wetness.</summary>
        public static ParticleSystem MakeSpray(Transform car)
        {
            return Make(car, new Vector3(0f, 0.3f, -2.4f), Quaternion.LookRotation(new Vector3(0f, 0.55f, -0.85f)),
                        1.0f, 1.9f, 3f, 8f, 0.7f, 1.8f, 3.2f, 26f, 0.6f, 0.25f, new Color(0.88f, 0.92f, 0.97f, 0.38f), 700);
        }

        /// <summary>A plume of smoke (a broken car, a lock-up). Set its emission rate to start and stop it.</summary>
        public static ParticleSystem MakeSmoke(Transform parent, Vector3 localPos, Color tint)
        {
            return Make(parent, localPos, Quaternion.LookRotation(Vector3.up), 1.4f, 2.4f, 1.5f, 3.5f, 0.7f, 1.5f, 3.2f, 14f, 0.3f, -0.05f, tint, 300);
        }
    }
}
