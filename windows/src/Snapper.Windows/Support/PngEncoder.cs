using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapper.Core.Capture;
namespace Snapper.Windows.Support;
internal static class PngEncoder
{
    public static byte[] Encode(CapturedImage image)
    {
        try
        {
            var dpi = ScreenshotOutputPlan.PngDpi(image.ScaleFactor);
            var source = BitmapSource.Create(image.Width, image.Height, dpi, dpi, PixelFormats.Bgr32,
                null, image.Bgra, checked(image.Width * 4));
            source.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch (Exception error) { throw new InvalidOperationException("The screenshot couldn't be encoded as PNG.", error); }
    }
}
