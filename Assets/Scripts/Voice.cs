using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace ApexDrive
{
    /// <summary>
    /// Spoken lines: team radio, pit-crew chatter and the replay commentators.
    ///
    /// Preferred: Piper, an offline neural text-to-speech engine, which sounds like a real person. Three voices run as
    /// small background processes (a British play-by-play commentator, an American race engineer, a woman adding
    /// colour). The engineer and crew go through a radio filter. The files live in
    /// %LOCALAPPDATA%\ApexDrive\piper (piper\piper.exe and voices\*.onnx).
    ///
    /// Fallback: if Piper is not installed, the Windows built-in voices (English ones only) are used instead, and if
    /// there is nothing at all the game is simply silent with subtitles.
    /// </summary>
    public static class Voice
    {
        public const int PLAYBYPLAY = 1, RADIO = 2, WOMAN = 3;

        // ------------------------------------------------------------------ Piper
        class Engine
        {
            public Process proc;
            public bool failed;
            public volatile string doneFile;            // set by the output handler when a wav has been written
            public string model; public float length;
        }

        static readonly Engine[] engines = new Engine[4];
        static string[] modelFile = { "", "en_GB-alan-medium.onnx", "en_US-ryan-medium.onnx", "en_US-amy-medium.onnx" };
        // nicer "high" quality models are used for the radio and the second commentator when they are installed
        static readonly string[] highModel = { "", "", "en_US-ryan-high.onnx", "en_GB-cori-high.onnx" };
        static readonly float[] lengthScale = { 1f, 0.92f, 0.86f, 0.98f };           // below 1 = faster speech

        static string piperRoot;
        static bool started, piperOk, sapiMode, failedAll;
        static AudioSource dry, radio;                   // commentators (clean) and engineer / crew (radio filter)

        // the speech pipeline: one line waiting to be generated, one being generated, one generated and waiting to play
        static string pendingText; static int pendingVoice; static float pendingAt;
        static bool generating; static int genVoice; static string genFile; static float genAt; static int fileCounter;
        static AudioClip readyClip; static int readyVoice; static float readyAt;
        static string lastSaid = ""; static float lastSaidAt;

        public static bool Available { get { return started && !failedAll; } }

        // a small log in %LOCALAPPDATA%/ApexDrive/voice.log so problems with the voices can be diagnosed
        static string logPath;
        static void Log(string s)
        {
            try
            {
                if (logPath == null) logPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), Path.Combine("ApexDrive", "voice.log"));
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.AppendAllText(logPath, System.DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + System.Environment.NewLine);
            }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[Voice] log file failed: " + ex.Message); }
        }

        // ------------------------------------------------------------------ start / stop
        public static void Start(GameObject host)
        {
            if (started) return;
            started = true;
            try { logPath = null; Directory.CreateDirectory(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "ApexDrive")); File.WriteAllText(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), Path.Combine("ApexDrive", "voice.log")), ""); } catch (System.Exception) { }
            Log("Voice.Start platform=" + Application.platform);
            if (Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.WindowsPlayer) { failedAll = true; return; }

            // Piper lives next to the project (PiperVoices) or, failing that, in %LOCALAPPDATA%/ApexDrive/piper
            string[] roots =
            {
                Path.Combine(Path.GetDirectoryName(Application.dataPath), "PiperVoices"),
                Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), Path.Combine("ApexDrive", "piper"))
            };
            bool haveExe = false, haveVoices = false;
            piperRoot = roots[0];
            foreach (string root in roots)
            {
                bool exe = File.Exists(Path.Combine(root, Path.Combine("piper", "piper.exe")));
                bool vo = true;
                for (int i = 1; i <= 3; i++) if (!File.Exists(Path.Combine(root, Path.Combine("voices", modelFile[i])))) vo = false;
                Log("looked in " + root + ": exe=" + exe + " voices=" + vo);
                if (exe && vo) { piperRoot = root; haveExe = true; haveVoices = true; break; }
            }
            piperOk = haveExe && haveVoices;
            if (piperOk)
                for (int i = 1; i <= 3; i++)
                    if (highModel[i].Length > 0 && File.Exists(Path.Combine(piperRoot, Path.Combine("voices", highModel[i])))) modelFile[i] = highModel[i];
            Log("piperRoot=" + piperRoot + " exe=" + haveExe + " voices=" + haveVoices + " -> piperOk=" + piperOk);

            if (piperOk) BuildPlayers(host);
            else StartSapi();
        }

        static void BuildPlayers(GameObject host)
        {
            var d = new GameObject("VoiceDry"); d.transform.SetParent(host.transform, false);
            dry = d.AddComponent<AudioSource>(); dry.playOnAwake = false; dry.spatialBlend = 0f;

            var r = new GameObject("VoiceRadio"); r.transform.SetParent(host.transform, false);
            radio = r.AddComponent<AudioSource>(); radio.playOnAwake = false; radio.spatialBlend = 0f;
            // a team radio is a little thin, but no distortion: it is a real person talking into a headset
            var hp = r.AddComponent<AudioHighPassFilter>(); hp.cutoffFrequency = 220f;
            var lp = r.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 5200f;
        }

        /// <summary>Starts a voice's engine ahead of time so its first line is not delayed.</summary>
        public static void Warm(int voice)
        {
            if (!started || !piperOk || voice < 1 || voice > 3) return;
            EnsureEngine(voice);
        }

        static Engine EnsureEngine(int voice)
        {
            var e = engines[voice];
            if (e != null && !e.failed && e.proc != null && !e.proc.HasExited) return e;
            e = new Engine { model = modelFile[voice], length = lengthScale[voice] };
            engines[voice] = e;
            try
            {
                string exe = Path.Combine(piperRoot, Path.Combine("piper", "piper.exe"));
                string args = "--model \"" + Path.Combine(piperRoot, Path.Combine("voices", e.model)) + "\" --json-input --length_scale " + e.length.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                              + " --sentence_silence 0.12 --noise_scale 0.85 --noise_w 0.95";
                var psi = new ProcessStartInfo(exe, args)
                {
                    WorkingDirectory = Path.Combine(piperRoot, "piper"),
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                var captured = e;
                e.proc = new Process { StartInfo = psi };
                e.proc.OutputDataReceived += (s, ev) => { if (!string.IsNullOrEmpty(ev.Data)) captured.doneFile = ev.Data.Trim(); };
                e.proc.ErrorDataReceived += (s, ev) => { };
                e.proc.Start();
                e.proc.BeginOutputReadLine();
                e.proc.BeginErrorReadLine();
            }
            catch (System.Exception ex) { e.failed = true; Log("could not start Piper voice " + voice + ": " + ex.Message); }
            if (!e.failed) Log("started Piper voice " + voice + " (" + e.model + ")");
            return e;
        }

        public static void Stop()
        {
            for (int i = 1; i <= 3; i++)
            {
                var e = engines[i];
                if (e == null || e.proc == null) continue;
                try
                {
                    if (!e.proc.HasExited) { e.proc.StandardInput.Close(); if (!e.proc.WaitForExit(200)) e.proc.Kill(); }
                }
                catch (System.Exception) { }
                engines[i] = null;
            }
            StopSapi();
            started = false; generating = false; pendingText = null; readyClip = null;
        }

        // ------------------------------------------------------------------ saying things
        static string Clean(string t)
        {
            var sb = new StringBuilder();
            foreach (char c in t)
            {
                if (c == '|' || c == '\n' || c == '\r' || c == '\t') sb.Append(' ');
                else if (c == '[' || c == ']' || c == '<' || c == '>' || c == '"' || c == '\\') continue;
                else sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        /// <summary>Speaks a line (rate is only used by the Windows fallback voices). The newest waiting line replaces older waiting ones.</summary>
        public static void Say(int voice, int rate, string text)
        {
            if (!GameSave.voices || !started || failedAll) return;
            text = Clean(text);
            if (text.Length == 0) return;
            if (text == lastSaid && Time.realtimeSinceStartup - lastSaidAt < 4f) return;      // do not repeat the same line
            lastSaid = text; lastSaidAt = Time.realtimeSinceStartup;
            Log("say v" + voice + ": " + text);
            if (piperOk) { pendingText = text; pendingVoice = voice; pendingAt = Time.realtimeSinceStartup; }
            else SapiSay(voice, rate, text);
        }

        static string JsonEscape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>Call every frame: feeds the engine, collects finished audio and plays it.</summary>
        public static void Update()
        {
            if (!started) return;
            if (!piperOk) { SapiUpdate(); return; }
            float now = Time.realtimeSinceStartup;

            // 1) a generated file has arrived: load it
            if (generating)
            {
                var e = engines[genVoice];
                if (e == null || e.failed || (e.proc != null && e.proc.HasExited)) { generating = false; }
                else if (e.doneFile != null)
                {
                    e.doneFile = null;
                    AudioClip clip = LoadWav(genFile, genVoice == RADIO);
                    try { File.Delete(genFile); } catch (System.Exception) { }
                    generating = false;
                    Log("generated in " + (now - genAt).ToString("0.00") + "s, clip=" + (clip != null));
                    if (clip != null && now - genAt < 8f) { readyClip = clip; readyVoice = genVoice; readyAt = now; }
                }
                else if (now - genAt > 15f) generating = false;               // never wait forever
            }

            // 2) play the prepared clip as soon as nothing is playing
            bool busy = (dry != null && dry.isPlaying) || (radio != null && radio.isPlaying);
            if (readyClip != null && !busy)
            {
                if (now - readyAt < 6f)
                {
                    var src = readyVoice == RADIO ? radio : dry;
                    float vol = Mathf.Clamp01(GameSave.volume * 1.4f);
                    src.clip = readyClip; src.volume = vol; src.pitch = 1f;
                    src.Play();
                    Log("playing (" + (readyVoice == RADIO ? "radio" : "clean") + ", volume " + vol.ToString("0.00") + ")");
                }
                readyClip = null;
            }

            // 3) start generating the waiting line
            if (!generating && readyClip == null && pendingText != null)
            {
                string text = pendingText; int voice = pendingVoice; float at = pendingAt;
                pendingText = null;
                if (now - at > 6f) return;                                     // too old to be worth saying
                var e = EnsureEngine(voice);
                if (e.failed || e.proc == null)
                {
                    Log("Piper unavailable, using the Windows voices");
                    piperOk = false; if (!sapiMode) StartSapi(); SapiSay(voice, 3, text);
                    return;
                }
                try
                {
                    genFile = Path.Combine(Path.GetTempPath(), "apexdrive_voice_" + (fileCounter++ % 20) + ".wav");
                    string json = "{\"text\": \"" + JsonEscape(text) + "\", \"output_file\": \"" + JsonEscape(genFile) + "\"}";
                    e.doneFile = null;
                    e.proc.StandardInput.WriteLine(json);
                    e.proc.StandardInput.Flush();
                    generating = true; genVoice = voice; genAt = now;
                }
                catch (System.Exception) { e.failed = true; }
            }
        }

        // 16-bit PCM wav -> AudioClip

        // ------------------------------------------------------------------ radio realism
        /// <summary>
        /// Makes a clean voice sound like a team radio: the push-to-talk squelch before and after, a little static under the
        /// words, and gentle compression (the way a headset mic flattens the dynamics). No distortion: it is still a person.
        /// </summary>
        static float[] RadioFx(float[] src, int rate)
        {
            var rng = new System.Random(src.Length * 31 + 7);
            int lead = (int)(rate * 0.16f), tail = (int)(rate * 0.14f);
            var o = new float[lead + src.Length + tail];

            // normalise and compress the voice
            float peak = 0.0001f;
            for (int i = 0; i < src.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(src[i]));
            float g = 0.85f / peak;
            for (int i = 0; i < src.Length; i++)
            {
                float x = src[i] * g * 1.5f;
                o[lead + i] = x / (1f + Mathf.Abs(x) * 0.6f) * 1.35f;
            }

            // static under everything: low-passed noise, a touch louder in the gaps between words
            float lp = 0f;
            for (int i = 0; i < o.Length; i++)
            {
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (n - lp) * 0.35f;
                float level = 0.012f + (i >= lead && i < lead + src.Length ? 0f : 0.010f);
                o[i] += lp * level * 2.2f;
            }

            // squelch: a short noise burst that dies away when the button is pressed and released
            Burst(o, 0, (int)(rate * 0.045f), 0.20f, rng);
            Burst(o, o.Length - (int)(rate * 0.04f), (int)(rate * 0.04f), 0.14f, rng);
            for (int i = 0; i < o.Length; i++) o[i] = Mathf.Clamp(o[i], -0.98f, 0.98f);
            return o;
        }

        static void Burst(float[] o, int start, int len, float amp, System.Random rng)
        {
            for (int i = 0; i < len && start + i < o.Length; i++)
            {
                float env = 1f - (float)i / len;
                o[start + i] += (float)(rng.NextDouble() * 2.0 - 1.0) * amp * env * env;
            }
        }

        static readonly System.Random chat = new System.Random();

        /// <summary>Rewrites a message the way a race engineer would say it over the radio.</summary>
        public static string RadioText(string s, string driverName)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("Radio: ", "").Replace("OK,", "Okay,").Replace("OK ", "Okay ");
            s = s.Replace("Stewards: ", "Stewards: ").Replace("+", "plus ").Replace(" - ", ", ").Replace("R to recover", "or recover the car");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?<![A-Za-z])P([0-9]+)(?![0-9])", "P $1");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(\d)/(\d)", "$1 of $2");
            s = s.Replace("tires", "tyres");
            bool urgent = s.Contains("!");
            string first = string.IsNullOrEmpty(driverName) ? "" : driverName.Split(' ')[0];
            // now and then he uses your name, the way engineers do; urgent calls stay short
            if (!urgent && first.Length > 0 && chat.Next(100) < 30 && !s.StartsWith("Okay") && !s.StartsWith("Stewards") && !s.StartsWith("Staying") && !s.StartsWith("Good"))
                s = first + ", " + char.ToLowerInvariant(s[0]) + s.Substring(1);
            if (urgent && s.ToLowerInvariant().Contains("box")) s = s.Replace("Box, box", "Box box box").Replace("box this lap", "box this lap, box box");
            return s;
        }

        static AudioClip LoadWav(string path, bool radioFx)
        {
            try
            {
                byte[] b = File.ReadAllBytes(path);
                if (b.Length < 44) return null;
                int channels = 1, rate = 22050, dataStart = -1, dataLen = 0;
                int pos = 12;
                while (pos + 8 <= b.Length)
                {
                    string id = Encoding.ASCII.GetString(b, pos, 4);
                    int size = System.BitConverter.ToInt32(b, pos + 4);
                    if (id == "fmt ")
                    {
                        channels = System.BitConverter.ToInt16(b, pos + 10);
                        rate = System.BitConverter.ToInt32(b, pos + 12);
                    }
                    else if (id == "data")
                    {
                        dataStart = pos + 8;
                        dataLen = (size <= 0 || dataStart + size > b.Length) ? b.Length - dataStart : size;
                        break;
                    }
                    pos += 8 + size + (size & 1);
                }
                if (dataStart < 0 || dataLen < 2) return null;
                int samples = dataLen / 2;
                var f = new float[samples];
                for (int i = 0; i < samples; i++) f[i] = System.BitConverter.ToInt16(b, dataStart + i * 2) / 32768f;
                if (radioFx && channels == 1) f = RadioFx(f, rate);
                var clip = AudioClip.Create("voice", f.Length / channels, channels, rate, false);
                clip.SetData(f, 0);
                return clip;
            }
            catch (System.Exception) { return null; }
        }

        // ------------------------------------------------------------------ fallback: the Windows voices (English ones only)
        static Process sapi;
        static bool sapiReady, sapiBusy;
        static string sapiPending; static float sapiPendingAt;

        const string SapiScript = @"
Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voices = @($s.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -like 'en*' } | ForEach-Object { $_.VoiceInfo })
if ($voices.Count -eq 0) { [Console]::Out.WriteLine('novoice'); exit }
$males = @($voices | Where-Object { $_.Gender -eq 'Male' })
$females = @($voices | Where-Object { $_.Gender -eq 'Female' })
if ($males.Count -eq 0) { $males = $voices }
if ($females.Count -eq 0) { $females = $males }
$m1 = $males[0]; $m2 = $m1; if ($males.Count -gt 1) { $m2 = $males[1] }; $f1 = $females[0]
[Console]::Out.WriteLine('ready')
while ($true) {
  $line = [Console]::In.ReadLine()
  if ($null -eq $line -or $line -eq 'quit') { break }
  $p = $line.Split('|', 4)
  if ($p.Length -lt 4) { continue }
  try {
    $v = $m1; if ($p[0] -eq '2') { $v = $m2 } elseif ($p[0] -eq '3') { $v = $f1 }
    $s.SelectVoice($v.Name)
    $s.Volume = [int]$p[1]
    $s.Rate = [int]$p[2]
    $s.Speak($p[3])
  } catch {}
  [Console]::Out.WriteLine('done')
}
";

        static void StartSapi()
        {
            sapiMode = true;
            try
            {
                string encoded = System.Convert.ToBase64String(Encoding.Unicode.GetBytes(SapiScript));
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                sapi = new Process { StartInfo = psi };
                sapi.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    if (e.Data.StartsWith("ready")) sapiReady = true;
                    else if (e.Data.StartsWith("done")) sapiBusy = false;
                    else if (e.Data.StartsWith("novoice")) failedAll = true;
                };
                sapi.ErrorDataReceived += (s, e) => { };
                sapi.Start();
                sapi.BeginOutputReadLine();
                sapi.BeginErrorReadLine();
            }
            catch (System.Exception) { failedAll = true; }
        }

        static void StopSapi()
        {
            try
            {
                if (sapi != null && !sapi.HasExited) { sapi.StandardInput.WriteLine("quit"); sapi.StandardInput.Close(); if (!sapi.WaitForExit(300)) sapi.Kill(); }
            }
            catch (System.Exception) { }
            sapi = null; sapiReady = false; sapiBusy = false; sapiPending = null;
        }

        static void SapiSay(int voice, int rate, string text)
        {
            int volume = Mathf.Clamp(Mathf.RoundToInt(GameSave.volume * 140f), 0, 100);
            string line = voice + "|" + volume + "|" + rate + "|" + text;
            if (!sapiReady || sapiBusy) { sapiPending = line; sapiPendingAt = Time.realtimeSinceStartup; return; }
            SapiSend(line);
        }

        static void SapiSend(string line)
        {
            try
            {
                if (sapi == null || sapi.HasExited) { failedAll = true; return; }
                sapiBusy = true;
                sapi.StandardInput.WriteLine(line);
                sapi.StandardInput.Flush();
            }
            catch (System.Exception) { failedAll = true; sapiBusy = false; }
        }

        static void SapiUpdate()
        {
            if (!sapiMode || failedAll || sapiPending == null || !sapiReady || sapiBusy) return;
            string line = sapiPending; sapiPending = null;
            if (Time.realtimeSinceStartup - sapiPendingAt > 6f) return;
            SapiSend(line);
        }
    }
}
