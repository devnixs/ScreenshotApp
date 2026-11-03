using Snapper.Core.App;
using Snapper.Core.Support;
namespace Snapper.Core.Tests;
public sealed class HotkeyRegistrationStateTests
{
    private static KeyChord C(int n)=>KeyChord.Create(KeyModifiers.Ctrl|KeyModifiers.Shift,0x30+n);
    private static IReadOnlyDictionary<CapturePurpose,KeyChord?> D(int? s=4,int? r=5,int? a=6)=>new Dictionary<CapturePurpose,KeyChord?>
    { [CapturePurpose.Screenshot]=s is {} x?C(x):null,[CapturePurpose.Record]=r is {} y?C(y):null,[CapturePurpose.Annotate]=a is {} z?C(z):null };
    private static IReadOnlyList<KeyChord> Sync(HotkeyRegistrationState state,IReadOnlyDictionary<CapturePurpose,KeyChord?>? desired=null,params CapturePurpose[] failed)
    {
        desired??=D();var plan=state.Plan(desired);return state.Commit(plan,plan.ToRegister.ToDictionary(x=>x.Purpose,x=>!failed.Contains(x.Purpose)),desired);
    }
    [Fact] public void IdsAreStable()
    {
        Assert.Equal(new[]{1,2,3},CapturePurposes.All.Select(HotkeyRegistrationState.IdOf));
        foreach(var p in CapturePurposes.All)Assert.Equal(p,HotkeyRegistrationState.PurposeForId(HotkeyRegistrationState.IdOf(p)));
        Assert.Null(HotkeyRegistrationState.PurposeForId(0));Assert.Null(HotkeyRegistrationState.PurposeForId(4));Assert.Null(HotkeyRegistrationState.PurposeForId(-1));
    }
    [Fact] public void InitialStatusIsUnbound()
    { var state=new HotkeyRegistrationState();foreach(var p in CapturePurposes.All){Assert.Equal(HotkeyStatus.Unbound,state.StatusOf(p));Assert.Null(state.ChordOf(p));} }
    [Fact] public void FirstSyncRegistersAll()
    {
        var state=new HotkeyRegistrationState();var plan=state.Plan(D());Assert.Empty(plan.ToUnregister);
        Assert.Equal(new[]{(CapturePurpose.Screenshot,C(4)),(CapturePurpose.Record,C(5)),(CapturePurpose.Annotate,C(6))},plan.ToRegister);
        Assert.Empty(Sync(state));Assert.All(CapturePurposes.All,p=>Assert.Equal(HotkeyStatus.Registered,state.StatusOf(p)));
    }
    [Fact] public void FirstSyncFailureIsNotified()
    {var state=new HotkeyRegistrationState();Assert.Equal(new[]{C(4)},Sync(state,null,CapturePurpose.Screenshot));Assert.Equal(HotkeyStatus.InUseByAnotherApp,state.StatusOf(CapturePurpose.Screenshot));Assert.Equal(HotkeyStatus.Registered,state.StatusOf(CapturePurpose.Record));}
    [Fact] public void TwoFailuresNotifiedInPurposeOrder()
    {var state=new HotkeyRegistrationState();Assert.Equal(new[]{C(4),C(6)},Sync(state,null,CapturePurpose.Annotate,CapturePurpose.Screenshot));}
    [Fact] public void UnchangedRegisteredChordsProduceEmptyPlan()
    {var state=new HotkeyRegistrationState();Sync(state);var plan=state.Plan(D());Assert.Empty(plan.ToUnregister);Assert.Empty(plan.ToRegister);Assert.Empty(Sync(state));}
    [Fact] public void ChangedChordUnregistersThenRegisters()
    {
        var state=new HotkeyRegistrationState();Sync(state);var plan=state.Plan(D(7));Assert.Equal(new[]{CapturePurpose.Screenshot},plan.ToUnregister);Assert.Equal(new[]{(CapturePurpose.Screenshot,C(7))},plan.ToRegister);
        Assert.Empty(Sync(state,D(7)));Assert.Equal(C(7),state.ChordOf(CapturePurpose.Screenshot));Assert.Equal(HotkeyStatus.Registered,state.StatusOf(CapturePurpose.Screenshot));
    }
    [Fact] public void ChangedChordFailureIsNotified()
    {var state=new HotkeyRegistrationState();Sync(state);Assert.Equal(new[]{C(7)},Sync(state,D(7),CapturePurpose.Screenshot));}
    [Fact] public void UnchangedFailedChordIsRetriedSilently()
    {
        var state=new HotkeyRegistrationState();Sync(state,null,CapturePurpose.Screenshot);var plan=state.Plan(D());Assert.Empty(plan.ToUnregister);Assert.Equal(new[]{(CapturePurpose.Screenshot,C(4))},plan.ToRegister);
        Assert.Empty(Sync(state,null,CapturePurpose.Screenshot));Assert.Equal(HotkeyStatus.InUseByAnotherApp,state.StatusOf(CapturePurpose.Screenshot));Assert.Empty(Sync(state));Assert.Equal(HotkeyStatus.Registered,state.StatusOf(CapturePurpose.Screenshot));
    }
    [Fact] public void SwapUnregistersBothBeforeRegistering()
    {var state=new HotkeyRegistrationState();Sync(state);var plan=state.Plan(D(6,5,4));Assert.Equal(new[]{CapturePurpose.Screenshot,CapturePurpose.Annotate},plan.ToUnregister);Assert.Equal(new[]{(CapturePurpose.Screenshot,C(6)),(CapturePurpose.Annotate,C(4))},plan.ToRegister);Assert.Empty(Sync(state,D(6,5,4)));}
    [Fact] public void DuplicateLaterPurposeIsNotRegistered()
    {
        var state=new HotkeyRegistrationState();var plan=state.Plan(D(4,4,6));Assert.Equal(new[]{(CapturePurpose.Screenshot,C(4)),(CapturePurpose.Annotate,C(6))},plan.ToRegister);
        Assert.Empty(Sync(state,D(4,4,6)));Assert.Equal(HotkeyStatus.DuplicateBinding,state.StatusOf(CapturePurpose.Record));Assert.Equal(C(4),state.ChordOf(CapturePurpose.Record));
    }
    [Fact] public void BecomingDuplicateUnregisters()
    {var state=new HotkeyRegistrationState();Sync(state);Assert.Equal(new[]{CapturePurpose.Record},state.Plan(D(4,4,6)).ToUnregister);Sync(state,D(4,4,6));Assert.Equal(HotkeyStatus.DuplicateBinding,state.StatusOf(CapturePurpose.Record));}
    [Fact] public void NullChordIsUnbound()
    {var state=new HotkeyRegistrationState();Sync(state);Assert.Equal(new[]{CapturePurpose.Annotate},state.Plan(D(a:null)).ToUnregister);Sync(state,D(a:null));Assert.Equal(HotkeyStatus.Unbound,state.StatusOf(CapturePurpose.Annotate));Assert.Null(state.ChordOf(CapturePurpose.Annotate));}
    [Fact] public void ResolvedDuplicateRegistersWithoutFalseNotification()
    {
        var state=new HotkeyRegistrationState();Sync(state,D(4,4,6));Assert.Equal(new[]{(CapturePurpose.Record,C(5))},state.Plan(D()).ToRegister);Assert.Equal(new[]{C(5)},Sync(state,D(),CapturePurpose.Record));
        Sync(state,D(),CapturePurpose.Record);Assert.Empty(Sync(state,D(),CapturePurpose.Record));
    }
    [Fact] public void DuplicatePromotionRetriesSilentlyWhenEarlierBindingCleared()
    {
        var state=new HotkeyRegistrationState();Sync(state,D(4,4,6));var next=D(null,4,6);var plan=state.Plan(next);
        Assert.Equal(new[]{CapturePurpose.Screenshot},plan.ToUnregister);Assert.Equal(new[]{(CapturePurpose.Record,C(4))},plan.ToRegister);Assert.Empty(Sync(state,next,CapturePurpose.Record));Assert.Equal(HotkeyStatus.InUseByAnotherApp,state.StatusOf(CapturePurpose.Record));
    }
    [Fact] public void ClearedBindingCanFailAndNotifyWhenReassigned()
    {var state=new HotkeyRegistrationState();Sync(state);Sync(state,D(s:null));Assert.Equal(new[]{C(4)},Sync(state,D(),CapturePurpose.Screenshot));}
    [Fact] public void PlanningDoesNotChangeCommittedState()
    {var state=new HotkeyRegistrationState();Sync(state);_=state.Plan(D(7));Assert.Equal(C(4),state.ChordOf(CapturePurpose.Screenshot));Assert.Equal(HotkeyStatus.Registered,state.StatusOf(CapturePurpose.Screenshot));}
    [Fact] public void ThreeDuplicatesHaveOnlyOneWinnerEvenWhenWinnerFails()
    {var state=new HotkeyRegistrationState();var desired=D(4,4,4);Assert.Single(state.Plan(desired).ToRegister);Assert.Equal(new[]{C(4)},Sync(state,desired,CapturePurpose.Screenshot));Assert.Equal(HotkeyStatus.DuplicateBinding,state.StatusOf(CapturePurpose.Record));Assert.Equal(HotkeyStatus.DuplicateBinding,state.StatusOf(CapturePurpose.Annotate));}
    [Fact] public void SuspendReturnsOnlyRegisteredAndPreservesBindings()
    {
        var state=new HotkeyRegistrationState();Sync(state,D(a:null),CapturePurpose.Record);
        Assert.Equal(new[]{CapturePurpose.Screenshot},state.Suspend());
        Assert.Equal(HotkeyStatus.Suspended,state.StatusOf(CapturePurpose.Screenshot));
        Assert.Equal(HotkeyStatus.InUseByAnotherApp,state.StatusOf(CapturePurpose.Record));
        Assert.Equal(HotkeyStatus.Unbound,state.StatusOf(CapturePurpose.Annotate));
        Assert.Equal(C(4),state.ChordOf(CapturePurpose.Screenshot));Assert.Empty(state.Suspend());
    }
    [Fact] public void ResumeUnchangedChordsReRegistersSilently()
    {
        var state=new HotkeyRegistrationState();Sync(state,D(a:null),CapturePurpose.Record);state.Suspend();
        var plan=state.Plan(D(a:null));Assert.Empty(plan.ToUnregister);
        Assert.Equal(new[]{(CapturePurpose.Screenshot,C(4)),(CapturePurpose.Record,C(5))},plan.ToRegister);
        Assert.Empty(Sync(state,D(a:null)));Assert.Equal(HotkeyStatus.Registered,state.StatusOf(CapturePurpose.Screenshot));Assert.Equal(HotkeyStatus.Registered,state.StatusOf(CapturePurpose.Record));
        state.Suspend();Assert.Empty(Sync(state,D(a:null),CapturePurpose.Screenshot));Assert.Equal(HotkeyStatus.InUseByAnotherApp,state.StatusOf(CapturePurpose.Screenshot));
    }
    [Fact] public void ResumeChangedChordNotifiesItsFailure()
    {
        var state=new HotkeyRegistrationState();Sync(state);state.Suspend();var desired=D(7);var plan=state.Plan(desired);
        Assert.Empty(plan.ToUnregister);Assert.Contains((CapturePurpose.Screenshot,C(7)),plan.ToRegister);
        Assert.Equal(new[]{C(7)},Sync(state,desired,CapturePurpose.Screenshot));
    }
    [Fact] public void ResumeClearedChordBecomesUnbound()
    {
        var state=new HotkeyRegistrationState();Sync(state);state.Suspend();var desired=D(s:null);var plan=state.Plan(desired);
        Assert.Empty(plan.ToUnregister);Assert.DoesNotContain(plan.ToRegister,x=>x.Purpose==CapturePurpose.Screenshot);
        Sync(state,desired);Assert.Equal(HotkeyStatus.Unbound,state.StatusOf(CapturePurpose.Screenshot));Assert.Null(state.ChordOf(CapturePurpose.Screenshot));
    }
    [Fact] public void SuspensionLeavesDuplicateStatusUnchanged()
    {var state=new HotkeyRegistrationState();Sync(state,D(4,4,6));Assert.Equal(new[]{CapturePurpose.Screenshot,CapturePurpose.Annotate},state.Suspend());Assert.Equal(HotkeyStatus.DuplicateBinding,state.StatusOf(CapturePurpose.Record));}
}
