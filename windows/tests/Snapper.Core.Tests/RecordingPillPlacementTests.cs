using Snapper.Core.Geometry;
using Snapper.Core.Recording;
namespace Snapper.Core.Tests;

public sealed class RecordingPillPlacementTests
{
    private static readonly SizeD Pill = new(200, 32);
    private static readonly DisplayInfo A = new("A", new(0, 0, 1920, 1080), new(0, 0, 1920, 1032), 96, true);
    private static readonly DisplayInfo B = new("B", new(0, 0, 2880, 1620), new(0, 0, 2880, 1548), 144, false);
    private static readonly DisplayInfo C = new("C", new(0, 0, 3840, 2160), new(0, 0, 3840, 2064), 192, false);
    private static readonly DisplayInfo L = new("L", new(-2880, -300, 2880, 1620), new(-2880, -300, 2880, 1548), 144, false);
    public static TheoryData<DisplayInfo, PixelRect, PillPosition, PixelRect> Placements => new()
    {
        { A, new(400,300,600,400), PillPosition.Below, new(600,708,200,32) },
        { A, new(400,592,600,400), PillPosition.Below, new(600,1000,200,32) },
        { A, new(400,593,600,400), PillPosition.Above, new(600,553,200,32) },
        { A, A.Bounds, PillPosition.Inside, new(860,992,200,32) },
        { A, A.WorkArea, PillPosition.Inside, new(860,992,200,32) },
        { A, new(0,300,100,200), PillPosition.Below, new(0,508,200,32) },
        { A, new(1860,300,60,200), PillPosition.Below, new(1720,508,200,32) },
        { A, new(700,500,20,10), PillPosition.Below, new(610,518,200,32) },
        { A with { WorkArea = new(0,48,1920,1032) }, A.Bounds, PillPosition.Inside, new(860,1040,200,32) },
        { B, new(600,450,900,600), PillPosition.Below, new(900,1062,300,48) },
        { B, B.Bounds, PillPosition.Inside, new(1290,1488,300,48) },
        { C, new(800,600,1200,800), PillPosition.Below, new(1200,1416,400,64) },
        { C, new(800,1200,1200,800), PillPosition.Above, new(1200,1120,400,64) },
        { L, new(-1500,0,600,450), PillPosition.Below, new(-1350,462,300,48) },
        { L, new(-2880,900,750,348), PillPosition.Above, new(-2655,840,300,48) },
        { L, new(-2880,0,75,75), PillPosition.Below, new(-2880,87,300,48) },
        { A with { Bounds = new(0,-1080,1920,1080), WorkArea = new(0,-1080,1920,1080) }, new(0,-1080,1920,1080), PillPosition.Inside, new(860,-40,200,32) }
    };
    [Theory, MemberData(nameof(Placements))]
    public void PlacesInWorkArea(DisplayInfo display, PixelRect rect, PillPosition position, PixelRect expected)
    {
        var result = RecordingPillPlacement.Place(Pill, rect, display);
        Assert.Equal(position, result.Position);
        Assert.Equal(expected, result.Frame);
        Assert.InRange(result.Frame.Left, display.WorkArea.Left, display.WorkArea.Right - result.Frame.Width);
        Assert.InRange(result.Frame.Top, display.WorkArea.Top, display.WorkArea.Bottom - result.Frame.Height);
    }
    [Theory, InlineData(1,200,32,8), InlineData(1.25,250,40,10), InlineData(1.5,300,48,12), InlineData(1.75,350,56,14), InlineData(2,400,64,16)]
    public void PixelSizeAndMarginsUseRecordedScale(double scale, int width, int height, int margin)
    {
        Assert.Equal(new PixelSize(width,height), RecordingPillPlacement.PillPixelSize(Pill,scale));
        Assert.Equal(margin, RecordingPillPlacement.MarginPixels(scale));
    }
    [Fact] public void CeilingContentSizeAndAwayFromZeroOrigin()
    {
        Assert.Equal(new PixelSize(302,48), RecordingPillPlacement.PillPixelSize(new(201,32),1.5));
        Assert.Equal(new PixelRect(900,1062,302,48), RecordingPillPlacement.Place(new(201,32),new(600,450,901,600),B).Frame);
        var negative = B with { Bounds = new(-2880,0,2880,1620), WorkArea = new(-2880,0,2880,1548) };
        Assert.Equal(-1981, RecordingPillPlacement.Place(new(201,32),new(-2280,450,901,600),negative).Frame.X);
        Assert.Equal(5, RecordingPillPlacement.MarginPixels(1.5,3));
    }
    [Fact] public void OversizedPillPinsToWorkAreaOrigin()
    {
        var result = RecordingPillPlacement.Place(new(2000,1100),A.WorkArea,A);
        Assert.Equal(new PixelRect(0,0,2000,1100),result.Frame);
        Assert.Equal(new PixelRect(-2880,-300,4000,2000), RecordingPillPlacement.ReachableFrame(new(100,100,4000,2000),L.WorkArea));
    }
    [Fact] public void WideTimerLabelStaysOnScreen() => Assert.Equal(1920,
        RecordingPillPlacement.Place(new(260,32),new(1860,300,60,200),A).Frame.Right);
    [Fact] public void HiddenFrameStaysPutUntilUnreachable()
    {
        var frame = new PixelRect(-900,100,200,40);
        Assert.Equal(frame, RecordingPillPlacement.ReachableFrame(frame,new(-1000,0,1000,900)));
        Assert.Equal(new PixelRect(0,100,200,40), RecordingPillPlacement.ReachableFrame(frame,new(0,20,1000,880)));
        Assert.Equal(new PixelRect(800,860,200,40), RecordingPillPlacement.ReachableFrame(new(900,1000,200,40),new(0,20,1000,880)));
    }
    [Theory, InlineData(120), InlineData(144), InlineData(192)]
    public void ConversionRoundsContentUpWithoutClipping(int dpi)
    {
        var scale = dpi / 96.0;
        var actual = DisplayGeometry.PixelSizeForDips(new(201.1,32.1),scale);
        Assert.True(actual.Width >= DisplayGeometry.DipsToPixels(201.1,dpi));
        Assert.True(actual.Height >= DisplayGeometry.DipsToPixels(32.1,dpi));
        Assert.Equal(201.1, DisplayGeometry.PixelsToDips(DisplayGeometry.DipsToPixels(201.1,scale),scale),10);
    }
    [Fact] public void WorkAreaRepositionsWithoutChangingSelectionTopology()
    {
        var changed = A with { WorkArea = new(48,0,1872,1080) };
        Assert.False(DisplayConfigurationChange.HasChanged([A],[changed]));
        Assert.True(DisplayConfigurationChange.HasWorkAreaChanged([A],[changed]));
        Assert.False(DisplayConfigurationChange.HasWorkAreaChanged([A,B],[B,A]));
        Assert.NotEqual(RecordingPillPlacement.Place(Pill,A.Bounds,A).Frame,
            RecordingPillPlacement.Place(Pill,A.Bounds,changed).Frame);
    }
}
