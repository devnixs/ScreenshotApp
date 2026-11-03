namespace Snapper.Core.Support.Imaging;

public static class CoverageRasterizer
{
    public static double[] Coverage(int width, int height, Func<double, double, bool> inside)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var result = new double[checked(width * height)];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var count = 0;
            for (var sy = 0; sy < 4; sy++)
            for (var sx = 0; sx < 4; sx++)
                if (inside(x + (sx + 0.5) / 4, y + (sy + 0.5) / 4)) count++;
            result[y * width + x] = count / 16.0;
        }
        return result;
    }
    public static Func<double, double, bool> RoundedRect(double x, double y, double width, double height, double radius)
    {
        var r = Math.Clamp(radius, 0, Math.Max(0, Math.Min(width, height) / 2));
        return (px, py) =>
        {
            if (width <= 0 || height <= 0 || px < x || py < y || px > x + width || py > y + height) return false;
            var cx = Math.Clamp(px, x + r, x + width - r);
            var cy = Math.Clamp(py, y + r, y + height - r);
            return (px - cx) * (px - cx) + (py - cy) * (py - cy) <= r * r;
        };
    }
    public static Func<double, double, bool> Segment(double x1, double y1, double x2, double y2, double width)
    {
        var dx = x2 - x1; var dy = y2 - y1;
        var length = Math.Sqrt(dx * dx + dy * dy); var half = width / 2;
        if (length == 0) return RoundedRect(x1 - half, y1 - half, width, width, 0);
        return (x, y) =>
        {
            var along = ((x - x1) * dx + (y - y1) * dy) / length;
            var across = ((x - x1) * -dy + (y - y1) * dx) / length;
            return width > 0 && along >= -half && along <= length + half && Math.Abs(across) <= half;
        };
    }
    public static Func<double, double, bool> Ring(double cx, double cy, double radius, double width) => (x, y) =>
    {
        var squared = (x - cx) * (x - cx) + (y - cy) * (y - cy);
        var inner = Math.Max(0, radius - width / 2); var outer = radius + width / 2;
        return width > 0 && squared >= inner * inner && squared <= outer * outer;
    };
    public static Func<double, double, bool> Disc(double cx, double cy, double radius) =>
        (x, y) => radius >= 0 && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius;
    public static Func<double, double, bool> Union(params Func<double, double, bool>[] shapes) =>
        (x, y) => shapes.Any(shape => shape(x, y));
    public static void Paint(RgbaImage image, double[] coverage, uint argb)
    {
        if (coverage.Length != image.Width * image.Height) throw new ArgumentException("Coverage dimensions must match the image.", nameof(coverage));
        var color = new[] { (argb >> 16) & 255, (argb >> 8) & 255, argb & 255 };
        for (var p = 0; p < coverage.Length; p++)
        {
            var a = (argb >> 24) / 255.0 * Math.Clamp(coverage[p], 0, 1);
            var i = p * 4; var destinationAlpha = image.Pixels[i + 3] / 255.0;
            var outAlpha = a + destinationAlpha * (1 - a);
            for (var c = 0; c < 3; c++)
                image.Pixels[i + c] = Round(outAlpha == 0 ? 0 : (color[c] * a + image.Pixels[i + c] * destinationAlpha * (1 - a)) / outAlpha);
            image.Pixels[i + 3] = Round(outAlpha * 255);
        }
    }
    private static byte Round(double v) => (byte)Math.Clamp(Math.Round(v, MidpointRounding.AwayFromZero), 0, 255);
}
