using Snapper.Core.Annotate.Model;
namespace Snapper.Core.Tests;
public sealed class CaretPlacementTests
{
    [Theory][InlineData(0, 4, 0)][InlineData(0, 5, 1)][InlineData(0, 7, 1)][InlineData(1, 7, 2)]
    [InlineData(2, 7, 2)][InlineData(3, 7, 2)][InlineData(5, 7, 6)][InlineData(-1, 7, 6)][InlineData(90, 7, 6)]
    public void MidpointAndCrLfSafety(int hit, double x, int expected) => Assert.Equal(expected, CaretPlacement.InsertionIndex("Hi\r\nYo", hit, x, 0, 10));
    [Fact] public void EmptyIsZero() => Assert.Equal(0, CaretPlacement.InsertionIndex("", 90, 9, 0, 10));
    [Fact] public void LandingOnLineBreakNeverSplitsPair() => Assert.Equal(2, CaretPlacement.InsertionIndex("Hi\r\nYo", 1, 10, 0, 10));
}
