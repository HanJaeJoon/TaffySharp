// Tests for style inputs which are valid to construct, but which are degenerate or hostile
// (non-finite numbers, extreme values, or internally inconsistent grid templates).
//
// These tests are primarily about layout *terminating* without panicking.
namespace TaffySharp.Tests.HandWritten;

public class AdversarialStylesTests
{
    private static Size<AvailableSpace> Definite(float size) => new(AvailableSpace.Definite(size), AvailableSpace.Definite(size));

    // A NaN flex factor makes every comparison in the "find the size of an fr" algorithm false,
    // which used to prevent that algorithm from ever finding a valid hypothetical fr size.
    [Fact]
    public void non_finite_flex_factor_terminates()
    {
        foreach (float flexFactor in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            TaffyTree<TestNodeContext> tree = new();
            NodeId child = tree.NewLeaf(new Style
            {
                Size = new Size<Dimension>(Dimension.Length(10f), Dimension.Length(10f)),
            });
            NodeId root = tree.NewWithChildren(
                new Style
                {
                    Display = Display.Grid,
                    GridTemplateColumns = new List<GridTemplateComponent>
                    {
                        GridTemplateComponent.Single(new TrackSizingFunction(MinTrackSizingFunction.Auto, MaxTrackSizingFunction.Fr(flexFactor))),
                    },
                },
                child);

            tree.ComputeLayout(root, Definite(100f));
        }
    }

    // A non-finite size on a grid container makes the space to fill non-finite, which used to
    // prevent the "find the size of an fr" algorithm from terminating.
    [Fact]
    public void non_finite_grid_container_size_terminates()
    {
        foreach (float width in new[] { float.NaN, float.PositiveInfinity })
        {
            TaffyTree<TestNodeContext> tree = new();
            NodeId child = tree.NewLeaf(new Style());
            NodeId root = tree.NewWithChildren(
                new Style
                {
                    Display = Display.Grid,
                    Size = new Size<Dimension>(Dimension.Length(width), Dimension.Length(100f)),
                    GridTemplateColumns = new List<GridTemplateComponent>
                    {
                        GridTemplateComponent.Repeat(RepetitionCount.Count(2), new[] { TrackSizingFunction.Fr(1f) }),
                    },
                },
                child);

            tree.ComputeLayout(root, Definite(100f));
        }
    }

    // repeat() definitions are allowed to contain no line name sets at all (the `repeat` style
    // helper creates such repetitions). Line numbering must still account for the tracks that
    // each repetition generates.
    [Fact]
    public void repetition_without_line_names_resolves_lines()
    {
        TaffyTree<TestNodeContext> tree = new();
        NodeId child = tree.NewLeaf(new Style
        {
            Size = new Size<Dimension>(Dimension.Length(5f), Dimension.Length(5f)),
            GridColumn = new Line<GridPlacement>(GridPlacement.NamedLine("c", 1), GridPlacement.Auto),
        });
        NodeId root = tree.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Size = new Size<Dimension>(Dimension.Length(100f), Dimension.Length(100f)),
                GridTemplateColumns = new List<GridTemplateComponent>
                {
                    GridTemplateComponent.Repeat(RepetitionCount.Count(2), new[] { TrackSizingFunction.Length(10f) }),
                    TrackSizingFunction.Length(10f),
                },
                GridTemplateColumnNames = new List<List<string>> { new(), new(), new() { "c" } },
            },
            child);

        tree.ComputeLayout(root, Definite(100f));

        // "c" is the 4th line of the grid: the line after the three 10px columns
        Assert.Equal(30f, tree.Layout(child).Location.X);
    }

    // A zero span (an invalid value which gets normalized to 1) combined with an unresolvable
    // named span used to size the implicit grid estimate to zero tracks, while auto-placement
    // resolved the same placement to a span of 1. The auto-placement search then looped forever
    // looking for a position that could never fit.
    [Fact]
    public void zero_span_and_named_span_terminates()
    {
        foreach (Direction direction in new[] { Direction.Ltr, Direction.Rtl })
        {
            TaffyTree<TestNodeContext> tree = new();
            NodeId child = tree.NewLeaf(new Style
            {
                GridColumn = new Line<GridPlacement>(GridPlacement.NamedSpan("does-not-exist", 1), GridPlacement.Span(0)),
            });
            NodeId root = tree.NewWithChildren(new Style { Display = Display.Grid, Direction = direction }, child);

            tree.ComputeLayout(root, Definite(100f));
        }
    }

    // A non-empty list of line name sets whose length does not match `track_count + 1` violates
    // the GridTemplateRepetition API contract and must panic rather than silently mis-numbering
    // the lines that follow the repetition.
    [Fact]
    public void repetition_with_wrong_line_name_set_count_panics()
    {
        Exception exception = Assert.ThrowsAny<Exception>(() =>
        {
            TaffyTree<TestNodeContext> tree = new();
            NodeId child = tree.NewLeaf(new Style());
            NodeId root = tree.NewWithChildren(
                new Style
                {
                    Display = Display.Grid,
                    GridTemplateColumns = new List<GridTemplateComponent>
                    {
                        GridTemplateComponent.Repeat(new GridTemplateRepetition(
                            RepetitionCount.Count(2),
                            new List<TrackSizingFunction> { TrackSizingFunction.Length(10f) },
                            new List<List<string>> { new() { "a" } })),
                    },
                    GridTemplateColumnNames = new List<List<string>> { new(), new() },
                },
                child);

            tree.ComputeLayout(root, Definite(100f));
        });
        Assert.Contains("line name sets", exception.Message);
    }

    // When auto-placement collides with an occupied area, the search cursor must jump past the
    // whole occupied interval. Jumping only within the queried area used to make the search
    // advance one track at a time through large occupied areas, so a small item searching a grid
    // occupied by a maximum-size item scanned (10000 tracks)^2 candidate positions.
    [Fact]
    public void small_item_in_max_size_occupied_grid_terminates()
    {
        TaffyTree<TestNodeContext> tree = new();
        NodeId big = tree.NewLeaf(new Style
        {
            GridRow = new Line<GridPlacement>(GridPlacement.Span(10000), GridPlacement.Auto),
            GridColumn = new Line<GridPlacement>(GridPlacement.Span(10000), GridPlacement.Auto),
        });
        NodeId small = tree.NewLeaf(new Style
        {
            GridRow = new Line<GridPlacement>(GridPlacement.Span(10000), GridPlacement.Auto),
            GridColumn = new Line<GridPlacement>(GridPlacement.Span(1), GridPlacement.Auto),
        });
        NodeId root = tree.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Size = new Size<Dimension>(Dimension.Length(100f), Dimension.Length(100f)),
            },
            big,
            small);

        tree.ComputeLayout(root, Definite(100f));
    }

    // Items may span thousands of tracks and be placed thousands of tracks outside of the explicit
    // grid. Auto-placing such items must not take an excessive amount of time.
    [Fact]
    public void extreme_placements_and_spans_terminate()
    {
        TaffyTree<TestNodeContext> tree = new();
        NodeId[] children = new[]
            {
                new Line<GridPlacement>(GridPlacement.Auto, GridPlacement.Span(2000)),
                new Line<GridPlacement>(GridPlacement.Line(-2000), GridPlacement.Line(2000)),
            }
            .Select(placement => tree.NewLeaf(new Style { GridRow = placement, GridColumn = placement }))
            .ToArray();
        NodeId root = tree.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Size = new Size<Dimension>(Dimension.Length(100f), Dimension.Length(100f)),
            },
            children);

        tree.ComputeLayout(root, Definite(100f));
    }
}
