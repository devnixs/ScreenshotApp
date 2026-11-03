using Snapper.Core.App;

namespace Snapper.Core.Tests;

public class TrayMenuClickGateTests
{
    [Theory]
    [InlineData(true, null, false)]
    [InlineData(false, null, true)]
    [InlineData(false, 100L, false)]
    [InlineData(false, 499L, false)]
    [InlineData(false, 500L, true)]
    [InlineData(false, 2000L, true)]
    public void OpensOnlyAfterMenuAndDoubleClickGate(bool visible, long? elapsed, bool expected) =>
        Assert.Equal(expected, TrayMenuClickGate.ShouldOpenMenu(visible, elapsed, 500));
}
