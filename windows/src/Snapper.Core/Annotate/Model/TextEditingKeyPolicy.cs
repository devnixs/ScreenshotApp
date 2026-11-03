namespace Snapper.Core.Annotate.Model;
public enum TextEditingKeyRoute { NotEditing, Commit, Editor, Window, Ignore }
public enum TextEditingKey { Escape, Enter, Tab, S, W, F4, Other }
public static class TextEditingKeyPolicy
{
    public static TextEditingKeyRoute Route(bool isEditing, TextEditingKey key, bool ctrl, bool shift, bool alt)
    {
        if (!isEditing) return TextEditingKeyRoute.NotEditing;
        if (key == TextEditingKey.Escape) return TextEditingKeyRoute.Commit;
        if (key == TextEditingKey.Tab) return TextEditingKeyRoute.Ignore;
        if (ctrl && !alt && key is TextEditingKey.Enter or TextEditingKey.S or TextEditingKey.W || alt && !ctrl && key == TextEditingKey.F4) return TextEditingKeyRoute.Window;
        return TextEditingKeyRoute.Editor;
    }
}
