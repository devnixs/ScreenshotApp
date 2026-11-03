using System.Globalization;
using Snapper.Core.Capture;
using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;
public sealed class SelectionReadoutTests
{
    [Theory]
    [InlineData(1440,900,"1440 × 900")] [InlineData(0,0,"0 × 0")] [InlineData(99.5,10.4,"100 × 10")]
    [InlineData(12345,4,"12345 × 4")] [InlineData(200,133.333333,"200 × 133")]
    [InlineData(200.666666,2,"201 × 2")] [InlineData(2560,1440,"2560 × 1440")]
    public void TextUsesRoundedDipsAndMultiplicationSign(double w,double h,string expected)
    {
        var old=CultureInfo.CurrentCulture;
        try {CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");Assert.Equal(expected,SelectionReadout.Text(new(w,h)));}
        finally {CultureInfo.CurrentCulture=old;}
    }
    [Theory]
    [InlineData(500,400,512,412)] [InlineData(1430,400,1358,412)] [InlineData(500,890,512,858)]
    [InlineData(1430,890,1358,858)] [InlineData(0,0,12,12)]
    public void PlacementFlipsAtEdges(double x,double y,double expectedX,double expectedY) =>
        Assert.Equal(new(expectedX,expectedY),SelectionReadout.Origin(new(60,20),new(x,y),new(0,0,1440,900)));
    [Fact] public void NegativeOriginDisplayRect() =>
        Assert.Equal(new(-77,843),SelectionReadout.Origin(new(60,20),new(-5,875),new(-1920,-200,1920,1080)));
    [Fact] public void OversizedLabelPinsToInsetLeft() =>
        Assert.Equal(4,SelectionReadout.Origin(new(2000,20),new(500,400),new(0,0,1440,900)).X);
    [Fact] public void OversizedLabelPinsToInsetTop() =>
        Assert.Equal(4,SelectionReadout.Origin(new(60,2000),new(500,400),new(0,0,1440,900)).Y);
    [Fact] public void LabelStaysInsideAcrossCursorGrid()
    {
        foreach(var display in new RectD[]{new(0,0,1440,900),new(-1920,-200,1920,1080)})
        for(var row=0;row<=10;row++)for(var col=0;col<=10;col++)
        {
            var p=SelectionReadout.Origin(new(60,20),new(display.X+display.Width*col/10,display.Y+display.Height*row/10),display);
            Assert.InRange(p.X,display.Left+4,display.Right-4-60);Assert.InRange(p.Y,display.Top+4,display.Bottom-4-20);
        }
    }
}
