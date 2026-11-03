using Snapper.Core.App;
namespace Snapper.Core.Tests;
public sealed class CapturePurposeTests
{
    [Fact] public void CanonicalOrder() => Assert.Equal(new[] {CapturePurpose.Screenshot,CapturePurpose.Record,CapturePurpose.Annotate},CapturePurposes.All);
}
