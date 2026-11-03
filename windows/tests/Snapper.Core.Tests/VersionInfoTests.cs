using System.Reflection;
using Snapper.Core.App;
namespace Snapper.Core.Tests;
public sealed class VersionInfoTests
{
    [Fact] public void ExactReleaseVersionWithoutGitSuffix()
    {
        var assembly = typeof(AppIconDesign).Assembly;
        Assert.Equal(new Version(1, 0, 0, 0), assembly.GetName().Version);
        Assert.Equal("1.0.0", assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
    }
}
