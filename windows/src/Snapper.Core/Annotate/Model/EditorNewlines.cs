namespace Snapper.Core.Annotate.Model;
public static class EditorNewlines
{
    public static string ToModel(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    public static string ToEditor(string text) => ToModel(text).Replace("\n", "\r\n", StringComparison.Ordinal);
}
