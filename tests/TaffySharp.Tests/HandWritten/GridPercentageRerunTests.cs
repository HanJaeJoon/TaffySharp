// Percentage track sizing functions resolve against the grid container's size. When that size is
// indefinite they are treated as auto while computing the container's size and then resolved against
// that size. This must happen regardless of whether the available space is definite (e.g. a
// shrink-to-fit grid container).
namespace TaffySharp.Tests.HandWritten;

public class GridPercentageRerunTests
{
    // Returns (container width, item width)
    private static (float, float) LayoutFitContentPercentColumn(AvailableSpace availableWidth)
    {
        TaffyTree<TestNodeContext> tree = new();
        NodeId item = tree.NewLeafWithContext(
            new Style(),
            TestNodeContext.AhemText("HH\u200BHH\u200BHH", WritingMode.Horizontal));
        NodeId root = tree.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                GridTemplateColumns = new List<GridTemplateComponent> { TrackSizingFunction.FitContent(LengthPercentage.Percent(0.5f)) },
                GridTemplateRows = new List<GridTemplateComponent> { TrackSizingFunction.Length(40f) },
            },
            item);
        tree.ComputeLayoutWithMeasure(
            root,
            new Size<AvailableSpace>(availableWidth, AvailableSpace.MaxContent),
            TestHelpers.MeasureFunction);
        return (tree.Layout(root).Size.Width, tree.Layout(item).Size.Width);
    }

    [Fact]
    public void fit_content_percentage_indefinite_container_max_content_available_space()
    {
        Assert.Equal((60f, 30f), LayoutFitContentPercentColumn(AvailableSpace.MaxContent));
    }

    [Fact]
    public void fit_content_percentage_indefinite_container_definite_available_space()
    {
        Assert.Equal((60f, 30f), LayoutFitContentPercentColumn(AvailableSpace.Definite(200f)));
    }

    // Returns (container height, row 1 height, row 2 height, row 3 height) for a grid with
    // `grid-template-rows: auto 20% auto` whose items are 20px, 60px and 20px tall respectively.
    // Mirrors WPT css/css-grid/layout-algorithm/grid-percent-rows-filled-shrinkwrap-001.html
    private static (float, float, float, float) LayoutPercentRowShrinkwrap(AvailableSpace availableHeight)
    {
        TaffyTree tree = new();
        Func<TaffyTree, float, NodeId> item = (t, height) => t.NewLeaf(new Style { Size = Size.FromLengths(20f, height) });
        NodeId a = item(tree, 20f);
        NodeId b = item(tree, 60f);
        NodeId c = item(tree, 20f);
        NodeId root = tree.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                GridTemplateColumns = new List<GridTemplateComponent> { TrackSizingFunction.Auto },
                GridTemplateRows = new List<GridTemplateComponent>
                {
                    TrackSizingFunction.Auto,
                    TrackSizingFunction.Percent(0.2f),
                    TrackSizingFunction.Auto,
                },
            },
            a,
            b,
            c);
        tree.ComputeLayout(root, new Size<AvailableSpace>(AvailableSpace.Definite(200f), availableHeight));
        float total = tree.Layout(root).Size.Height;
        Func<NodeId, float> y = node => tree.Layout(node).Location.Y;
        return (total, y(b), y(c) - y(b), total - y(c));
    }

    // Percentage rows must be re-resolved against the container's (first pass) height even when column sizing
    // is not re-run. The auto rows then stretch to fill the remaining space.
    [Fact]
    public void percentage_rows_indefinite_container_max_content_available_space()
    {
        Assert.Equal((100f, 40f, 20f, 40f), LayoutPercentRowShrinkwrap(AvailableSpace.MaxContent));
    }

    [Fact]
    public void percentage_rows_indefinite_container_definite_available_space()
    {
        Assert.Equal((100f, 40f, 20f, 40f), LayoutPercentRowShrinkwrap(AvailableSpace.Definite(500f)));
    }
}
