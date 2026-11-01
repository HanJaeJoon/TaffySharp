namespace TaffySharp;

/// <summary>Whether we are performing a full layout, or we merely need to size the node.</summary>
public enum RunMode
{
    /// <summary>A full layout for this node and all its children should be computed.</summary>
    PerformLayout,
    /// <summary>The layout algorithm should be executed such that an accurate container size for the node can be determined.</summary>
    ComputeSize,
    /// <summary>This node should have a null layout set as it has been hidden (<c>display: none</c>).</summary>
    PerformHiddenLayout,
}

/// <summary>Whether styles should be taken into account when computing size.</summary>
public enum SizingMode
{
    /// <summary>Only content contributions should be taken into account.</summary>
    ContentSize,
    /// <summary>Inherent size styles should be taken into account in addition to content contributions.</summary>
    InherentSize,
}

/// <summary>A set of margins that are available for collapsing with for block layout's margin collapsing.</summary>
public struct CollapsibleMarginSet : IEquatable<CollapsibleMarginSet>
{
    private float _positive;
    private float _negative;

    private CollapsibleMarginSet(float positive, float negative)
    {
        _positive = positive;
        _negative = negative;
    }

    public static CollapsibleMarginSet Zero => default;

    /// <summary>Create a set from a single margin.</summary>
    public static CollapsibleMarginSet FromMargin(float margin) =>
        margin >= 0f ? new(margin, 0f) : new(0f, margin);

    /// <summary>Collapse a single margin with this set.</summary>
    public readonly CollapsibleMarginSet CollapseWithMargin(float margin)
    {
        CollapsibleMarginSet result = this;
        if (margin >= 0f)
        {
            result._positive = MathHelpers.Max(_positive, margin);
        }
        else
        {
            result._negative = MathHelpers.Min(_negative, margin);
        }

        return result;
    }

    /// <summary>Collapse another margin set with this set.</summary>
    public readonly CollapsibleMarginSet CollapseWithSet(CollapsibleMarginSet other) =>
        new(MathHelpers.Max(_positive, other._positive), MathHelpers.Min(_negative, other._negative));

    /// <summary>Resolve the resultant margin from this set once all collapsible margins have been collapsed into it.</summary>
    public readonly float Resolve() => _positive + _negative;

    public readonly bool Equals(CollapsibleMarginSet other) => _positive.Equals(other._positive) && _negative.Equals(other._negative);

    public override readonly bool Equals(object? obj) => obj is CollapsibleMarginSet other && Equals(other);

    public override readonly int GetHashCode() => (_positive, _negative).GetHashCode();

    public static bool operator ==(CollapsibleMarginSet a, CollapsibleMarginSet b) => a.Equals(b);

    public static bool operator !=(CollapsibleMarginSet a, CollapsibleMarginSet b) => !a.Equals(b);
}

/// <summary>An axis that layout algorithms can be requested to compute a size for.</summary>
public enum RequestedAxis
{
    Horizontal,
    Vertical,
    Both,
}

public static class RequestedAxisExtensions
{
    public static RequestedAxis Transpose(this RequestedAxis self) => self switch
    {
        RequestedAxis.Horizontal => RequestedAxis.Vertical,
        RequestedAxis.Vertical => RequestedAxis.Horizontal,
        _ => RequestedAxis.Both,
    };

    public static RequestedAxis ToRequestedAxis(this AbsoluteAxis axis) =>
        axis == AbsoluteAxis.Horizontal ? RequestedAxis.Horizontal : RequestedAxis.Vertical;

    /// <summary>Rust <c>TryFrom&lt;RequestedAxis&gt; for AbsoluteAxis</c>: <c>null</c> for <c>Both</c>.</summary>
    public static AbsoluteAxis? ToAbsoluteAxis(this RequestedAxis self) => self switch
    {
        RequestedAxis.Horizontal => AbsoluteAxis.Horizontal,
        RequestedAxis.Vertical => AbsoluteAxis.Vertical,
        _ => null,
    };
}

