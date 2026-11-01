namespace TaffySharp.Tests.HandWritten;

public class RoundingTests
{
    [Fact]
    public void rounding_doesnt_leave_gaps()
    {
        // First create an instance of TaffyTree
        TaffyTree<TestNodeContext> taffy = new();

        Size<Dimension> wSquare = Size.FromLengths(100.3f, 100.3f);
        NodeId childA = taffy.NewLeaf(new Style { Size = wSquare });
        NodeId childB = taffy.NewLeaf(new Style { Size = wSquare });

        NodeId rootNode = taffy.NewWithChildren(
            new Style
            {
                Size = Size.FromLengths(963.3333f, 1000f),
                JustifyContent = AlignContent.Center,
            },
            childA,
            childB);

        taffy.ComputeLayout(rootNode, Size.MaxContent);
        taffy.PrintTree(rootNode);

        Layout layoutA = taffy.Layout(childA);
        Layout layoutB = taffy.Layout(childB);
        Assert.Equal(layoutB.Location.X, layoutA.Location.X + layoutA.Size.Width);
    }
}
