namespace TaffySharp.Tests.HandWritten;

public class MinMaxOverridesTests
{
    [Fact]
    public void min_overrides_max()
    {
        TaffyTree<TestNodeContext> taffy = new();

        NodeId child = taffy.NewLeaf(new Style
        {
            Size = Size.FromLengths(50f, 50f),
            MinSize = new Size<LengthPercentageAuto>(LengthPercentageAuto.Length(100f), LengthPercentageAuto.Length(100f)),
            MaxSize = new Size<LengthPercentageAuto>(LengthPercentageAuto.Length(10f), LengthPercentageAuto.Length(10f)),
        });

        taffy.ComputeLayout(child, new Size<AvailableSpace>(AvailableSpace.Definite(100f), AvailableSpace.Definite(100f)));

        Assert.Equal(new Size<float>(100f, 100f), taffy.Layout(child).Size);
    }

    [Fact]
    public void max_overrides_size()
    {
        TaffyTree<TestNodeContext> taffy = new();

        NodeId child = taffy.NewLeaf(new Style
        {
            Size = Size.FromLengths(50f, 50f),
            MaxSize = new Size<LengthPercentageAuto>(LengthPercentageAuto.Length(10f), LengthPercentageAuto.Length(10f)),
        });

        taffy.ComputeLayout(child, new Size<AvailableSpace>(AvailableSpace.Definite(100f), AvailableSpace.Definite(100f)));

        Assert.Equal(new Size<float>(10f, 10f), taffy.Layout(child).Size);
    }

    [Fact]
    public void min_overrides_size()
    {
        TaffyTree<TestNodeContext> taffy = new();

        NodeId child = taffy.NewLeaf(new Style
        {
            Size = Size.FromLengths(50f, 50f),
            MinSize = new Size<LengthPercentageAuto>(LengthPercentageAuto.Length(100f), LengthPercentageAuto.Length(100f)),
        });

        taffy.ComputeLayout(child, new Size<AvailableSpace>(AvailableSpace.Definite(100f), AvailableSpace.Definite(100f)));

        Assert.Equal(new Size<float>(100f, 100f), taffy.Layout(child).Size);
    }
}