/// <summary>A struct containing the inputs constraints/hints for laying out a node, which are passed in by the parent.</summary>
public struct LayoutInput : IEquatable<LayoutInput>
{
    /// <summary>Whether we only need to know the Node's size, or whether we need to perform a full layout.</summary>
    public RunMode RunMode;
    /// <summary>Whether a Node's style sizes should be taken into account or ignored.</summary>
    public SizingMode SizingMode;
    /// <summary>Which axis we need the size of.</summary>
    public RequestedAxis Axis;
    /// <summary>Known dimensions represent dimensions (width/height) which should be taken as fixed when performing layout.</summary>
    public Size<float?> KnownDimensions;
    /// <summary>Whether each known dimension is a definite size (for percentage resolution).</summary>
    public Size<bool> KnownDimensionsAreDefinite;
    /// <summary>Parent size dimensions are intended to be used for percentage resolution.</summary>
    public Size<float?> ParentSize;
    /// <summary>Available space represents an amount of space to layout into, and is used as a soft constraint.</summary>
    public Size<AvailableSpace> AvailableSpace;
    /// <summary>Specific to CSS Block layout. Used for correctly computing margin collapsing.</summary>
    public Line<bool> VerticalMarginsAreCollapsible;

    /// <summary>A LayoutInput that can be used to request hidden layout.</summary>
    public static LayoutInput Hidden => new()
    {
        RunMode = RunMode.PerformHiddenLayout,
        KnownDimensions = Size.None,
        KnownDimensionsAreDefinite = new(true, true),
        ParentSize = Size.None,
        AvailableSpace = Size.MaxContent,
        SizingMode = SizingMode.InherentSize,
        Axis = RequestedAxis.Both,
        VerticalMarginsAreCollapsible = Line.False,
    };

    /// <summary>The same input with the axes swapped.</summary>
    public readonly LayoutInput Transpose() => new()
    {
        RunMode = RunMode,
        SizingMode = SizingMode,
        Axis = Axis.Transpose(),
        KnownDimensions = KnownDimensions.Transpose(),
        KnownDimensionsAreDefinite = KnownDimensionsAreDefinite.Transpose(),
        ParentSize = ParentSize.Transpose(),
        AvailableSpace = AvailableSpace.Transpose(),
        VerticalMarginsAreCollapsible = Line.False,
    };

    public readonly bool Equals(LayoutInput other) =>
        RunMode == other.RunMode && SizingMode == other.SizingMode && Axis == other.Axis
        && KnownDimensions == other.KnownDimensions && KnownDimensionsAreDefinite == other.KnownDimensionsAreDefinite
        && ParentSize == other.ParentSize && AvailableSpace == other.AvailableSpace
        && VerticalMarginsAreCollapsible == other.VerticalMarginsAreCollapsible;

    public override readonly bool Equals(object? obj) => obj is LayoutInput other && Equals(other);

    public override readonly int GetHashCode() => (RunMode, SizingMode, Axis, KnownDimensions, ParentSize, AvailableSpace).GetHashCode();
}

/// <summary>The first and last baselines of a node.</summary>
public struct Baselines : IEquatable<Baselines>
{
    public float? First;
    public float? Last;

    public Baselines(float? first, float? last)
    {
        First = first;
        Last = last;
    }

    public static Baselines None => default;

    public static Baselines FromFirst(float? first) => new(first, null);

    public readonly bool Equals(Baselines other) => First == other.First && Last == other.Last;

    public override readonly bool Equals(object? obj) => obj is Baselines other && Equals(other);

    public override readonly int GetHashCode() => (First, Last).GetHashCode();
}

/// <summary>The edge of the static-position rectangle an out-of-flow box is aligned to in one axis.</summary>
public enum AxisStaticEdge
{
    Start,
    Center,
    End,
}

/// <summary>How an out-of-flow box is aligned to its static-position rectangle in one axis.</summary>
public struct AxisStaticAlign : IEquatable<AxisStaticAlign>
{
    public AxisStaticEdge Keyword;
    public AlignmentSafety Safety;
    /// <summary>The edge used instead of <see cref="Keyword"/> when safe alignment overflows.</summary>
    public AxisStaticEdge Fallback;

    public AxisStaticAlign(AxisStaticEdge keyword, AlignmentSafety safety, AxisStaticEdge fallback)
    {
        Keyword = keyword;
        Safety = safety;
        Fallback = fallback;
    }

    public static AxisStaticAlign FromKeyword(AxisStaticEdge keyword) => new(keyword, AlignmentSafety.Default, keyword);

    public readonly bool Equals(AxisStaticAlign other) => Keyword == other.Keyword && Safety == other.Safety && Fallback == other.Fallback;

    public override readonly bool Equals(object? obj) => obj is AxisStaticAlign other && Equals(other);

    public override readonly int GetHashCode() => (Keyword, Safety, Fallback).GetHashCode();
}

/// <summary>The static position of an out-of-flow box in one axis: an area and an alignment within it.</summary>
public struct AxisStaticPosition : IEquatable<AxisStaticPosition>
{
    public Line<float> Area;
    public AxisStaticAlign Align;

    public AxisStaticPosition(Line<float> area, AxisStaticAlign align)
    {
        Area = area;
        Align = align;
    }

