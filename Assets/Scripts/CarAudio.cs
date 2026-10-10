using System.Collections.Generic;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// Procedural sound, no audio files: your engine with a turbo whine and backfire pops on lifting off, rival engines
    /// that sweep past with real Doppler, a different sound for each surface (tyre squeal, kerb rumble, gravel, wet
    /// hiss), echo near the grandstands, impacts, crowd cheer and crackly radio static.
    /// </summary>
    public class CarAudio
    {
        readonly AudioSource engine, screech, oneShot, turbo, rumble, gravel, hiss, wind, squeal, patter, crowd;
        readonly AudioReverbFilter reverb;
        readonly AudioClip engineClip, thud, cheer, radioClip, popClip, shiftClip;
        int lastShiftCount; float crowdVol;
        const int RATE = 22050;

        class Rival { public AudioSource src; public Racer car; public float pitchMul; }
        readonly List<Rival> rivals = new List<Rival>();
        float lastThrottle;
        bool lastEcho;

        public CarAudio(GameObject host)
        {
            engine = host.AddComponent<AudioSource>();
            screech = host.AddComponent<AudioSource>();
            oneShot = host.AddComponent<AudioSource>();
            turbo = host.AddComponent<AudioSource>();
            rumble = host.AddComponent<AudioSource>();
            gravel = host.AddComponent<AudioSource>();
            hiss = host.AddComponent<AudioSource>();
            wind = host.AddComponent<AudioSource>();
            squeal = host.AddComponent<AudioSource>();
            patter = host.AddComponent<AudioSource>();
            crowd = host.AddComponent<AudioSource>();
            reverb = host.AddComponent<AudioReverbFilter>();
            reverb.reverbPreset = AudioReverbPreset.Off;

            engineClip = EngineClip();
            engine.clip = engineClip; engine.loop = true; engine.playOnAwake = false; engine.volume = 0f;
            screech.clip = NoiseClip("screech", 1f, 0.5f); screech.loop = true; screech.playOnAwake = false; screech.volume = 0f;
            turbo.clip = TurboClip(); turbo.loop = true; turbo.playOnAwake = false; turbo.volume = 0f;
            rumble.clip = RumbleClip(); rumble.loop = true; rumble.playOnAwake = false; rumble.volume = 0f;
            gravel.clip = GravelClip(); gravel.loop = true; gravel.playOnAwake = false; gravel.volume = 0f;
            hiss.clip = HissClip(); hiss.loop = true; hiss.playOnAwake = false; hiss.volume = 0f;
            wind.clip = WindClip(); wind.loop = true; wind.playOnAwake = false; wind.volume = 0f;
            squeal.clip = SquealClip(); squeal.loop = true; squeal.playOnAwake = false; squeal.volume = 0f;
            patter.clip = PatterClip(); patter.loop = true; patter.playOnAwake = false; patter.volume = 0f;
            crowd.clip = CrowdClip(); crowd.loop = true; crowd.playOnAwake = false; crowd.volume = 0f;
            shiftClip = ShiftClip();
            oneShot.playOnAwake = false;
            thud = ThudClip();
            cheer = CheerClip();
            radioClip = RadioClip();
            popClip = PopClip();
        }

        // ---------------------------------------------------------------- clips
        static AudioClip EngineClip()
        {
            // exactly 50 cycles of a 100 Hz tone so the loop point is seamless
            int n = RATE / 2;
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / RATE;
                float w = 2f * Mathf.PI * 100f * t;
                d[i] = 0.5f * Mathf.Sin(w) + 0.3f * Mathf.Sin(2f * w) + 0.18f * Mathf.Sin(3f * w) + 0.1f * Mathf.Sin(5f * w + 0.6f) + 0.06f * Mathf.Sin(7f * w);
            }
            var c = AudioClip.Create("engine", n, 1, RATE, false);
            c.SetData(d, 0);
            return c;
        }

        // a high whistle (880 whole cycles of 1760 Hz in half a second, so it loops cleanly) with a little shimmer
        static AudioClip TurboClip()
        {
            int n = RATE / 2;
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / RATE;
                float w = 2f * Mathf.PI * 1760f * t;
                d[i] = 0.6f * Mathf.Sin(w) + 0.25f * Mathf.Sin(2f * w + 0.4f) + 0.1f * Mathf.Sin(3f * w);
            }
            var c = AudioClip.Create("turbo", n, 1, RATE, false);
            c.SetData(d, 0);
            return c;
        }

        // low rumble: brown noise plus a 25 Hz thump (25 whole cycles in one second)
        static AudioClip RumbleClip()
        {
            int n = RATE;
            var d = new float[n];
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float r = Random.value * 2f - 1f;
                prev = prev * 0.97f + r * 0.03f;
                float t = (float)i / RATE;
                d[i] = Mathf.Clamp(prev * 7f + 0.35f * Mathf.Sin(2f * Mathf.PI * 25f * t), -1f, 1f) * 0.6f;
            }
            var c = AudioClip.Create("rumble", n, 1, RATE, false);
            c.SetData(d, 0);
            return c;
        }

        // gravel and grass: lots of small crackles
        static AudioClip GravelClip()
        {
            int n = RATE;
            var d = new float[n];
            float env = 0f;
            for (int i = 0; i < n; i++)
            {
                if (Random.value < 0.012f) env = 0.5f + Random.value * 0.5f;
                env *= 0.985f;
                d[i] = (Random.value * 2f - 1f) * env;
            }
            var c = AudioClip.Create("gravel", n, 1, RATE, false);
            c.SetData(d, 0);
            return c;
        }

        // wet-road hiss: noise with the low end taken out
        static AudioClip HissClip()
        {
            int n = RATE;
            var d = new float[n];
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float r = Random.value * 2f - 1f;
                d[i] = (r - prev) * 0.5f;
                prev = r;
            }
            var c = AudioClip.Create("hiss", n, 1, RATE, false);
            c.SetData(d, 0);
            return c;
        }

        // air rushing past the helmet: noise with the highest and lowest parts taken off, so it roars rather than hisses
        static AudioClip WindClip()
        {
            int n = RATE * 2;
            var d = new float[n];
            float lp1 = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float r = Random.value * 2f - 1f;
                lp1 += (r - lp1) * 0.12f; lp2 += (lp1 - lp2) * 0.05f;
                float swell = 0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * i / n * 2f);
                d[i] = Mathf.Clamp((lp1 - lp2) * 6f * swell, -1f, 1f) * 0.5f;
            }
            var c = AudioClip.Create("wind", n, 1, RATE, false);
            c.SetData(d, 0);
            return c;
        }

        // the squeal of brakes at the limit: two close high tones (whole cycles in one second) and a little hiss
        static AudioClip SquealClip()
        {
            int n = RATE;
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / RATE;
                d[i] = (0.5f * Mathf.Sin(2f * Mathf.PI * 2400f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * 2630f * t) + 0.15f * (Random.value * 2f - 1f)) * 0.6f;
            }
            var c = AudioClip.Create("squeal", n, 1, RATE, false);
            c.SetData(d, 0);
            return c;
        }

        // rain tapping on the helmet: sparse short ticks
        static AudioClip PatterClip()
        {
            int n = RATE;
            var d = new float[n];
            float env = 0f, prev = 0f;
            for (int i = 0; i < n; i++)
            {
                if (Random.value < 0.0035f) env = 0.4f + Random.value * 0.6f;
                env *= 0.965f;
                float r = Random.value * 2f - 1f;
                d[i] = (r - prev * 0.8f) * env * 0.7f;
                prev = r;
            }
            var c = AudioClip.Create("patter", n, 1, RATE, false);
            c.SetData(d, 0);
            return c;
        }

        // a crowd's murmur: low noise that swells and falls
        static AudioClip CrowdClip()
        {
            int n = RATE * 3;
            var d = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float r = Random.value * 2f - 1f;
                lp += (r - lp) * 0.08f;
                float t = (float)i / n;
                float swell = 0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * t * 3f) * Mathf.Sin(2f * Mathf.PI * t * 5f + 1f);
                d[i] = lp * 1.8f * swell;
            }
            var c = AudioClip.Create("crowd", n, 1, RATE, false);
            c.SetData(d, 0);
            return c;
        }

        // a gearshift: a short clunk with a puff of noise
        static AudioClip ShiftClip()
        {
            int n = (int)(RATE * 0.1f);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                d[i] = (Mathf.Sin(2f * Mathf.PI * 90f * i / RATE) * 0.7f + (Random.value * 2f - 1f) * 0.5f) * Mathf.Exp(-t * 12f);
            }
            var c = AudioClip.Create("shift", n, 1, RATE, false);
            c.SetData(d, 0);
            return c;
        }

        static AudioClip NoiseClip(string name, float seconds, float amp)
        {
            int n = (int)(RATE * seconds);
            var d = new float[n];
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float r = Random.value * 2f - 1f;
                prev = prev * 0.6f + r * 0.4f;
                d[i] = prev * amp;
            }
            var c = AudioClip.Create(name, n, 1, RATE, false);
            c.SetData(d, 0);
            return c;
        }

        // the "kssh" of a team radio: a short beep, then crackly static fading out
        static AudioClip RadioClip()
        {
            int n = (int)(RATE * 0.4f);
            var d = new float[n];
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / RATE;
                float v;
                if (t < 0.06f) v = 0.5f * Mathf.Sin(2f * Mathf.PI * 1500f * t);
                else
                {
                    float r = Random.value * 2f - 1f;
                    prev = prev * 0.3f + r * 0.7f;
                    float crackle = Random.value < 0.08f ? 1.6f : 0.6f;
                    v = prev * crackle * Mathf.Exp(-(t - 0.06f) * 9f) * 0.6f;
                }
                d[i] = Mathf.Clamp(v, -1f, 1f);
            }
            var c = AudioClip.Create("radio", n, 1, RATE, false);
            c.SetData(d, 0);
            return c;
        }

        // an exhaust pop when you lift off
        static AudioClip PopClip()
        {
            int n = (int)(RATE * 0.14f);
            var d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                phase += 2f * Mathf.PI * Mathf.Lerp(150f, 60f, t) / RATE;
                float noise = (Random.value * 2f - 1f) * 0.8f;
                d[i] = (Mathf.Sin(phase) * 0.7f + noise) * Mathf.Exp(-t * 14f);
            }
            var c = AudioClip.Create("pop", n, 1, RATE, false);
            c.SetData(d, 0);
            return c;
        }

        static AudioClip ThudClip()
        {
            int n = (int)(RATE * 0.25f);
            var d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float f = Mathf.Lerp(95f, 35f, t);
                phase += 2f * Mathf.PI * f / RATE;
                d[i] = Mathf.Sin(phase) * Mathf.Exp(-t * 7f) * 0.9f;
            }
            var c = AudioClip.Create("thud", n, 1, RATE, false);
            c.SetData(d, 0);
            return c;
        }

        static AudioClip CheerClip()
        {
            int n = (int)(RATE * 1.4f);
            var d = new float[n];
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = t < 0.18f ? t / 0.18f : Mathf.Max(0f, 1f - (t - 0.18f) / 0.82f);
                float r = Random.value * 2f - 1f;
                prev = prev * 0.5f + r * 0.5f;
                d[i] = prev * env * 0.5f;
            }
            var c = AudioClip.Create("cheer", n, 1, RATE, false);
            c.SetData(d, 0);
            return c;
        }

        // ---------------------------------------------------------------- rivals: real Doppler comes from the engine's 3D sources
        public void ClearRivals() { rivals.Clear(); }

        public void SetRivals(List<Racer> racers)
        {
            rivals.Clear();
            for (int i = 0; i < racers.Count; i++)
            {
                var r = racers[i];
                if (r.isPlayer || r.go == null) continue;
                var s = r.go.AddComponent<AudioSource>();
                s.clip = engineClip; s.loop = true; s.playOnAwake = false; s.volume = 0f;
                s.spatialBlend = 1f; s.dopplerLevel = 1.6f; s.minDistance = 6f; s.maxDistance = 170f; s.rolloffMode = AudioRolloffMode.Linear;
                s.Play();
                rivals.Add(new Rival { src = s, car = r, pitchMul = 0.94f + 0.12f * ((i * 37) % 10) / 10f });
            }
        }

        void MuteRivals()
        {
            foreach (var r in rivals) if (r.src != null) r.src.volume = 0f;
        }

        // ---------------------------------------------------------------- per frame
        public void Tick(bool racing, float speedFrac, bool sliding)
        {
            float vol = GameSave.volume;
            if (racing)
            {
                if (!engine.isPlaying) engine.Play();
                engine.pitch = 0.55f + Mathf.Clamp01(speedFrac) * 2.2f;
                engine.volume = 0.22f * vol;
                bool slide = sliding && speedFrac > 0.08f;
                if (slide && !screech.isPlaying) screech.Play();
                screech.volume = slide ? 0.12f * vol : 0f;
            }
            else
            {
                engine.volume = 0f;
                screech.volume = 0f;
                turbo.volume = 0f; rumble.volume = 0f; gravel.volume = 0f; hiss.volume = 0f;
                wind.volume = 0f; squeal.volume = 0f; patter.volume = 0f; crowd.volume = 0f;
                MuteRivals();
            }
        }

        static void Loop(AudioSource s, float volume)
        {
            if (volume > 0.002f && !s.isPlaying) s.Play();
            s.volume = volume;
        }

        /// <summary>Everything that depends on what the car is doing: turbo, pops, surfaces, rival engines, echo.</summary>
        public void Extras(PlayerCar p, float wetness, bool nearStand, bool cockpit = true)
        {
            float vol = GameSave.volume;
            float sf = Mathf.Clamp01(p.speed / 80f);

            // the engine follows the gearbox: revs climb through each gear and fall at every shift, the limiter bounces at the top
            if (p.dyn != null)
            {
                float rev = Mathf.Clamp01((p.dyn.rpm - 4200f) / 8800f);
                engine.pitch = 0.55f + rev * 2.25f;
                engine.volume = (cockpit ? 0.17f : 0.24f) * (0.75f + 0.25f * (p.inThrottle ? 1f : 0f) + (p.dyn.limiter ? 0.05f : 0f)) * vol;
                if (p.dyn.shiftCount != lastShiftCount) { lastShiftCount = p.dyn.shiftCount; oneShot.PlayOneShot(shiftClip, 0.22f * vol); }
                // wind round the helmet grows with the square of the speed; a brake chirp when the fronts are overloaded; rain on the visor
                Loop(wind, sf * sf * (cockpit ? 0.42f : 0.2f) * vol);
                wind.pitch = 0.8f + sf * 0.5f;
                Loop(squeal, p.dyn.squeal * 0.16f * vol);
                squeal.pitch = 0.92f + 0.14f * p.dyn.squeal;
                Loop(patter, (cockpit && wetness > 0.25f) ? Mathf.Clamp01(wetness) * (0.30f - 0.15f * sf) * vol : 0f);
                crowdVol = Mathf.MoveTowards(crowdVol, nearStand ? 1f : 0f, Time.deltaTime * (nearStand ? 0.6f : 0.4f));
                Loop(crowd, crowdVol * (0.10f + 0.10f * sf) * vol);
            }

            // turbo whine follows the throttle; lifting off at speed gives a backfire pop
            bool thr = p.inThrottle;
            turbo.pitch = 0.6f + sf * 1.6f;
            Loop(turbo, (thr ? 0.05f : 0.012f) * vol * sf);
            if (lastThrottle > 0.5f && !thr && p.speed > 40f) oneShot.PlayOneShot(popClip, 0.35f * vol);
            lastThrottle = thr ? 1f : 0f;

            // each surface has its own sound
            Loop(rumble, p.onKerb ? p.kerbAmt * (0.2f + 0.8f * sf) * 0.35f * vol : 0f);
            rumble.pitch = 0.7f + sf * 0.9f;
            Loop(gravel, p.offTrack ? sf * 0.3f * vol : 0f);
            Loop(hiss, wetness > 0.3f ? wetness * sf * 0.12f * vol : 0f);

            // the rivals: louder and higher the faster they go; the 3D source does the Doppler as they pass
            foreach (var r in rivals)
            {
                if (r.src == null) continue;
                bool silent = r.car.gone || (r.car.dnf && r.car.speed < 1f);
                r.src.pitch = (0.5f + Mathf.Clamp01(r.car.speed / 82f) * 2.0f) * r.pitchMul;
                r.src.volume = silent ? 0f : 0.34f * vol * Mathf.Clamp01(0.25f + r.car.speed / 25f);
            }

            // grandstands bounce the sound back
            if (nearStand != lastEcho)
            {
                lastEcho = nearStand;
                reverb.reverbPreset = nearStand ? AudioReverbPreset.Arena : AudioReverbPreset.Off;
            }
        }

        public void Radio() { oneShot.PlayOneShot(radioClip, 0.28f * GameSave.volume); }
        public void Thud() { oneShot.PlayOneShot(thud, 0.8f * GameSave.volume); }
        public void Cheer() { oneShot.PlayOneShot(cheer, 0.5f * GameSave.volume); }
    }
}
