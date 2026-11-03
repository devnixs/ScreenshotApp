using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Media.MediaFoundation;
namespace Snapper.Windows.Interop;

internal static class MediaFoundationRuntime
{
    private static readonly object gate = new();
    private static bool started, shutdownRequested;
    private static int writers;
    public static void EnsureStarted()
    {
        lock (gate)
        {
            if (started) return;
            // mfapi.h: (MF_SDK_VERSION << 16) | MF_API_VERSION.
            PInvoke.MFStartup(0x00020070, 0).ThrowOnFailure();
            started = true;
        }
    }
    public static void WriterStarted() { lock (gate) { EnsureStarted(); writers++; } }
    public static void WriterEnded() { lock (gate) { writers--; if (shutdownRequested && writers == 0) Shutdown(); } }
    public static void Shutdown()
    {
        lock (gate) { shutdownRequested = true; if (!started || writers > 0) return; var result = PInvoke.MFShutdown(); if (result.Failed) Debug.WriteLine($"Snapper: MFShutdown: {result}"); started = false; }
    }
    public static long QpcTicks => (long)((decimal)Stopwatch.GetTimestamp() * TimeSpan.TicksPerSecond / Stopwatch.Frequency);
    public static ulong Pack(uint high, uint low) => ((ulong)high << 32) | low;
    public static void SetSize(IMFAttributes attributes, Guid key, int width, int height) => attributes.SetUINT64(key, Pack((uint)width, (uint)height));
    public static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
}
