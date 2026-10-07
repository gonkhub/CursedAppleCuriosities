using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using UnityEngine;

namespace CursedAppleCuriosities.Deadlock
{
    /// <summary>
    /// Reads the player's own Deadlock install (ported from HeroBrew): hero models and animations (converted once
    /// by Source2Viewer-CLI, MIT), sounds and HUD icons, copied into a local cache.
    ///
    /// Ban safety: Deadlock is read-only data. It is never launched, written, hooked or injected; nothing is
    /// read while deadlock.exe runs (the cache is used instead); no Valve file is ever shipped with the mod.
    /// </summary>
    public static class DeadlockInstall
    {
        public const int DeadlockAppId = 1422450;

        public class ModelRequest
        {
            public string Hero = "";
            public string DlModel = "";          // model path in pak01, e.g. models/heroes_wip/lash/lash.vmdl_c
            public string[] Clips = Array.Empty<string>();
            public string[] HideMeshes = Array.Empty<string>();
        }

        public static ConfigEntry<string>? ConverterPath;
        public static ConfigEntry<bool>? ShowModels;

        public static string CacheDir { get; private set; } = "";
        public static volatile bool FilesDone;
        public static string Status { get; private set; } = "not started";

        private static readonly List<string> WantedSounds = new List<string>();   // vpk path prefixes (variants _NN)
        private static readonly List<string> WantedIcons = new List<string>();    // exact vtex_c paths
        private static readonly List<ModelRequest> WantedModels = new List<ModelRequest>();

        /// <summary>Cached MP3 files per wanted sound prefix (all variants).</summary>
        private static readonly Dictionary<string, List<string>> SoundFiles = new Dictionary<string, List<string>>();
        private static readonly Dictionary<string, string> IconFiles = new Dictionary<string, string>();
        private static readonly Dictionary<string, Texture2D> IconTextures = new Dictionary<string, Texture2D>();

        private static readonly Dictionary<string, string> ReadyModels = new Dictionary<string, string>();  // hero -> glb
        private static readonly Dictionary<string, string> ReadyClips = new Dictionary<string, string>();   // hero -> clips glb
        private static readonly HashSet<string> FailedModels = new HashSet<string>();
        private static readonly Dictionary<string, GlbModel> LoadedModels = new Dictionary<string, GlbModel>();
        private static readonly Dictionary<string, Dictionary<string, AnimClip>> LoadedClips = new Dictionary<string, Dictionary<string, AnimClip>>();

        private static void Log(string s) => CursedAppleCuriositiesPlugin.Logger?.LogInfo("[Deadlock] " + s);
        private static void Warn(string s) => CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Deadlock] " + s);

        public static void Bind(ConfigFile config)
        {
            ConverterPath = config.Bind("Deadlock", "Converter", "",
                "Source2Viewer-CLI.exe used once to convert Deadlock hero models from YOUR Deadlock install. Blank = the vrf folder next to the mod DLL.");
            ShowModels = config.Bind("Deadlock", "Hero Models", true,
                "Show Deadlock hero models (needs Deadlock installed). Off = heroes use the stand-in vanilla model.");
            CacheDir = Path.Combine(Application.persistentDataPath, "CursedApple", "deadlock"); // main thread only
            _gameDir = Path.GetFullPath(Application.dataPath).Replace('/', '\\');
        }

        public static void WantSound(string vpkPrefix) { if (!WantedSounds.Contains(vpkPrefix)) WantedSounds.Add(vpkPrefix); }
        public static void WantIcon(string vtexPath) { if (!WantedIcons.Contains(vtexPath)) WantedIcons.Add(vtexPath); }
        public static void WantModel(ModelRequest m) => WantedModels.Add(m);

