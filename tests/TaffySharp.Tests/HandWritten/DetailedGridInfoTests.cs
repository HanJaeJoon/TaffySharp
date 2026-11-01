// Tests for the detailed grid info: per-track positions, per-line names, and resolved track
// list serialization.
using System.Globalization;

namespace TaffySharp.Tests.HandWritten;

public class DetailedGridInfoTests
{
    private static Size<AvailableSpace> Definite(float width, float height) => new(AvailableSpace.Definite(width), AvailableSpace.Definite(height));

    private static DetailedGridInfo GetDetailedGridInfo(TaffyTree<TestNodeContext> tree, NodeId node) =>
        tree.DetailedLayoutInfo(node).Grid ?? throw new InvalidOperationException("expected detailed grid info");

    private static List<GridTemplateComponent> Lengths(params float[] values) =>
        values.Select(value => (GridTemplateComponent)TrackSizingFunction.Length(value)).ToList();

    private static Line<GridPlacement> AutoLine() => new(GridPlacement.Auto, GridPlacement.Auto);

    private static Line<GridPlacement> Lines(short start, short end) => new(GridPlacement.Line(start), GridPlacement.Line(end));

    private static (Point<float> Location, Size<float> Size)? Area(float x, float y, float width, float height) =>
        (new Point<float>(x, y), new Size<float>(width, height));

