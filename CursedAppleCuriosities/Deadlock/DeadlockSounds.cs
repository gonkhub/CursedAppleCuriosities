using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CursedAppleCuriosities.Deadlock
{
    /// <summary>One sound: a pak01 path prefix (variants path_NN share it) and how it plays.</summary>
    public class DeadlockSound
    {
        public string Id = "";
        public string VpkPrefix = "";
        public float Volume = 1f;
        public float Range = 40f;
        public float Chance = 1f;
        public float MinGap = 0.1f;
        public bool Voice;
        /// <summary>Vanilla Wwise event played instead when this player has no Deadlock copy (or null).</summary>
        public string? Fallback;
    }

    /// <summary>
    /// Plays Deadlock sounds from the player's own cache. RoR2 has Unity audio disabled (it uses Wwise), so the
    /// MP3s are decoded with NLayer (MIT) and mixed by <see cref="NativeAudio"/>; this component sets each voice's
    /// left/right gain every frame from the camera (distance falloff and panning) and RoR2's master x SFX volume.
    /// Entity states run OnEnter on every client, so each client plays its own copy; nothing is networked.
    /// </summary>
    public class DeadlockSounds : MonoBehaviour
    {
        private class Playing
        {
            public DeadlockVoice Voice = null!;
            public DeadlockSound Row = null!;
            public Vector3 Pos;
            public Transform? Follow;
            public float Until = float.MaxValue;
        }

        private static DeadlockSounds? _instance;
        private static readonly Dictionary<string, DeadlockSound> Rows = new Dictionary<string, DeadlockSound>();
        private static readonly Dictionary<string, List<PcmClip>> Clips = new Dictionary<string, List<PcmClip>>();
        private static readonly Dictionary<string, float> LastPlayed = new Dictionary<string, float>();
        private static readonly List<Playing> Active = new List<Playing>();
        private static float _lastVoice, _volume = 1f, _nextVolumeRead;
        private static volatile bool _decoded;
        private bool _decoding;

        public static void Register(DeadlockSound s)
        {
            Rows[s.Id] = s;
            DeadlockInstall.WantSound(s.VpkPrefix);
        }

        public static void Ensure(GameObject host)
        {
            if (_instance == null) _instance = host.AddComponent<DeadlockSounds>();
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextVolumeRead) { _nextVolumeRead = Time.unscaledTime + 1f; _volume = ReadVolume(); }
            if (!_decoding && DeadlockInstall.FilesDone) { _decoding = true; Decode(); }
            UpdateVoices();
        }

        /// <summary>Background thread: decode every cached MP3 to mono 16-bit once.</summary>
        private static void Decode()
        {
            var rows = new List<DeadlockSound>(Rows.Values);
            var files = new Dictionary<string, List<string>>();
            foreach (DeadlockSound row in rows) files[row.Id] = DeadlockInstall.SoundFilesFor(row.VpkPrefix);
            new System.Threading.Thread(() =>
            {
                int count = 0;
                foreach (DeadlockSound row in rows)
                {
                    var list = new List<PcmClip>();
                    foreach (string file in files[row.Id])
                    {
                        try { list.Add(DecodeMp3(file)); count++; }
                        catch (System.Exception e) { CursedAppleCuriositiesPlugin.Logger?.LogWarning($"[Deadlock] sound {Path.GetFileName(file)}: {e.Message}"); }
                    }
                    lock (Clips) Clips[row.Id] = list;
                }
                _decoded = true;
                NativeAudio.Start();
                CursedAppleCuriositiesPlugin.Logger?.LogInfo($"[Deadlock] sounds: {count} clips decoded for {rows.Count} sounds");
            }) { IsBackground = true, Name = "CursedApple Deadlock sound decoder" }.Start();
        }

        private static PcmClip DecodeMp3(string file)
        {
            using (var mp3 = new NLayer.MpegFile(file))
            {
                int ch = Mathf.Max(1, mp3.Channels);
                var mono = new List<short>(mp3.SampleRate * 2);
                var buf = new float[4096 * ch];
                int n;
                while ((n = mp3.ReadSamples(buf, 0, buf.Length)) > 0)
                    for (int i = 0; i + ch - 1 < n; i += ch)
                    {
                        float sum = 0f;
                        for (int c = 0; c < ch; c++) sum += buf[i + c];
                        mono.Add((short)Mathf.Clamp(sum / ch * 32767f, -32768f, 32767f));
                    }
                return new PcmClip { Samples = mono.ToArray(), Rate = mp3.SampleRate };
            }
        }

        /// <summary>RoR2's master x SFX volume sliders (0-100 each), as a 0-1 multiplier.</summary>
        private static float ReadVolume()
        {
            float Get(string name)
            {
                var cv = RoR2.Console.instance?.FindConVar(name);
                return cv != null && float.TryParse(cv.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? Mathf.Clamp01(v / 100f) : 1f;
            }
            return Get("volume_master") * Get("volume_sfx");
        }

        /// <summary>Distance falloff and panning from the camera; your own hero's sounds are close to centred.</summary>
        private static void UpdateVoices()
        {
            Camera cam = Camera.main;
            bool unfocused = !Application.isFocused;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                Playing p = Active[i];
                if (p.Voice.Finished || p.Voice.Stopped || Time.time > p.Until) { p.Voice.Stopped = true; Active.RemoveAt(i); continue; }
                if (p.Follow != null) p.Pos = p.Follow.position + Vector3.up;
                float gain = p.Row.Volume * _volume * (unfocused ? 0f : 1f), pan = 0f;
                if (cam != null)
                {
                    Vector3 to = p.Pos - cam.transform.position;
                    float dist = to.magnitude, max = p.Row.Range * 1.5f;
                    gain *= dist <= 4f ? 1f : Mathf.Clamp01(1f - (dist - 4f) / Mathf.Max(1f, max - 4f));
                    if (dist > 12f) pan = Mathf.Clamp(Vector3.Dot(to / dist, cam.transform.right), -1f, 1f) * 0.8f;
                }
                float a = (pan + 1f) * Mathf.PI / 4f; // equal-power pan
                p.Voice.GainL = gain * Mathf.Cos(a) * 1.414f;
                p.Voice.GainR = gain * Mathf.Sin(a) * 1.414f;
            }
        }

        private static PcmClip? Pick(string id, bool first = false)
        {
            if (!_decoded) return null;
            lock (Clips)
            {
                if (!Clips.TryGetValue(id, out var list) || list.Count == 0) return null;
                return first ? list[0] : list[Random.Range(0, list.Count)];
            }
        }

        private static DeadlockVoice Start(DeadlockSound row, PcmClip clip, Vector3 pos, GameObject? follow, bool loop, float seconds)
        {
            var voice = new DeadlockVoice { Clip = clip, Loop = loop };
            var p = new Playing { Voice = voice, Row = row, Pos = pos, Follow = follow != null ? follow.transform : null };
            if (seconds > 0f) p.Until = Time.time + seconds;
            Active.Add(p);
            UpdateVoices(); // gains set before the mixer first hears it
            NativeAudio.Add(voice);
            return voice;
        }

        /// <summary>Play a sound at a point, or following a GameObject (follow != null).</summary>
        public static void Play(string id, Vector3 pos, GameObject? follow = null)
        {
            if (!Rows.TryGetValue(id, out DeadlockSound row)) return;
            if (LastPlayed.TryGetValue(id, out float last) && Time.time - last < row.MinGap) return;
            if (row.Voice && Time.time - _lastVoice < 1.5f) return; // never talk over yourself
            if (Random.value > row.Chance) return;
            PcmClip? clip = Pick(id);
            if (clip == null || !NativeAudio.Available)
            {
                if (row.Fallback != null && follow != null) RoR2.Util.PlaySound(row.Fallback, follow);
                return;
            }
            LastPlayed[id] = Time.time;
            if (row.Voice) _lastVoice = Time.time;
            Start(row, clip, follow != null ? follow.transform.position : pos, follow, false, 0f);
        }

        /// <summary>A looping sound on an object for up to seconds; returns it so it can be stopped early.</summary>
        public static DeadlockVoice? Loop(string id, GameObject on, float seconds)
        {
            if (on == null || !Rows.TryGetValue(id, out DeadlockSound row) || !NativeAudio.Available) return null;
            PcmClip? clip = Pick(id, first: true);
            return clip == null ? null : Start(row, clip, on.transform.position, on, true, seconds);
        }

        public static void Stop(DeadlockVoice? voice)
        {
            if (voice != null) voice.Stopped = true;
        }
    }
}
