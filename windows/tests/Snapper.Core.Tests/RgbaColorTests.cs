using Snapper.Core.Annotate.Model;
namespace Snapper.Core.Tests;
public sealed class RgbaColorTests
{
    [Fact] public void PresetOrderAndValues()
    {
        Assert.Equal(new[] { ColorPreset.Red, ColorPreset.Orange, ColorPreset.Yellow, ColorPreset.Green, ColorPreset.Blue, ColorPreset.Purple, ColorPreset.Black, ColorPreset.White }, Enum.GetValues<ColorPreset>());
        (byte, byte, byte)[] bytes = [(255,59,48), (255,149,0), (255,204,0), (52,199,89), (0,122,255), (175,82,222), (0,0,0), (255,255,255)];
        var presets = Enum.GetValues<ColorPreset>();
        Assert.Equal(presets.Select(p => p.Color()), RgbaColor.Presets);
        Assert.Equal(new[] { "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Black", "White" }, presets.Select(p => p.DisplayName()));
        for (var i = 0; i < bytes.Length; i++)
        {
            var (r,g,b) = bytes[i]; var color = presets[i].Color();
            Assert.Equal(r / 255.0, color.R); Assert.Equal(g / 255.0, color.G); Assert.Equal(b / 255.0, color.B); Assert.Equal(1, color.Alpha);
            Assert.Equal((r,g,b,(byte)255), color.ToBytes());
        }
    }
    [Fact] public void ConstructorClamps()
    {
        Assert.Equal(new RgbaColor(1,0,0.5,1), new RgbaColor(2,-1,0.5,3));
        Assert.Equal(new RgbaColor(0,0,0,1), new RgbaColor(double.NaN,double.PositiveInfinity,double.NegativeInfinity,double.NaN));
        Assert.Equal(new RgbaColor(0,0,0,0), default(RgbaColor));
    }
    [Fact] public void StrictRejectsEveryInvalidComponent()
    {
        foreach (var invalid in new[] { -0.1, 1.5, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            for (var i = 0; i < 4; i++) { var parts = new[] {0.5,0.5,0.5,1.0}; parts[i] = invalid; Assert.False(RgbaColor.TryCreateStrict(parts[0],parts[1],parts[2],parts[3],out _)); }
        Assert.True(RgbaColor.TryCreateStrict(0.5,0.5,0.5,1,out var color)); Assert.Equal(new RgbaColor(0.5,0.5,0.5), color);
    }
    [Fact] public void BytesUseAwayFromZeroRounding()
    {
        Assert.Equal(RgbaColor.Red, RgbaColor.FromBytes(255,59,48));
        Assert.Equal(((byte)128,(byte)128,(byte)128,(byte)255), new RgbaColor(0.5,0.5,0.5).ToBytes());
    }
}
