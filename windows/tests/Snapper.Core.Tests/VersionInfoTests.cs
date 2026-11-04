using System.Reflection;
using Snapper.Core.App;
namespace Snapper.Core.Tests;
public sealed class VersionInfoTests
{
    // The release script bumps <Version>, so assert the shape rather than a fixed number.
    [Fact] public void ExactReleaseVersionWithoutGitSuffix()
    {
        var assembly = typeof(AppIconDesign).Assembly;
        var version = assembly.GetName().Version;
        Assert.NotNull(version);
        Assert.Equal(0, version.Revision);
        Assert.Equal(
            $"{version.Major}.{version.Minor}.{version.Build}",
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
    }
}
