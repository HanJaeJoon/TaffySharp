namespace TaffySharp;

/// <summary>An entire tree of UI nodes. The default implementation of the layout tree interfaces, with no node context.</summary>
public sealed class TaffyTree : TaffyTree<object>
{
    public TaffyTree()
    {
    }

    public TaffyTree(int capacity)
        : base(capacity)
    {
    }
}

/// <summary>
/// An entire tree of UI nodes. Each node can carry a <typeparamref name="TNodeContext"/>, which is passed
/// to the measure function of <see cref="ComputeLayoutWithMeasure"/> for leaf nodes.
/// </summary>
public class TaffyTree<TNodeContext> : ITraverseTree, ICacheTree, IPrintTree
{
    internal sealed class NodeData
    {
        public NodeData(Style style) => Style = style;

        public Style Style;
        public Layout UnroundedLayout;
        public Layout FinalLayout;
        public bool HasContext;
        public TNodeContext? Context;
        public readonly List<NodeId> HoistedChildren = new();
        public readonly Cache Cache = new();
        public DetailedLayoutInfo DetailedLayoutInfo = TaffySharp.DetailedLayoutInfo.None;
        public List<NodeId> Children = new();
        public NodeId? Parent;

        public ClearState MarkDirty() => Cache.Clear();
    }

    internal readonly SlotMap<NodeData> Nodes;
    internal bool UseRounding = true;

    /// <summary>Creates a new TaffyTree.</summary>
    public TaffyTree()
        : this(16)
    {
    }

    /// <summary>Creates a new TaffyTree that can store <paramref name="capacity"/> nodes before reallocation.</summary>
    public TaffyTree(int capacity) => Nodes = new SlotMap<NodeData>(capacity);

    /// <summary>Enable rounding of layout values. Rounding is enabled by default.</summary>
    public void EnableRounding() => UseRounding = true;

    /// <summary>Disable rounding of layout values. Rounding is enabled by default.</summary>
    public void DisableRounding() => UseRounding = false;

    /// <summary>Creates and adds a new unattached leaf node to the tree, and returns the node of the new node.</summary>
    public NodeId NewLeaf(Style layout) => Nodes.Insert(new NodeData(layout));

    /// <summary>Creates and adds a new unattached leaf node to the tree with an associated context.</summary>
    public NodeId NewLeafWithContext(Style layout, TNodeContext context) =>
        Nodes.Insert(new NodeData(layout) { HasContext = true, Context = context });

    /// <summary>Creates and adds a new node, which may have any number of children.</summary>
    public NodeId NewWithChildren(Style layout, params NodeId[] children)
    {
        NodeId id = Nodes.Insert(new NodeData(layout) { Children = new List<NodeId>(children) });
        foreach (NodeId child in children)
        {
            Nodes[child].Parent = id;
        }

        return id;
    }

    /// <summary>Drops all nodes in the tree.</summary>
    public void Clear() => Nodes.Clear();

    /// <summary>Remove a specific node from the tree and drop it. Its children are detached, not removed.</summary>
    public NodeId Remove(NodeId node)
    {
        NodeData data = Nodes[node];
        if (data.Parent is NodeId parent)
        {
            if (Nodes.TryGetValue(parent, out NodeData parentData))
            {
                parentData.Children.RemoveAll(f => f == node);
            }

            MarkDirty(parent);
        }

        // Remove "parent" references to a node when removing that node
        foreach (NodeId child in data.Children)
        {
            Nodes[child].Parent = null;
        }

        Nodes.Remove(node);
        return node;
    }

    /// <summary>Sets the context data associated with the node. Passing <c>null</c> removes the context.</summary>
    public void SetNodeContext(NodeId node, TNodeContext? measure)
    {
        NodeData data = Nodes[node];
        if (measure is not null)
        {
            data.HasContext = true;
            data.Context = measure;
        }
        else
        {
            data.HasContext = false;
            data.Context = default;
        }

        MarkDirty(node);
    }

    /// <summary>Gets the context data associated with the node, or <c>default</c> if there is none.</summary>
    public TNodeContext? GetNodeContext(NodeId node)
    {
        NodeData data = Nodes[node];
        return data.HasContext ? data.Context : default;
    }

