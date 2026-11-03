using System.Windows.Media;
using System.Windows.Threading;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Windows.Annotate;

public sealed class WpfTextMeasurer : ITextMeasurer
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    public double LineHeight(double fontPixelSize)
    {
        dispatcher.VerifyAccess();
        return Math.Ceiling(TextStyle.CreateFormattedText(" ", fontPixelSize, Brushes.Black).Height);
    }
    public SizeD LayoutSize(string text, double fontPixelSize)
    {
        dispatcher.VerifyAccess();
        var measured = text.Length == 0 || text.EndsWith('\n') ? text + " " : text;
        var formatted = TextStyle.CreateFormattedText(measured, fontPixelSize, Brushes.Black);
        return new(text.Length == 0 ? 0 : Math.Ceiling(formatted.WidthIncludingTrailingWhitespace), Math.Max(LineHeight(fontPixelSize), Math.Ceiling(formatted.Height)));
    }
}
