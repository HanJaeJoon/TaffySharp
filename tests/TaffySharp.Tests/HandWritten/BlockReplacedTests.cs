// Sizing of replaced (ItemIsReplaced = true) children of block containers.
//
// Block layout stretch-sizes in-flow children to the container width, but block-level
// replaced elements are exempt: with `width: auto` they use their intrinsic size
// (https://www.w3.org/TR/CSS22/visudet.html#block-replaced-width).
namespace TaffySharp.Tests.HandWritten;

public class BlockReplacedTests
{
    // A 142x20 badge-like image (height = width * 20/142)
    private static TestNodeContext Badge() => TestNodeContext.AspectRatio(142f, 20f / 142f);

    private static Style BlockContainer(float width) => new()
    {
        Display = Display.Block,
        Size = new Size<Dimension>(Dimension.Length(width), Dimension.Auto),
    };

    private static void ComputeLayout(TaffyTree<TestNodeContext> taffy, NodeId root) =>
        taffy.ComputeLayoutWithMeasure(root, Size.MaxContent, TestHelpers.MeasureFunction);

    [Fact]
    public void auto_width_uses_intrinsic_size()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeafWithContext(new Style { Display = Display.Block, ItemIsReplaced = true }, Badge());
        NodeId root = taffy.NewWithChildren(BlockContainer(600f), child);
        ComputeLayout(taffy, root);

        Layout childLayout = taffy.Layout(child);
        Assert.Equal(142f, childLayout.Size.Width);
        Assert.Equal(20f, childLayout.Size.Height);
    }

    [Fact]
    public void non_replaced_child_is_stretch_sized()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeafWithContext(new Style { Display = Display.Block }, Badge());
        NodeId root = taffy.NewWithChildren(BlockContainer(600f), child);
        ComputeLayout(taffy, root);

        Assert.Equal(600f, taffy.Layout(child).Size.Width);
    }

    [Fact]
    public void explicit_width_is_used()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeafWithContext(
            new Style
            {
                Display = Display.Block,
                ItemIsReplaced = true,
                Size = Size.FromLengths(300f, 50f),
            },
            Badge());
        NodeId root = taffy.NewWithChildren(BlockContainer(600f), child);
        ComputeLayout(taffy, root);

        Layout childLayout = taffy.Layout(child);
        Assert.Equal(300f, childLayout.Size.Width);
        Assert.Equal(50f, childLayout.Size.Height);
    }

    [Fact]
    public void max_width_clamps_intrinsic_size()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeafWithContext(
            new Style
            {
                Display = Display.Block,
                ItemIsReplaced = true,
                MaxSize = new Size<LengthPercentageAuto>(LengthPercentageAuto.Percent(1f), LengthPercentageAuto.Auto),
            },
            Badge());
        NodeId root = taffy.NewWithChildren(BlockContainer(100f), child);
        ComputeLayout(taffy, root);

        Layout childLayout = taffy.Layout(child);
        Assert.Equal(100f, childLayout.Size.Width);
    }

    [Fact]
    public void auto_margins_center_replaced_child()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeafWithContext(
            new Style
            {
                Display = Display.Block,
                ItemIsReplaced = true,
                Margin = new Rect<LengthPercentageAuto>(
                    LengthPercentageAuto.Auto,
                    LengthPercentageAuto.Auto,
                    LengthPercentageAuto.Zero,
                    LengthPercentageAuto.Zero),
            },
            Badge());
        NodeId root = taffy.NewWithChildren(BlockContainer(600f), child);
        ComputeLayout(taffy, root);

        Layout childLayout = taffy.Layout(child);
        Assert.Equal(142f, childLayout.Size.Width);
        Assert.Equal((600f - 142f) / 2f, childLayout.Location.X);
    }
}