    /// <summary>Adds a <paramref name="child"/> node under the supplied <paramref name="parent"/>.</summary>
    public void AddChild(NodeId parent, NodeId child)
    {
        Nodes[child].Parent = parent;
        Nodes[parent].Children.Add(child);
        MarkDirty(parent);
    }

    /// <summary>Inserts a <paramref name="child"/> node at the given <paramref name="childIndex"/> under the supplied <paramref name="parent"/>, shifting all children after it to the right.</summary>
    public void InsertChildAtIndex(NodeId parent, int childIndex, NodeId child)
    {
        List<NodeId> children = Nodes[parent].Children;
        if (childIndex > children.Count)
        {
            throw ChildIndexOutOfBounds(parent, childIndex, children.Count);
        }

        Nodes[child].Parent = parent;
        children.Insert(childIndex, child);
        MarkDirty(parent);
    }

    /// <summary>Directly sets the children of the supplied <paramref name="parent"/>.</summary>
    public void SetChildren(NodeId parent, params NodeId[] children)
    {
        // Remove node as parent from all its current children.
        foreach (NodeId child in Nodes[parent].Children)
        {
            Nodes[child].Parent = null;
        }

        // Build up relation node <-> child
        foreach (NodeId child in children)
        {
            // Remove child from previous parent
            if (Nodes[child].Parent is NodeId previousParent)
            {
                RemoveChild(previousParent, child);
            }

            Nodes[child].Parent = parent;
        }

        List<NodeId> parentChildren = Nodes[parent].Children;
        parentChildren.Clear();
        parentChildren.AddRange(children);
        MarkDirty(parent);
    }

    /// <summary>Removes the <paramref name="child"/> of the parent node. The child is not removed from the tree entirely.</summary>
    public NodeId RemoveChild(NodeId parent, NodeId child)
    {
        int index = Nodes[parent].Children.IndexOf(child);
        if (index < 0)
        {
            throw new ArgumentException($"{child} is not a child of {parent}", nameof(child));
        }

        return RemoveChildAtIndex(parent, index);
    }

    /// <summary>Removes the child at the given index from the parent. The child is not removed from the tree entirely.</summary>
    public NodeId RemoveChildAtIndex(NodeId parent, int childIndex)
    {
        List<NodeId> children = Nodes[parent].Children;
        if (childIndex < 0 || childIndex >= children.Count)
        {
            throw ChildIndexOutOfBounds(parent, childIndex, children.Count);
        }

        NodeId child = children[childIndex];
        children.RemoveAt(childIndex);
        Nodes[child].Parent = null;
        MarkDirty(parent);
        return child;
    }

    /// <summary>Removes <paramref name="count"/> children starting at <paramref name="start"/> from the parent. The children are not removed from the tree entirely.</summary>
    public void RemoveChildrenRange(NodeId parent, int start, int count)
    {
        List<NodeId> children = Nodes[parent].Children;
        foreach (NodeId child in children.GetRange(start, count))
        {
            Nodes[child].Parent = null;
        }

        children.RemoveRange(start, count);
        MarkDirty(parent);
    }

    /// <summary>Replaces the child at the given <paramref name="childIndex"/> from the parent node with the new child node. The old child is not removed from the tree entirely.</summary>
    public NodeId ReplaceChildAtIndex(NodeId parent, int childIndex, NodeId newChild)
    {
        List<NodeId> children = Nodes[parent].Children;
        if (childIndex < 0 || childIndex >= children.Count)
        {
            throw ChildIndexOutOfBounds(parent, childIndex, children.Count);
        }

        Nodes[newChild].Parent = parent;
        NodeId oldChild = children[childIndex];
        children[childIndex] = newChild;
        Nodes[oldChild].Parent = null;
        MarkDirty(parent);
        return oldChild;
    }

    /// <summary>Returns the child node of the parent node at the provided <paramref name="childIndex"/>.</summary>
    public NodeId ChildAtIndex(NodeId parent, int childIndex)
    {
        List<NodeId> children = Nodes[parent].Children;
        if (childIndex < 0 || childIndex >= children.Count)
        {
            throw ChildIndexOutOfBounds(parent, childIndex, children.Count);
        }

        return children[childIndex];
    }

