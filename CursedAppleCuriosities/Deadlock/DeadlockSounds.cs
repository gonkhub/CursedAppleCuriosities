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
        /// <summary>Per play: pitch varies by up to +/- this share, volume drops by up to this share (rapid fire stays lively).</summary>
        public float PitchJitter, VolumeJitter;
        /// <summary>Distance never takes it below this share of its volume: team callouts (pings) heard map-wide, still panned.</summary>
        public float MinGain;
        /// <summary>Vanilla Wwise event played instead when this player has no Deadlock copy (or null).</summary>
        public string? Fallback;
        /// <summary>Mix bus, set from the path on Register (see <see cref="DeadlockSounds.BusOf"/>).</summary>
        public DeadlockSounds.Bus Bus;
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
            public bool Followed; // started on an object: Unity's null check turns true once it is destroyed
            public float Until = float.MaxValue;
            public float Gain = 1f;
            public object? Owner; // DeadlockSounds.Owner when it started (a lobby display): StopOwned silences it
        }

        private static DeadlockSounds? _instance;
        private static readonly Dictionary<string, DeadlockSound> Rows = new Dictionary<string, DeadlockSound>();
        private static readonly Dictionary<string, List<PcmClip>> Clips = new Dictionary<string, List<PcmClip>>();
        private static readonly Dictionary<string, float> LastPlayed = new Dictionary<string, float>();
        private static readonly Dictionary<string, int> LastPick = new Dictionary<string, int>();
        private static readonly List<Playing> Active = new List<Playing>();
        private static readonly Dictionary<string, float> LastVoice = new Dictionary<string, float>(); // per speaker
        private static float _volume = 1f, _nextVolumeRead;
        private static volatile bool _decoded;
        private bool _decoding;

        /// <summary>
        /// Mix buses. Deadlock's raw files are mastered hot next to RoR2's mix, and footsteps and guns (which you hear
        /// constantly) worst of all; each row's own Volume stays the balance within its bus.
        /// </summary>
        public enum Bus { Voice, Ability, Movement, Footstep, Weapon }

        private static readonly (string Key, float Default, string Info)[] BusConfig =
        {
            ("Voice Volume", 1f, "Hero voice lines, pings and the Soul Urn."),
            ("Ability Volume", 0.8f, "Ability and item sounds."),
            ("Movement Volume", 0.6f, "Jumps, landings, dashes, slides and wall jumps."),
            ("Footstep Volume", 0.45f, "Hero footsteps."),
            ("Weapon Volume", 0.5f, "Hero gunfire, reloads and weapon impacts."),
        };
        private static BepInEx.Configuration.ConfigEntry<float>[]? _busVolume;

        public static void Bind(BepInEx.Configuration.ConfigFile config)
        {
            _busVolume = new BepInEx.Configuration.ConfigEntry<float>[BusConfig.Length];
            for (int i = 0; i < BusConfig.Length; i++)
                _busVolume[i] = config.Bind("Audio", BusConfig[i].Key, BusConfig[i].Default,
                    BusConfig[i].Info + " Multiplies the mod's Deadlock sounds on top of RoR2's master and SFX sliders (0-2, 1 = as recorded).");
        }

        private static float BusVolume(Bus bus) => _busVolume != null ? Mathf.Clamp(_busVolume[(int)bus].Value, 0f, 2f) : BusConfig[(int)bus].Default;

        /// <summary>Which bus a Deadlock path mixes on: its folder in pak01 says what kind of sound it is.</summary>
        public static Bus BusOf(DeadlockSound s)
        {
            string p = s.VpkPrefix;
            if (s.Voice || p.StartsWith("sounds/vo/")) return Bus.Voice;
            if (p.StartsWith("sounds/weapons/")) return Bus.Weapon;
            if (p.StartsWith("sounds/player/footsteps/")) return p.Contains("jumpland") ? Bus.Movement : Bus.Footstep;
            if (p.Contains("umbrella")) return Bus.Ability; // Mina's hover lives under player/movement but is her kit
            if (p.StartsWith("sounds/player/movement/") || p.StartsWith("sounds/player/wall_jump") || p.StartsWith("sounds/gameplay/slide/")) return Bus.Movement;
            return Bus.Ability; // abilities, items (mods), melee
        }

        public static void Register(DeadlockSound s)
        {
            s.Bus = BusOf(s);
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

        /// <summary>
        /// Where this client hears from: the body its camera is following (your hero, or whoever you spectate), with
        /// left/right from the camera. Taken from RoR2's own camera rig; Camera.main is not reliably set in RoR2, and
        /// falling back to "no listener" used to play every sound unattenuated, so players heard each other map-wide.
        /// </summary>
        private static bool Listener(out Vector3 at, out Vector3 right)
        {
            foreach (RoR2.CameraRigController rig in RoR2.CameraRigController.readOnlyInstancesList)
            {
                if (rig == null || rig.localUserViewer == null || rig.sceneCam == null) continue;
                Transform cam = rig.sceneCam.transform;
                at = rig.targetBody != null ? rig.targetBody.corePosition : cam.position;
                right = cam.right;
                return true;
            }
            Camera main = Camera.main;
            if (main != null) { at = main.transform.position; right = main.transform.right; return true; }
            at = right = default;
            return false;
        }

        /// <summary>Distance falloff and panning around the listener; your own hero's sounds are full and centred.</summary>
        private static void UpdateVoices()
        {
            bool hasListener = Listener(out Vector3 ear, out Vector3 right);
            bool inRun = RoR2.Run.instance != null;
            bool unfocused = !Application.isFocused;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                Playing p = Active[i];
                // A loop whose object is gone or switched off ends with it. Without this, an owner that never called
                // Stop (destroyed some other way, pooled, or a scene change) left the loop droning at the spot it was
                // last seen until its timer ran out: up to an hour for buff loops (playtest 2026-10-12, multiplayer too).
                bool orphan = p.Voice.Loop && p.Followed && (p.Follow == null || !p.Follow.gameObject.activeInHierarchy);
                if (p.Voice.Finished || p.Voice.Stopped || orphan || Time.time > p.Until) { p.Voice.Stopped = true; Active.RemoveAt(i); continue; }
                if (p.Follow != null) p.Pos = p.Follow.position + Vector3.up;
                float gain = p.Row.Volume * BusVolume(p.Row.Bus) * p.Gain * _volume * (unfocused ? 0f : 1f), pan = 0f;
                if (hasListener)
                {
                    Vector3 to = p.Pos - ear;
                    float dist = to.magnitude, near = 5f, max = Mathf.Max(near + 1f, p.Row.Range);
                    float fade = Mathf.Clamp01(1f - (dist - near) / (max - near));
                    gain *= Mathf.Max(p.Row.MinGain, fade * fade); // squared: drops off quickly past the near zone, silent at Range
                    if (dist > 0.01f) pan = Mathf.Clamp(Vector3.Dot(to / dist, right), -1f, 1f) * 0.8f * Mathf.InverseLerp(2f, 8f, dist);
                }
                else if (inRun) gain = 0f; // mid-run with nowhere to hear from (loading, scene change): stay quiet
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
                if (first || list.Count == 1) return list[0];
                // Never the same take twice in a row.
                int i = Random.Range(0, list.Count);
                if (LastPick.TryGetValue(id, out int last) && i == last) i = (i + 1 + Random.Range(0, list.Count - 1)) % list.Count;
                LastPick[id] = i;
                return list[i];
            }
        }

        private static DeadlockVoice Start(DeadlockSound row, PcmClip clip, Vector3 pos, GameObject? follow, bool loop, float seconds)
        {
            var voice = new DeadlockVoice { Clip = clip, Loop = loop };
            if (row.PitchJitter > 0f) voice.Pitch = 1.0 + Random.Range(-row.PitchJitter, row.PitchJitter);
            var p = new Playing { Voice = voice, Row = row, Pos = pos, Follow = follow != null ? follow.transform : null, Followed = follow != null, Owner = Owner };
            if (row.VolumeJitter > 0f) p.Gain = 1f - Random.Range(0f, row.VolumeJitter);
            if (seconds > 0f) p.Until = Time.time + seconds;
            Active.Add(p);
            UpdateVoices(); // gains set before the mixer first hears it
            NativeAudio.Add(voice);
            return voice;
        }

        public static bool Has(string id) => Rows.ContainsKey(id);

        /// <summary>How many takes of a sound this player's cache holds (0 before decoding or without Deadlock).</summary>
        public static int Takes(string id)
        {
            if (!_decoded) return 0;
            lock (Clips) return Clips.TryGetValue(id, out var list) ? list.Count : 0;
        }

        /// <summary>
        /// Play a sound at a point, or following a GameObject (follow != null). <paramref name="speaker"/> names who is
        /// talking when that isn't the followed object (the Soul Urn riding on its carrier), so the two don't cut each other off.
        /// </summary>
        public static void Play(string id, Vector3 pos, GameObject? follow = null, string? speaker = null)
        {
            if (!Rows.TryGetValue(id, out DeadlockSound row)) return;
            if (LastPlayed.TryGetValue(id, out float last) && Time.time - last < row.MinGap) return;
            // A hero never talks over themself; other heroes' lines don't block yours. The speaker is the object the
            // line follows, or the hero prefix ("abrams_vo_death") for lines left at a point.
            speaker ??= follow != null ? follow.GetInstanceID().ToString() : id.Substring(0, Mathf.Max(0, id.IndexOf('_')));
            if (row.Voice && LastVoice.TryGetValue(speaker, out float spoke) && Time.time - spoke < 1.5f) return;
            if (Random.value > row.Chance) return;
            PcmClip? clip = Pick(id);
            if (clip == null || !NativeAudio.Available)
            {
                if (row.Fallback != null && follow != null) RoR2.Util.PlaySound(row.Fallback, follow);
                return;
            }
            LastPlayed[id] = Time.time;
            if (row.Voice) LastVoice[speaker] = Time.time;
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

        /// <summary>
        /// Who is playing sounds right now (main thread; set around a lobby display's intro and select line). Every sound started
        /// meanwhile belongs to them, and StopOwned silences them all at once.
        /// </summary>
        public static object? Owner;

        /// <summary>Silence every sound started for this owner (user 3.0: a hero clicked off in the lobby goes quiet at once).</summary>
        public static void StopOwned(object owner)
        {
            foreach (Playing p in Active) if (p.Owner == owner) p.Voice.Stopped = true;
        }
    }
}
