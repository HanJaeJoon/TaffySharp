namespace TaffySharp.Tests.HandWritten;

public class PositionTests
{
    private static (TaffyTree Taffy, NodeId Child) LeafWithInset(Position position, Display display)
    {
        TaffyTree taffy = new();
        NodeId child = taffy.NewLeaf(new Style
        {
            Position = position,
            Size = Size.FromLengths(10f, 10f),
            Inset = new Rect<LengthPercentageAuto>(
                LengthPercentageAuto.Length(5f),
                LengthPercentageAuto.Auto,
                LengthPercentageAuto.Length(7f),
                LengthPercentageAuto.Auto),
        });
        NodeId root = taffy.NewWithChildren(new Style { Display = display, Size = Size.FromLengths(100f, 100f) }, child);
        taffy.ComputeLayout(root, Size.MaxContent);
        return (taffy, child);
    }

    private static void AssertEqual<T>(T expected, T actual, string message) =>
        Assert.True(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, actual {actual}");

    [Fact]
    public void static_and_sticky_ignore_inset()
    {
        foreach (Display display in new[] { Display.Block, Display.Flex, Display.Grid })
        {
            foreach (Position position in new[] { Position.Static, Position.Sticky })
            {
                (TaffyTree taffy, NodeId child) = LeafWithInset(position, display);
                Layout layout = taffy.Layout(child);
                AssertEqual(new Point<float>(0f, 0f), layout.Location, $"{position} in {display}");
            }

            (TaffyTree relativeTaffy, NodeId relativeChild) = LeafWithInset(Position.Relative, display);
            AssertEqual(new Point<float>(5f, 7f), relativeTaffy.Layout(relativeChild).Location, $"Relative in {display}");
        }
    }

    [Fact]
    public void sticky_is_positioned_but_in_flow()
    {
        Assert.True(Position.Sticky.IsPositioned());
        Assert.False(Position.Sticky.IsOutOfFlow());
        Assert.False(Position.Static.IsPositioned());
    }
}
