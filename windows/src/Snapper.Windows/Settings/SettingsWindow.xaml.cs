using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Snapper.Core.App;
using Snapper.Core.Settings;
namespace Snapper.Windows.Settings;

internal partial class SettingsWindow : Window
{
    public bool AllowClose { get; set; }
    public IReadOnlyCollection<SettingsTab> AvailableTabs { get; } = new[] { SettingsTab.General, SettingsTab.Shortcuts, SettingsTab.Recording };
    public SettingsTab? SelectedTab
    {
        get => (Tabs.SelectedItem as TabItem)?.Tag as SettingsTab?;
        set
        {
            foreach (TabItem tab in Tabs.Items) if (Equals(tab.Tag, value)) { Tabs.SelectedItem = tab; break; }
        }
    }
    public SettingsWindow(SettingsViewModel model)
    {
        InitializeComponent();
        DataContext = model; model.Window = this;
        FocusManager.SetFocusedElement(this, Tabs.SelectedItem as TabItem);
        Activated += (_, _) => model.RefreshOnActivate();
        IsVisibleChanged += (_, _) => { if (!IsVisible) model.SettingsHidden(); };
        Tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != Tabs) return;
            FocusTabs();
            if (SelectedTab == SettingsTab.Recording) model.ReconcileMicrophone();
        };
        PreviewMouseDown += (_, e) =>
        {
            // Blank labels/panels do not take keyboard focus by themselves.
            // A click outside every recorder still ends recording before the clicked control runs.
            var element = e.OriginalSource as DependencyObject;
            while (element is not null)
            {
                if (element is ShortcutRecorder) return;
                element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
            }
            FocusTabs();
        };
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; FocusTabs(); Hide(); } };
        foreach (var row in model.ShortcutRows)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 7, 0, 12), DataContext = row };
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); header.ColumnDefinitions.Add(new ColumnDefinition());
            header.Children.Add(new TextBlock { Text = row.Label, VerticalAlignment = VerticalAlignment.Center });
            var recorder = new ShortcutRecorder(row, model) { HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetColumn(recorder, 1); header.Children.Add(recorder); stack.Children.Add(header);
            var warning = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(110, 6, 0, 0) };
            warning.SetBinding(VisibilityProperty, new Binding(nameof(row.ShowsConflictWarning)) { Converter = new BooleanToVisibilityConverter() });
            warning.Children.Add(new TextBlock { Text = "\uE7BA", FontFamily = new FontFamily("Segoe Fluent Icons"), Foreground = Brushes.DarkGoldenrod, Margin = new Thickness(0, 0, 6, 0) });
            warning.Children.Add(new TextBlock { Text = HotkeyFailureMessage.SettingsWarning, TextWrapping = TextWrapping.Wrap, MaxWidth = 200 });
            stack.Children.Add(warning);
            var hint = new TextBlock { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(110, 4, 0, 0) };
            hint.SetBinding(TextBlock.TextProperty, nameof(row.HintText)); stack.Children.Add(hint);
            ShortcutRows.Children.Add(stack);
        }
    }
    public void FocusTabs()
    {
        if (Tabs.SelectedItem is not TabItem header) return;
        FocusManager.SetFocusedElement(this, header);
        Keyboard.Focus(header);
    }
    private void ChooseFolder(object sender, RoutedEventArgs e) { if (DataContext is SettingsViewModel model) model.ChooseSaveFolder(); }
    private void OpenFolder(object sender, RoutedEventArgs e) { if (DataContext is SettingsViewModel model) model.OpenSaveFolder(); }
    private void OpenMicrophonePrivacy(object sender, RoutedEventArgs e) => SettingsViewModel.OpenMicrophonePrivacy();
    private void OpenStartupApps(object sender, RoutedEventArgs e) => LaunchAtLogin.OpenStartupApps();
}

/// <summary>Tab selection stays on the header until the user traverses into its content.</summary>
internal sealed class SettingsTabItem : TabItem
{
    protected override void OnPreviewGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus != this)
        {
            base.OnPreviewGotKeyboardFocus(e);
            return;
        }
        // TabControl selects/focuses before SelectionChanged. TabItem's implementation
        // can forward this focus straight into the first recorder, briefly suspending
        // hotkeys even if a later handler corrects it. Select here without that redirect.
        // Native Tab/Shift+Tab traversal into the content remains unchanged.
        SetCurrentValue(IsSelectedProperty, true);
    }
}
