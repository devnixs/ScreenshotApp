using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Snapper.Core.Annotate.Model;
namespace Snapper.Windows.Annotate;

internal static class TextStyle
{
    public static readonly Typeface Typeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    public static FormattedText CreateFormattedText(string modelText, double fontPixelSize, Brush brush) =>
        new(modelText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface, fontPixelSize, brush,
            null, TextFormattingMode.Ideal, 1.0);
    public static void ApplyTo(System.Windows.Controls.TextBox editor, double fontPixelSize, RgbaColor color)
    {
        editor.FontFamily = Typeface.FontFamily; editor.FontWeight = Typeface.Weight;
        editor.FontStyle = Typeface.Style; editor.FontStretch = Typeface.Stretch; editor.FontSize = fontPixelSize;
        editor.CaretBrush = ShapeRenderer.Brush(color);
        editor.FlowDirection = FlowDirection.LeftToRight;
        editor.Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag);
        TextOptions.SetTextFormattingMode(editor, TextFormattingMode.Ideal);
    }
}
