using Snapper.Core.Support;
namespace Snapper.Core.Annotate;

public enum CanvasKeyCommand { DeleteSelection, Deselect, Undo, Redo }
public static class CanvasKeyMap
{
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
