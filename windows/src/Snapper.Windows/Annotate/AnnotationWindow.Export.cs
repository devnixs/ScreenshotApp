using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Capture;
using Snapper.Core.Support;
using Snapper.Windows.Support;
using Windows.Win32;
using Windows.Win32.Foundation;
using Forms = System.Windows.Forms;

namespace Snapper.Windows.Annotate;

internal sealed partial class AnnotationWindow
{
    private bool closeConfirmed, sessionEnding, isPrompting, isExporting, isClosed, blockReasonRegistered;
    private HwndSource? sessionSource;
    private Forms.TaskDialogPage? discardPrompt;
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public DateTime CaptureTime => CaptureDate;
    public Task ClosedTask => closed.Task;

    private void BuildExportToolbar()
    {
        AddExportButton("Save", "\uE74E", "Save (Ctrl+S)", async () => await SaveAsync());
        AddExportButton("Copy", "\uE8C8", "Copy (Ctrl+C)", async () => await TryCopyFlattenedImageAsync());
    }
    private void AddExportButton(string label, string glyph, string tooltip, Action action)
    {
        var button = new Button { Content = Glyph(glyph), ToolTip = tooltip, Focusable = false, IsTabStop = false,
            Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(2, 0, 2, 0) };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action(); ActionsPanel.Children.Add(button);
    }
    private async void ExportKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = KeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= KeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= KeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= KeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= KeyModifiers.Win;
        if (!KeyChord.TryCreate(modifiers, KeyInterop.VirtualKeyFromKey(key), out var chord)) return;
        var command = CanvasKeyMap.ExportCommand(chord, TextEditing.IsEditing, Canvas.IsDragging);
        if (command is null) return;
        e.Handled = true;
        switch (command)
        {
            case CanvasKeyCommand.Save: await SaveAsync(); break;
            case CanvasKeyCommand.CopyImage: await TryCopyFlattenedImageAsync(); break;
            case CanvasKeyCommand.Close: Close(); break;
            case CanvasKeyCommand.Refuse: Beep(); break;
        }
    }
    private static void Beep() => System.Media.SystemSounds.Beep.Play();

    public async Task SaveAsync()
    {
        if (Canvas.IsDragging || HasOpenDialog) { Beep(); return; }
        TextEditing.Commit();
        string? folder;
        try { folder = SaveFolder.CreatePolicy().Prepare(settings.SaveFolder); }
        catch (Exception error) { Debug.WriteLine($"Snapper: Annotation save default folder unavailable: {error.Message}"); folder = null; }
        var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png", DefaultExt = ".png", AddExtension = true, OverwritePrompt = true,
            FileName = AnnotationSaveDefaults.SuggestedFileName(CaptureTime, folder, File.Exists)
        };
        if (folder is not null) dialog.InitialDirectory = folder;
        hasOpenDialog = true;
        try { if (dialog.ShowDialog(this) != true || isClosed) return; }
        finally { hasOpenDialog = false; }
        isExporting = true;
        try
        {
            var snapshot = Document.Clone();
            var bitmap = AnnotationExporter.Flatten(Canvas.Image, snapshot);
            var scale = snapshot.PixelsPerDip;
            await Task.Run(() => AtomicFileWriter.Replace(dialog.FileName, PngEncoder.Encode(Captured(bitmap, scale))));
            MarkExportedSnapshotClean(snapshot);
        }
        catch (Exception error)
        {
            if (!isClosed) ErrorAlerts.ShowError(UnsavedAnnotationsPolicy.SaveFailedTitle(Path.GetDirectoryName(dialog.FileName) ?? dialog.FileName), error.Message);
        }
        finally { isExporting = false; }
    }

    /// <summary>Shared with Copy & Close. Rasterization stays on the dispatcher; encoding uses the frozen raster off-thread.</summary>
    public async Task<bool> TryCopyFlattenedImageAsync()
    {
        if (Canvas.IsDragging || HasOpenDialog) { Beep(); return false; }
        TextEditing.Commit();
        isExporting = true;
        try
        {
            var snapshot = Document.Clone();
            var bitmap = AnnotationExporter.Flatten(Canvas.Image, snapshot);
            var scale = snapshot.PixelsPerDip;
            var (png, dib) = await Task.Run(() =>
            {
                var image = Captured(bitmap, scale);
                return (PngEncoder.Encode(image), DibEncoder.FromBgra(image.Bgra, image.Width, image.Height));
            });
            if (isClosed) return false;
            if (!Support.Clipboard.Copy(png, dib)) throw new InvalidOperationException("The clipboard couldn't accept the annotated image.");
            MarkExportedSnapshotClean(snapshot);
            return true;
        }
        catch (Exception error)
        {
            if (!isClosed) ErrorAlerts.ShowError(UnsavedAnnotationsPolicy.CopyFailedTitle, error.Message);
            return false;
        }
        finally { isExporting = false; }
    }
    private static CapturedImage Captured(BitmapSource source, double scale)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        var stride = checked(converted.PixelWidth * 4);
        var bytes = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(bytes, stride, 0);
        return new(converted.PixelWidth, converted.PixelHeight, bytes, scale);
    }
    private void MarkExportedSnapshotClean(AnnotationDocument snapshot)
    {
        // Export encoding may yield to further edits; only the exported content becomes clean.
        if (!isClosed && AnnotationExportPlan.CanMarkClean(Document, snapshot, TextEditing.IsEditing)) Document.MarkClean();
    }

    private void ConfirmWindowClosing(CancelEventArgs args)
    {
        if (closeConfirmed || sessionEnding) return;
        if (isPrompting) { args.Cancel = true; Beep(); return; }
        TextEditing.Commit();
        if (UnsavedAnnotationsPolicy.CloseDecisionFor(Document.IsDirty) == CloseDecision.CloseImmediately) return;
        isPrompting = true;
        discardPrompt = AnnotationPrompts.DiscardPage();
        try
        {
            var result = Forms.TaskDialog.ShowDialog(Handle, discardPrompt);
            if (closeConfirmed || sessionEnding || result == discardPrompt.Buttons[0]) closeConfirmed = true;
            else args.Cancel = true;
        }
        finally { isPrompting = false; discardPrompt = null; }
    }
    public void CloseWithoutPrompt()
    {
        closeConfirmed = true;
        if (isPrompting) discardPrompt?.BoundDialog?.Close();
        else if (!isClosed) Close();
    }
    public void PrepareForSessionEnd() => sessionEnding = true;

    private void InitializeSessionHook(object? sender, EventArgs args)
    {
        sessionSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        sessionSource?.AddHook(SessionMessage);
    }
    private nint SessionMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == PInvoke.WM_QUERYENDSESSION)
        {
            TextEditing.Commit();
            var critical = (lParam.ToInt64() & PInvoke.ENDSESSION_CRITICAL) != 0;
            var blocks = UnsavedAnnotationsPolicy.SessionEndDecisionFor(Document.IsDirty, critical) == SessionEndDecision.Block;
            SetAnnotationBlockReason(blocks);
            if (blocks) { handled = true; return 0; }
        }
        else if (message == PInvoke.WM_ENDSESSION)
        {
            if (wParam != 0) sessionEnding = true;
            else SetAnnotationBlockReason(false);
        }
        return 0;
    }
    private void SetAnnotationBlockReason(bool blocks)
    {
        if (blocks == blockReasonRegistered) return;
        var hwnd = new HWND(Handle != 0 ? Handle : new WindowInteropHelper(this).Handle);
        var success = blocks ? PInvoke.ShutdownBlockReasonCreate(hwnd, UnsavedAnnotationsPolicy.ShutdownBlockReason) : PInvoke.ShutdownBlockReasonDestroy(hwnd);
        if (success) blockReasonRegistered = blocks;
        else Debug.WriteLine($"Snapper: Annotation shutdown reason: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
    }
    private void DisposeSessionHook()
    {
        SetAnnotationBlockReason(false);
        if (sessionSource is { IsDisposed: false } source) source.RemoveHook(SessionMessage);
        sessionSource = null;
    }
}