    /// <summary>Returns the total number of nodes in the tree.</summary>
    public int TotalNodeCount() => Nodes.Count;

    /// <summary>Returns the node of the parent of <paramref name="childId"/>, if it exists.</summary>
    public NodeId? Parent(NodeId childId) => Nodes[childId].Parent;

    /// <summary>Returns a list of children that belong to the parent node.</summary>
    public List<NodeId> Children(NodeId parent) => new(Nodes[parent].Children);

    /// <summary>Returns the out-of-flow boxes for which the node is the containing block.</summary>
    public IReadOnlyList<NodeId> HoistedChildren(NodeId node) => Nodes[node].HoistedChildren;

    /// <summary>Sets the style of the provided node.</summary>
    public void SetStyle(NodeId node, Style style)
    {
        Nodes[node].Style = style;
        MarkDirty(node);
    }

    /// <summary>Gets the style of the provided node. Call <see cref="MarkDirty"/> after mutating it in place.</summary>
    public Style Style(NodeId node) => Nodes[node].Style;

    /// <summary>Return this node's layout (rounded unless rounding is disabled).</summary>
    public Layout Layout(NodeId node) => UseRounding ? Nodes[node].FinalLayout : Nodes[node].UnroundedLayout;

    /// <summary>Returns this node's unrounded layout.</summary>
    public Layout UnroundedLayout(NodeId node) => Nodes[node].UnroundedLayout;

    /// <summary>Get the detailed layout information (only grid containers have any).</summary>
    public DetailedLayoutInfo DetailedLayoutInfo(NodeId nodeId) => Nodes[nodeId].DetailedLayoutInfo;

    /// <summary>Marks the layout of this node and its ancestors as outdated.</summary>
    public void MarkDirty(NodeId node)
    {
        NodeId? current = node;
        while (current is NodeId key && Nodes.TryGetValue(key, out NodeData data))
        {
            if (data.MarkDirty() == ClearState.AlreadyEmpty)
            {
                // Node was already marked as dirty. No need to visit ancestors
                // as they should be marked as dirty already.
                break;
            }

            current = data.Parent;
        }
    }

    /// <summary>Indicates whether the layout of this node needs to be recomputed.</summary>
    public bool Dirty(NodeId node) => Nodes[node].Cache.IsEmpty();

    /// <summary>Updates the stored layout of the provided node and its children, measuring leaves with <paramref name="measureFunction"/>.</summary>
    public void ComputeLayoutWithMeasure(
        NodeId nodeId,
        Size<AvailableSpace> availableSpace,
        Func<LayoutInput, NodeId, TNodeContext?, Style, LayoutOutput> measureFunction)
    {
        bool useRounding = UseRounding;
        TaffyView<TNodeContext> taffyView = new(this, measureFunction);
        Compute.ComputeRootLayout(taffyView, nodeId, availableSpace);
        if (useRounding)
        {
            Compute.RoundLayout(taffyView, nodeId);
        }
    }

    /// <summary>Updates the stored layout of the provided node and its children (leaves have a zero content size).</summary>
    public void ComputeLayout(NodeId node, Size<AvailableSpace> availableSpace) =>
        ComputeLayoutWithMeasure(node, availableSpace, static (inputs, _, _, style) =>
            Compute.ComputeLeafLayout(inputs, style, static (_, _) => 0f, static (_, _) => Size.Zero));

    /// <summary>Prints a debug representation of the tree's layout.</summary>
    public void PrintTree(NodeId root) => TreePrinter.PrintTree(this, root);

    private static ArgumentOutOfRangeException ChildIndexOutOfBounds(NodeId parent, int childIndex, int childCount) =>
        new(nameof(childIndex), $"Index (is {childIndex}) should be < child_count ({childCount}) for parent node {parent}");

    // ITraversePartialTree
    public IEnumerable<NodeId> ChildIds(NodeId parentNodeId) => Nodes[parentNodeId].Children;

    public int ChildCount(NodeId parentNodeId) => Nodes[parentNodeId].Children.Count;

    public NodeId GetChildId(NodeId parentNodeId, int childIndex) => Nodes[parentNodeId].Children[childIndex];

    // ICacheTree
    LayoutOutput? ICacheTree.CacheGet(NodeId nodeId, in LayoutInput input) => Nodes[nodeId].Cache.Get(input);

