using Snapper.Core.App;
namespace Snapper.Core.Tests;

public sealed class AppModeMachineTests
{
    private static readonly AppModeEvent[] Events = [
        new AppModeEvent.Shortcut(CapturePurpose.Screenshot), new AppModeEvent.Shortcut(CapturePurpose.Record), new AppModeEvent.Shortcut(CapturePurpose.Annotate),
        new AppModeEvent.MenuItem(CapturePurpose.Screenshot), new AppModeEvent.MenuItem(CapturePurpose.Record), new AppModeEvent.MenuItem(CapturePurpose.Annotate),
        new AppModeEvent.TrayClicked(TrayButton.Left), new AppModeEvent.TrayClicked(TrayButton.Right),
        new AppModeEvent.SelectionFinished(), new AppModeEvent.SelectionCancelled(), new AppModeEvent.RecordingEnded()
    ];
    private static readonly AppMode I = new AppMode.Idle(), S = new AppMode.Selecting(CapturePurpose.Screenshot),
        R = new AppMode.Selecting(CapturePurpose.Record), A = new AppMode.Selecting(CapturePurpose.Annotate), V = new AppMode.Recording();
    private static readonly AppModeAction N = new AppModeAction.None(), Stop = new AppModeAction.StopRecording(), Open = new AppModeAction.OpenMenu();
    // Literal expected results per event, independently of the production switches.
    private static readonly (AppMode Initial, (AppModeAction Action, AppMode Final)[] Results)[] Table = [
        (I, [(new AppModeAction.ShowOverlay(CapturePurpose.Screenshot), S), (new AppModeAction.ShowOverlay(CapturePurpose.Record), R), (new AppModeAction.ShowOverlay(CapturePurpose.Annotate), A),
             (new AppModeAction.ShowOverlay(CapturePurpose.Screenshot), S), (new AppModeAction.ShowOverlay(CapturePurpose.Record), R), (new AppModeAction.ShowOverlay(CapturePurpose.Annotate), A),
             (Open,I), (Open,I), (N,I), (N,I), (N,I)]),
        (S, [(N,S),(N,S),(N,S),(N,S),(N,S),(N,S),(N,S),(N,S),(new AppModeAction.CompleteSelection(CapturePurpose.Screenshot),I),(N,I),(N,S)]),
        (R, [(N,R),(N,R),(N,R),(N,R),(N,R),(N,R),(N,R),(N,R),(new AppModeAction.CompleteSelection(CapturePurpose.Record),V),(N,I),(N,R)]),
        (A, [(N,A),(N,A),(N,A),(N,A),(N,A),(N,A),(N,A),(N,A),(new AppModeAction.CompleteSelection(CapturePurpose.Annotate),I),(N,I),(N,A)]),
        (V, [(N,V),(Stop,V),(N,V),(N,V),(Stop,V),(N,V),(Stop,V),(Stop,V),(N,V),(N,V),(N,I)])
    ];
    public static IEnumerable<object[]> Transitions => Table.SelectMany(row => row.Results.Select((result,index) =>
        new object[] { row.Initial, Events[index], result.Action, result.Final }));
    private static AppModeMachine MachineIn(AppMode mode)
    {
        var machine = new AppModeMachine();
        switch(mode)
        {
            case AppMode.Selecting(var p): machine.Handle(new AppModeEvent.Shortcut(p)); break;
            case AppMode.Recording: machine.Handle(new AppModeEvent.Shortcut(CapturePurpose.Record)); machine.Handle(new AppModeEvent.SelectionFinished()); break;
        }
        return machine;
    }
    [Fact] public void LiteralTableCoversEveryCell()
    {
        Assert.Equal(5,Table.Length); Assert.All(Table,row=>Assert.Equal(11,row.Results.Length)); Assert.Equal(55,Transitions.Count());
    }
    [Theory, MemberData(nameof(Transitions))]
    public void TransitionMatchesProductRules(AppMode initial, AppModeEvent e, AppModeAction action, AppMode final)
    {
        var machine = MachineIn(initial); Assert.Equal(initial,machine.Mode);
        Assert.Equal(action,machine.Handle(e)); Assert.Equal(final,machine.Mode);
    }
    [Fact] public void RepeatedStopsRemainRecordingUntilSessionEnds()
    {
        var machine=MachineIn(V);
        AppModeEvent[] events=[new AppModeEvent.Shortcut(CapturePurpose.Record),new AppModeEvent.Shortcut(CapturePurpose.Record),new AppModeEvent.TrayClicked(TrayButton.Left),new AppModeEvent.TrayClicked(TrayButton.Left),new AppModeEvent.TrayClicked(TrayButton.Right),new AppModeEvent.MenuItem(CapturePurpose.Record)];
        foreach(var e in events) { Assert.Equal(Stop,machine.Handle(e));Assert.Equal(V,machine.Mode); }
        Assert.Equal(N,machine.Handle(new AppModeEvent.RecordingEnded())); Assert.Equal(I,machine.Mode);
        Assert.Equal(new AppModeAction.ShowOverlay(CapturePurpose.Screenshot),machine.Handle(new AppModeEvent.Shortcut(CapturePurpose.Screenshot))); Assert.Equal(S,machine.Mode);
    }
    [Fact] public void ConvertingIsRecording()
    {
        var machine=MachineIn(V);Assert.Equal(Stop,machine.Handle(new AppModeEvent.Shortcut(CapturePurpose.Record)));
        Assert.Equal(Stop,machine.Handle(new AppModeEvent.TrayClicked(TrayButton.Left)));
        Assert.Equal(N,machine.Handle(new AppModeEvent.Shortcut(CapturePurpose.Screenshot)));Assert.Equal(N,machine.Handle(new AppModeEvent.Shortcut(CapturePurpose.Annotate)));Assert.Equal(V,machine.Mode);
        machine.Handle(new AppModeEvent.RecordingEnded());Assert.Equal(I,machine.Mode);
    }
    [Fact] public void CancellationAndCompletionAllowNewSelections()
    {
        var machine=new AppModeMachine();Assert.Equal(new AppModeAction.ShowOverlay(CapturePurpose.Annotate),machine.Handle(new AppModeEvent.Shortcut(CapturePurpose.Annotate)));Assert.Equal(A,machine.Mode);
        Assert.Equal(N,machine.Handle(new AppModeEvent.SelectionCancelled()));Assert.Equal(I,machine.Mode);
        Assert.Equal(new AppModeAction.ShowOverlay(CapturePurpose.Screenshot),machine.Handle(new AppModeEvent.MenuItem(CapturePurpose.Screenshot)));
        Assert.Equal(new AppModeAction.CompleteSelection(CapturePurpose.Screenshot),machine.Handle(new AppModeEvent.SelectionFinished()));Assert.Equal(I,machine.Mode);
    }
    [Theory, InlineData(TrayButton.Left), InlineData(TrayButton.Right)] public void BothTrayButtonsOpenMenuWhenIdle(TrayButton button)
    { var machine=new AppModeMachine();Assert.Equal(Open,machine.Handle(new AppModeEvent.TrayClicked(button)));Assert.Equal(I,machine.Mode); }
    [Fact] public void RecordValueEquality()
    { Assert.Equal(new AppMode.Selecting(CapturePurpose.Record),new AppMode.Selecting(CapturePurpose.Record));Assert.Equal(new AppMode.Idle(),new AppMode.Idle());Assert.NotEqual(new AppMode.Selecting(CapturePurpose.Record),new AppMode.Selecting(CapturePurpose.Annotate)); }
    [Fact] public void TransitionAppliedBeforeReentrantCancellation()
    { var machine=new AppModeMachine();var action=machine.Handle(new AppModeEvent.Shortcut(CapturePurpose.Annotate));Assert.IsType<AppModeAction.ShowOverlay>(action);Assert.Equal(A,machine.Mode);machine.Handle(new AppModeEvent.SelectionCancelled());Assert.Equal(I,machine.Mode); }
}
