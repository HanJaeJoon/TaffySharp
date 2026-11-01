// fit-content() with a calc() limit. Taffy stores the calc() as an opaque handle which is resolved
// through ILayoutPartialTree.ResolveCalcValue, so these tests use a minimal custom tree whose calc
// values are `pct * basis + px`.
namespace TaffySharp.Tests.HandWritten;

public class FitContentCalcTests
{
    // A calc() value of the form `pct * basis + px`.
    private sealed class Calc
    {
        public Calc(float px, float pct)
        {
            Px = px;
            Pct = pct;
        }

        public float Px { get; }

        public float Pct { get; }
    }

    private static LengthPercentage CalcLength(float px, float pct) => LengthPercentage.Calc(new Calc(px, pct));

    private static float ResolveCalc(object val, float basis)
    {
        Calc calc = (Calc)val;
        return calc.Pct * basis + calc.Px;
    }

    private enum Kind
    {
        Block,
        Grid,
        // A leaf with the given min-content width, max-content width and height
        Leaf,
    }

    private sealed class Node : ILayoutContainingBlock, ICacheTree, ILayoutBlockContainer, ILayoutGridContainer
    {
        private static readonly NodeId Root = new(ulong.MaxValue);

        public Node(Kind kind, Style style, List<Node> children)
        {
            NodeKind = kind;
            Style = style;
            Children = children;
        }

        public Kind NodeKind { get; }

        public float MinWidth { get; private set; }

        public float MaxWidth { get; private set; }

        public float Height { get; private set; }

        public Style Style { get; }

        public Cache Cache { get; } = new();

        public Layout Layout { get; set; } = Layout.WithOrder(0);

        public List<Node> Children { get; }

        public List<NodeId> HoistedChildren { get; } = new();

        public static Node Leaf(float minWidth, float maxWidth, float height, Style style) =>
            new(Kind.Leaf, style, new List<Node>()) { MinWidth = minWidth, MaxWidth = maxWidth, Height = height };

        private Node NodeFromId(NodeId nodeId) => nodeId == Root ? this : Children[(int)nodeId.Value];

        public void ComputeLayout(Size<AvailableSpace> availableSpace) => Compute.ComputeRootLayout(this, Root, availableSpace);

        // ITraversePartialTree
        public IEnumerable<NodeId> ChildIds(NodeId parentNodeId) =>
            Enumerable.Range(0, Children.Count).Select(index => new NodeId((ulong)index));

        public int ChildCount(NodeId parentNodeId) => Children.Count;

        public NodeId GetChildId(NodeId parentNodeId, int childIndex) => new((ulong)childIndex);

        // ILayoutPartialTree
        public ICoreStyle GetCoreContainerStyle(NodeId nodeId) => NodeFromId(nodeId).Style;

        public void SetUnroundedLayout(NodeId nodeId, in Layout layout) => NodeFromId(nodeId).Layout = layout;

        public float ResolveCalcValue(object val, float basis) => ResolveCalc(val, basis);

        public LayoutOutput ComputeChildLayout(NodeId nodeId, LayoutInput inputs) =>
            Compute.ComputeCachedLayout(this, nodeId, inputs, (parent, id, nodeInputs) =>
            {
                Node node = parent.NodeFromId(id);
                switch (node.NodeKind)
                {
                    case Kind.Block:
                        return Compute.ComputeBlockLayout(node, id, nodeInputs, null);
                    case Kind.Grid:
                        return Compute.ComputeGridLayout(node, id, nodeInputs);
                    default:
                        float minWidth = node.MinWidth;
                        float maxWidth = node.MaxWidth;
                        float height = node.Height;
                        return Compute.ComputeLeafLayout(nodeInputs, node.Style, ResolveCalc, (knownDimensions, availableSpace) =>
                        {
                            float width = knownDimensions.Width ?? (availableSpace.Width.IsDefinite
                                ? MathHelpers.Clamp(availableSpace.Width.Value, minWidth, maxWidth)
                                : availableSpace.Width.IsMinContent ? minWidth : maxWidth);
                            return new Size<float>(width, knownDimensions.Height ?? height);
                        });
                }
            });

        // ILayoutContainingBlock
        public IOofItemStyle GetOofItemStyle(NodeId nodeId) => NodeFromId(nodeId).Style;

        public void ClearHoistedChildren(NodeId nodeId) => NodeFromId(nodeId).HoistedChildren.Clear();

        public void AddHoistedChildren(NodeId nodeId, IReadOnlyList<NodeId> hoisted) => NodeFromId(nodeId).HoistedChildren.AddRange(hoisted);

        public DetailedLayoutInfo GetDetailedLayoutInfo(NodeId nodeId) => DetailedLayoutInfo.None;

        // ICacheTree
        public LayoutOutput? CacheGet(NodeId nodeId, in LayoutInput input) => NodeFromId(nodeId).Cache.Get(input);

        public void CacheStore(NodeId nodeId, in LayoutInput input, LayoutOutput layoutOutput) => NodeFromId(nodeId).Cache.Store(input, layoutOutput);

        public void CacheClear(NodeId nodeId) => NodeFromId(nodeId).Cache.Clear();

        // ILayoutBlockContainer
        public IBlockContainerStyle GetBlockContainerStyle(NodeId nodeId) => NodeFromId(nodeId).Style;

        public IBlockItemStyle GetBlockChildStyle(NodeId childNodeId) => NodeFromId(childNodeId).Style;

