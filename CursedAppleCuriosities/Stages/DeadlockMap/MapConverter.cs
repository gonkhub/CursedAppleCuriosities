using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CursedAppleCuriosities.Deadlock;

namespace CursedAppleCuriosities.Stages.DeadlockMap
{
    public enum MapState { NotStarted, NoDeadlock, Converting, Ready, Failed }

    /// <summary>
    /// Keeps a section of a Deadlock map converted from the player's own install. The conversion itself runs in its own
    /// process (CursedAppleMapConvert.exe, next to the DLL): the game never carries its memory or CPU load, and the
    /// process stops itself past a memory limit. Here: decide whether the cache is current, launch it, follow its status.
    ///
    /// Ban safety, as for the heroes: Deadlock is only read (the VPK directory here, Source2Viewer-CLI in the converter),
    /// never while it runs, and nothing it holds is shipped; the result lives in the player's own cache.
    /// </summary>
    public class MapConverter
    {
        /// <summary>Bump when the cache content changes (players then convert again).</summary>
        public const int PipelineVersion = 5;
        private const int MaxMemoryMb = 2500;

        public readonly MapSectionDef Def;
        public volatile MapState State;
        public volatile string Status = "not started";
        /// <summary>This install's fingerprint of the section (map build + pipeline): equal on two machines = same geometry.</summary>
        public volatile string? Fingerprint;
        public string CacheFile => Path.Combine(Folder, "section.bin");
        public string NavFile => Path.Combine(Folder, "nav.bin");
        public string Folder => Path.Combine(DeadlockInstall.CacheDir, "maps", Def.Map + "_" + Def.Id);
        public string TexFolder => Path.Combine(Folder, "tex");

        private static void Log(string s) => CursedAppleCuriositiesPlugin.Logger?.LogInfo("[DeadlockMap] " + s);
        private static void Warn(string s) => CursedAppleCuriositiesPlugin.Logger?.LogWarning("[DeadlockMap] " + s);

        public MapConverter(MapSectionDef def) { Def = def; }

        public void Start()
        {
            string folder = Folder, exe = Path.Combine(Path.GetDirectoryName(typeof(MapConverter).Assembly.Location), "CursedAppleMapConvert.exe");
            new Thread(() =>
            {
                try { Run(folder, exe); }
                catch (Exception e) { State = MapState.Failed; Status = "failed: " + e.Message; Warn(e.ToString()); }
            }) { IsBackground = true, Name = "CursedApple map converter", Priority = ThreadPriority.BelowNormal }.Start();
        }

        /// <summary>The section's outline, origin and sealed volumes: a change to any of them means converting again.</summary>
        private static uint ShapeHash(MapSectionDef d)
        {
            uint h = 2166136261;
            void Add(float v) { h ^= (uint)BitConverter.ToInt32(BitConverter.GetBytes(v), 0); h *= 16777619; }
            foreach (float v in d.Outline) Add(v);
            foreach (float v in d.Origin) Add(v);
            foreach (int v in d.NoWalk) Add(v);
            return h;
        }

        /// <summary>Two fingerprints that differ only in Deadlock's map checksum.</summary>
        private static bool SameSection(string a, string b)
        {
            string[] x = a.Split(':'), y = b.Split(':');
            if (x.Length != 5 || y.Length != 5) return false;
            for (int i = 0; i < 5; i++) if (i != 1 && x[i] != y[i]) return false;
            return true;
        }

        private void Run(string folder, string exe)
        {
            string? citadel = DeadlockInstall.CitadelDir();
            string mapVpk = citadel != null ? Path.Combine(citadel, "maps", Def.Map + ".vpk") : "";
            if (citadel == null || !File.Exists(mapVpk))
            {
                State = MapState.NoDeadlock;
                Status = citadel == null ? "Deadlock not found" : $"Deadlock has no {Def.Map}";
                Log(Status);
                return;
            }
            string cacheFile = Path.Combine(folder, "section.bin");
            string? cached = File.Exists(cacheFile) ? MapCache.ReadFingerprint(cacheFile) : null;
            if (DeadlockInstall.DeadlockRunning())
            {
                // Never read Deadlock while it runs: the cache it made earlier is trusted as is.
                if (cached != null) { Fingerprint = cached; State = MapState.Ready; Status = "cached (Deadlock is running, not re-checked)"; }
                else { State = MapState.NoDeadlock; Status = "Deadlock is running: close it and restart Risk of Rain 2 to convert the map"; }
                Log(Status);
                return;
            }
            string node = $"maps/{Def.Map}/worldnodes/n0.vwnod_c";
            var crcs = DeadlockInstall.VpkCrcs(mapVpk, p => p == node);
            if (!crcs.TryGetValue(node, out uint crc)) throw new InvalidDataException($"{node} not in {mapVpk}");
            string fingerprint = $"{Def.Map}:{crc:x8}:{Def.Id}:{PipelineVersion}:{ShapeHash(Def):x8}";
            if (cached == fingerprint) { Fingerprint = fingerprint; State = MapState.Ready; Status = "cached"; Log($"{Def.Id}: cached ({fingerprint})"); return; }
            string cli = DeadlockInstall.Converter();
            if (!File.Exists(cli) || !File.Exists(exe)) { State = MapState.Failed; Status = "converter missing"; Warn($"{Status}: {cli} / {exe}"); return; }

            State = MapState.Converting;
            Status = "converting (first launch after a Deadlock map update: several minutes)";
            Log($"{Def.Id}: converting {Def.Map} ({fingerprint}, was {cached ?? "none"}) in {Path.GetFileName(exe)}...");
            var watch = Stopwatch.StartNew();
            var psi = new ProcessStartInfo(exe, $"--cli \"{cli}\" --citadel \"{citadel}\" --section {Def.Id} --fingerprint \"{fingerprint}\" --out \"{folder}\" --max-mb {MaxMemoryMb} --parallel 2")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            int exit;
            using (Process p = Process.Start(psi))
            {
                p.ErrorDataReceived += (o, e) => { if (!string.IsNullOrEmpty(e.Data)) Warn(e.Data); };
                p.BeginErrorReadLine();
                string? line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    if (line.StartsWith("status: ")) Status = "converting: " + line.Substring(8);
                    else if (line.StartsWith("log: ")) Log(line.Substring(5));
                }
                p.WaitForExit();
                exit = p.ExitCode;
            }
            if (exit != 0 || MapCache.ReadFingerprint(cacheFile) != fingerprint)
            {
                string why = exit == 3 ? "conversion stopped at its memory limit" : $"conversion failed (exit {exit})";
                // The converter only replaces the cache on success, so the one from the previous Deadlock build is still
                // there: keep playing on it when only Deadlock's map changed (same section, pipeline and shape).
                string? kept = File.Exists(cacheFile) ? MapCache.ReadFingerprint(cacheFile) : null;
                if (kept != null && SameSection(kept, fingerprint))
                {
                    Fingerprint = kept;
                    State = MapState.Ready;
                    Status = $"{why}: using the map from the previous Deadlock build";
                    Warn($"{Def.Id}: {Status} ({kept})");
                    return;
                }
                State = MapState.Failed;
                Status = why;
                Warn(Status);
                return;
            }
            if (File.Exists(Path.Combine(folder, "nav.bin"))) File.Delete(Path.Combine(folder, "nav.bin")); // built for the old geometry
            Fingerprint = fingerprint;
            State = MapState.Ready;
            Status = $"converted in {watch.Elapsed.TotalMinutes:0.0} min";
            Log($"{Def.Id}: {Status}");
        }
    }
}
