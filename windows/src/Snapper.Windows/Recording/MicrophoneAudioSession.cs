using System.Diagnostics;
using Snapper.Core.Recording;
namespace Snapper.Windows.Recording;

/// <summary>Serializes timeline and pooled-chunk ownership; only the microphone callback writes live audio.</summary>
internal sealed class MicrophoneAudioSession : IDisposable
{
    private readonly MicrophoneSource source;
    private readonly Mp4Writer writer;
    private readonly AudioTimeline timeline;
    private readonly object gate = new();
    private readonly Queue<MicrophoneChunk> pending = new();
    private int pendingFrames;
    private long requestedEnd = long.MinValue;
    private bool hasOrigin, disposed;
    public MicrophoneAudioSession(MicrophoneSource source, Mp4Writer writer)
    {
        this.source = source; this.writer = writer; timeline = new(source.Format.SampleRate);
        writer.FirstSampleWritten += FirstVideo;
    }
    public void Start() => source.Start(Receive, result => Debug.WriteLine($"Snapper: Microphone ended ({result}); continuing video."));
    private void FirstVideo() { lock (gate) { if (!disposed) Drain(); } }
    private void Receive(MicrophoneChunk chunk)
    {
        lock (gate)
        {
            if (disposed) { chunk.Dispose(); return; }
            pending.Enqueue(chunk); pendingFrames += chunk.Frames;
            var maximum = source.Format.SampleRate * 2;
            while (pendingFrames > maximum && pending.TryDequeue(out var old))
            { pendingFrames -= old.Frames; old.Dispose(); }
            Drain();
        }
    }
    private void Drain()
    {
        var end = Interlocked.Read(ref requestedEnd);
        if (end != long.MinValue) timeline.SetEnd(end);
        if (!hasOrigin)
        {
            if (writer.TimelineOriginQpc is not { } origin) return;
            timeline.SetOrigin(origin); hasOrigin = true;
        }
        while (pending.TryDequeue(out var chunk))
        {
            pendingFrames -= chunk.Frames;
            using (chunk)
                foreach (var placement in timeline.Place(chunk.Qpc, chunk.Frames))
                    writer.WriteAudio(placement, chunk.Bytes.AsSpan(0, chunk.Length));
        }
    }
    // Called at Stop processing time; does not wait on microphone encoding or block the dispatcher.
    public void SetEnd(long qpcAtStop) => Interlocked.Exchange(ref requestedEnd, qpcAtStop);
    public void Stop(long qpcAtStop)
    {
        SetEnd(qpcAtStop);
        source.Stop();
        lock (gate) { if (!disposed) Drain(); }
    }
    public void Dispose()
    {
        source.Dispose();
        writer.FirstSampleWritten -= FirstVideo;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            while (pending.TryDequeue(out var chunk)) chunk.Dispose();
            pendingFrames = 0;
        }
    }
}
