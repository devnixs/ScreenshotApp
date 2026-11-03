namespace Snapper.Core.Annotate.Model;
public enum StrokeWidth { S, M, L }
public static class StrokeWidthExtensions
{
    public static double Points(this StrokeWidth width) => width switch { StrokeWidth.S => 2, StrokeWidth.M => 4, StrokeWidth.L => 8, _ => throw new ArgumentOutOfRangeException(nameof(width)) };
    public static double TextPointSize(this StrokeWidth width) => width switch { StrokeWidth.S => 16, StrokeWidth.M => 24, StrokeWidth.L => 36, _ => throw new ArgumentOutOfRangeException(nameof(width)) };
    public static string Label(this StrokeWidth width) => width switch { StrokeWidth.S => "S", StrokeWidth.M => "M", StrokeWidth.L => "L", _ => throw new ArgumentOutOfRangeException(nameof(width)) };
}