    [Fact]
    public void no_line_names()
    {
        TaffyTree<TestNodeContext> tree = new();
        NodeId child = tree.NewLeaf(new Style());
        NodeId root = tree.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Size = Size.FromLengths(100f, 50f),
                GridTemplateColumns = Lengths(40f, 60f),
                GridTemplateRows = Lengths(50f),
            },
            child);
        tree.ComputeLayout(root, Definite(100f, 50f));

        DetailedGridInfo info = GetDetailedGridInfo(tree, root);
        Assert.True(info.Columns.LineNames.IsEmpty());
        Assert.True(info.Rows.LineNames.IsEmpty());
        Assert.Empty(info.Columns.IterLineNames());
        Assert.Equal("40px 60px", info.GridTemplateColumns(1f));
        Assert.Equal("50px", info.GridTemplateRows(1f));
    }

    [Fact]
    public void zoomed_track_list()
    {
        TaffyTree<TestNodeContext> tree = new();
        NodeId child = tree.NewLeaf(new Style());
        NodeId root = tree.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                GridTemplateColumns = Lengths(80f, 120f),
                GridTemplateRows = Lengths(100f),
            },
            child);
        tree.ComputeLayout(root, Definite(200f, 100f));

        DetailedGridInfo info = GetDetailedGridInfo(tree, root);
        Assert.Equal("40px 60px", info.GridTemplateColumns(2f));
        Assert.Equal("50px", info.GridTemplateRows(2f));
    }

    [Fact]
    public void template_and_area_line_names()
    {
        TaffyTree<TestNodeContext> tree = new();
        NodeId child = tree.NewLeaf(new Style());
        NodeId root = tree.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Size = Size.FromLengths(100f, 50f),
                GridTemplateColumns = Lengths(40f, 60f),
                GridTemplateColumnNames = new List<List<string>>
                {
                    new() { "full-start" },
                    new() { "main-start" },
                    new() { "main-end", "full-end" },
                },
                GridTemplateRows = Lengths(50f),
                GridTemplateAreas = new GridTemplateAreas
                {
                    Areas = new List<GridTemplateArea> { new("hero", 1, 2, 1, 3) },
                    RowCount = 1,
                    ColumnCount = 2,
                },
            },
            child);
        tree.ComputeLayout(root, Definite(100f, 50f));

        DetailedGridInfo info = GetDetailedGridInfo(tree, root);
        List<IReadOnlyList<string>> columnLines = info.Columns.IterLineNames().ToList();
        Assert.Equal(3, columnLines.Count);
        Assert.Equal(new[] { "full-start" }, columnLines[0]);
        Assert.Equal(new[] { "main-start" }, columnLines[1]);
        Assert.Equal(new[] { "main-end", "full-end" }, columnLines[2]);
        Assert.Equal(new[] { "main-start" }, info.Columns.NamesForLine(1));

        // Implicit `<area>-start`/`<area>-end` names are not part of the resolved track list
        Assert.Empty(info.Rows.IterLineNames());

        Assert.Equal("[full-start] 40px [main-start] 60px [main-end full-end]", info.GridTemplateColumns(1f));
        Assert.Equal("50px", info.GridTemplateRows(1f));

        // ...but they still resolve for placement
        Assert.Equal(
            new Rect<float>(0f, 100f, 0f, 50f),
            info.ResolveAbsoluteGridArea(
                Lines(1, 2),
                new Line<GridPlacement>(GridPlacement.NamedLine("hero-start", 1), GridPlacement.NamedLine("hero-end", 1)),
                Direction.Ltr,
                new Rect<float>(0f, 100f, 0f, 50f)));
    }

    [Fact]
    public void repeat_expansion_line_names()
    {
        TaffyTree<TestNodeContext> tree = new();
        NodeId child = tree.NewLeaf(new Style());
        NodeId root = tree.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Size = Size.FromLengths(100f, 50f),
                GridTemplateColumns = new List<GridTemplateComponent>
                {
                    GridTemplateComponent.Repeat(new GridTemplateRepetition(
                        RepetitionCount.Count(2),
                        new List<TrackSizingFunction> { TrackSizingFunction.Length(50f) },
                        new List<List<string>> { new() { "col-start" }, new() { "col-end" } })),
                },
                GridTemplateColumnNames = new List<List<string>> { new() { "outer-start" }, new() { "outer-end" } },
                GridTemplateRows = Lengths(50f),
            },
            child);
        tree.ComputeLayout(root, Definite(100f, 50f));

        DetailedGridInfo info = GetDetailedGridInfo(tree, root);
        List<IReadOnlyList<string>> columnLines = info.Columns.IterLineNames().ToList();
        Assert.Equal(3, columnLines.Count);
        // First repetition's end line collapses with the second repetition's start line,
        // and the last repetition's end line collapses with the following template name set
        Assert.Equal(new[] { "outer-start", "col-start" }, columnLines[0]);
        Assert.Equal(new[] { "col-end", "col-start" }, columnLines[1]);
        Assert.Equal(new[] { "col-end", "outer-end" }, columnLines[2]);

        Assert.Equal(
            "[outer-start col-start] 50px [col-end col-start] 50px [col-end outer-end]",
            info.GridTemplateColumns(1f));
    }

    [Fact]
    public void implicit_tracks_shift_line_names()
    {
        TaffyTree<TestNodeContext> tree = new();
        NodeId child = tree.NewLeaf(new Style
        {
            GridColumn = new Line<GridPlacement>(GridPlacement.Line(-3), GridPlacement.Auto),
        });
        NodeId root = tree.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Size = Size.FromLengths(100f, 50f),
                GridTemplateColumns = Lengths(50f),
                GridTemplateColumnNames = new List<List<string>> { new() { "a" }, new() { "b" } },
                GridTemplateRows = Lengths(50f),
            },
            child);
        tree.ComputeLayout(root, Definite(100f, 50f));

        DetailedGridInfo info = GetDetailedGridInfo(tree, root);
        Assert.Equal(1, info.Columns.NegativeImplicitTracks);
        List<IReadOnlyList<string>> columnLines = info.Columns.IterLineNames().ToList();
        // One leading implicit track: its lines are unnamed, explicit names shift by one
        Assert.Equal(3, columnLines.Count);
        Assert.Empty(columnLines[0]);
        Assert.Equal(new[] { "a" }, columnLines[1]);
        Assert.Equal(new[] { "b" }, columnLines[2]);
        float firstTrackSize = info.Columns.Positions[0].End - info.Columns.Positions[0].Start;
        string expected = firstTrackSize.ToString(CultureInfo.InvariantCulture) + "px [a] 50px [b]";
        Assert.Equal(expected, info.GridTemplateColumns(1f));
    }

    [Fact]
    public void item_grid_area()
    {
        TaffyTree<TestNodeContext> tree = new();
        NodeId childA = tree.NewLeaf(new Style());
        NodeId childB = tree.NewLeaf(new Style
        {
            GridRow = new Line<GridPlacement>(GridPlacement.Line(2), GridPlacement.Auto),
            GridColumn = Lines(1, 3),
        });
        NodeId container = tree.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                GridTemplateColumns = Lengths(40f, 60f),
                GridTemplateRows = Lengths(50f, 30f),
                Gap = new Size<LengthPercentage>(LengthPercentage.Length(10f), LengthPercentage.Length(10f)),
                Padding = new Rect<LengthPercentage>(
                    LengthPercentage.Length(5f),
                    LengthPercentage.Length(5f),
                    LengthPercentage.Length(5f),
                    LengthPercentage.Length(5f)),
            },
            childA,
            childB);

        tree.ComputeLayout(container, Size.MaxContent);
        DetailedGridInfo info = GetDetailedGridInfo(tree, container);

        // child_a is auto-placed into row 1 / column 1
        Assert.Equal(Area(5f, 5f, 40f, 50f), info.ItemGridArea(0));
        // child_b spans both columns in row 2
        Assert.Equal(Area(5f, 65f, 110f, 30f), info.ItemGridArea(1));
        // out of bounds index
        Assert.Null(info.ItemGridArea(2));

        Assert.Equal(
            new Rect<float>(5f, 115f, 5f, 95f),
            info.ResolveAbsoluteGridArea(Lines(1, 3), Lines(1, 3), Direction.Ltr, new Rect<float>(0f, 120f, 0f, 100f)));
        Assert.Equal(
            new Rect<float>(0f, 45f, 0f, 100f),
            info.ResolveAbsoluteGridArea(
                AutoLine(),
                new Line<GridPlacement>(GridPlacement.Auto, GridPlacement.Line(2)),
                Direction.Ltr,
                new Rect<float>(0f, 120f, 0f, 100f)));
        Assert.Equal(
            new Rect<float>(115f, 120f, 0f, 100f),
            info.ResolveAbsoluteGridArea(
                AutoLine(),
                new Line<GridPlacement>(GridPlacement.Line(-1), GridPlacement.Auto),
                Direction.Ltr,
                new Rect<float>(0f, 120f, 0f, 100f)));
        Assert.Equal(
            // An unknown named line resolves to the first implicit line past the explicit grid,
            // which (being outside the grid) falls back to the padding edge. The resulting
            // start/end pair is swapped, so this is equivalent to `2 / <padding-end>`.
            new Rect<float>(55f, 120f, 0f, 100f),
            info.ResolveAbsoluteGridArea(
                AutoLine(),
                new Line<GridPlacement>(GridPlacement.NamedLine("missing", 1), GridPlacement.Line(2)),
                Direction.Ltr,
                new Rect<float>(0f, 120f, 0f, 100f)));
        Assert.Equal(
            new Rect<float>(0f, 120f, 0f, 100f),
            info.ResolveAbsoluteGridArea(
                AutoLine(),
                new Line<GridPlacement>(GridPlacement.Span(2), GridPlacement.Span(3)),
                Direction.Ltr,
                new Rect<float>(0f, 120f, 0f, 100f)));
    }

    [Fact]
    public void absolute_grid_area_named_span()
    {
        TaffyTree<TestNodeContext> tree = new();
        NodeId child = tree.NewLeaf(new Style());
        NodeId root = tree.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Size = Size.FromLengths(90f, 50f),
                GridTemplateColumns = Lengths(30f, 30f, 30f),
                GridTemplateColumnNames = new List<List<string>>
                {
                    new() { "a" },
                    new() { "b" },
                    new() { "b" },
                    new() { "c" },
                },
                GridTemplateRows = Lengths(50f),
            },
            child);
        tree.ComputeLayout(root, Definite(90f, 50f));

        DetailedGridInfo info = GetDetailedGridInfo(tree, root);
        Assert.Equal(
            new Rect<float>(0f, 60f, 0f, 50f),
            info.ResolveAbsoluteGridArea(
                AutoLine(),
                new Line<GridPlacement>(GridPlacement.NamedLine("a", 1), GridPlacement.NamedSpan("b", 2)),
                Direction.Ltr,
                new Rect<float>(0f, 90f, 0f, 50f)));
        Assert.Equal(
            new Rect<float>(60f, 90f, 0f, 50f),
            info.ResolveAbsoluteGridArea(
                AutoLine(),
                new Line<GridPlacement>(GridPlacement.NamedLine("b", 2), GridPlacement.NamedLine("c", 1)),
                Direction.Ltr,
                new Rect<float>(0f, 90f, 0f, 50f)));
    }

    [Fact]
    public void rtl_line_names_are_logical_order()
    {
        TaffyTree<TestNodeContext> tree = new();
        NodeId child = tree.NewLeaf(new Style());
        NodeId root = tree.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Direction = Direction.Rtl,
                Size = Size.FromLengths(100f, 50f),
                GridTemplateColumns = Lengths(40f, 60f),
                GridTemplateColumnNames = new List<List<string>> { new() { "a" }, new() { "b" }, new() { "c" } },
                GridTemplateRows = Lengths(50f),
            },
            child);
        tree.ComputeLayout(root, Definite(100f, 50f));

        DetailedGridInfo info = GetDetailedGridInfo(tree, root);
        List<IReadOnlyList<string>> columnLines = info.Columns.IterLineNames().ToList();
        // Tracks and line names are in logical order regardless of direction
        Assert.Equal(new[] { "a" }, columnLines[0]);
        Assert.Equal(new[] { "b" }, columnLines[1]);
        Assert.Equal(new[] { "c" }, columnLines[2]);
        Assert.Equal("[a] 40px [b] 60px [c]", info.GridTemplateColumns(1f));

        // Positions hold physical coordinates: logical track 1 is physically rightmost in RTL
        Assert.Equal(new Line<float>(60f, 100f), info.Columns.Positions[0]);
        Assert.Equal(new Line<float>(0f, 60f), info.Columns.Positions[1]);
        // ItemGridArea resolves the physical rectangle of the (auto-placed) item's grid area
        Assert.Equal(Area(60f, 0f, 40f, 50f), info.ItemGridArea(0));
        Assert.Equal(
            new Rect<float>(60f, 100f, 0f, 50f),
            info.ResolveAbsoluteGridArea(AutoLine(), Lines(1, 2), Direction.Rtl, new Rect<float>(0f, 100f, 0f, 50f)));
        Assert.Equal(
            new Rect<float>(60f, 100f, 0f, 50f),
            info.ResolveAbsoluteGridArea(
                AutoLine(),
                new Line<GridPlacement>(GridPlacement.NamedLine("a", 1), GridPlacement.NamedLine("b", 1)),
                Direction.Rtl,
                new Rect<float>(0f, 100f, 0f, 50f)));
        Assert.Equal(
            new Rect<float>(0f, 100f, 0f, 50f),
            info.ResolveAbsoluteGridArea(
                AutoLine(),
                new Line<GridPlacement>(GridPlacement.Line(1), GridPlacement.Auto),
                Direction.Rtl,
                new Rect<float>(0f, 100f, 0f, 50f)));
    }
}
