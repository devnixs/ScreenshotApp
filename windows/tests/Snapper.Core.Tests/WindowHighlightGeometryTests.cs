using Snapper.Core.Capture;
using Snapper.Core.Geometry;
using static Snapper.Core.Tests.WindowSelectionTests;
namespace Snapper.Core.Tests;
public sealed class WindowHighlightGeometryTests
{
    [Fact] public void WindowOn150PercentNegativeOriginMonitor()
    {
        var rect=Assert.IsType<RectD>(WindowHighlightGeometry.LocalRect(new(-2000,300,900,600),C));
        Assert.Equal(373.333333,rect.X,5);Assert.Equal(200,rect.Y);Assert.Equal(600,rect.Width);Assert.Equal(400,rect.Height);
    }
    [Fact] public void SpanningWindowSplitsAcrossMixedDpi()
    {
        var frame=new PixelRect(1500,100,1000,400);
        Assert.Equal(new RectD(1500,100,420,400),WindowHighlightGeometry.LocalRect(frame,A));
        Assert.Equal(new RectD(0,320,290,200),WindowHighlightGeometry.LocalRect(frame,B));
        Assert.Null(WindowHighlightGeometry.LocalRect(frame,C));
    }
    [Fact] public void IncludesTaskbarArea() => Assert.Equal(new RectD(100,1040,300,40),WindowHighlightGeometry.LocalRect(new(100,1040,300,80),A));
    [Fact] public void TouchingEdgeAndEmptyFrameReturnNull()
    {Assert.Null(WindowHighlightGeometry.LocalRect(new(-100,10,100,100),A));Assert.Null(WindowHighlightGeometry.LocalRect(new(10,10,0,100),A));}
}
