namespace TaffySharp.Tests.HandWritten;

public class CachingTests
{
    private static TestNodeContext NodeContext() => TestNodeContext.Fixed(50f, 50f);

    private static Line<GridPlacement> GridLine(short index) => new(GridPlacement.Line(index), GridPlacement.Auto);

    [Fact]
    public void measure_count_flexbox()
    {
        TaffyTree<TestNodeContext> taffy = new();

        NodeId leaf = taffy.NewLeafWithContext(new Style(), NodeContext());

        NodeId node = taffy.NewWithChildren(Style.Default, leaf);
        for (int i = 0; i < 100; i++)
        {
            node = taffy.NewWithChildren(Style.Default, node);
        }

        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);

        Assert.Equal(6, taffy.GetNodeContext(leaf)!.Count);
    }

    [Fact]
    public void measure_count_grid()
    {
        TaffyTree<TestNodeContext> taffy = new();

        Func<Style> style = () => new Style { Display = Display.Grid };
        NodeId leaf = taffy.NewLeafWithContext(style(), NodeContext());

        NodeId node = taffy.NewWithChildren(Style.Default, leaf);
        for (int i = 0; i < 100; i++)
        {
            node = taffy.NewWithChildren(Style.Default, node);
        }

        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);
        Assert.Equal(6, taffy.GetNodeContext(leaf)!.Count);
    }

    // A grid container that is only asked for its width (e.g. because it is an item of another
    // grid whose columns are being sized) must not size its rows, as that measures the height of
    // all of its items and, recursively, of everything below them.
    [Fact]
    public void measure_count_nested_grid()
    {
        TaffyTree<TestNodeContext> taffy = new();

        Func<Style> style = () => new Style { Display = Display.Grid };
        // The leaf's min-content width (20), max-content width (80) and final width (50) all differ, so
        // measuring its height at each of the first two widths can never be served from the cache.
        TestNodeContext text = TestNodeContext.AhemText("HH\u200BHH\u200BHH\u200BHH", WritingMode.Horizontal);
        NodeId leaf = taffy.NewLeafWithContext(new Style(), text);
        NodeId inner = taffy.NewWithChildren(style(), leaf);
        NodeId middle = taffy.NewWithChildren(style(), inner);
        Style outerStyle = style();
        outerStyle.Size = new Size<Dimension>(Dimension.Length(50f), Dimension.Auto);
        NodeId outer = taffy.NewWithChildren(outerStyle, middle);

        taffy.ComputeLayoutWithMeasure(outer, Size.MaxContent, TestHelpers.MeasureFunction);
        Assert.Equal(new Size<float>(50f, 20f), taffy.Layout(leaf).Size);
        Assert.Equal(6, taffy.GetNodeContext(leaf)!.Count);
    }

    // A grid item whose size in the axis being measured is already known (here: a definite width) does not
    // need to be measured to determine its contribution to the size of the tracks in that axis.
    // (Without that short-circuit the leaf is measured 5 times)
    [Fact]
    public void measure_count_grid_item_with_known_width()
    {
        TaffyTree<TestNodeContext> taffy = new();

        TestNodeContext text = TestNodeContext.AhemText("HH\u200BHH\u200BHH\u200BHH", WritingMode.Horizontal);
        NodeId leaf = taffy.NewLeafWithContext(
            new Style { Size = new Size<Dimension>(Dimension.Length(40f), Dimension.Auto) },
            text);
        NodeId grid = taffy.NewWithChildren(new Style { Display = Display.Grid }, leaf);

        taffy.ComputeLayoutWithMeasure(grid, Size.MaxContent, TestHelpers.MeasureFunction);
        Assert.Equal(new Size<float>(40f, 20f), taffy.Layout(grid).Size);
        Assert.Equal(new Size<float>(40f, 20f), taffy.Layout(leaf).Size);
        Assert.Equal(3, taffy.GetNodeContext(leaf)!.Count);
    }

    // A grid item in a track that is not intrinsically sized does not contribute to the size of that track, so
    // it does not need to be measured when sizing the tracks (even if another track in the axis is intrinsic).
    // (Without that short-circuit the leaf is measured 5 times)
    [Fact]
    public void measure_count_grid_item_in_fixed_track()
    {
        TaffyTree<TestNodeContext> taffy = new();

        TestNodeContext text = TestNodeContext.AhemText("HH\u200BHH\u200BHH\u200BHH", WritingMode.Horizontal);
        NodeId leaf = taffy.NewLeafWithContext(
            new Style { JustifySelf = AlignItems.Start, GridColumn = GridLine(1) },
            text);
        NodeId sibling = taffy.NewLeaf(new Style
        {
            Size = Size.FromLengths(50f, 50f),
            GridColumn = GridLine(2),
        });
        NodeId grid = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                GridTemplateColumns = new List<GridTemplateComponent> { TrackSizingFunction.Length(100f), TrackSizingFunction.Auto },
            },
            leaf,
            sibling);

        taffy.ComputeLayoutWithMeasure(grid, Size.MaxContent, TestHelpers.MeasureFunction);
        Assert.Equal(new Size<float>(150f, 50f), taffy.Layout(grid).Size);
        Assert.Equal(new Size<float>(80f, 50f), taffy.Layout(leaf).Size);
        Assert.Equal(3, taffy.GetNodeContext(leaf)!.Count);
    }

    // A flex container that is only asked for its main size (e.g. because it is an item of a grid
    // whose tracks are being sized) must not measure the cross size of its items.
    // (Without that short-circuit the leaf is measured 13 times in the row case and 12 times in the column case)
    [Fact]
    public void measure_count_flex_in_grid()
    {
        foreach ((FlexDirection flexDirection, int expectedCount) in new[] { (FlexDirection.Row, 9), (FlexDirection.Column, 10) })
        {
            TaffyTree<TestNodeContext> taffy = new();

            TestNodeContext text = TestNodeContext.AhemText("HH\u200BHH\u200BHH\u200BHH", WritingMode.Horizontal);
            NodeId leaf = taffy.NewLeafWithContext(new Style(), text);
            NodeId flex = taffy.NewWithChildren(new Style { Display = Display.Flex, FlexDirection = flexDirection }, leaf);
            NodeId grid = taffy.NewWithChildren(
                new Style
                {
                    Display = Display.Grid,
                    Size = new Size<Dimension>(Dimension.Length(50f), Dimension.Auto),
                },
                flex);

            taffy.ComputeLayoutWithMeasure(grid, Size.MaxContent, TestHelpers.MeasureFunction);
            int count = taffy.GetNodeContext(leaf)!.Count;
            Assert.Equal(new Size<float>(50f, 20f), taffy.Layout(leaf).Size);
            Assert.Equal(expectedCount, count);
        }
    }

    // A node's size measured for one axis must not be returned from the cache when the other
    // axis is queried, as the size in the non-requested axis is not guaranteed to be valid.
    [Fact]
    public void grid_block_item_height_not_polluted_by_width_measure()
    {
        TaffyTree<TestNodeContext> taffy = new();

        Func<short, Style> itemStyle = row => new Style
        {
            Display = Display.Block,
            GridRow = new Line<GridPlacement>(GridPlacement.Line(row), GridPlacement.Line((short)(row + 1))),
            GridColumn = new Line<GridPlacement>(GridPlacement.Line(1), GridPlacement.Line(2)),
        };

        // Each grid item is a block container wrapping a measured leaf. The block layout
        // algorithm short-circuits width-only measures without computing a height, so a
        // subsequent height query must not reuse that cached result.
        Func<short, NodeId> makeItem = row =>
        {
            NodeId leaf = taffy.NewLeafWithContext(new Style(), TestNodeContext.Fixed(100f, 19f));
            return taffy.NewWithChildren(itemStyle(row), leaf);
        };
        NodeId a = makeItem(1);
        NodeId b = makeItem(2);
        NodeId c = makeItem(3);

        NodeId container = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                // The `auto` column is required to trigger intrinsic (width) sizing of the
                // items before the row-sizing (height) pass.
                GridTemplateColumns = new List<GridTemplateComponent> { TrackSizingFunction.Percent(0.6f), TrackSizingFunction.Auto },
                Size = new Size<Dimension>(Dimension.Length(1000f), Dimension.Auto),
            },
            a,
            b,
            c);

        taffy.ComputeLayoutWithMeasure(
            container,
            new Size<AvailableSpace>(AvailableSpace.Definite(1000f), AvailableSpace.Definite(1000f)),
            TestHelpers.MeasureFunction);

        NodeId[] nodes = { a, b, c };
        for (int index = 0; index < nodes.Length; index++)
        {
            Layout layout = taffy.Layout(nodes[index]);
            Assert.Equal(19f, layout.Size.Height);
            Assert.Equal(19f * index, layout.Location.Y);
        }
    }

    // An item whose spanned tracks cannot receive its min-/max-content contribution in any
    // intrinsic track sizing step (definite min, flexible max) must not be measured for it.
    [Fact]
    public void grid_item_spanning_only_unaffected_tracks_is_not_measured()
    {
        TaffyTree<TestNodeContext> taffy = new();

        TestNodeContext text = TestNodeContext.AhemText("HH HH HH HH", WritingMode.Horizontal);
        Style leafStyle = new() { GridColumn = new Line<GridPlacement>(GridPlacement.Line(2), GridPlacement.Line(3)) };
        NodeId leaf = taffy.NewLeafWithContext(leafStyle, text);
        NodeId grid = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Size = Size.FromLengths(400f, 50f),
                GridTemplateColumns = new List<GridTemplateComponent>
                {
                    TrackSizingFunction.Length(100f),
                    TrackSizingFunction.MinMax(MinTrackSizingFunction.Length(0f), MaxTrackSizingFunction.Fr(1f)),
                },
                GridTemplateRows = new List<GridTemplateComponent> { TrackSizingFunction.Length(50f) },
            },
            leaf);

        taffy.ComputeLayoutWithMeasure(grid, Size.MaxContent, TestHelpers.MeasureFunction);

        // Only the final layout pass measures the leaf; track sizing does not.
        Assert.Equal(300f, taffy.Layout(leaf).Size.Width);
        Assert.Equal(1, taffy.GetNodeContext(leaf)!.Count);
    }
}
