namespace Snapper.Core.Geometry;

public sealed record DisplayInfo(string DeviceName, PixelRect Bounds, PixelRect WorkArea, int Dpi, bool IsPrimary)
{
    public string DeviceName { get; init; } = DeviceName ?? throw new ArgumentNullException(nameof(DeviceName));
    public int Dpi { get; init; } = Dpi > 0 ? Dpi : throw new ArgumentOutOfRangeException(nameof(Dpi));
    public double Scale => Dpi / (double)DisplayGeometry.BaseDpi;
    public SizeD DipSize => DisplayGeometry.DipSize(RectD.FromPixelRect(Bounds), this);
}
