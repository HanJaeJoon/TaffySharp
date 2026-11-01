namespace TaffySharp;

/// <summary>Taffy's abstraction for downward tree traversal (only the direct children are reachable).</summary>
public interface ITraversePartialTree
{
    /// <summary>Get the list of children IDs for the given node.</summary>
    IEnumerable<NodeId> ChildIds(NodeId parentNodeId);

    /// <summary>Get the number of children for the given node.</summary>
    int ChildCount(NodeId parentNodeId);

    /// <summary>Get a specific child of a node, where the index represents the nth child.</summary>
    NodeId GetChildId(NodeId parentNodeId, int childIndex);
}

/// <summary>A marker interface which extends <see cref="ITraversePartialTree"/>: the whole tree is reachable recursively.</summary>
public interface ITraverseTree : ITraversePartialTree
{
}

/// <summary>
/// Any type that implements this interface can be laid out using Taffy's algorithms.
/// It is the core interface the layout algorithms need from a tree.
/// </summary>
public interface ILayoutPartialTree : ITraversePartialTree
{
    /// <summary>Get the core style for the specified node.</summary>
    ICoreStyle GetCoreContainerStyle(NodeId nodeId);

    /// <summary>Resolve a <c>calc()</c> value (as created by <see cref="LengthPercentage.Calc"/>) against a basis. Return 0 if calc is unused.</summary>
    float ResolveCalcValue(object val, float basis);

    /// <summary>Set the node's unrounded layout.</summary>
    void SetUnroundedLayout(NodeId nodeId, in Layout layout);

    /// <summary>Compute the specified node's size or full layout given the specified constraints.</summary>
    LayoutOutput ComputeChildLayout(NodeId nodeId, LayoutInput inputs);
}

/// <summary>Extends <see cref="ILayoutPartialTree"/> with what is needed to lay out out-of-flow boxes whose containing block is a node.</summary>
public interface ILayoutContainingBlock : ILayoutPartialTree
{
    /// <summary>Get the style an out-of-flow box needs.</summary>
    IOofItemStyle GetOofItemStyle(NodeId nodeId);

    /// <summary>Forget the out-of-flow boxes previously laid out with this node as their containing block.</summary>
    void ClearHoistedChildren(NodeId nodeId);

    /// <summary>Record out-of-flow boxes that were laid out with this node as their containing block.</summary>
    void AddHoistedChildren(NodeId nodeId, IReadOnlyList<NodeId> hoisted);

    /// <summary>Get the detailed layout info of a node (only grid containers store any; return <see cref="DetailedLayoutInfo.None"/> otherwise).</summary>
    DetailedLayoutInfo GetDetailedLayoutInfo(NodeId nodeId);
}

/// <summary>Trait used by the <c>ComputeCachedLayout</c> method which allows cached layout results to be stored and retrieved.</summary>
public interface ICacheTree
{
    /// <summary>Try to retrieve a cached result from the cache.</summary>
    LayoutOutput? CacheGet(NodeId nodeId, in LayoutInput input);

    /// <summary>Store a computed size in the cache.</summary>
    void CacheStore(NodeId nodeId, in LayoutInput input, LayoutOutput layoutOutput);

    /// <summary>Clear all cache entries for the node.</summary>
    void CacheClear(NodeId nodeId);
}

/// <summary>Trait used by the <see cref="Compute.RoundLayout"/> method which takes a tree of unrounded float-valued layouts and performs rounding.</summary>
public interface IRoundTree : ITraverseTree
{
    /// <summary>Get the node's unrounded layout.</summary>
    Layout GetUnroundedLayout(NodeId nodeId);

    /// <summary>Set the node's final layout.</summary>
    void SetFinalLayout(NodeId nodeId, in Layout layout);

    /// <summary>Whether the node is out-of-flow (absolute or fixed) and therefore reached through its containing block.</summary>
    bool IsOutOfFlow(NodeId nodeId);

    /// <summary>The number of out-of-flow boxes for which this node is the containing block.</summary>
    int HoistedChildCount(NodeId nodeId);

    /// <summary>Get the nth out-of-flow box for which this node is the containing block.</summary>
    NodeId GetHoistedChildId(NodeId nodeId, int index);
}

/// <summary>Trait used by the <see cref="TreePrinter.PrintTree"/> method to print a debug representation.</summary>
public interface IPrintTree : ITraverseTree
{
    /// <summary>Get a debug label for the node (typically the type of node: flexbox, grid, text, image, etc).</summary>
    string GetDebugLabel(NodeId nodeId);

    /// <summary>Get a reference to the node's final layout.</summary>
    Layout GetFinalLayout(NodeId nodeId);
}

