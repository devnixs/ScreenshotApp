using Snapper.Core.Geometry;
namespace Snapper.Core.Recording;

public readonly record struct LetterboxPlacement(PixelRect Destination, bool IsIdentity);
public static class LetterboxFit
{
    public static LetterboxPlacement? Fit(PixelSize source, PixelSize output)
    {
        if (source.Width <= 0 || source.Height <= 0 || output.Width <= 0 || output.Height <= 0) return null;
        if (source == output) return new(new(0, 0, output.Width, output.Height), true);
        var scale = Math.Min((double)output.Width / source.Width, (double)output.Height / source.Height);
        var width = Math.Clamp((int)Math.Round(source.Width * scale, MidpointRounding.AwayFromZero), 1, output.Width);
        var height = Math.Clamp((int)Math.Round(source.Height * scale, MidpointRounding.AwayFromZero), 1, output.Height);
        return new(new((output.Width - width) / 2, (output.Height - height) / 2, width, height), false);
    }
}
