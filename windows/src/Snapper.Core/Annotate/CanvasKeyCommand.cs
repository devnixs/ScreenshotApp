using Snapper.Core.Support;
namespace Snapper.Core.Annotate;

public enum CanvasKeyCommand { DeleteSelection, Deselect, Undo, Redo, Save, CopyImage, Close, Refuse }
public static class CanvasKeyMap
{
    /// <summary>Export commands bubble from the focused editor. Ctrl+C never falls back to the image while editing.</summary>
    public static CanvasKeyCommand? ExportCommand(KeyChord chord, bool isEditingText, bool isDragging)
    {
        if (chord.Modifiers != KeyModifiers.Ctrl) return null;
        return chord.VirtualKey switch
        {
            0x53 => isDragging ? CanvasKeyCommand.Refuse : CanvasKeyCommand.Save,
            0x43 => isEditingText ? null : isDragging ? CanvasKeyCommand.Refuse : CanvasKeyCommand.CopyImage,
            0x57 => CanvasKeyCommand.Close,
            _ => null
        };
    }
    public static CanvasKeyCommand? Map(int virtualKey, KeyModifiers modifiers)
    {
        if ((modifiers & (KeyModifiers.Alt | KeyModifiers.Win)) != 0) return null;
        return (virtualKey, modifiers) switch
        {
            (0x5A, KeyModifiers.Ctrl) => CanvasKeyCommand.Undo,
            (0x5A, KeyModifiers.Ctrl | KeyModifiers.Shift) or (0x59, KeyModifiers.Ctrl) => CanvasKeyCommand.Redo,
            (0x2E or 0x08, KeyModifiers.None or KeyModifiers.Shift) => CanvasKeyCommand.DeleteSelection,
            (0x1B, KeyModifiers.None or KeyModifiers.Shift) => CanvasKeyCommand.Deselect,
            _ => null
        };
    }
}