        public LayoutOutput ComputeBlockChildLayout(NodeId nodeId, LayoutInput inputs, BlockContext? blockCtx) => ComputeChildLayout(nodeId, inputs);

        // ILayoutGridContainer
        public IGridContainerStyle GetGridContainerStyle(NodeId nodeId) => NodeFromId(nodeId).Style;

        public IGridItemStyle GetGridChildStyle(NodeId childNodeId) => NodeFromId(childNodeId).Style;

        public void SetDetailedGridInfo(NodeId nodeId, DetailedGridInfo detailedGridInfo)
        {
        }
    }

    // Mirrors WPT css/css-grid/grid-definition/fit-content-track-sizing.html (`calculatedArgument`):
    // a 300px tall grid with `grid-template-rows: fit-content(calc(10% + 5px))` and a 400px tall item
    // with `min-height: 0` sizes the row to 35px.
    [Fact]
    public void grid_track_fit_content_calc_limit()
    {
        Node root = new(
            Kind.Grid,
            new Style
            {
                Display = Display.Grid,
                Size = Size.FromLengths(100f, 300f),
                GridTemplateRows = new List<GridTemplateComponent> { TrackSizingFunction.FitContent(CalcLength(5f, 0.1f)) },
            },
            new List<Node>
            {
                Node.Leaf(
                    0f,
                    0f,
                    400f,
                    new Style { MinSize = new Size<LengthPercentageAuto>(LengthPercentageAuto.Auto, LengthPercentageAuto.Length(0f)) }),
            });
        root.ComputeLayout(Size.MaxContent);
        Assert.Equal(35f, root.Children[0].Layout.Size.Height);
    }

    // `width: fit-content(calc(10% + 70px))` on a block-level box in a 300px wide container is
    // `fit-content(100px)`: the max-content width clamped by the limit and floored by the min-content width.
    [Fact]
    public void block_item_fit_content_calc_width()
    {
        Dimension limit = Dimension.FitContentCalc(CalcLength(70f, 0.1f).Raw.CalcValue!);
        Node root = new(
            Kind.Block,
            new Style
            {
                Display = Display.Block,
                Size = new Size<Dimension>(Dimension.Length(300f), Dimension.Auto),
            },
            new List<Node>
            {
                Node.Leaf(50f, 400f, 10f, new Style { Size = new Size<Dimension>(limit, Dimension.Auto) }),
            });
        root.ComputeLayout(Size.MaxContent);
        Assert.Equal(100f, root.Children[0].Layout.Size.Width);
    }

    // Returns (container width, item width) for a single-column grid with the given column sizing
    private static (float ContainerWidth, float ItemWidth) LayoutSingleColumnGrid(
        GridTemplateComponent column,
        Dimension containerWidth,
        AvailableSpace availableWidth)
    {
        Node root = new(
            Kind.Grid,
            new Style
            {
                Display = Display.Grid,
                Size = new Size<Dimension>(containerWidth, Dimension.Length(40f)),
                GridTemplateColumns = new List<GridTemplateComponent> { column },
            },
            new List<Node> { Node.Leaf(50f, 150f, 10f, new Style()) });
        root.ComputeLayout(new Size<AvailableSpace>(availableWidth, AvailableSpace.MaxContent));
        return (root.Layout.Size.Width, root.Children[0].Layout.Size.Width);
    }

    // A calc() limit resolves to the same value as the equivalent length or percentage limit, including
    // when the container's width is indefinite (which re-runs track sizing against the container's own width).
    [Fact]
    public void grid_track_fit_content_calc_matches_equivalent_length_and_percentage()
    {
        // Definite container: calc(20% + 10px) of 200px == 50px
        (float, float) calcResult = LayoutSingleColumnGrid(
            TrackSizingFunction.FitContent(CalcLength(10f, 0.2f)),
            Dimension.Length(200f),
            AvailableSpace.MaxContent);
        (float, float) pxResult = LayoutSingleColumnGrid(
            TrackSizingFunction.FitContent(LengthPercentage.Length(50f)),
            Dimension.Length(200f),
            AvailableSpace.MaxContent);
        Assert.Equal(pxResult, calcResult);
        Assert.Equal(50f, calcResult.Item2);

        // Indefinite (shrink-to-fit) container: calc(50% + 0px) == 50%
        foreach (AvailableSpace availableWidth in new[] { AvailableSpace.Definite(200f), AvailableSpace.MaxContent, AvailableSpace.MinContent })
        {
            (float, float) indefiniteCalcResult = LayoutSingleColumnGrid(
                TrackSizingFunction.FitContent(CalcLength(0f, 0.5f)),
                Dimension.Auto,
                availableWidth);
            (float, float) pctResult = LayoutSingleColumnGrid(
                TrackSizingFunction.FitContent(LengthPercentage.Percent(0.5f)),
                Dimension.Auto,
                availableWidth);
            Assert.True(
                pctResult.Equals(indefiniteCalcResult),
                $"available width: {availableWidth}: expected {pctResult}, actual {indefiniteCalcResult}");
        }

        // ... and the percentage really is resolved against the container's width
        (float containerWidth, float itemWidth) = LayoutSingleColumnGrid(
            TrackSizingFunction.FitContent(CalcLength(0f, 0.5f)),
            Dimension.Auto,
            AvailableSpace.MaxContent);
        Assert.Equal(150f, containerWidth);
        Assert.Equal(75f, itemWidth);
    }
}
