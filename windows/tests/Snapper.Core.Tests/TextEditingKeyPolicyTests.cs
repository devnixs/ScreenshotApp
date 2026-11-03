using Snapper.Core.Annotate.Model;
namespace Snapper.Core.Tests;
public sealed class TextEditingKeyPolicyTests
{
    [Theory]
    [InlineData(TextEditingKey.Escape, false, false, false, TextEditingKeyRoute.Commit)]
    [InlineData(TextEditingKey.Enter, false, false, false, TextEditingKeyRoute.Editor)]
    [InlineData(TextEditingKey.Enter, false, true, false, TextEditingKeyRoute.Editor)]
    [InlineData(TextEditingKey.Enter, true, false, false, TextEditingKeyRoute.Window)]
    [InlineData(TextEditingKey.S, true, false, false, TextEditingKeyRoute.Window)]
    [InlineData(TextEditingKey.W, true, false, false, TextEditingKeyRoute.Window)]
    [InlineData(TextEditingKey.F4, false, false, true, TextEditingKeyRoute.Window)]
    [InlineData(TextEditingKey.Tab, false, false, false, TextEditingKeyRoute.Ignore)]
    [InlineData(TextEditingKey.Tab, false, true, false, TextEditingKeyRoute.Ignore)]
    [InlineData(TextEditingKey.Other, false, false, false, TextEditingKeyRoute.Editor)]
    [InlineData(TextEditingKey.Other, true, false, false, TextEditingKeyRoute.Editor)]
    [InlineData(TextEditingKey.Other, true, true, false, TextEditingKeyRoute.Editor)]
    public void EditingRoutes(TextEditingKey key, bool ctrl, bool shift, bool alt, TextEditingKeyRoute expected) => Assert.Equal(expected, TextEditingKeyPolicy.Route(true, key, ctrl, shift, alt));
    [Fact] public void NotEditingAlwaysFallsThrough()
    {
        foreach (var key in Enum.GetValues<TextEditingKey>())
            foreach (var ctrl in new[] { false, true }) foreach (var shift in new[] { false, true }) foreach (var alt in new[] { false, true })
                Assert.Equal(TextEditingKeyRoute.NotEditing, TextEditingKeyPolicy.Route(false, key, ctrl, shift, alt));
    }
}
