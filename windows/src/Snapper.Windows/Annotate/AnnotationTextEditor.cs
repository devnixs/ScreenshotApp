using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Snapper.Windows.Annotate;

internal sealed class AnnotationTextEditor : TextBox
{
    public AnnotationTextEditor()
    {
        AcceptsReturn = true; AcceptsTab = false; TextWrapping = TextWrapping.NoWrap;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden; VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        SpellCheck.SetIsEnabled(this, false); IsUndoEnabled = true;
        Background = Brushes.Transparent; Foreground = Brushes.Transparent;
        BorderThickness = new Thickness(0); Padding = new Thickness(0); MinWidth = 2;
        FocusVisualStyle = null; SelectionBrush = SystemColors.HighlightBrush; SelectionOpacity = .4;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        var content = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        content.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        content.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        content.SetValue(ScrollViewer.CanContentScrollProperty, false);
        content.SetValue(FocusableProperty, false);
        Template = new ControlTemplate(typeof(TextBox)) { VisualTree = content };
        InputBindings.Add(new KeyBinding(ApplicationCommands.Redo, new KeyGesture(Key.Z, ModifierKeys.Control | ModifierKeys.Shift)));
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Tab) e.Handled = true; };
        RequestBringIntoView += (_, e) => e.Handled = true;
    }
}
