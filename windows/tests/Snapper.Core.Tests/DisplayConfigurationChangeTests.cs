using Snapper.Core.Geometry;
using static Snapper.Core.Tests.DisplayFixtures;

namespace Snapper.Core.Tests;

public sealed class DisplayConfigurationChangeTests
{
    [Fact] public void IdenticalAndReorderedSetsDoNotChange()
    {
        Assert.False(DisplayConfigurationChange.HasChanged([P, R], [P, R]));
        Assert.False(DisplayConfigurationChange.HasChanged([P, R], [R, P]));
        Assert.False(DisplayConfigurationChange.HasChanged([], []));
        Assert.False(DisplayConfigurationChange.HasChanged([P, P, R], [R, P]));
    }
    [Fact] public void ConnectionAndDisconnectionChange()
    {
        Assert.True(DisplayConfigurationChange.HasChanged([P], [P, R]));
        Assert.True(DisplayConfigurationChange.HasChanged([P, R], [P]));
        Assert.True(DisplayConfigurationChange.HasChanged([P], []));
        Assert.True(DisplayConfigurationChange.HasChanged([], [P]));
    }
    [Fact] public void DeviceReplacementChanges() => Assert.True(DisplayConfigurationChange.HasChanged([P, R], [P, R with { DeviceName = @"\\.\DISPLAY5" }]));
    [Fact] public void ArrangementChanges()
    {
        Assert.True(DisplayConfigurationChange.HasChanged([P, R], [P, R with { Bounds = new(1920, 100, 2880, 1620) }]));
        Assert.True(DisplayConfigurationChange.HasChanged([P, L], [P, L with { Bounds = new(-3840, -500, 3840, 2160) }]));
    }
    [Fact] public void ResolutionChanges() => Assert.True(DisplayConfigurationChange.HasChanged([P, R], [P, R with { Bounds = new(1920, 0, 1920, 1080) }]));
    [Fact] public void DpiOnlyChanges()
    {
        Assert.True(DisplayConfigurationChange.HasChanged([P, R], [P, R with { Dpi = 120 }]));
        Assert.True(DisplayConfigurationChange.HasChanged([P], [P with { Dpi = 144 }]));
    }
    [Fact] public void PrimarySwapChanges() => Assert.True(DisplayConfigurationChange.HasChanged([P, R], [P with { IsPrimary = false }, R with { IsPrimary = true }]));
    [Fact] public void WorkAreaChangesAreSilentlyIgnored() => Assert.False(DisplayConfigurationChange.HasChanged([P, R], [P with { WorkArea = P.Bounds }, R with { WorkArea = new(1920, 48, 2880, 1572) }]));
}
