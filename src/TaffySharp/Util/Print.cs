namespace TaffySharp;

/// <summary>Prints a debug representation of a computed layout tree.</summary>
public static class TreePrinter
{
    /// <summary>Prints a debug representation of the computed layout for a tree of nodes, starting with the passed root node, to the console.</summary>
    public static void PrintTree(IPrintTree tree, NodeId root)
    {
        // Buffer into a string so that output can be captured in tests
        using StringWriter writer = new();
        WriteTree(writer, tree, root);
        Console.WriteLine(writer.ToString());
    }

    /// <summary>Writes a debug representation of the computed layout for a tree of nodes, starting with the passed root node.</summary>
    public static void WriteTree(TextWriter writer, IPrintTree tree, NodeId root)
    {
        writer.WriteLine("TREE");
        WriteNode(writer, tree, root, false, string.Empty);
    }

    private static void WriteNode(TextWriter writer, IPrintTree tree, NodeId nodeId, bool hasSibling, string linesString)
    {
        Layout layout = tree.GetFinalLayout(nodeId);
        string display = tree.GetDebugLabel(nodeId);
        int numChildren = tree.ChildCount(nodeId);
        string forkString = hasSibling ? "|-- " : "`-- ";
        Rect<float> overflow = layout.ScrollableOverflowRect;
        writer.WriteLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}{1} {2} [x: {3,-4} y: {4,-4} w: {5,-4} h: {6,-4} overflow: l:{7} r:{8} t:{9} b:{10}, border: l:{11} r:{12} t:{13} b:{14}, padding: l:{15} r:{16} t:{17} b:{18}] ({19})",
            linesString, forkString, display,
            layout.Location.X, layout.Location.Y, layout.Size.Width, layout.Size.Height,
            overflow.Left, overflow.Right, overflow.Top, overflow.Bottom,
            layout.Border.Left, layout.Border.Right, layout.Border.Top, layout.Border.Bottom,
            layout.Padding.Left, layout.Padding.Right, layout.Padding.Top, layout.Padding.Bottom,
            nodeId));
        string bar = hasSibling ? "|   " : "    ";
        string newString = linesString + bar;

        // Recurse into children
        int index = 0;
        foreach (NodeId child in tree.ChildIds(nodeId))
        {
            WriteNode(writer, tree, child, index < numChildren - 1, newString);
            index++;
        }
    }
}