    /// <summary>A static position anchored at a single point.</summary>
    public static AxisStaticPosition FromEdge(float anchor, AxisStaticEdge edge) => new(new(anchor, anchor), AxisStaticAlign.FromKeyword(edge));

    /// <summary>A static position aligned within <paramref name="area"/> by a (resolved) self-alignment value.</summary>
    public static AxisStaticPosition FromAlignment(AlignItems alignment, Line<float> area, bool axisIsRtl)
    {
        System.Diagnostics.Debug.Assert(
            alignment.Keyword is not (AlignItemsKeyword.SelfStart or AlignItemsKeyword.SelfEnd),
            "self-start/self-end must be resolved before computing the static position");

        static AxisStaticEdge EdgeFor(AlignItemsKeyword keyword, bool axisIsRtl)
        {
            bool startPosition = (keyword is not (AlignItemsKeyword.End or AlignItemsKeyword.FlexEnd)) ^ axisIsRtl;
            if (keyword == AlignItemsKeyword.Center)
            {
                return AxisStaticEdge.Center;
            }

            return startPosition ? AxisStaticEdge.Start : AxisStaticEdge.End;
        }

        AlignItemsKeyword fallback = alignment.Safety == AlignmentSafety.Safe ? AlignItemsKeyword.Start : alignment.Keyword;
        return new(area, new AxisStaticAlign(EdgeFor(alignment.Keyword, axisIsRtl), alignment.Safety, EdgeFor(fallback, axisIsRtl)));
    }

    public readonly bool Equals(AxisStaticPosition other) => Area == other.Area && Align.Equals(other.Align);

    public override readonly bool Equals(object? obj) => obj is AxisStaticPosition other && Equals(other);

    public override readonly int GetHashCode() => (Area, Align).GetHashCode();
}

/// <summary>An out-of-flow box waiting for its containing block to lay it out.</summary>
public struct OofCandidate : IEquatable<OofCandidate>
{
    public NodeId Node;
    /// <summary>The order of the box within its parent (used for <see cref="Layout.Order"/>).</summary>
    public uint Order;
    public Position Position;
    public Point<AxisStaticPosition> StaticPosition;

    public readonly bool Equals(OofCandidate other) =>
        Node == other.Node && Order == other.Order && Position == other.Position && StaticPosition == other.StaticPosition;

    public override readonly bool Equals(object? obj) => obj is OofCandidate other && Equals(other);

    public override readonly int GetHashCode() => (Node, Order, Position).GetHashCode();
}

/// <summary>
/// A list of out-of-flow candidates. This is a value type around a lazily created list: copying it
/// shares the list, so use <see cref="Clone"/> where upstream clones it.
/// </summary>
public struct OofCandidates : IEquatable<OofCandidates>
{
    private List<OofCandidate>? _inner;

    private OofCandidates(List<OofCandidate>? inner) => _inner = inner;

    public static OofCandidates None => default;

    public readonly bool IsEmpty => _inner is null || _inner.Count == 0;

    public readonly int Count => _inner?.Count ?? 0;

    public readonly OofCandidate this[int index] => _inner![index];

    /// <summary>The candidates (empty when there are none).</summary>
    public readonly IReadOnlyList<OofCandidate> AsSlice() => (IReadOnlyList<OofCandidate>?)_inner ?? Array.Empty<OofCandidate>();

    public void Push(OofCandidate candidate)
    {
        _inner ??= new List<OofCandidate>();
        _inner.Add(candidate);
    }

    /// <summary>Moves all candidates of <paramref name="other"/> to the end of this list, leaving it empty.</summary>
    public void Append(ref OofCandidates other)
    {
        if (other.IsEmpty)
        {
            return;
        }

        if (_inner is null)
        {
            _inner = other._inner;
            other._inner = null;
        }
        else
        {
            _inner.AddRange(other._inner!);
            other._inner = null;
        }
    }

    /// <summary>Takes the candidates out, leaving this list empty.</summary>
    public OofCandidates Take()
    {
        OofCandidates taken = new(_inner);
        _inner = null;
        return taken;
    }

    public void Clear() => _inner = null;

    /// <summary>Offsets the static positions of all candidates.</summary>
    public readonly void Translate(Point<float> offset)
    {
        if (_inner is null)
        {
            return;
        }

        for (int i = 0; i < _inner.Count; i++)
        {
            OofCandidate candidate = _inner[i];
            candidate.StaticPosition.X.Area.Start += offset.X;
            candidate.StaticPosition.X.Area.End += offset.X;
            candidate.StaticPosition.Y.Area.Start += offset.Y;
            candidate.StaticPosition.Y.Area.End += offset.Y;
            _inner[i] = candidate;
        }
    }

