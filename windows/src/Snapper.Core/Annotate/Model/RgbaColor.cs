namespace Snapper.Core.Annotate.Model;

public readonly record struct RgbaColor
{
    // Short component names avoid colliding with the named Red/Green/Blue presets in C#.
    public double R { get; }
    public double G { get; }
    public double B { get; }
    public double Alpha { get; }
    public RgbaColor(double red, double green, double blue, double alpha = 1)
    {
        R = Clamp(red); G = Clamp(green); B = Clamp(blue); Alpha = Clamp(alpha, 1);
    }
    private static double Clamp(double value, double fallback = 0) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
    public static bool TryCreateStrict(double r, double g, double b, double a, out RgbaColor color)
    {
        color = default;
        if (!Valid(r) || !Valid(g) || !Valid(b) || !Valid(a)) return false;
        color = new(r, g, b, a); return true;
    }
    private static bool Valid(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
    public static RgbaColor FromBytes(byte r, byte g, byte b, byte a = 255) => new(r / 255.0, g / 255.0, b / 255.0, a / 255.0);
    public (byte R, byte G, byte B, byte A) ToBytes() => (Byte(R), Byte(G), Byte(B), Byte(Alpha));
    private static byte Byte(double value) => (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero);
    public static RgbaColor Red => ColorPreset.Red.Color();
    public static RgbaColor Orange => ColorPreset.Orange.Color();
    public static RgbaColor Yellow => ColorPreset.Yellow.Color();
    public static RgbaColor Green => ColorPreset.Green.Color();
    public static RgbaColor Blue => ColorPreset.Blue.Color();
    public static RgbaColor Purple => ColorPreset.Purple.Color();
    public static RgbaColor Black => ColorPreset.Black.Color();
    public static RgbaColor White => ColorPreset.White.Color();
    public static IReadOnlyList<RgbaColor> Presets { get; } = Array.AsReadOnly(Enum.GetValues<ColorPreset>().Select(p => p.Color()).ToArray());
}

public enum ColorPreset { Red, Orange, Yellow, Green, Blue, Purple, Black, White }
public static class ColorPresetExtensions
{
    public static RgbaColor Color(this ColorPreset preset) => preset switch
    {
        ColorPreset.Red => RgbaColor.FromBytes(255, 59, 48),
        ColorPreset.Orange => RgbaColor.FromBytes(255, 149, 0),
        ColorPreset.Yellow => RgbaColor.FromBytes(255, 204, 0),
        ColorPreset.Green => RgbaColor.FromBytes(52, 199, 89),
        ColorPreset.Blue => RgbaColor.FromBytes(0, 122, 255),
        ColorPreset.Purple => RgbaColor.FromBytes(175, 82, 222),
        ColorPreset.Black => RgbaColor.FromBytes(0, 0, 0),
        ColorPreset.White => RgbaColor.FromBytes(255, 255, 255),
        _ => throw new ArgumentOutOfRangeException(nameof(preset))
    };
    public static string DisplayName(this ColorPreset preset) => preset switch
    {
        ColorPreset.Red => "Red", ColorPreset.Orange => "Orange", ColorPreset.Yellow => "Yellow", ColorPreset.Green => "Green",
        ColorPreset.Blue => "Blue", ColorPreset.Purple => "Purple", ColorPreset.Black => "Black", ColorPreset.White => "White",
        _ => throw new ArgumentOutOfRangeException(nameof(preset))
    };
}
