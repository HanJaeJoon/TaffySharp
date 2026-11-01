namespace TaffySharp.Tests.HandWritten;

public class BaselineTests
{
    // A node context that pairs an intrinsic size with a first-baseline offset
    // (measured from the node's top edge in the block axis).
    private sealed class BaselineContext
    {
        public BaselineContext(Size<float> size, float? baselineY)
        {
            Size = size;
            BaselineY = baselineY;
        }

        public Size<float> Size { get; }

        public float? BaselineY { get; }
    }

    private static LayoutOutput BaselineMeasureFunction(LayoutInput inputs, NodeId nodeId, BaselineContext? context, Style style)
    {
        if (context is null)
        {
            return LayoutOutput.Default;
        }

        return LayoutOutput.FromSizesAndBaselines(context.Size, Rect.Zero, Baselines.FromFirst(context.BaselineY));
    }

    private static Rect<LengthPercentage> PaddingBottom(float bottom) =>
        new(LengthPercentage.Zero, LengthPercentage.Zero, LengthPercentage.Zero, LengthPercentage.Length(bottom));

    // Two flex items with different intrinsic baselines are aligned along their baselines
    // when the container uses `align-items: baseline`. The item with the smaller distance
    // from its top to its baseline should be shifted down so that both baselines coincide.
    [Fact]
    public void flex_baseline_alignment_uses_measure_function_baseline()
    {
        TaffyTree<BaselineContext> taffy = new();

        // Child A: 50x50 box with baseline 40px from the top
        NodeId childA = taffy.NewLeafWithContext(new Style(), new BaselineContext(new Size<float>(50f, 50f), 40f));

        // Child B: 30x30 box with baseline 20px from the top
        NodeId childB = taffy.NewLeafWithContext(new Style(), new BaselineContext(new Size<float>(30f, 30f), 20f));

        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Flex,
                FlexDirection = FlexDirection.Row,
                AlignItems = AlignItems.Baseline,
                Size = Size.FromLengths(200f, 100f),
            },
            childA,
            childB);

        taffy.ComputeLayoutWithMeasure(root, Size.MaxContent, BaselineMeasureFunction);

        Layout layoutA = taffy.Layout(childA);
        Layout layoutB = taffy.Layout(childB);

        // Child A sets the max baseline (40 px from its top), so it sits at the top of the line.
        Assert.Equal(0f, layoutA.Location.Y);
        // Child B is shifted down by (40 - 20) = 20 px so its baseline aligns with child A's.
        Assert.Equal(20f, layoutB.Location.Y);

        // Sanity-check: both children's baselines now sit at y = 40 in the container.
        Assert.Equal(layoutB.Location.Y + 20f, layoutA.Location.Y + 40f);
    }

    // A block container child that is a scroll container and has no baseline of its own
    // synthesizes a baseline from its border-box bottom edge, which propagates to the
    // block container's first baseline.
    // See https://github.com/w3c/csswg-drafts/issues/7660
    [Fact]
    public void block_scroll_container_child_without_baseline_synthesizes_from_border_box()
    {
        TaffyTree<BaselineContext> taffy = new();

        // An empty `overflow: hidden` block, 30x30
        NodeId scrollChild = taffy.NewLeafWithContext(
            new Style
            {
                Display = Display.Block,
                Overflow = new Point<Overflow>(Overflow.Hidden, Overflow.Hidden),
                Size = Size.FromLengths(30f, 30f),
            },
            new BaselineContext(new Size<float>(30f, 30f), null));

        // A block container wrapping the scroll container, with bottom padding so that the
        // synthesized baseline (30) differs from the wrapper's border-box bottom edge (50).
        NodeId itemA = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Block,
                Size = new Size<Dimension>(Dimension.Length(40f), Dimension.Auto),
                Padding = PaddingBottom(20f),
            },
            scrollChild);

        // Reference item: 40x80 box with baseline 10px from the top
        NodeId itemB = taffy.NewLeafWithContext(new Style(), new BaselineContext(new Size<float>(40f, 80f), 10f));

        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Flex,
                FlexDirection = FlexDirection.Row,
                AlignItems = AlignItems.Baseline,
                Size = Size.FromLengths(200f, 120f),
            },
            itemA,
            itemB);

        taffy.ComputeLayoutWithMeasure(root, Size.MaxContent, BaselineMeasureFunction);

        // Item A's baseline is synthesized at the scroll container's bottom border edge (30),
        // not at item A's own bottom edge (50). Item A sets the max baseline, so it sits at
        // the top and item B is shifted down by (30 - 10) = 20px.
        Assert.Equal(0f, taffy.Layout(itemA).Location.Y);
        Assert.Equal(20f, taffy.Layout(itemB).Location.Y);
    }

    // A block container child that is NOT a scroll container and has no baseline
    // contributes no baseline to its block container parent.
    [Fact]
    public void block_non_scroll_container_child_without_baseline_contributes_nothing()
    {
        TaffyTree<BaselineContext> taffy = new();

        // An empty `overflow: visible` block, 30x30
        NodeId plainChild = taffy.NewLeafWithContext(
            new Style
            {
                Display = Display.Block,
                Size = Size.FromLengths(30f, 30f),
            },
            new BaselineContext(new Size<float>(30f, 30f), null));

        NodeId itemA = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Block,
                Size = new Size<Dimension>(Dimension.Length(40f), Dimension.Auto),
                Padding = PaddingBottom(20f),
            },
            plainChild);

        // Reference item: 40x80 box with baseline 10px from the top
        NodeId itemB = taffy.NewLeafWithContext(new Style(), new BaselineContext(new Size<float>(40f, 80f), 10f));

        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Flex,
                FlexDirection = FlexDirection.Row,
                AlignItems = AlignItems.Baseline,
                Size = Size.FromLengths(200f, 120f),
            },
            itemA,
            itemB);

        taffy.ComputeLayoutWithMeasure(root, Size.MaxContent, BaselineMeasureFunction);

        // Item A has no baseline, so the flexbox algorithm synthesizes one at its border-box
        // bottom edge (50). Item B is shifted down by (50 - 10) = 40px.
        Assert.Equal(0f, taffy.Layout(itemA).Location.Y);
        Assert.Equal(40f, taffy.Layout(itemB).Location.Y);
    }

    // A block container child that is a scroll container contributes its content baseline
    // clamped to its border box, so baselines of clipped content cannot leak below the
    // child's border-box bottom edge.
    // See https://github.com/w3c/csswg-drafts/issues/7660
    [Fact]
    public void block_scroll_container_child_baseline_is_clamped_to_border_box()
    {
        TaffyTree<BaselineContext> taffy = new();

        // Content with a baseline 48px from the top, inside a 30px tall scroll container
        NodeId content = taffy.NewLeafWithContext(
            new Style { Display = Display.Block },
            new BaselineContext(new Size<float>(30f, 60f), 48f));

        NodeId scrollChild = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Block,
                Overflow = new Point<Overflow>(Overflow.Hidden, Overflow.Hidden),
                Size = Size.FromLengths(30f, 30f),
            },
            content);

        NodeId itemA = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Block,
                Size = new Size<Dimension>(Dimension.Length(40f), Dimension.Auto),
                Padding = PaddingBottom(20f),
            },
            scrollChild);

        // Reference item: 40x80 box with baseline 10px from the top
        NodeId itemB = taffy.NewLeafWithContext(new Style(), new BaselineContext(new Size<float>(40f, 80f), 10f));

        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Flex,
                FlexDirection = FlexDirection.Row,
                AlignItems = AlignItems.Baseline,
                Size = Size.FromLengths(200f, 120f),
            },
            itemA,
            itemB);

        taffy.ComputeLayoutWithMeasure(root, Size.MaxContent, BaselineMeasureFunction);

        // The content baseline (48) is clamped to the scroll container's border-box height (30).
        // Item A's baseline is 30, so item B is shifted down by (30 - 10) = 20px.
        Assert.Equal(0f, taffy.Layout(itemA).Location.Y);
        Assert.Equal(20f, taffy.Layout(itemB).Location.Y);
    }

    // Sanity-check: without `align-items: baseline`, items align at the cross-start edge
    // regardless of the baseline reported by the measure function.
    [Fact]
    public void flex_baseline_is_ignored_when_alignment_is_not_baseline()
    {
        TaffyTree<BaselineContext> taffy = new();

        NodeId childA = taffy.NewLeafWithContext(new Style(), new BaselineContext(new Size<float>(50f, 50f), 40f));
        NodeId childB = taffy.NewLeafWithContext(new Style(), new BaselineContext(new Size<float>(30f, 30f), 20f));

        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Flex,
                FlexDirection = FlexDirection.Row,
                AlignItems = AlignItems.FlexStart,
                Size = Size.FromLengths(200f, 100f),
            },
            childA,
            childB);

        taffy.ComputeLayoutWithMeasure(root, Size.MaxContent, BaselineMeasureFunction);

        Assert.Equal(0f, taffy.Layout(childA).Location.Y);
        Assert.Equal(0f, taffy.Layout(childB).Location.Y);
    }
}
