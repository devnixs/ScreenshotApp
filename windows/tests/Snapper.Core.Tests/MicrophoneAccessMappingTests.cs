using Snapper.Core.Support;
using Snapper.Core.Settings;
namespace Snapper.Core.Tests;
public sealed class MicrophoneAccessMappingTests
{
    [Theory]
    [InlineData(MicrophoneCapabilityStatus.Allowed, MicrophoneAuthorization.Authorized)]
    [InlineData(MicrophoneCapabilityStatus.DeniedByUser, MicrophoneAuthorization.Denied)]
    [InlineData(MicrophoneCapabilityStatus.DeniedBySystem, MicrophoneAuthorization.Restricted)]
    [InlineData(MicrophoneCapabilityStatus.UserPromptRequired, MicrophoneAuthorization.NotDetermined)]
    [InlineData(MicrophoneCapabilityStatus.NotDeclaredByApp, MicrophoneAuthorization.NotDetermined)]
    [InlineData(MicrophoneCapabilityStatus.Unavailable, MicrophoneAuthorization.NotDetermined)]
    public void FromCapability(MicrophoneCapabilityStatus status, MicrophoneAuthorization expected) => Assert.Equal(expected, MicrophoneAccessMapping.FromCapability(status));
    [Theory]
    [InlineData(MicrophoneOpenResult.Opened, true)] [InlineData(MicrophoneOpenResult.AccessDenied, false)]
    [InlineData(MicrophoneOpenResult.NoDevice, true)] [InlineData(MicrophoneOpenResult.Failed, true)]
    public void ProbeGrantsAccess(MicrophoneOpenResult result, bool expected) => Assert.Equal(expected, MicrophoneAccessMapping.ProbeGrantsAccess(result));
    [Theory]
    [InlineData(unchecked((int)0x80070005), MicrophoneOpenResult.AccessDenied)]
    [InlineData(unchecked((int)0x80070490), MicrophoneOpenResult.NoDevice)]
    [InlineData(unchecked((int)0xC00DABE0), MicrophoneOpenResult.NoDevice)]
    [InlineData(unchecked((int)0x80004005), MicrophoneOpenResult.Failed)]
    public void ClassifyHResult(int result, MicrophoneOpenResult expected) => Assert.Equal(expected, MicrophoneAccessMapping.ClassifyHResult(result));
}
