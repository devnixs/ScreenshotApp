using Snapper.Core.Geometry;
namespace Snapper.Core.Recording;

public enum PillPosition { Below, Above, Inside }
public readonly record struct PillPlacementResult(PixelRect Frame, PillPosition Position);

/// <summary>Placement in physical virtual-screen pixels, constrained to the recorded monitor's work area.</summary>
public static class RecordingPillPlacement
{
    public const double DefaultMarginDips = 8, PillHeightDips = 32;
    public static PixelSize PillPixelSize(SizeD pillSizeDips, double scale) => DisplayGeometry.PixelSizeForDips(pillSizeDips, scale);
    public static int MarginPixels(double scale, double marginDips = DefaultMarginDips) =>
        (int)Math.Round(DisplayGeometry.DipsToPixels(marginDips, scale), MidpointRounding.AwayFromZero);
    public static PillPlacementResult Place(SizeD pillSizeDips, PixelRect recordedRect, DisplayInfo display,
        double marginDips = DefaultMarginDips)
    {
        var size = PillPixelSize(pillSizeDips, display.Scale);
        var margin = MarginPixels(display.Scale, marginDips);
        var area = display.WorkArea;
        var y = recordedRect.Bottom + margin;
        var position = PillPosition.Below;
        if (y + size.Height > area.Bottom)
        {
            y = recordedRect.Top - margin - size.Height;
            position = PillPosition.Above;
            if (y < area.Top)
            {
                position = PillPosition.Inside;
                y = Math.Min(recordedRect.Bottom, area.Bottom) - margin - size.Height;
            }
        }
        var x = (int)Math.Round(recordedRect.Left + recordedRect.Width / 2.0 - size.Width / 2.0, MidpointRounding.AwayFromZero);
        return new(ReachableFrame(new(x, y, size.Width, size.Height), area), position);
    }
    public static PixelRect ReachableFrame(PixelRect frame, PixelRect workArea) =>
        new(Math.Max(workArea.Left, Math.Min(frame.Left, workArea.Right - frame.Width)),
            Math.Max(workArea.Top, Math.Min(frame.Top, workArea.Bottom - frame.Height)), frame.Width, frame.Height);
}
