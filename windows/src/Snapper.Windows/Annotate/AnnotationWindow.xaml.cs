using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
using Snapper.Core.Settings;
using Snapper.Core.Support;
using Snapper.Windows.Support;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
namespace Snapper.Windows.Annotate;

internal sealed partial class AnnotationWindow : Window
{
    private readonly AppSettings settings;
    private ToolbarStyleState styleState;
    private readonly Dictionary<AnnotationTool, RadioButton> tools = [];
    private readonly Dictionary<StrokeWidth, RadioButton> widths = [];
    private readonly List<Border> swatches = [];
    private nint handle;
    private AnnotationTool currentTool = AnnotationTools.DefaultTool;
    public AnnotationCanvas Canvas { get; }
    public AnnotationDocument Document => Canvas.Document;
    public DateTime CaptureDate { get; }
    public bool HasOpenDialog { get; private set; }
    public nint Handle => handle;
    public AnnotationTool CurrentTool
    {
        get => currentTool;
        set { currentTool = value; Canvas.CurrentTool = value; if (tools.TryGetValue(value, out var button)) button.IsChecked = true; }
    }
    public AnnotationWindow(AnnotationDocument document, BitmapSource image, AppSettings settings, DateTime captureDate)
    {
        InitializeComponent();
        this.settings = settings; CaptureDate = captureDate;
        Title = new FileNamer().BaseName(CaptureKind.Screenshot, new DateTimeOffset(captureDate));
        Canvas = new(document, image);
        CanvasHost.Children.Add(Canvas);
        styleState = new(new(settings.AnnotationColor, settings.AnnotationStrokeWidth));
        BuildToolbar(); RefreshStyle(); CurrentTool = AnnotationTools.DefaultTool;
        Document.Changed += DocumentChanged;
        PreviewKeyDown += KeyDownPreview; PreviewKeyUp += KeyUpPreview;
        Deactivated += WindowDeactivated; Closed += WindowClosed;
    }
    private void BuildToolbar()
    {
        foreach (var descriptor in AnnotationToolbarLayout.Tools)
        {
            var button = new RadioButton { GroupName = "AnnotationTools", Style = (Style)FindResource("ToolButton"),
                Content = Glyph(descriptor.Glyph, descriptor.GlyphRotation), ToolTip = descriptor.ToolTip,
                IsEnabled = descriptor.Tool != AnnotationTool.Text, Focusable = false, IsTabStop = false };
            AutomationProperties.SetName(button, descriptor.Label);
            button.Click += (_, _) => { CurrentTool = descriptor.Tool; FocusCanvas(); };
            tools.Add(descriptor.Tool, button); ToolsPanel.Children.Add(button);
        }
        Separator();
        foreach (var preset in Enum.GetValues<ColorPreset>())
        {
            var color = preset.Color();
            var ring = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12), Padding = new Thickness(2),
                Child = new Ellipse { Width = 16, Height = 16, Fill = ShapeRenderer.Brush(color), Stroke = SystemColors.ControlDarkBrush, StrokeThickness = 1 } };
            var button = new Button { Content = ring, ToolTip = preset.DisplayName(), Focusable = false, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, IsTabStop = false };
            AutomationProperties.SetName(button, preset.DisplayName());
            button.Click += (_, _) => Pick(new ToolbarStyleEvent.ColorPicked(color));
            swatches.Add(ring); ToolsPanel.Children.Add(button);
        }
        var customRing = new Border { BorderThickness = new Thickness(2), Padding = new Thickness(2), CornerRadius = new CornerRadius(4), Child = Glyph(AnnotationToolbarLayout.CustomColorGlyph) };
        var custom = new Button { Content = customRing, ToolTip = AnnotationToolbarLayout.CustomColorToolTip, Focusable = false, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, IsTabStop = false };
        AutomationProperties.SetName(custom, AnnotationToolbarLayout.CustomColorToolTip);
        custom.Click += (_, _) => PickCustomColor();
        swatches.Add(customRing); ToolsPanel.Children.Add(custom);
        Separator();
        foreach (var width in Enum.GetValues<StrokeWidth>())
        {
            var index = (int)width;
            var button = new RadioButton { GroupName = "AnnotationWidths", Style = (Style)FindResource("ToolButton"),
                Content = AnnotationToolbarLayout.StrokeLabels[index], ToolTip = AnnotationToolbarLayout.StrokeToolTips[index], Focusable = false, IsTabStop = false };
            AutomationProperties.SetName(button, AnnotationToolbarLayout.StrokeToolTips[index]);
            button.Click += (_, _) => Pick(new ToolbarStyleEvent.StrokeWidthPicked(width));
            widths.Add(width, button); ToolsPanel.Children.Add(button);
        }
    }
    private static TextBlock Glyph(string text, double rotation = 0) => new()
    { Text = text, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16, Width = 16, Height = 20,
        TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(rotation) };
    private void Separator() => ToolsPanel.Children.Add(new Border { Width = 1, Background = SystemColors.ControlDarkBrush, Margin = new Thickness(4, 3, 4, 3) });
    public void Pick(ToolbarStyleEvent e)
    {
        if (Canvas.IsDragging) { RefreshStyle(); return; }
        var effects = styleState.Reduce(e);
        Canvas.Editor.ApplyStyle(effects, (shape, width) => shape.WithStrokeWidth(width));
        if (effects.PersistColor is { } color) settings.AnnotationColor = color;
        if (effects.PersistStrokeWidth is { } width) settings.AnnotationStrokeWidth = width;
        RefreshStyle(); FocusCanvas();
    }
    private void DocumentChanged(object? sender, EventArgs args) => RefreshStyle();
    private void RefreshStyle()
    {
        styleState.Reduce(new ToolbarStyleEvent.SelectedShapeStyleChanged(Document.SelectedShape?.Style()));
        Canvas.CurrentColor = styleState.CurrentStyle.Color; Canvas.CurrentStrokeWidth = styleState.CurrentStyle.StrokeWidth;
        var selected = styleState.SwatchSelection is SwatchSelection.Preset(var index) ? index : 8;
        for (var i = 0; i < swatches.Count; i++) swatches[i].BorderBrush = i == selected ? SystemColors.HighlightBrush : Brushes.Transparent;
        foreach (var (width, button) in widths) button.IsChecked = width == styleState.DisplayedStyle.StrokeWidth;
    }
    private void PickCustomColor()
    {
        if (Canvas.IsDragging) return;
        HasOpenDialog = true;
        try { if (ColorDialogPresenter.Pick(this, styleState.DisplayedStyle.Color) is { } color) Pick(new ToolbarStyleEvent.ColorPicked(color)); }
        finally { HasOpenDialog = false; FocusCanvas(); }
    }
    private void KeyDownPreview(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase) return; // 0042 supplies text editing routing.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        var coreModifiers = KeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) coreModifiers |= KeyModifiers.Ctrl;
        if (modifiers.HasFlag(ModifierKeys.Alt)) coreModifiers |= KeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) coreModifiers |= KeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) coreModifiers |= KeyModifiers.Win;
        if (CanvasKeyMap.Map(KeyInterop.VirtualKeyFromKey(key), coreModifiers) is { } command)
        { e.Handled = Canvas.Execute(command); return; }
        if (key == Key.W && modifiers == ModifierKeys.Control) { Close(); e.Handled = true; return; }
        if (key is Key.LeftShift or Key.RightShift && Canvas.IsDragging) { Canvas.RefreshPreview(); return; }
        var letter = key switch { Key.V => "v", Key.R => "r", Key.A => "a", Key.T => "t", _ => "" };
        if (AnnotationTools.ForShortcut(letter, (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0, false) is { } tool)
        { CurrentTool = tool; e.Handled = true; }
    }
    private void KeyUpPreview(object sender, KeyEventArgs e)
    { if (e.Key is Key.LeftShift or Key.RightShift && Canvas.IsDragging) Canvas.RefreshPreview(); }
    public void FocusCanvas() { Canvas.Focus(); Keyboard.Focus(Canvas); }
    private void WindowDeactivated(object? sender, EventArgs args) => Canvas.AbortDrag();
    private void WindowClosed(object? sender, EventArgs args)
    {
        CaptureExclusion.Unregister(new HWND(Handle));
        PreviewKeyDown -= KeyDownPreview; PreviewKeyUp -= KeyUpPreview; Deactivated -= WindowDeactivated; Closed -= WindowClosed;
        Document.Changed -= DocumentChanged;
        Canvas.Dispose(); CanvasHost.Children.Clear(); Content = null;
    }
    public unsafe void Place(DisplayInfo display, IReadOnlyCollection<PixelPoint> existingOrigins)
    {
        var hwnd = new HWND(new WindowInteropHelper(this).EnsureHandle());
        handle = hwnd;
        CaptureExclusion.Register(hwnd); // Normal annotation windows remain capturable, but never selectable as targets.
        var wa = display.WorkArea;
        var flags = SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        PInvoke.SetWindowPos(hwnd, default, wa.X, wa.Y, 200, 200, flags);
        var dpi = PInvoke.GetDpiForWindow(hwnd); if (dpi == 0) dpi = (uint)display.Dpi;
        var scale = dpi / 96.0;
        var rect = new RECT();
        var style = (WINDOW_STYLE)(uint)(nuint)PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        var exStyle = (WINDOW_EX_STYLE)(uint)(nuint)PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        if (!PInvoke.AdjustWindowRectExForDpi(ref rect, style, false, exStyle, dpi)) throw new System.ComponentModel.Win32Exception();
        ToolbarRow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // Include toolbar margin and its containing border's bottom line.
        var toolbar = ToolbarRow.DesiredSize;
        var chrome = new SizeD(DisplayGeometry.PixelsToDips(rect.right - rect.left, scale),
            DisplayGeometry.PixelsToDips(rect.bottom - rect.top, scale) + toolbar.Height + 1);
        var minimum = new SizeD(Math.Max(480, toolbar.Width), 300);
        var placement = AnnotationWindowSizing.Frame(Document.ImagePixelSize, Document.PixelsPerDip, wa, scale, chrome, minimum);
        var frame = AnnotationWindowSizing.Cascaded(placement.WindowRect, existingOrigins, wa, (int)Math.Round(DisplayGeometry.DipsToPixels(24, scale), MidpointRounding.AwayFromZero));
        MinWidth = Math.Min(DisplayGeometry.PixelsToDips(wa.Width, scale), minimum.Width + chrome.Width);
        MinHeight = Math.Min(DisplayGeometry.PixelsToDips(wa.Height, scale), minimum.Height + chrome.Height);
        PInvoke.SetWindowPos(hwnd, default, frame.X, frame.Y, frame.Width, frame.Height, flags);
    }
}
