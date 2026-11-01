namespace TaffySharp;

public static partial class Compute
{
    /// <summary>Computes the layout of a flexbox container and its children (Rust <c>compute_flexbox_layout</c>).</summary>
    public static LayoutOutput ComputeFlexboxLayout(ILayoutFlexboxContainer tree, NodeId node, LayoutInput inputs) =>
        FlexboxAlgorithm.ComputeFlexboxLayout(tree, node, inputs);
}