    /// <summary>A copy with its own list.</summary>
    public readonly OofCandidates Clone() => _inner is null ? default : new(new List<OofCandidate>(_inner));

    public readonly List<OofCandidate>.Enumerator GetEnumerator() => (_inner ?? EmptyList).GetEnumerator();

    private static readonly List<OofCandidate> EmptyList = new();

    public readonly bool Equals(OofCandidates other) => AsSlice().SequenceEqual(other.AsSlice());

    public override readonly bool Equals(object? obj) => obj is OofCandidates other && Equals(other);

    public override readonly int GetHashCode() => Count;
}

/// <summary>The area out-of-flow boxes whose containing block is this node are positioned in.</summary>
public struct OofPositioningArea : IEquatable<OofPositioningArea>
{
    public Size<float> Size;
    public Point<float> Offset;

    public OofPositioningArea(Size<float> size, Point<float> offset)
    {
        Size = size;
        Offset = offset;
    }

    public readonly bool Equals(OofPositioningArea other) => Size == other.Size && Offset == other.Offset;

    public override readonly bool Equals(object? obj) => obj is OofPositioningArea other && Equals(other);

    public override readonly int GetHashCode() => (Size, Offset).GetHashCode();
}

/// <summary>A struct containing the result of laying a single node, which is returned up to the parent node.</summary>
public struct LayoutOutput : IEquatable<LayoutOutput>
{
    /// <summary>The size of the node.</summary>
    public Size<float> Size;
    /// <summary>The scrollable overflow rect of the node's content (relative to the node's border box).</summary>
    public Rect<float> ScrollableOverflowRect;
    /// <summary>The first baseline of the node in each dimension, if any.</summary>
    public Baselines Baselines;
    /// <summary>Top margin that can be collapsed with. This is used for CSS block layout.</summary>
    public CollapsibleMarginSet TopMargin;
    /// <summary>Bottom margin that can be collapsed with. This is used for CSS block layout.</summary>
    public CollapsibleMarginSet BottomMargin;
    /// <summary>Whether margins can be collapsed through this node. This is used for CSS block layout.</summary>
    public bool MarginsCanCollapseThrough;
    /// <summary>Out-of-flow descendants that have not been laid out yet because their containing block is an ancestor.</summary>
    public OofCandidates OofCandidates;
    /// <summary>The area out-of-flow boxes for which this node is the containing block are positioned in.</summary>
    public OofPositioningArea? OofPositioningArea;

    /// <summary>An all-zero LayoutOutput for hidden nodes.</summary>
    public static LayoutOutput Hidden => default;

    /// <summary>A blank layout output.</summary>
    public static LayoutOutput Default => Hidden;

    /// <summary>Constructor to create a LayoutOutput from just the size and baselines.</summary>
    public static LayoutOutput FromSizesAndBaselines(Size<float> size, Rect<float> scrollableOverflowRect, Baselines baselines) => new()
    {
        Size = size,
        ScrollableOverflowRect = scrollableOverflowRect,
        Baselines = baselines,
    };

    /// <summary>Construct a LayoutOutput from just the container and content sizes.</summary>
    public static LayoutOutput FromSizes(Size<float> size, Rect<float> scrollableOverflowRect) =>
        FromSizesAndBaselines(size, scrollableOverflowRect, Baselines.None);

    /// <summary>Construct a LayoutOutput from just the container's size.</summary>
    public static LayoutOutput FromOuterSize(Size<float> size) => FromSizes(size, Rect.Zero);

    /// <summary>The same output with the axes swapped (margin collapsing metadata and baselines are reset).</summary>
    public readonly LayoutOutput Transpose() => new()
    {
        Size = Size.Transpose(),
        ScrollableOverflowRect = ScrollableOverflowRect.Transpose(),
        Baselines = Baselines.None,
        OofCandidates = OofCandidates,
        OofPositioningArea = OofPositioningArea is { } area ? new OofPositioningArea(area.Size.Transpose(), area.Offset.Transpose()) : null,
    };

    /// <summary>A copy with its own list of out-of-flow candidates (Rust <c>clone</c>).</summary>
    public readonly LayoutOutput Clone()
    {
        LayoutOutput clone = this;
        clone.OofCandidates = OofCandidates.Clone();
        return clone;
    }

