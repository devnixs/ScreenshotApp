using Snapper.Core.Geometry;

namespace Snapper.Core.Annotate.Model;

/// <summary>Measures Segoe UI Bold at fontPixelSize, treating one image pixel as one DIP at 96 DPI
/// (FormattedText pixelsPerDip = 1), independently of monitor scale. Layout size excludes the
/// outline: widest line width and total line height. Empty strings have one line; trailing
/// newlines add a line.</summary>
public interface ITextMeasurer
{
    SizeD LayoutSize(string text, double fontPixelSize);
    double LineHeight(double fontPixelSize);
}
