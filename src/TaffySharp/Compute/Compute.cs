namespace TaffySharp;

/// <summary>
/// The layout algorithms and the functions that drive them (Rust <c>taffy::compute</c>). The
/// algorithms work with any tree implementing the <c>ILayout*</c> interfaces; <see cref="TaffyTree{TNodeContext}"/>
/// is the built-in implementation.
/// </summary>
public static partial class Compute
{
    /// <summary>
    /// Attempts to find a cached layout for the specified node and layout inputs.
    /// Uses the provided function to compute the layout (and then stores the result in the cache) if no cached layout is found.
    /// </summary>
    public static LayoutOutput ComputeCachedLayout<TTree>(
        TTree tree,
        NodeId node,
        LayoutInput inputs,
        Func<TTree, NodeId, LayoutInput, LayoutOutput> computeUncached)
        where TTree : ICacheTree
    {
        // First we check if we have a cached result for the given input
        LayoutOutput? cacheEntry = tree.CacheGet(node, inputs);
        if (cacheEntry is LayoutOutput cached)
        {
            return cached;
        }

        LayoutOutput computedSizeAndBaselines = computeUncached(tree, node, inputs);

        // Cache result
        tree.CacheStore(node, inputs, computedSizeAndBaselines.Clone());
        return computedSizeAndBaselines;
    }

    /// <summary>
    /// Rounds the calculated layout to exact pixel values.
    /// In order to ensure that no gaps in the layout are introduced we round the cumulative position
    /// of each node and derive the rounded size from the rounded positions of its edges.
    /// </summary>
    public static void RoundLayout(IRoundTree tree, NodeId nodeId) => RoundLayoutInner(tree, nodeId, 0f, 0f);

    private static void RoundLayoutInner(IRoundTree tree, NodeId nodeId, float cumulativeX, float cumulativeY)
    {
        Layout unroundedLayout = tree.GetUnroundedLayout(nodeId);
        Layout layout = unroundedLayout;

        float parentX = cumulativeX;
        float parentY = cumulativeY;
        cumulativeX += unroundedLayout.Location.X;
        cumulativeY += unroundedLayout.Location.Y;

        layout.Location.X = MathHelpers.Round(cumulativeX) - MathHelpers.Round(parentX);
        layout.Location.Y = MathHelpers.Round(cumulativeY) - MathHelpers.Round(parentY);
        layout.Size.Width = MathHelpers.Round(cumulativeX + unroundedLayout.Size.Width) - MathHelpers.Round(cumulativeX);
        layout.Size.Height = MathHelpers.Round(cumulativeY + unroundedLayout.Size.Height) - MathHelpers.Round(cumulativeY);
        layout.ScrollbarSize.Width = MathHelpers.Round(unroundedLayout.ScrollbarSize.Width);
        layout.ScrollbarSize.Height = MathHelpers.Round(unroundedLayout.ScrollbarSize.Height);
        layout.Border.Left = MathHelpers.Round(cumulativeX + unroundedLayout.Border.Left) - MathHelpers.Round(cumulativeX);
        layout.Border.Right = MathHelpers.Round(cumulativeX + unroundedLayout.Size.Width)
            - MathHelpers.Round(cumulativeX + unroundedLayout.Size.Width - unroundedLayout.Border.Right);
        layout.Border.Top = MathHelpers.Round(cumulativeY + unroundedLayout.Border.Top) - MathHelpers.Round(cumulativeY);
        layout.Border.Bottom = MathHelpers.Round(cumulativeY + unroundedLayout.Size.Height)
            - MathHelpers.Round(cumulativeY + unroundedLayout.Size.Height - unroundedLayout.Border.Bottom);
        layout.Padding.Left = MathHelpers.Round(cumulativeX + unroundedLayout.Padding.Left) - MathHelpers.Round(cumulativeX);
        layout.Padding.Right = MathHelpers.Round(cumulativeX + unroundedLayout.Size.Width)
            - MathHelpers.Round(cumulativeX + unroundedLayout.Size.Width - unroundedLayout.Padding.Right);
        layout.Padding.Top = MathHelpers.Round(cumulativeY + unroundedLayout.Padding.Top) - MathHelpers.Round(cumulativeY);
        layout.Padding.Bottom = MathHelpers.Round(cumulativeY + unroundedLayout.Size.Height)
            - MathHelpers.Round(cumulativeY + unroundedLayout.Size.Height - unroundedLayout.Padding.Bottom);

        Rect<float> unroundedRect = unroundedLayout.ScrollableOverflowRect;
        layout.ScrollableOverflowRect.Left = MathHelpers.Round(cumulativeX + unroundedRect.Left) - MathHelpers.Round(cumulativeX);
        layout.ScrollableOverflowRect.Right = MathHelpers.Round(cumulativeX + unroundedRect.Right) - MathHelpers.Round(cumulativeX);
        layout.ScrollableOverflowRect.Top = MathHelpers.Round(cumulativeY + unroundedRect.Top) - MathHelpers.Round(cumulativeY);
        layout.ScrollableOverflowRect.Bottom = MathHelpers.Round(cumulativeY + unroundedRect.Bottom) - MathHelpers.Round(cumulativeY);

        tree.SetFinalLayout(nodeId, layout);

        // Recurse into in-flow children. Out-of-flow (absolute/fixed) children are skipped here:
        // they are instead visited via their containing block's hoisted child list below, which
        // ensures each node is visited exactly once and that its cumulative offset is accumulated
        // relative to its containing block (which its location is relative to).
        int childCount = tree.ChildCount(nodeId);
        for (int index = 0; index < childCount; index++)
        {
            NodeId child = tree.GetChildId(nodeId, index);
            if (!tree.IsOutOfFlow(child))
            {
                RoundLayoutInner(tree, child, cumulativeX, cumulativeY);
            }
        }

        // Recurse into out-of-flow boxes for which this node is the containing block
        int hoistedCount = tree.HoistedChildCount(nodeId);
        for (int index = 0; index < hoistedCount; index++)
        {
            NodeId child = tree.GetHoistedChildId(nodeId, index);
            RoundLayoutInner(tree, child, cumulativeX, cumulativeY);
        }
    }

    /// <summary>Creates a layout for this node and its children, recursively. Each hidden node has zero size and is placed at the origin.</summary>
    public static LayoutOutput ComputeHiddenLayout<TTree>(TTree tree, NodeId node)
        where TTree : ILayoutPartialTree, ICacheTree
    {
        // Clear cache and set zeroed-out layout for the node
        tree.CacheClear(node);
        tree.SetUnroundedLayout(node, Layout.WithOrder(0));

        // Perform hidden layout on all children
        int childCount = tree.ChildCount(node);
        for (int index = 0; index < childCount; index++)
        {
            NodeId childId = tree.GetChildId(node, index);
            tree.ComputeChildLayout(childId, LayoutInput.Hidden);
        }

        return LayoutOutput.Hidden;
    }
}
