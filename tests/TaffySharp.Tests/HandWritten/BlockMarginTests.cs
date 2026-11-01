namespace TaffySharp.Tests.HandWritten;

public class BlockMarginTests
{
    private static Style Block(float marginTop, float marginBottom, Dimension height) => new()
    {
        Display = Display.Block,
        Size = new Size<Dimension>(Dimension.Auto, height),
        Margin = new Rect<LengthPercentageAuto>(
            LengthPercentageAuto.Zero,
            LengthPercentageAuto.Zero,
            LengthPercentageAuto.Length(marginTop),
            LengthPercentageAuto.Length(marginBottom)),
    };

    // Margin collapsing changes where boxes are placed, but Layout.Margin is each box's own margin
    [Fact]
    public void layout_margin_excludes_collapsed_margins()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId inner = taffy.NewLeaf(Block(13f, 20f, Dimension.Length(10f)));
        NodeId outer = taffy.NewWithChildren(Block(8f, 5f, Dimension.Auto), inner);
        NodeId sibling = taffy.NewLeaf(Block(30f, 0f, Dimension.Length(10f)));
        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.FlowRoot,
                Size = new Size<Dimension>(Dimension.Length(100f), Dimension.Auto),
            },
            outer,
            sibling);

        taffy.ComputeLayout(root, Size.MaxContent);

        // The inner box's margins collapse through the outer box's edges
        Layout outerLayout = taffy.Layout(outer);
        Assert.Equal(13f, outerLayout.Location.Y);
        Assert.Equal(10f, outerLayout.Size.Height);
        Assert.Equal(8f, outerLayout.Margin.Top);
        Assert.Equal(5f, outerLayout.Margin.Bottom);

        Layout innerLayout = taffy.Layout(inner);
        Assert.Equal(0f, innerLayout.Location.Y);
        Assert.Equal(13f, innerLayout.Margin.Top);
        Assert.Equal(20f, innerLayout.Margin.Bottom);

        // The sibling is placed after the largest of the adjoining margins (5, 20, 30)
        Layout siblingLayout = taffy.Layout(sibling);
        Assert.Equal(53f, siblingLayout.Location.Y);
        Assert.Equal(30f, siblingLayout.Margin.Top);
    }
}
