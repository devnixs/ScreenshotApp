using Snapper.Core.App;

namespace Snapper.Core.Tests;

public class TrayGlyphGeometryTests
{
    [Theory]
    [InlineData(16, 1)]
    [InlineData(20, 1)]
    [InlineData(24, 2)]
    [InlineData(28, 2)]
    [InlineData(32, 2)]
    [InlineData(48, 3)]
    public void ViewfinderIsUnclippedSymmetricAndPixelAligned(int size, double stroke)
    {
        var drawing = TrayGlyphGeometry.Viewfinder(size);
        Assert.Equal(stroke, drawing.StrokeWidth);
        Assert.Equal(8, drawing.Lines.Count);
        var lens = Assert.Single(drawing.Circles);
        Assert.False(lens.Filled);
        Assert.Equal(size / 2.0, lens.CenterX);
        Assert.Equal(size / 2.0, lens.CenterY);
        Assert.InRange(lens.CenterX - lens.Radius - stroke / 2, 0, size);
        Assert.InRange(lens.CenterX + lens.Radius + stroke / 2, 0, size);
        Assert.InRange(lens.CenterY - lens.Radius - stroke / 2, 0, size);
        Assert.InRange(lens.CenterY + lens.Radius + stroke / 2, 0, size);
        foreach (var line in drawing.Lines)
        {
            foreach (var coordinate in new[] { line.X1, line.X2, line.Y1, line.Y2 })
            {
                Assert.InRange(coordinate - stroke / 2, 0, size);
                Assert.InRange(coordinate + stroke / 2, 0, size);
            }
            Assert.True(line.X1 == line.X2 || line.Y1 == line.Y2);
            var axis = line.X1 == line.X2 ? line.X1 : line.Y1;
            Assert.Equal(stroke % 2 == 0 ? 0 : 0.5, axis - Math.Floor(axis));
            var length = Math.Abs(line.X2 - line.X1) + Math.Abs(line.Y2 - line.Y1);
            var bracketLength = Math.Round(size * 0.3, MidpointRounding.AwayFromZero);
            Assert.InRange(length, bracketLength - stroke, bracketLength + stroke);
            Assert.True(Distance(lens.CenterX, lens.CenterY, line) > lens.Radius + stroke);
            Assert.Contains(drawing.Lines, candidate => SameSegment(candidate,
                new(size - line.X1, line.Y1, size - line.X2, line.Y2)));
            Assert.Contains(drawing.Lines, candidate => SameSegment(candidate,
                new(line.X1, size - line.Y1, line.X2, size - line.Y2)));
        }
    }

    [Theory]
    [InlineData(16, 5)]
    [InlineData(32, 10)]
    public void RecordDotIsCenteredAndFilled(int size, double radius)
    {
        var drawing = TrayGlyphGeometry.RecordDot(size);
        Assert.Empty(drawing.Lines);
        Assert.Equal(0, drawing.StrokeWidth);
        Assert.Equal(new(size / 2.0, size / 2.0, radius, true), Assert.Single(drawing.Circles));
    }

    private static bool SameSegment(GlyphLine a, GlyphLine b) =>
        (Near(a.X1, b.X1) && Near(a.Y1, b.Y1) && Near(a.X2, b.X2) && Near(a.Y2, b.Y2)) ||
        (Near(a.X1, b.X2) && Near(a.Y1, b.Y2) && Near(a.X2, b.X1) && Near(a.Y2, b.Y1));
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;
    private static double Distance(double x, double y, GlyphLine line)
    {
        var dx = line.X2 - line.X1;
        var dy = line.Y2 - line.Y1;
        var t = Math.Clamp(((x - line.X1) * dx + (y - line.Y1) * dy) / (dx * dx + dy * dy), 0, 1);
        return Math.Sqrt(Math.Pow(x - line.X1 - t * dx, 2) + Math.Pow(y - line.Y1 - t * dy, 2));
    }
}
