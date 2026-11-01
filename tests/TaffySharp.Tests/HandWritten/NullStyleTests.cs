namespace TaffySharp.Tests.HandWritten;

// A null style used to be stored silently and only failed later, inside ComputeLayout, with a NullReferenceException
public class NullStyleTests
{
    [Fact]
    public void node_constructors_reject_null_style()
    {
        TaffyTree<string> taffy = new();
        int before = taffy.TotalNodeCount();

        Assert.Throws<ArgumentNullException>(() => taffy.NewLeaf(null!));
        Assert.Throws<ArgumentNullException>(() => taffy.NewLeafWithContext(null!, "text"));
        Assert.Throws<ArgumentNullException>(() => taffy.NewWithChildren(null!));
        Assert.Equal(before, taffy.TotalNodeCount());
    }

    [Fact]
    public void set_style_rejects_null_and_keeps_the_old_style()
    {
        TaffyTree taffy = new();
        Style style = new() { Size = Size.FromLengths(10f, 10f) };
        NodeId node = taffy.NewLeaf(style);

        Assert.Throws<ArgumentNullException>(() => taffy.SetStyle(node, null!));
        Assert.Same(style, taffy.Style(node));
    }
}