/// <summary>Extends <see cref="ILayoutPartialTree"/> with getters for the styles required for Flexbox layout.</summary>
public interface ILayoutFlexboxContainer : ILayoutPartialTree
{
    IFlexboxContainerStyle GetFlexboxContainerStyle(NodeId nodeId);

    IFlexboxItemStyle GetFlexboxChildStyle(NodeId childNodeId);
}

/// <summary>Extends <see cref="ILayoutPartialTree"/> with getters for the styles required for CSS Grid layout.</summary>
public interface ILayoutGridContainer : ILayoutPartialTree
{
    IGridContainerStyle GetGridContainerStyle(NodeId nodeId);

    IGridItemStyle GetGridChildStyle(NodeId childNodeId);

    /// <summary>Set the node's detailed grid information (implementations may ignore it).</summary>
    void SetDetailedGridInfo(NodeId nodeId, DetailedGridInfo detailedGridInfo);
}

/// <summary>Extends <see cref="ILayoutPartialTree"/> with getters for the styles required for CSS Block layout.</summary>
public interface ILayoutBlockContainer : ILayoutPartialTree
{
    IBlockContainerStyle GetBlockContainerStyle(NodeId nodeId);

    IBlockItemStyle GetBlockChildStyle(NodeId childNodeId);

    /// <summary>
    /// Compute the layout of a child of a block container, passing the block context through so that
    /// floats and margin collapsing can cross into the child. Implementations without float support can
    /// ignore <paramref name="blockCtx"/> and call <see cref="ILayoutPartialTree.ComputeChildLayout"/>.
    /// </summary>
    LayoutOutput ComputeBlockChildLayout(NodeId nodeId, LayoutInput inputs, BlockContext? blockCtx);
}

/// <summary>Rust <c>LayoutPartialTreeExt</c>: convenience wrappers around <see cref="ILayoutPartialTree.ComputeChildLayout"/>.</summary>
internal static class LayoutPartialTreeExtensions
{
    /// <summary>Compute the size of the node given the specified constraints.</summary>
    public static float MeasureChildSize(
        this ILayoutPartialTree tree,
        NodeId nodeId,
        Size<float?> knownDimensions,
        Size<float?> parentSize,
        Size<AvailableSpace> availableSpace,
        SizingMode sizingMode,
        AbsoluteAxis axis,
        Line<bool> verticalMarginsAreCollapsible) =>
        tree.ComputeChildLayout(nodeId, new LayoutInput
        {
            KnownDimensions = knownDimensions,
            KnownDimensionsAreDefinite = new(true, true),
            ParentSize = parentSize,
            AvailableSpace = availableSpace,
            SizingMode = sizingMode,
            Axis = axis.ToRequestedAxis(),
            RunMode = RunMode.ComputeSize,
            VerticalMarginsAreCollapsible = verticalMarginsAreCollapsible,
        }).Size.GetAbs(axis);

    /// <summary>Compute the size of the node in both axes given the specified constraints.</summary>
    public static Size<float> MeasureChildSizeBoth(
        this ILayoutPartialTree tree,
        NodeId nodeId,
        Size<float?> knownDimensions,
        Size<float?> parentSize,
        Size<AvailableSpace> availableSpace,
        SizingMode sizingMode,
        Line<bool> verticalMarginsAreCollapsible) =>
        tree.ComputeChildLayout(nodeId, new LayoutInput
        {
            KnownDimensions = knownDimensions,
            KnownDimensionsAreDefinite = new(true, true),
            ParentSize = parentSize,
            AvailableSpace = availableSpace,
            SizingMode = sizingMode,
            Axis = RequestedAxis.Both,
            RunMode = RunMode.ComputeSize,
            VerticalMarginsAreCollapsible = verticalMarginsAreCollapsible,
        }).Size;

    /// <summary>Perform a full layout on the node given the specified constraints.</summary>
    public static LayoutOutput PerformChildLayout(
        this ILayoutPartialTree tree,
        NodeId nodeId,
        Size<float?> knownDimensions,
        Size<float?> parentSize,
        Size<AvailableSpace> availableSpace,
        SizingMode sizingMode,
        Line<bool> verticalMarginsAreCollapsible) =>
        tree.ComputeChildLayout(nodeId, new LayoutInput
        {
            KnownDimensions = knownDimensions,
            KnownDimensionsAreDefinite = new(true, true),
            ParentSize = parentSize,
            AvailableSpace = availableSpace,
            SizingMode = sizingMode,
            Axis = RequestedAxis.Both,
            RunMode = RunMode.PerformLayout,
            VerticalMarginsAreCollapsible = verticalMarginsAreCollapsible,
        });

    /// <summary>Alias to <see cref="ILayoutPartialTree.ResolveCalcValue"/> with a shorter name.</summary>
    public static float Calc(this ILayoutPartialTree tree, object val, float basis) => tree.ResolveCalcValue(val, basis);
}
