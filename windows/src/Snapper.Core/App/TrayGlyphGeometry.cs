namespace Snapper.Core.App;

public readonly record struct GlyphLine(double X1, double Y1, double X2, double Y2);
public readonly record struct GlyphCircle(double CenterX, double CenterY, double Radius, bool Filled);
public sealed record TrayGlyphDrawing(double StrokeWidth, IReadOnlyList<GlyphLine> Lines, IReadOnlyList<GlyphCircle> Circles);

public static class TrayGlyphGeometry
{
    public static TrayGlyphDrawing Viewfinder(int size)
    {
        var stroke = Math.Max(1, Math.Round(size / 16.0, MidpointRounding.AwayFromZero));
        const double margin = 1;
        var c = margin + stroke / 2;
        var end = margin + Math.Round(size * 0.3, MidpointRounding.AwayFromZero);
        var lines = new List<GlyphLine>();
        foreach (var mirrorX in new[] { false, true })
        foreach (var mirrorY in new[] { false, true })
        {
            double X(double x) => mirrorX ? size - x : x;
            double Y(double y) => mirrorY ? size - y : y;
            lines.Add(new(X(c), Y(c), X(end), Y(c)));
            lines.Add(new(X(c), Y(c), X(c), Y(end)));
        }
        return new(stroke, lines.AsReadOnly(), Array.AsReadOnly(new[]
        {
            new GlyphCircle(size / 2.0, size / 2.0, Math.Round(size * 0.18), false)
        }));
    }

    public static TrayGlyphDrawing RecordDot(int size) => new(0, Array.Empty<GlyphLine>(),
        Array.AsReadOnly(new[] { new GlyphCircle(size / 2.0, size / 2.0, Math.Round(size * 0.625) / 2, true) }));
}
