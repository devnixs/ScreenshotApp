using Snapper.Core.Capture;
using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;
public sealed class WindowCandidateFilterTests
{
    internal static WindowCandidate Candidate(nint handle = 1, PixelRect? frame = null) =>
        new(handle, frame ?? new(10, 10, 800, 600), 20, "Notepad", true, false, false, false, null);
    [Fact] public void VisibleNormalWindowIsSelectable() => Assert.True(WindowCandidateFilter.IsSelectable(Candidate(), 99, new HashSet<nint>()));
    [Theory]
    [InlineData("hidden")][InlineData("minimized")][InlineData("cloaked")][InlineData("tool")]
    [InlineData("own")][InlineData("registered")][InlineData("Progman")][InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")][InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("progman")][InlineData("SHELL_TRAYWND")]
    public void EachExclusionRuleRejects(string rule)
    {
        var c = Excluded(rule);
        Assert.False(WindowCandidateFilter.IsSelectable(c, 99, new HashSet<nint>{2}));
    }
    internal static WindowCandidate Excluded(string rule) => rule switch
    {
        "hidden" => Candidate() with { IsVisible = false },
        "minimized" => Candidate() with { IsMinimized = true },
        "cloaked" => Candidate() with { IsCloaked = true },
        "tool" => Candidate() with { IsToolWindow = true },
        "own" => Candidate() with { ProcessId = 99 },
        "registered" => Candidate(2),
        _ => Candidate() with { ClassName = rule }
    };
    [Theory][InlineData(0,0,false)][InlineData(3,100,false)][InlineData(100,3,false)][InlineData(4,4,true)]
    public void MinimumSizeBoundary(int w, int h, bool expected) =>
        Assert.Equal(expected, WindowCandidateFilter.IsSelectable(Candidate(frame: new(0,0,w,h)),99,new HashSet<nint>()));
}
