using Snapper.Core.Capture;
using Snapper.Core.Geometry;
using static Snapper.Core.Tests.WindowCandidateFilterTests;
namespace Snapper.Core.Tests;
public sealed class WindowHitTesterTests
{
    [Fact] public void TopmostWindowWins()
    {
        var a = Candidate(frame: new(10,10,100,100)); var b = Candidate(2,new(50,50,200,200));
        var t = new WindowHitTester([a,b],99,new HashSet<nint>());
        Assert.Equal(a,t.WindowAt(new(60,60)));Assert.Equal(b,t.WindowAt(new(200,200)));Assert.Null(t.WindowAt(new(0,0)));
    }
    [Theory][InlineData("own")][InlineData("registered")][InlineData("cloaked")][InlineData("tool")][InlineData("minimized")][InlineData("Shell_TrayWnd")]
    public void NonSelectableWindowsAreSkippedNotBlocking(string rule)
    {
        var back=Candidate(10,new(10,10,100,100));var front=Excluded(rule) with { Frame=new(0,0,1920,1080) };
        var t=new WindowHitTester([front,back],99,new HashSet<nint>{2});
        Assert.False(t.IsSelectable(front));Assert.Equal(back,t.WindowAt(new(50,50)));Assert.Null(t.WindowAt(new(500,500)));
    }
    [Theory][InlineData(10,10,true)][InlineData(110,50,false)][InlineData(50,110,false)]
    public void ContainsUsesHalfOpenEdges(int x,int y,bool expected) => Assert.Equal(expected,
        new WindowHitTester([Candidate(frame:new(10,10,100,100))],99,new HashSet<nint>()).WindowAt(new(x,y)) is not null);
    [Fact] public void NegativeCoordinates()
    {
        var c=Candidate(frame:new(-1920,-300,800,600));var t=new WindowHitTester([c],99,new HashSet<nint>());
        Assert.Equal(c,t.WindowAt(new(-1500,0)));Assert.Null(t.WindowAt(new(-1000,0)));
    }
    [Fact] public void EmptyCandidatesReturnNull() => Assert.Null(new WindowHitTester([],99,new HashSet<nint>()).WindowAt(new(0,0)));
    [Fact] public void ConstructorCopiesInputs()
    {
        var candidates=new List<WindowCandidate>{Candidate()};var excluded=new HashSet<nint>();var t=new WindowHitTester(candidates,99,excluded);
        candidates.Clear();excluded.Add(1);Assert.NotNull(t.WindowAt(new(20,20)));
    }
}
