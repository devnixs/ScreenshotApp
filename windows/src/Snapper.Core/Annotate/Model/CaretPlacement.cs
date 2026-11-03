namespace Snapper.Core.Annotate.Model;
public static class CaretPlacement
{
    public static int InsertionIndex(string editorText, int hitIndex, double clickX, double charLeft, double charRight)
    {
        if (editorText.Length == 0 || hitIndex < 0) return editorText.Length;
        var i = Math.Clamp(hitIndex, 0, editorText.Length);
        if (i < editorText.Length && editorText[i] is not ('\r' or '\n') && clickX >= (charLeft + charRight) / 2) i++;
        if (i > 0 && i < editorText.Length && editorText[i] == '\n' && editorText[i - 1] == '\r') i--;
        return i;
    }
}
