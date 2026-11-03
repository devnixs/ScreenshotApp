namespace Snapper.Core.App;

public enum TrayIconState { Idle, Recording, Converting }
public enum TrayGlyph { Viewfinder, RecordDot }
public readonly record struct TrayIconAppearance(TrayGlyph Glyph, uint Argb, string Tooltip);

public static class TrayIconStyle
{
    public const uint DarkTaskbarForeground = 0xFFFFFFFF;
    public const uint LightTaskbarForeground = 0xFF000000;
    public const uint RecordingRed = 0xFFE81123;

    public static TrayIconAppearance Resolve(TrayIconState state, bool taskbarIsLight) => state switch
    {
        TrayIconState.Idle => new(TrayGlyph.Viewfinder,
            taskbarIsLight ? LightTaskbarForeground : DarkTaskbarForeground, "Snapper"),
        TrayIconState.Recording or TrayIconState.Converting => new(TrayGlyph.RecordDot, RecordingRed, "Snapper — Recording"),
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    public static bool TaskbarIsLight(int? systemUsesLightTheme) => systemUsesLightTheme is not null and not 0;
    public static int IconPixelSize(int dpi) => dpi <= 0 ? 16 : (int)Math.Round(16.0 * dpi / 96, MidpointRounding.AwayFromZero);
}
