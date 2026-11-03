using Snapper.Core.Annotate.Model;
namespace Snapper.Core.Tests;
public sealed class EditorNewlinesTests
{
    [Theory][InlineData("A\r\nB")][InlineData("A\rB")][InlineData("A\nB")]
    public void ModelNormalizes(string text) => Assert.Equal("A\nB", EditorNewlines.ToModel(text));
    [Theory][InlineData("A\nB")][InlineData("A\r\nB")]
    public void EditorDoesNotDouble(string text) => Assert.Equal("A\r\nB", EditorNewlines.ToEditor(text));
    [Theory][InlineData("")][InlineData("\n")][InlineData("a\n\nb\n")]
    public void RoundTrip(string text) => Assert.Equal(text, EditorNewlines.ToModel(EditorNewlines.ToEditor(text)));
}