    void ICacheTree.CacheStore(NodeId nodeId, in LayoutInput input, LayoutOutput layoutOutput) => Nodes[nodeId].Cache.Store(input, layoutOutput);

    void ICacheTree.CacheClear(NodeId nodeId) => Nodes[nodeId].Cache.Clear();

    // IPrintTree
    string IPrintTree.GetDebugLabel(NodeId nodeId)
    {
        NodeData node = Nodes[nodeId];
        Display display = node.Style.Display;
        int numChildren = ChildCount(nodeId);
        if (display == Display.None)
        {
            return "NONE";
        }

        if (numChildren == 0)
        {
            return "LEAF";
        }

        return display switch
        {
            Display.Block => "BLOCK",
            Display.FlowRoot => "FLOW-ROOT",
            Display.Flex => node.Style.FlexDirection.IsRow() ? "FLEX ROW" : "FLEX COL",
            _ => "GRID",
        };
    }

    Layout IPrintTree.GetFinalLayout(NodeId nodeId) => UseRounding ? Nodes[nodeId].FinalLayout : Nodes[nodeId].UnroundedLayout;
}

/// <summary>View over a <see cref="TaffyTree{TNodeContext}"/> and a measure function that implements the layout interfaces.</summary>
internal sealed class TaffyView<TNodeContext> : ILayoutContainingBlock, ICacheTree, ILayoutBlockContainer,
    ILayoutFlexboxContainer, ILayoutGridContainer, IRoundTree
{
    private readonly TaffyTree<TNodeContext> _taffy;
    private readonly Func<LayoutInput, NodeId, TNodeContext?, Style, LayoutOutput> _measureFunction;

    public TaffyView(TaffyTree<TNodeContext> taffy, Func<LayoutInput, NodeId, TNodeContext?, Style, LayoutOutput> measureFunction)
    {
        _taffy = taffy;
        _measureFunction = measureFunction;
    }

    private TaffyTree<TNodeContext>.NodeData Node(NodeId id) => _taffy.Nodes[id];

    private LayoutOutput ComputeChildLayout(NodeId nodeId, LayoutInput inputs, BlockContext? blockCtx)
    {
        // If RunMode is PerformHiddenLayout then this indicates that an ancestor node is Display.None
        // and thus that we should lay out this node using hidden layout regardless of it's own display style.
        if (inputs.RunMode == RunMode.PerformHiddenLayout)
        {
            // RoundLayout reaches out-of-flow boxes only through their containing block's
            // hoisted child list, and a box inside a hidden subtree is in no such list. Write the
            // final layout here so that it does not keep the layout from an earlier visible pass.
            Node(nodeId).FinalLayout = Layout.WithOrder(0);
            return Compute.ComputeHiddenLayout(this, nodeId);
        }

        // Check the cache for an entry matching the node and inputs, computing (and caching) a new result if there is none
        return Compute.ComputeCachedLayout(this, nodeId, inputs, (tree, node, nodeInputs) =>
        {
            TaffyTree<TNodeContext>.NodeData data = tree.Node(node);
            Display displayMode = data.Style.Display;
            bool hasChildren = data.Children.Count > 0;

            // Dispatch to a layout algorithm based on the node's display style and whether the node has children or not.
            LayoutOutput output;
            if (displayMode == Display.None)
            {
                output = Compute.ComputeHiddenLayout(tree, node);
            }
            else if (!hasChildren)
            {
                TNodeContext? nodeContext = data.HasContext ? data.Context : default;
                output = tree._measureFunction(nodeInputs, node, nodeContext, data.Style);
            }
            else
            {
                output = displayMode switch
                {
                    Display.Block => Compute.ComputeBlockLayout(tree, node, nodeInputs, blockCtx),
                    Display.FlowRoot => Compute.ComputeBlockLayout(tree, node, nodeInputs, null),
                    Display.Flex => Compute.ComputeFlexboxLayout(tree, node, nodeInputs),
                    _ => Compute.ComputeGridLayout(tree, node, nodeInputs),
                };
            }

            // Lay out any out-of-flow candidates for which this node is the containing block.
            // The rest bubble up via output.OofCandidates. This runs inside the cache-miss
            // callback so that cached outputs already contain the processed candidate list.
            // Only full layout passes run it: measure passes must not write hoisted box layouts
            // (which would not be rewritten if the final layout pass is a cache hit).
            if (nodeInputs.RunMode == RunMode.PerformLayout)
            {
                Compute.ComputeOofLayout(tree, node, ref output);
            }

            return output;
        });
    }

    // ITraversePartialTree
    public IEnumerable<NodeId> ChildIds(NodeId parentNodeId) => _taffy.ChildIds(parentNodeId);

    public int ChildCount(NodeId parentNodeId) => _taffy.ChildCount(parentNodeId);

    public NodeId GetChildId(NodeId parentNodeId, int childIndex) => _taffy.GetChildId(parentNodeId, childIndex);

    // ILayoutPartialTree
    public ICoreStyle GetCoreContainerStyle(NodeId nodeId) => Node(nodeId).Style;

    public void SetUnroundedLayout(NodeId nodeId, in Layout layout) => Node(nodeId).UnroundedLayout = layout;

    public float ResolveCalcValue(object val, float basis) => 0f;

    public LayoutOutput ComputeChildLayout(NodeId nodeId, LayoutInput inputs) => ComputeChildLayout(nodeId, inputs, null);

    // ILayoutContainingBlock
    public IOofItemStyle GetOofItemStyle(NodeId nodeId) => Node(nodeId).Style;

    public void ClearHoistedChildren(NodeId nodeId) => Node(nodeId).HoistedChildren.Clear();

    public void AddHoistedChildren(NodeId nodeId, IReadOnlyList<NodeId> hoisted) => Node(nodeId).HoistedChildren.AddRange(hoisted);

    public DetailedLayoutInfo GetDetailedLayoutInfo(NodeId nodeId) => Node(nodeId).DetailedLayoutInfo;

    // ICacheTree
    public LayoutOutput? CacheGet(NodeId nodeId, in LayoutInput input) => Node(nodeId).Cache.Get(input);

    public void CacheStore(NodeId nodeId, in LayoutInput input, LayoutOutput layoutOutput) => Node(nodeId).Cache.Store(input, layoutOutput);

    public void CacheClear(NodeId nodeId) => Node(nodeId).Cache.Clear();

    // ILayoutBlockContainer
    public IBlockContainerStyle GetBlockContainerStyle(NodeId nodeId) => Node(nodeId).Style;

    public IBlockItemStyle GetBlockChildStyle(NodeId childNodeId) => Node(childNodeId).Style;

    public LayoutOutput ComputeBlockChildLayout(NodeId nodeId, LayoutInput inputs, BlockContext? blockCtx) => ComputeChildLayout(nodeId, inputs, blockCtx);

    // ILayoutFlexboxContainer
    public IFlexboxContainerStyle GetFlexboxContainerStyle(NodeId nodeId) => Node(nodeId).Style;

    public IFlexboxItemStyle GetFlexboxChildStyle(NodeId childNodeId) => Node(childNodeId).Style;

    // ILayoutGridContainer
    public IGridContainerStyle GetGridContainerStyle(NodeId nodeId) => Node(nodeId).Style;

    public IGridItemStyle GetGridChildStyle(NodeId childNodeId) => Node(childNodeId).Style;

    public void SetDetailedGridInfo(NodeId nodeId, DetailedGridInfo detailedGridInfo) =>
        Node(nodeId).DetailedLayoutInfo = DetailedLayoutInfo.FromGrid(detailedGridInfo);

    // IRoundTree
    public Layout GetUnroundedLayout(NodeId nodeId) => Node(nodeId).UnroundedLayout;

    public void SetFinalLayout(NodeId nodeId, in Layout layout) => Node(nodeId).FinalLayout = layout;

    public bool IsOutOfFlow(NodeId nodeId)
    {
        Style style = Node(nodeId).Style;
        return style.Position.IsOutOfFlow() && style.Display != Display.None;
    }

    public int HoistedChildCount(NodeId nodeId) => Node(nodeId).HoistedChildren.Count;

    public NodeId GetHoistedChildId(NodeId nodeId, int index) => Node(nodeId).HoistedChildren[index];
}
