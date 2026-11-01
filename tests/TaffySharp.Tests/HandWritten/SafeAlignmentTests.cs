// Tests for the `safe` and `unsafe` overflow-position keywords on align-* and justify-*.
//
// Spec: https://www.w3.org/TR/css-align-3/#overflow-values
//
// When the alignment subject overflows the alignment container, a `safe` value behaves
// as logical `start`; an `unsafe` value (the default) keeps its requested position even
// when that causes data loss at the start edge.
namespace TaffySharp.Tests.HandWritten;

public class SafeAlignmentTests
{
    private static List<GridTemplateComponent> Lengths(params float[] values) =>
        values.Select(value => (GridTemplateComponent)TrackSizingFunction.Length(value)).ToList();

    [Fact]
    public void grid_safe_align_self_falls_back_to_start_on_overflow()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeaf(new Style
        {
            Size = Size.FromLengths(50f, 150f),
            AlignSelf = AlignItems.SafeEnd,
        });
        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Size = Size.FromLengths(100f, 100f),
                GridTemplateRows = Lengths(100f),
                GridTemplateColumns = Lengths(100f),
            },
            child);

        taffy.ComputeLayout(root, Size.MaxContent);

        // Child overflows the 100-tall grid area, so safe end falls back to start (y == 0).
        Assert.Equal(0f, taffy.Layout(child).Location.Y);
    }

    [Fact]
    public void grid_safe_align_self_behaves_as_end_when_no_overflow()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeaf(new Style
        {
            Size = Size.FromLengths(50f, 40f),
            AlignSelf = AlignItems.SafeEnd,
        });
        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Size = Size.FromLengths(100f, 100f),
                GridTemplateRows = Lengths(100f),
                GridTemplateColumns = Lengths(100f),
            },
            child);

        taffy.ComputeLayout(root, Size.MaxContent);

        // No overflow, so safe end behaves as end: y == 100 - 40 == 60
        Assert.Equal(60f, taffy.Layout(child).Location.Y);
    }

    [Fact]
    public void grid_unsafe_align_self_keeps_overflowing_position()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeaf(new Style
        {
            Size = Size.FromLengths(50f, 150f),
            AlignSelf = AlignItems.End,
        });
        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Size = Size.FromLengths(100f, 100f),
                GridTemplateRows = Lengths(100f),
                GridTemplateColumns = Lengths(100f),
            },
            child);

        taffy.ComputeLayout(root, Size.MaxContent);

        // Plain End with overflow keeps end alignment: y == 100 - 150 == -50
        Assert.Equal(-50f, taffy.Layout(child).Location.Y);
    }

    [Fact]
    public void grid_safe_justify_self_falls_back_to_start_on_overflow()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeaf(new Style
        {
            Size = Size.FromLengths(150f, 50f),
            JustifySelf = AlignItems.SafeEnd,
        });
        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Size = Size.FromLengths(100f, 100f),
                GridTemplateRows = Lengths(100f),
                GridTemplateColumns = Lengths(100f),
            },
            child);

        taffy.ComputeLayout(root, Size.MaxContent);

        Assert.Equal(0f, taffy.Layout(child).Location.X);
    }

    [Fact]
    public void grid_safe_justify_self_rtl_falls_back_to_rtl_start_edge()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeaf(new Style
        {
            Size = Size.FromLengths(150f, 50f),
            JustifySelf = AlignItems.SafeEnd,
        });
        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Direction = Direction.Rtl,
                Size = Size.FromLengths(100f, 100f),
                GridTemplateRows = Lengths(100f),
                GridTemplateColumns = Lengths(100f),
            },
            child);

        taffy.ComputeLayout(root, Size.MaxContent);

        // In RTL the inline-start edge is the right side. The 150-wide child in the
        // 100-wide container, anchored at the RTL Start edge, sits at x == -50.
        Assert.Equal(-50f, taffy.Layout(child).Location.X);
    }

    [Fact]
    public void grid_safe_align_content_overflow_falls_back_to_start()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId childA = taffy.NewLeaf(new Style { Size = Size.FromLengths(40f, 80f) });
        NodeId childB = taffy.NewLeaf(new Style { Size = Size.FromLengths(40f, 80f) });
        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Size = Size.FromLengths(100f, 100f),
                GridTemplateRows = Lengths(80f, 80f),
                GridTemplateColumns = Lengths(40f),
                AlignContent = AlignContent.SafeEnd,
            },
            childA,
            childB);

        taffy.ComputeLayout(root, Size.MaxContent);

        // Tracks total 160 in a 100-tall container - overflow. Safe end falls back to
        // start: first track at y == 0.
        Assert.Equal(0f, taffy.Layout(childA).Location.Y);
    }

    [Fact]
    public void flex_safe_align_self_falls_back_to_start_on_overflow()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeaf(new Style
        {
            Size = Size.FromLengths(50f, 150f),
            AlignSelf = AlignItems.SafeEnd,
        });
        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Flex,
                Size = Size.FromLengths(100f, 100f),
            },
            child);

        taffy.ComputeLayout(root, Size.MaxContent);

        // Cross-axis overflow: safe end falls back to start (y == 0).
        Assert.Equal(0f, taffy.Layout(child).Location.Y);
    }

    [Fact]
    public void flex_unsafe_align_self_keeps_overflowing_position()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeaf(new Style
        {
            Size = Size.FromLengths(50f, 150f),
            AlignSelf = AlignItems.End,
        });
        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Flex,
                Size = Size.FromLengths(100f, 100f),
            },
            child);

        taffy.ComputeLayout(root, Size.MaxContent);

        // Plain End with cross-axis overflow keeps end alignment: y == 100 - 150 == -50
        Assert.Equal(-50f, taffy.Layout(child).Location.Y);
    }

    [Fact]
    public void flex_safe_justify_content_falls_back_to_start_on_overflow()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId childA = taffy.NewLeaf(new Style { Size = Size.FromLengths(80f, 50f) });
        NodeId childB = taffy.NewLeaf(new Style { Size = Size.FromLengths(80f, 50f) });
        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Flex,
                Size = Size.FromLengths(100f, 100f),
                JustifyContent = AlignContent.SafeEnd,
            },
            childA,
            childB);

        taffy.ComputeLayout(root, Size.MaxContent);

        // Items total 160 in a 100-wide container - overflow. Safe end falls back to
        // start: first item anchored at x == 0.
        Assert.Equal(0f, taffy.Layout(childA).Location.X);
    }

    [Fact]
    public void flex_safe_align_content_falls_back_to_start_on_multiline_overflow()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId childA = taffy.NewLeaf(new Style { Size = Size.FromLengths(60f, 80f) });
        NodeId childB = taffy.NewLeaf(new Style { Size = Size.FromLengths(60f, 80f) });
        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Flex,
                FlexWrap = FlexWrap.Wrap,
                Size = Size.FromLengths(100f, 100f),
                AlignContent = AlignContent.SafeEnd,
            },
            childA,
            childB);

        taffy.ComputeLayout(root, Size.MaxContent);

        // Two 80-tall lines wrap and overflow the 100-tall container. Safe end falls back
        // to start: first line at y == 0.
        Assert.Equal(0f, taffy.Layout(childA).Location.Y);
    }
}
