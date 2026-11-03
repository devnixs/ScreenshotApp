using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Snapper.Core.Settings;
using Snapper.Core.Support;
namespace Snapper.Windows.Settings;

internal sealed class ShortcutRecorder : UserControl
{
    private readonly ShortcutRecorderSession session;
    private readonly ShortcutRowViewModel row;
    private readonly SettingsViewModel model;
    private readonly Border border = new() { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(8, 6, 8, 6) };
    private readonly TextBlock preview = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button clear = new() { Content = "\uE711", FontFamily = new FontFamily("Segoe Fluent Icons"), ToolTip = "Clear", Focusable = false, IsTabStop = false, Padding = new Thickness(3, 0, 3, 0), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
    private Window? owner;
    public ShortcutRecorder(ShortcutRowViewModel row, SettingsViewModel model)
    {
        this.row = row; this.model = model;
        session = new ShortcutRecorderSession(row.Purpose, model.CurrentBindings);
        Focusable = true; IsTabStop = true; Width = 180; Cursor = Cursors.Arrow;
        System.Windows.Automation.AutomationProperties.SetName(this, row.Label.TrimEnd(':') + " shortcut");
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition()); content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(preview); Grid.SetColumn(clear, 1); content.Children.Add(clear);
        border.Child = content; Content = border;
        clear.Click += (_, e) => { e.Handled = true; Apply(session.ClearClicked()); };
        PreviewMouseDown += (_, e) => { if (!e.Handled && !clear.IsMouseOver) { Focus(); e.Handled = true; } };
        GotKeyboardFocus += (_, _) => { Apply(session.FocusGained()); Redraw(); };
        LostKeyboardFocus += (_, _) => EndRecording();
        IsVisibleChanged += (_, _) => { if (!IsVisible) ReleaseFocus(); };
        Loaded += (_, _) =>
        {
            owner = System.Windows.Window.GetWindow(this);
            if (owner is not null) owner.Deactivated += Deactivated;
            row.PropertyChanged += RowChanged;
            Redraw();
        };
        Unloaded += (_, _) =>
        {
            EndRecording();
            if (owner is not null) owner.Deactivated -= Deactivated;
            row.PropertyChanged -= RowChanged;
            owner = null;
        };
        PreviewKeyDown += KeyDownPreview;
        PreviewKeyUp += (_, e) =>
        {
            if (!session.IsRecording) return;
            var key = RealKey(e);
            Apply(session.KeyUp(KeyInterop.VirtualKeyFromKey(key), Modifiers()));
            e.Handled = true; Redraw();
        };
        Redraw();
    }
    private void Deactivated(object? sender, EventArgs e) => ReleaseFocus();
    private void RowChanged(object? sender, PropertyChangedEventArgs e) => Redraw();
    private void EndRecording() { Apply(session.FocusLost()); Redraw(); }
    private static Key RealKey(KeyEventArgs e) => e.Key switch
    { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, Key.DeadCharProcessed => e.DeadCharProcessedKey, _ => e.Key };
    private static KeyModifiers Modifiers()
    {
        var m = Keyboard.Modifiers;
        return (m.HasFlag(ModifierKeys.Control) ? KeyModifiers.Ctrl : 0) | (m.HasFlag(ModifierKeys.Alt) ? KeyModifiers.Alt : 0) |
            (m.HasFlag(ModifierKeys.Shift) ? KeyModifiers.Shift : 0) | (m.HasFlag(ModifierKeys.Windows) ? KeyModifiers.Win : 0);
    }
    private void KeyDownPreview(object sender, KeyEventArgs e)
    {
        if (!session.IsRecording) return;
        var effects = session.KeyDown(KeyInterop.VirtualKeyFromKey(RealKey(e)), Modifiers());
        var pass = effects.Any(x => x is RecorderEffect.PassKeyThrough);
        if (pass)
        {
            // End the session while leaving native WPF Tab traversal at the recorder's position.
            // Moving to the tab header first would cycle back into this same recorder.
            EndRecording();
        }
        Apply(effects, pass);
        e.Handled = !pass;
        Redraw();
    }
    private void ReleaseFocus()
    {
        if (owner is SettingsWindow window) window.FocusTabs();
        EndRecording();
    }
    private void Apply(IReadOnlyList<RecorderEffect> effects, bool tabPassThrough = false)
    {
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case RecorderEffect.SuspendHotkeys: model.SuspendHotkeys(); break;
                case RecorderEffect.ResumeHotkeys: model.ResumeHotkeys(); break;
                case RecorderEffect.Commit(var chord): row.Binding = chord; break;
                case RecorderEffect.Beep: System.Media.SystemSounds.Beep.Play(); break;
                case RecorderEffect.ShowHint(var hint): row.HintText = hint; row.Refresh(); break;
                case RecorderEffect.ShowDuplicateAlert(var reason):
                    ReleaseFocus();
                    if (owner is { } window) MessageBox.Show(window, reason, "Snapper", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                case RecorderEffect.ReleaseFocus: if (!tabPassThrough) ReleaseFocus(); break;
                case RecorderEffect.PassKeyThrough: break;
            }
        }
    }
    private void Redraw()
    {
        preview.Text = ShortcutRecorderSession.PreviewText(session.IsRecording, session.HeldModifiers, row.Binding);
        clear.Visibility = !session.IsRecording && row.Binding is not null ? Visibility.Visible : Visibility.Collapsed;
        border.BorderBrush = session.IsRecording ? SystemColors.HighlightBrush : SystemColors.ActiveBorderBrush;
        border.Background = SystemColors.WindowBrush;
    }
}