        /// <summary>One background thread: copy sounds and icons, then convert any model not cached yet.</summary>
        public static void Start()
        {
            string converter = !string.IsNullOrWhiteSpace(ConverterPath?.Value) ? ConverterPath!.Value : DefaultConverter();
            new System.Threading.Thread(() =>
            {
                try { ExtractFiles(); }
                catch (Exception e) { Status = "failed: " + e.Message; Warn("files: " + e); }
                finally { FilesDone = true; }
                foreach (ModelRequest m in WantedModels)
                {
                    try { ConvertModel(m, converter); }
                    catch (Exception e) { lock (FailedModels) FailedModels.Add(m.Hero); Warn($"model {m.Hero}: {e.Message}"); }
                }
            }) { IsBackground = true, Name = "CursedApple Deadlock reader" }.Start();
        }

        private static string DefaultConverter()
        {
            string here = Path.GetDirectoryName(typeof(DeadlockInstall).Assembly.Location);
            return Path.Combine(here, "vrf", "Source2Viewer-CLI.exe");
        }

        // ---- main-thread accessors ----

        /// <summary>The hero's model template once converted (null until then, when unavailable, or switched off).</summary>
        public static GlbModel? Model(string hero, Func<string, Texture2D?, Material> makeMaterial)
        {
            if (ShowModels != null && !ShowModels.Value) return null;
            lock (FailedModels) if (FailedModels.Contains(hero)) return null;
            if (LoadedModels.TryGetValue(hero, out GlbModel m)) return m;
            string? file;
            lock (ReadyModels) ReadyModels.TryGetValue(hero, out file);
            if (file == null) return null;
            try
            {
                var hide = new HashSet<string>();
                foreach (ModelRequest r in WantedModels) if (r.Hero == hero) foreach (string s in r.HideMeshes) hide.Add(s);
                m = GlbLoader.Load(file, makeMaterial, hide);
                LoadedModels[hero] = m;
                Log($"model for {hero} loaded: {m.Bones.Count} bones, height {m.Height:0.00}");
                return m;
            }
            catch (Exception e)
            {
                lock (FailedModels) FailedModels.Add(hero);
                Warn($"model {hero} failed to load: {e}");
                return null;
            }
        }

        /// <summary>The hero's animation clips by name (null until converted or when unavailable).</summary>
        public static Dictionary<string, AnimClip>? Clips(string hero)
        {
            if (LoadedClips.TryGetValue(hero, out var clips)) return clips;
            string? file;
            lock (ReadyClips) ReadyClips.TryGetValue(hero, out file);
            if (file == null) return null;
            try
            {
                clips = GlbLoader.LoadClips(file);
                Log($"animations for {hero} loaded: {clips.Count} clips");
            }
            catch (Exception e) { clips = new Dictionary<string, AnimClip>(); Warn($"animations {hero} failed to load: {e}"); }
            LoadedClips[hero] = clips;
            return clips;
        }

        /// <summary>Cached MP3 files for a sound prefix (empty until extracted or when unavailable).</summary>
        public static List<string> SoundFilesFor(string vpkPrefix)
        {
            lock (SoundFiles) return SoundFiles.TryGetValue(vpkPrefix, out var l) ? new List<string>(l) : new List<string>();
        }

