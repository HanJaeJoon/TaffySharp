namespace TaffySharp.Tests.HandWritten;

public class RootConstraintsTests
{
    [Fact]
    public void root_with_percentage_size()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId node = taffy.NewLeaf(new Style
        {
            Size = new Size<Dimension>(Dimension.Percent(1f), Dimension.Percent(1f)),
        });

        taffy.ComputeLayout(node, new Size<AvailableSpace>(AvailableSpace.Definite(100f), AvailableSpace.Definite(200f)));
        Layout layout = taffy.Layout(node);

        Assert.Equal(100f, layout.Size.Width);
        Assert.Equal(200f, layout.Size.Height);
    }

    [Fact]
    public void root_with_no_size()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId node = taffy.NewLeaf(new Style());

        taffy.ComputeLayout(node, new Size<AvailableSpace>(AvailableSpace.Definite(100f), AvailableSpace.Definite(100f)));
        Layout layout = taffy.Layout(node);

        Assert.Equal(0f, layout.Size.Width);
        Assert.Equal(0f, layout.Size.Height);
    }

    [Fact]
    public void root_with_larger_size()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId node = taffy.NewLeaf(new Style
        {
            Size = new Size<Dimension>(Dimension.Length(200f), Dimension.Length(200f)),
        });

        taffy.ComputeLayout(node, new Size<AvailableSpace>(AvailableSpace.Definite(100f), AvailableSpace.Definite(100f)));
        Layout layout = taffy.Layout(node);

        Assert.Equal(200f, layout.Size.Width);
        Assert.Equal(200f, layout.Size.Height);
    }

    [Fact]
    public void root_padding_and_border_larger_than_definite_size()
    {
        TaffyTree tree = new(16);

        NodeId child = tree.NewLeaf(new Style());

        NodeId root = tree.NewWithChildren(
            new Style
            {
                Size = Size.FromLengths(10f, 10f),
                Padding = new Rect<LengthPercentage>(
                    LengthPercentage.Length(10f),
                    LengthPercentage.Length(10f),
                    LengthPercentage.Length(10f),
                    LengthPercentage.Length(10f)),

                Border = new Rect<LengthPercentage>(
                    LengthPercentage.Length(10f),
                    LengthPercentage.Length(10f),
                    LengthPercentage.Length(10f),
                    LengthPercentage.Length(10f)),
            },
            child);

        tree.ComputeLayout(root, Size.MaxContent);

        Layout layout = tree.Layout(root);

        Assert.Equal(40f, layout.Size.Width);
        Assert.Equal(40f, layout.Size.Height);
    }
}
