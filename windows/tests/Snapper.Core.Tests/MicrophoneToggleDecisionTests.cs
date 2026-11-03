using Snapper.Core.Settings;
namespace Snapper.Core.Tests;
public sealed class MicrophoneToggleDecisionTests
{
    [Theory]
    [InlineData(MicrophoneAuthorization.Authorized, true, false, false)]
    [InlineData(MicrophoneAuthorization.NotDetermined, false, false, true)]
    [InlineData(MicrophoneAuthorization.Denied, false, true, false)]
    [InlineData(MicrophoneAuthorization.Restricted, false, true, false)]
    public void ToggleDecisionTable(MicrophoneAuthorization status, bool stored, bool denied, bool request)
    {
        Assert.Equal(new(false, false, false), MicrophoneToggleDecision.UserToggled(false, status));
        Assert.Equal(new(stored, denied, request), MicrophoneToggleDecision.UserToggled(true, status));
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void RequestCompletion(bool granted) => Assert.Equal(new(granted, !granted, false), MicrophoneToggleDecision.AccessRequestFinished(granted));
    [Theory]
    [InlineData(MicrophoneAuthorization.Authorized)] [InlineData(MicrophoneAuthorization.NotDetermined)]
    [InlineData(MicrophoneAuthorization.Denied)] [InlineData(MicrophoneAuthorization.Restricted)]
    public void ReconcileNeverRequests(MicrophoneAuthorization status)
    {
        Assert.Equal(new(false, false, false), MicrophoneToggleDecision.ReconcileOnAppear(false, status));
        var denied = status is MicrophoneAuthorization.Denied or MicrophoneAuthorization.Restricted;
        Assert.Equal(new(!denied, denied, false), MicrophoneToggleDecision.ReconcileOnAppear(true, status));
    }
    [Fact]
    public void PendingRequestHonorsLatestIntentAndDoesNotDuplicateRequests()
    {
        var state = new MicrophoneRequestState();
        state.UserToggled(true); Assert.True(state.BeginRequest()); Assert.False(state.BeginRequest());
        state.UserToggled(false); Assert.False(state.FinishRequest()); Assert.False(state.IsPending);
        state.UserToggled(true); Assert.True(state.BeginRequest()); Assert.True(state.FinishRequest()); Assert.False(state.IsPending);
    }
    [Fact]
    public void OnOffOnWhilePendingAppliesToLatestOn()
    {
        var state = new MicrophoneRequestState(); state.UserToggled(true); Assert.True(state.BeginRequest());
        state.UserToggled(false); state.UserToggled(true); Assert.False(state.BeginRequest()); Assert.True(state.FinishRequest());
    }
}
