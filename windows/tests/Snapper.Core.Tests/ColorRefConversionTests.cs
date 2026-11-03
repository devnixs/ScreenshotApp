using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
namespace Snapper.Core.Tests;
public sealed class ColorRefConversionTests
{
    [Fact] public void ChannelsUseColorRefOrder()
    { Assert.Equal(0x00303BFFu, ColorRefConversion.ToColorRef(RgbaColor.Red)); Assert.Equal(RgbaColor.Blue, ColorRefConversion.FromColorRef(0x00FF7A00)); }
    [Fact] public void EveryPresetRoundTripsExactly()
    { foreach (var color in RgbaColor.Presets) Assert.Equal(color, ColorRefConversion.FromColorRef(ColorRefConversion.ToColorRef(color))); }
    [Fact] public void AlphaAndHighByteAreIgnored()
    { Assert.Equal(new(1, 0, 0), ColorRefConversion.FromColorRef(0xFF0000FF)); Assert.Equal(0x00FFFFFFu, ColorRefConversion.ToColorRef(new(1, 1, 1, .5))); }
}
