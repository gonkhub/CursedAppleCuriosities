using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace CursedAppleCuriosities.Deadlock
{
    /// <summary>One decoded sound: mono 16-bit samples at its own rate.</summary>
    public class PcmClip
    {
        public short[] Samples = Array.Empty<short>();
        public int Rate = 44100;
        public float Length => Samples.Length / (float)Rate;
    }

    /// <summary>A playing sound. The main thread sets its gains every frame; the mixer thread reads them.</summary>
    public class DeadlockVoice
    {
        public PcmClip Clip = null!;
        public bool Loop;
        public volatile float GainL, GainR;
        public volatile bool Stopped;   // set by anyone: the mixer drops it
        public volatile bool Finished;  // set by the mixer when a one-shot reaches its end
        internal double Pos;
    }

    /// <summary>
    /// RoR2 ships with Unity's audio engine switched off (AudioManager m_DisableAudio; all game sound is Wwise),
    /// so Unity AudioSources are silent. This is a small software mixer that writes straight to the Windows audio
    /// output (winmm waveOut, also present under Proton/Wine): 48 kHz stereo, a few short buffers queued.
    /// </summary>
    public static class NativeAudio
    {
        private const int Rate = 48000, Frames = 1024, BufferCount = 4;
        private static readonly List<DeadlockVoice> Voices = new List<DeadlockVoice>();
        private static Thread? _thread;
        private static volatile bool _running;
        public static bool Available { get; private set; }

        public static void Start()
        {
            if (_thread != null) return;
            _running = true;
            _thread = new Thread(Run) { IsBackground = true, Name = "CursedApple Deadlock audio" };
            _thread.Start();
        }

        public static void Add(DeadlockVoice v) { lock (Voices) Voices.Add(v); }

        // ---- winmm ----

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHdr
        {
            public IntPtr lpData;
            public int dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public int dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        private const int WHDR_DONE = 1;

        [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr h, int device, ref WaveFormatEx fmt, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);

        private static void Run()
        {
            try
            {
                var fmt = new WaveFormatEx { wFormatTag = 1, nChannels = 2, nSamplesPerSec = Rate, wBitsPerSample = 16, nBlockAlign = 4, nAvgBytesPerSec = Rate * 4 };
                if (waveOutOpen(out IntPtr h, -1, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0)
                {
                    CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Deadlock] audio: no Windows audio output (waveOutOpen failed)");
                    return;
                }
                int hdrSize = Marshal.SizeOf<WaveHdr>();
                int flagsAt = (int)Marshal.OffsetOf<WaveHdr>("dwFlags");
                var hdrs = new IntPtr[BufferCount];
                var written = new bool[BufferCount];
                for (int i = 0; i < BufferCount; i++)
                {
                    hdrs[i] = Marshal.AllocHGlobal(hdrSize);
                    var wh = new WaveHdr { lpData = Marshal.AllocHGlobal(Frames * 4), dwBufferLength = Frames * 4 };
                    Marshal.StructureToPtr(wh, hdrs[i], false);
                    waveOutPrepareHeader(h, hdrs[i], hdrSize);
                }
                Available = true;
                CursedAppleCuriositiesPlugin.Logger?.LogInfo("[Deadlock] audio: own output open (RoR2 has Unity audio disabled)");
                var mix = new float[Frames * 2];
                var pcm = new short[Frames * 2];
                while (_running)
                {
                    bool wrote = false;
                    for (int i = 0; i < BufferCount; i++)
                    {
                        if (written[i] && (Marshal.ReadInt32(hdrs[i], flagsAt) & WHDR_DONE) == 0) continue; // still playing
                        Mix(mix);
                        for (int k = 0; k < mix.Length; k++) pcm[k] = (short)Math.Max(-32768f, Math.Min(32767f, mix[k] * 32767f));
                        var wh = Marshal.PtrToStructure<WaveHdr>(hdrs[i]);
                        Marshal.Copy(pcm, 0, wh.lpData, pcm.Length);
                        waveOutWrite(h, hdrs[i], hdrSize);
                        written[i] = true;
                        wrote = true;
                    }
                    if (!wrote) Thread.Sleep(3);
                }
            }
            catch (Exception e)
            {
                CursedAppleCuriositiesPlugin.Logger?.LogWarning("[Deadlock] audio thread: " + e.Message);
            }
        }

        /// <summary>Sum every voice into one stereo block (linear resampling from each clip's rate).</summary>
        private static void Mix(float[] mix)
        {
            Array.Clear(mix, 0, mix.Length);
            lock (Voices)
            {
                for (int v = Voices.Count - 1; v >= 0; v--)
                {
                    DeadlockVoice voice = Voices[v];
                    short[] s = voice.Clip.Samples;
                    if (voice.Stopped || s.Length == 0) { Voices.RemoveAt(v); continue; }
                    double step = voice.Clip.Rate / (double)Rate;
                    float l = voice.GainL, r = voice.GainR;
                    for (int f = 0; f < Frames; f++)
                    {
                        if (voice.Pos >= s.Length - 1)
                        {
                            if (!voice.Loop) { voice.Finished = true; break; }
                            voice.Pos -= s.Length - 1;
                        }
                        int i = (int)voice.Pos;
                        float t = (float)(voice.Pos - i);
                        float x = (s[i] + (s[i + 1] - s[i]) * t) / 32768f;
                        mix[f * 2] += x * l;
                        mix[f * 2 + 1] += x * r;
                        voice.Pos += step;
                    }
                    if (voice.Finished) Voices.RemoveAt(v);
                }
            }
        }
    }
}