    public readonly bool Equals(LayoutOutput other) =>
        Size == other.Size && ScrollableOverflowRect == other.ScrollableOverflowRect && Baselines.Equals(other.Baselines)
        && TopMargin == other.TopMargin && BottomMargin == other.BottomMargin && MarginsCanCollapseThrough == other.MarginsCanCollapseThrough
        && OofCandidates.Equals(other.OofCandidates) && Nullable.Equals(OofPositioningArea, other.OofPositioningArea);

    public override readonly bool Equals(object? obj) => obj is LayoutOutput other && Equals(other);

    public override readonly int GetHashCode() => Size.GetHashCode();
}

/// <summary>The final result of a layout algorithm for a single node.</summary>
public struct Layout : IEquatable<Layout>
{
    /// <summary>The relative ordering of the node. Nodes with a higher order should be rendered on top of those with a lower order.</summary>
    public uint Order;
    /// <summary>The top-left corner of the node, relative to its parent (or containing block for out-of-flow boxes).</summary>
    public Point<float> Location;
    /// <summary>The width and height of the node.</summary>
    public Size<float> Size;
    /// <summary>The scrollable overflow rect of the node's content, relative to its border box.</summary>
    public Rect<float> ScrollableOverflowRect;
    /// <summary>The size of the scrollbars in each dimension. If there is no scrollbar then the size will be zero.</summary>
    public Size<float> ScrollbarSize;
    /// <summary>The size of the borders of the node.</summary>
    public Rect<float> Border;
    /// <summary>The size of the padding of the node.</summary>
    public Rect<float> Padding;
    /// <summary>The size of the margin of the node.</summary>
    public Rect<float> Margin;

    /// <summary>Creates a new zero-Layout.</summary>
    public static Layout New() => default;

    /// <summary>Creates a new zero-Layout with the supplied order value.</summary>
    public static Layout WithOrder(uint order) => new() { Order = order };

    /// <summary>Get the width of the node's content box.</summary>
    public readonly float ContentBoxWidth() => Size.Width - Padding.Left - Padding.Right - Border.Left - Border.Right;

    /// <summary>Get the height of the node's content box.</summary>
    public readonly float ContentBoxHeight() => Size.Height - Padding.Top - Padding.Bottom - Border.Top - Border.Bottom;

    /// <summary>Get the size of the node's content box.</summary>
    public readonly Size<float> ContentBoxSize() => new(ContentBoxWidth(), ContentBoxHeight());

    /// <summary>Get x offset of the node's content box relative to its parent's border box.</summary>
    public readonly float ContentBoxX() => Location.X + Border.Left + Padding.Left;

    /// <summary>Get y offset of the node's content box relative to its parent's border box.</summary>
    public readonly float ContentBoxY() => Location.Y + Border.Top + Padding.Top;

    /// <summary>The amount of overflow in the horizontal axis (the scrollable width).</summary>
    public readonly float ScrollWidth() => MathHelpers.Max(
        0f,
        ScrollableOverflowRect.Right + MathHelpers.Min(ScrollbarSize.Width, Size.Width) - Size.Width + Border.Left + Border.Right);

    /// <summary>The amount of overflow in the vertical axis (the scrollable height).</summary>
    public readonly float ScrollHeight() => MathHelpers.Max(
        0f,
        ScrollableOverflowRect.Bottom + MathHelpers.Min(ScrollbarSize.Height, Size.Height) - Size.Height + Border.Top + Border.Bottom);

    public readonly bool Equals(Layout other) =>
        Order == other.Order && Location == other.Location && Size == other.Size && ScrollableOverflowRect == other.ScrollableOverflowRect
        && ScrollbarSize == other.ScrollbarSize && Border == other.Border && Padding == other.Padding && Margin == other.Margin;

    public override readonly bool Equals(object? obj) => obj is Layout other && Equals(other);

    public override readonly int GetHashCode() => (Order, Location, Size).GetHashCode();

    public static bool operator ==(Layout a, Layout b) => a.Equals(b);

    public static bool operator !=(Layout a, Layout b) => !a.Equals(b);

    public override readonly string ToString() => $"Layout {{ order: {Order}, location: {Location}, size: {Size} }}";
}

/// <summary>Detailed layout info that some algorithms store for a node (only CSS Grid stores any).</summary>
public sealed class DetailedLayoutInfo
{
    private DetailedLayoutInfo(DetailedGridInfo? grid) => Grid = grid;

    /// <summary>No detailed info.</summary>
    public static DetailedLayoutInfo None { get; } = new(null);

    /// <summary>The detailed grid info, or <c>null</c> if the node is not a grid container.</summary>
    public DetailedGridInfo? Grid { get; }

    public static DetailedLayoutInfo FromGrid(DetailedGridInfo grid) => new(grid ?? throw new ArgumentNullException(nameof(grid)));
}
