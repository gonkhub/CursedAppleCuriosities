using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CursedAppleCuriosities.Stages.DeadlockMap;

namespace CursedAppleMapConvert
{
    /// <summary>
    /// CursedAppleMapConvert --cli &lt;Source2Viewer-CLI.exe&gt; --citadel &lt;game\citadel&gt; --section &lt;id&gt; --fingerprint &lt;fp&gt; --out &lt;folder&gt;
    ///   [--max-mb 3000] [--parallel 2]
    /// Prints "status: ..." and "log: ..." lines for the mod. Exit 0 = cache written, 2 = bad arguments, 3 = memory limit, 1 = failed.
    /// Stops itself if its memory passes --max-mb, so a bad conversion can never starve the machine.
    /// </summary>
    internal static class Program
    {
        private static readonly object Out = new object();
        /// <summary>The converter processes this run started (the memory guard stops only these).</summary>
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Process> Children = new System.Collections.Concurrent.ConcurrentDictionary<int, Process>();
        public static void Status(string s) { lock (Out) Console.WriteLine("status: " + s); }
        public static void Log(string s) { lock (Out) Console.WriteLine("log: " + s); }
        public static bool Trace;
        public static int MaxBatches = int.MaxValue;
        /// <summary>Trace mode: a line with the managed heap and the process's private memory.</summary>
        public static void Mem(string what)
        {
            if (!Trace) return;
            Process self = Process.GetCurrentProcess();
            Log($"mem {GC.GetTotalMemory(false) / (1024 * 1024)} MB managed, {self.PrivateMemorySize64 / (1024 * 1024)} MB private: {what}");
        }

        private static int Main(string[] argv)
        {
            var a = new Dictionary<string, string>();
            for (int i = 0; i < argv.Length; i++)
            {
                bool flag = i + 1 >= argv.Length || argv[i + 1].StartsWith("--");
                a[argv[i].TrimStart('-')] = flag ? "" : argv[++i];
            }
            if (!a.TryGetValue("cli", out string cli) || !a.TryGetValue("citadel", out string citadel) || !a.TryGetValue("section", out string id)
                || !a.TryGetValue("fingerprint", out string fp) || !a.TryGetValue("out", out string outDir))
            {
                Console.Error.WriteLine("usage: --cli <exe> --citadel <dir> --section <id> --fingerprint <fp> --out <folder> [--max-mb N] [--parallel N]");
                return 2;
            }
            MapSectionDef? def = id == MapSectionDef.SunkenPlaza.Id ? MapSectionDef.SunkenPlaza : null;
            if (def == null) { Console.Error.WriteLine("unknown section " + id); return 2; }
            long maxMb = a.TryGetValue("max-mb", out string m) && long.TryParse(m, out long mm) ? mm : 3000;
            int parallel = a.TryGetValue("parallel", out string p) && int.TryParse(p, out int pp) ? pp : 2;
            Trace = a.ContainsKey("trace");
            if (a.TryGetValue("max-batches", out string mbs) && int.TryParse(mbs, out int mbn)) MaxBatches = mbn;

            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            StartMemoryGuard(maxMb);
            var watch = Stopwatch.StartNew();
            try
            {
                Directory.CreateDirectory(outDir);
                new Converter(def, cli, citadel, outDir, fp, parallel).Run();
                Log($"done in {watch.Elapsed.TotalMinutes:0.0} min, peak memory {Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024)} MB");
                return 0;
            }
            catch (Exception e)
            {
                Log("failed: " + e);
                return 1;
            }
        }

        /// <summary>Checks memory twice a second; past the limit, kills its converter children and exits.</summary>
        private static void StartMemoryGuard(long maxMb)
        {
            var t = new Thread(() =>
            {
                Process self = Process.GetCurrentProcess();
                while (true)
                {
                    Thread.Sleep(500);
                    self.Refresh();
                    long mb = self.PrivateMemorySize64 / (1024 * 1024);
                    if (mb <= maxMb) continue;
                    Log($"memory limit: {mb} MB > {maxMb} MB, stopping");
                    foreach (Process child in Children.Values)
                        try { if (!child.HasExited) child.Kill(); } catch { }
                    Environment.Exit(3);
                }
            }) { IsBackground = true, Name = "memory guard" };
            t.Start();
        }
    }
}
