using Snapper.Core.Annotate.Model;
namespace Snapper.Core.Tests;
public sealed class StrokeWidthTests
{
    [Fact] public void ValuesMatchToolbarAndTextMetrics()
    {
        var values = Enum.GetValues<StrokeWidth>();
        Assert.Equal(new[] { StrokeWidth.S, StrokeWidth.M, StrokeWidth.L }, values);
        Assert.Equal(new double[] { 2,4,8 }, values.Select(v => v.Points()));
        Assert.Equal(new double[] { 16,24,36 }, values.Select(v => v.TextPointSize()));
        Assert.Equal(new[] { "S","M","L" }, values.Select(v => v.Label()));
    }
}