        /// <summary>An icon texture for a pak01 vtex path, or null (not extracted yet, or no Deadlock here).</summary>
        public static Texture2D? Icon(string vtexPath)
        {
            if (IconTextures.TryGetValue(vtexPath, out Texture2D tex)) return tex;
            string? file;
            lock (IconFiles) IconFiles.TryGetValue(vtexPath, out file);
            if (file == null || !File.Exists(file)) return null;
            byte[] b = File.ReadAllBytes(file);
            if (b.Length > 8 && ((b[0] == 0x89 && b[1] == (byte)'P') || (b[0] == 0xFF && b[1] == 0xD8))) // PNG or JPEG
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = Path.GetFileNameWithoutExtension(file), wrapMode = TextureWrapMode.Clamp };
                GlbLoader.LoadPng(tex, b);
            }
            else
            {
                int w = BitConverter.ToInt32(b, 0), h = BitConverter.ToInt32(b, 4);
                var flipped = new byte[w * h * 4];
                for (int y = 0; y < h; y++) Buffer.BlockCopy(b, 8 + y * w * 4, flipped, (h - 1 - y) * w * 4, w * 4); // Source rows run top-down
                tex = new Texture2D(w, h, TextureFormat.BGRA32, false) { name = Path.GetFileNameWithoutExtension(file), wrapMode = TextureWrapMode.Clamp };
                tex.LoadRawTextureData(flipped);
                tex.Apply();
            }
            IconTextures[vtexPath] = tex;
            return tex;
        }

        // ---- finding Deadlock ----

        public static bool DeadlockRunning() => Process.GetProcessesByName("deadlock").Length > 0;

        /// <summary>The player's Deadlock game/citadel folder, or null.</summary>
        public static string? CitadelDir()
        {
            foreach (string root in Candidates())
            {
                string citadel = Path.Combine(root, "game", "citadel");
                if (File.Exists(Path.Combine(citadel, "pak01_dir.vpk"))) return citadel;
            }
            return null;
        }

        private static string _gameDir = "";

        private static IEnumerable<string> Candidates()
        {
            string env = Environment.GetEnvironmentVariable("CURSEDAPPLE_DEADLOCK_DIR");
            if (!string.IsNullOrEmpty(env)) yield return env;
            // Steam libraries: the default Steam folder, the library RoR2 itself runs from, and every library
            // the libraryfolders.vdf files list.
            var libraries = new List<string> { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" };
            int steamapps = _gameDir.IndexOf(@"\steamapps\", StringComparison.OrdinalIgnoreCase);
            if (steamapps > 0) libraries.Add(_gameDir.Substring(0, steamapps));
            foreach (string lib in libraries.ToArray())
            {
                string vdf = Path.Combine(lib, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            foreach (string lib in libraries)
            {
                string manifest = Path.Combine(lib, "steamapps", $"appmanifest_{DeadlockAppId}.acf");
                if (!File.Exists(manifest)) continue;
                Match dir = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                if (dir.Success) yield return Path.Combine(lib, "steamapps", "common", dir.Groups[1].Value);
            }
        }

        // ---- sounds and icons ----

        private static string CachedName(string vpkPath) =>
            vpkPath.EndsWith(".vsnd_c") ? vpkPath.Replace(".vsnd_c", ".mp3")
            : vpkPath.EndsWith(".vtex_c") ? vpkPath.Replace(".vtex_c", ".bgra")
            : vpkPath;

        private static void ExtractFiles()
        {
            string? dir = CitadelDir();
            string indexFile = Path.Combine(CacheDir, "index.txt");
            if (dir == null || DeadlockRunning())
            {
                Status = dir == null ? "Deadlock not found: cached files only" : "Deadlock is running: cached files only";
                foreach (var kv in ReadIndex(indexFile))
                {
                    string f = Path.Combine(CacheDir, CachedName(kv.Key));
                    if (File.Exists(f)) Register(kv.Key, f);
                }
                Log(Status);
                return;
            }
            var index = ReadIndex(indexFile);
            var newIndex = new Dictionary<string, uint>();
            int copied = 0, kept = 0;
            foreach (VpkEntry e in ReadVpkDirectory(Path.Combine(dir, "pak01_dir.vpk")))
            {
                string out_ = Path.Combine(CacheDir, CachedName(e.Path));
                newIndex[e.Path] = e.Crc;
                if (index.TryGetValue(e.Path, out uint crc) && crc == e.Crc && File.Exists(out_)) { kept++; Register(e.Path, out_); continue; }
                byte[] data = ReadEntry(dir, e);
                byte[]? converted = e.Path.EndsWith(".vtex_c") ? IconFromVtex(data) : Mp3FromVsnd(data);
                if (converted == null) { Warn($"{e.Path}: unsupported format"); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(out_));
                File.WriteAllBytes(out_, converted);
                Register(e.Path, out_);
                copied++;
            }
            WriteIndex(indexFile, newIndex);
            Status = $"{copied} copied, {kept} already cached";
            Log("files: " + Status);
        }

        private static void Register(string vpkFile, string cached)
        {
            if (vpkFile.EndsWith(".vtex_c")) { lock (IconFiles) IconFiles[vpkFile] = cached; return; }
            foreach (string w in WantedSounds)
            {
                if (!MatchesSound(w, vpkFile)) continue;
                lock (SoundFiles)
                {
                    if (!SoundFiles.TryGetValue(w, out var list)) SoundFiles[w] = list = new List<string>();
                    if (!list.Contains(cached)) list.Add(cached);
                    list.Sort();
                }
            }
        }

        /// <summary>"prefix.vsnd_c" or "prefix_NN.vsnd_c" (sound variants).</summary>
        private static bool MatchesSound(string prefix, string file)
        {
            if (!file.StartsWith(prefix, StringComparison.Ordinal) || !file.EndsWith(".vsnd_c", StringComparison.Ordinal)) return false;
            string mid = file.Substring(prefix.Length, file.Length - prefix.Length - ".vsnd_c".Length);
            return mid.Length == 0 || Regex.IsMatch(mid, @"^_\d+$");
        }

        // ---- models ----

        private static string Fnv(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h.ToString("x8");
        }

        private static void ConvertModel(ModelRequest m, string converter)
        {
            string baseName = Path.GetFileNameWithoutExtension(m.DlModel);
            string folder = Path.Combine(CacheDir, "models", m.Hero);
            string cache = Path.Combine(folder, baseName + ".glb");
            string clips = string.Join(",", new SortedSet<string>(m.Clips, StringComparer.Ordinal));
            string animCache = Path.Combine(folder, baseName + "_clips_" + Fnv(clips) + ".glb"); // new clip list = new file

            if (File.Exists(cache)) lock (ReadyModels) ReadyModels[m.Hero] = cache;
            if (File.Exists(animCache)) lock (ReadyClips) ReadyClips[m.Hero] = animCache;
            if (File.Exists(cache) && (clips.Length == 0 || File.Exists(animCache))) { Log($"model {m.Hero}: cached"); return; }

            string? citadel = CitadelDir();
            if (citadel == null) { Log($"model {m.Hero}: Deadlock not found, stand-in model"); return; }
            if (DeadlockRunning()) { Log($"model {m.Hero}: Deadlock is running, conversion waits for the next launch"); return; }
            if (!File.Exists(converter)) { Warn($"model {m.Hero}: converter missing at {converter}"); lock (FailedModels) FailedModels.Add(m.Hero); return; }
            Directory.CreateDirectory(folder);
            string vpk = Path.Combine(citadel, "pak01_dir.vpk");

            if (!File.Exists(cache))
            {
                Log($"converting {m.Hero} model (once, read-only)...");
                if (RunConverter(converter, $"-i \"{vpk}\" -f \"{m.DlModel}\" -d --gltf_export_format glb --gltf_export_materials " +
                        "--gltf_textures_adapt --gltf_export_animations --gltf_animation_list cursedapple_none", cache))
                {
                    lock (ReadyModels) ReadyModels[m.Hero] = cache;
                    Log($"{m.Hero} model cached at {cache}");
                }
                else { Warn($"{m.Hero}: conversion produced no model"); lock (FailedModels) FailedModels.Add(m.Hero); return; }
            }
            if (clips.Length > 0 && !File.Exists(animCache))
            {
                Log($"converting {m.Clips.Length} {m.Hero} animations (once, read-only)...");
                if (RunConverter(converter, $"-i \"{vpk}\" -f \"{m.DlModel}\" -d --gltf_export_format glb --gltf_export_animations --gltf_animation_list \"{clips}\"", animCache))
                {
                    lock (ReadyClips) ReadyClips[m.Hero] = animCache;
                    Log($"{m.Hero} animations cached at {animCache}");
                }
                else Warn($"{m.Hero}: animation conversion produced nothing (model stays in its bind pose)");
            }
        }

        /// <summary>Run the converter into a temporary file, renamed when complete (a cut-short run never looks cached).</summary>
        private static bool RunConverter(string converter, string args, string output)
        {
            string part = output + ".part.glb";
            var psi = new ProcessStartInfo(converter, $"{args} -o \"{part}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (Process p = Process.Start(psi))
            {
                // Drain both streams: an unread stream fills its buffer and the converter blocks forever.
                p.ErrorDataReceived += (o, e) => { };
                p.BeginErrorReadLine();
                p.StandardOutput.ReadToEnd();
                p.WaitForExit(15 * 60 * 1000);
            }
            if (!File.Exists(part)) return false;
            if (File.Exists(output)) File.Delete(output);
            File.Move(part, output);
            return true;
        }

        // ---- VPK v2 (read-only) ----

        private struct VpkEntry
        {
            public string Path;
            public uint Crc;
            public ushort Archive;
            public uint Offset, Length;
            public byte[] Preload;
            public long DataStart;
        }

        private static List<VpkEntry> ReadVpkDirectory(string file)
        {
            var result = new List<VpkEntry>();
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs))
            {
                if (br.ReadUInt32() != 0x55aa1234) throw new InvalidDataException("not a VPK");
                uint version = br.ReadUInt32();
                uint treeSize = br.ReadUInt32();
                int header = version == 2 ? 28 : 12;
                fs.Position = header;
                for (;;)
                {
                    string ext = ReadZ(br);
                    if (ext.Length == 0) break;
                    for (;;)
                    {
                        string dir = ReadZ(br);
                        if (dir.Length == 0) break;
                        for (;;)
                        {
                            string name = ReadZ(br);
                            if (name.Length == 0) break;
                            uint crc = br.ReadUInt32();
                            ushort preload = br.ReadUInt16();
                            ushort archive = br.ReadUInt16();
                            uint offset = br.ReadUInt32();
                            uint length = br.ReadUInt32();
                            br.ReadUInt16(); // terminator
                            string path = (dir == " " ? "" : dir + "/") + name + "." + ext;
                            bool want = ext == "vsnd_c" ? WantedSounds.Exists(w => MatchesSound(w, path)) : ext == "vtex_c" && WantedIcons.Contains(path);
                            byte[] pre = preload > 0 ? br.ReadBytes(preload) : Array.Empty<byte>();
                            if (want)
                                result.Add(new VpkEntry { Path = path, Crc = crc, Archive = archive, Offset = offset, Length = length, Preload = pre, DataStart = header + treeSize });
                        }
                    }
                }
            }
            return result;
        }

        private static byte[] ReadEntry(string citadel, VpkEntry e)
        {
            var data = new byte[e.Preload.Length + e.Length];
            Buffer.BlockCopy(e.Preload, 0, data, 0, e.Preload.Length);
            if (e.Length == 0) return data;
            string file = e.Archive == 0x7fff ? Path.Combine(citadel, "pak01_dir.vpk") : Path.Combine(citadel, $"pak01_{e.Archive:000}.vpk");
            long at = e.Archive == 0x7fff ? e.DataStart + e.Offset : e.Offset;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Position = at;
                int read = 0;
                while (read < e.Length)
                {
                    int n = fs.Read(data, e.Preload.Length + read, (int)e.Length - read);
                    if (n <= 0) throw new EndOfStreamException(e.Path);
                    read += n;
                }
            }
            return data;
        }

        /// <summary>
        /// A compiled Source 2 texture (vtex_c): uncompressed BGRA8888 (HUD icons) or an embedded PNG/JPEG.
        /// Output: int width, int height, then the largest mip's BGRA pixels; or the PNG/JPEG file as is.
        /// </summary>
        private static byte[]? IconFromVtex(byte[] b)
        {
            if (b.Length < 16) return null;
            int blockOffset = 8 + BitConverter.ToInt32(b, 8);
            int blockCount = BitConverter.ToInt32(b, 12);
            int end = 0, data = -1;
            for (int i = 0; i < blockCount; i++)
            {
                int p = blockOffset + i * 12;
                int off = p + 4 + BitConverter.ToInt32(b, p + 4);
                int size = BitConverter.ToInt32(b, p + 8);
                if (Encoding.ASCII.GetString(b, p, 4) == "DATA") data = off;
                end = Math.Max(end, off + size);
            }
            if (data < 0) return null;
            int width = BitConverter.ToUInt16(b, data + 20);
            int height = BitConverter.ToUInt16(b, data + 22);
            byte format = b[data + 26];
            int bytes = width * height * 4;
            if (format == 15 || format == 16 || format == 17 || format == 18)
            {
                for (int i = end; i + 8 < b.Length; i++)
                    if ((b[i] == 0x89 && b[i + 1] == (byte)'P' && b[i + 2] == (byte)'N' && b[i + 3] == (byte)'G') || (b[i] == 0xFF && b[i + 1] == 0xD8 && b[i + 2] == 0xFF))
                    {
                        var img = new byte[b.Length - i];
                        Buffer.BlockCopy(b, i, img, 0, img.Length);
                        return img;
                    }
                return null;
            }
            if (format != 28 || width == 0 || height == 0 || b.Length - end < bytes) return null;
            var o = new byte[8 + bytes];
            Buffer.BlockCopy(BitConverter.GetBytes(width), 0, o, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(height), 0, o, 4, 4);
            Buffer.BlockCopy(b, b.Length - bytes, o, 8, bytes);
            return o;
        }

        /// <summary>A compiled Source 2 sound (vsnd_c) holds MP3 audio after the resource blocks.</summary>
        private static byte[]? Mp3FromVsnd(byte[] b)
        {
            if (b.Length < 16) return null;
            int blockOffset = 8 + BitConverter.ToInt32(b, 8);
            int blockCount = BitConverter.ToInt32(b, 12);
            int end = 0;
            for (int i = 0; i < blockCount; i++)
            {
                int p = blockOffset + i * 12;
                if (p + 12 > b.Length) return null;
                int off = p + 4 + BitConverter.ToInt32(b, p + 4);
                int size = BitConverter.ToInt32(b, p + 8);
                end = Math.Max(end, off + size);
            }
            if (end <= 0 || end + 2 >= b.Length) return null;
            bool id3 = b[end] == (byte)'I' && b[end + 1] == (byte)'D' && b[end + 2] == (byte)'3';
            bool frame = b[end] == 0xFF && (b[end + 1] & 0xE0) == 0xE0;
            if (!id3 && !frame) return null;
            var mp3 = new byte[b.Length - end];
            Buffer.BlockCopy(b, end, mp3, 0, mp3.Length);
            return mp3;
        }

        private static string ReadZ(BinaryReader br)
        {
            var sb = new StringBuilder();
            byte c;
            while ((c = br.ReadByte()) != 0) sb.Append((char)c);
            return sb.ToString();
        }

        private static Dictionary<string, uint> ReadIndex(string file)
        {
            var d = new Dictionary<string, uint>();
            if (!File.Exists(file)) return d;
            foreach (string line in File.ReadAllLines(file))
            {
                int sp = line.LastIndexOf(' ');
                if (sp > 0 && uint.TryParse(line.Substring(sp + 1), out uint crc)) d[line.Substring(0, sp)] = crc;
            }
            return d;
        }

        private static void WriteIndex(string file, Dictionary<string, uint> index)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var lines = new List<string>();
            foreach (var kv in index) lines.Add(kv.Key + " " + kv.Value);
            File.WriteAllLines(file, lines.ToArray());
        }
    }
}
