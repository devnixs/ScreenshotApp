using System.IO;
using System.Diagnostics;
using System.Media;
using Snapper.Core.Support;
namespace Snapper.Windows.Support;
internal sealed class ShutterSound : IDisposable
{
    private MemoryStream? stream;
    private SoundPlayer? player;
    private bool failed, disposed;
    public void Play()
    {
        if (failed || disposed) return;
        try
        {
            if (player is null)
            {
                stream = new MemoryStream(ShutterSoundSynthesizer.CreateWav());
                player = new SoundPlayer(stream);
                player.Load();
            }
            player.Play();
        }
        catch (Exception error) { failed = true; Debug.WriteLine($"Snapper: Shutter sound unavailable: {error.Message}"); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        player?.Dispose(); stream?.Dispose();
        player = null; stream = null;
    }
}
