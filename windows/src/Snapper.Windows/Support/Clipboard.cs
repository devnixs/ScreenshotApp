using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
namespace Snapper.Windows.Support;
internal static class Clipboard
{
    public static bool Copy(byte[] png, byte[] dib)
    {
        Application.Current.Dispatcher.VerifyAccess();
        try
        {
            using var pngStream = new MemoryStream(png);
            using var dibStream = new MemoryStream(dib);
            var data = new DataObject();
            data.SetData("PNG", pngStream);
            data.SetData(DataFormats.Dib, dibStream);
            System.Windows.Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (ExternalException error) { Debug.WriteLine($"Snapper: Couldn't copy image: {error}"); return false; }
    }
}
