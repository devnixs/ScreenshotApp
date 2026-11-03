using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;

namespace Snapper.Core.Tests.Fakes;

public sealed class FakeTextMeasurer : ITextMeasurer
{
    public SizeD LayoutSize(string text, double fontPixelSize) => new(text.Length * fontPixelSize, fontPixelSize);
    public double LineHeight(double fontPixelSize) => fontPixelSize;
}
