namespace TaffySharp.Tests.HandWritten;

public class BorderAndPaddingTests
{
    private const string IgnoredUpstream = "Ignored upstream (#[ignore])";

    private static Rect<T> ArrToRect<T>(T[] items) => new(items[0], items[1], items[2], items[3]);

    private static Rect<LengthPercentage> SingleSide(int i)
    {
        LengthPercentage[] lengths = { LengthPercentage.Zero, LengthPercentage.Zero, LengthPercentage.Zero, LengthPercentage.Zero };
        lengths[i] = LengthPercentage.Length(10f);
        return ArrToRect(lengths);
    }

    [Fact(Skip = IgnoredUpstream)]
    public void border_on_a_single_axis_doesnt_increase_size()
    {
        for (int i = 0; i < 4; i++)
        {
            TaffyTree<TestNodeContext> taffy = new();
            NodeId node = taffy.NewLeaf(new Style { Border = SingleSide(i) });

            taffy.ComputeLayout(node, new Size<AvailableSpace>(AvailableSpace.Definite(100f), AvailableSpace.Definite(100f)));

            Layout layout = taffy.Layout(node);
            Assert.Equal(0f, layout.Size.Width * layout.Size.Height);
        }
    }

    [Fact(Skip = IgnoredUpstream)]
    public void padding_on_a_single_axis_doesnt_increase_size()
    {
        for (int i = 0; i < 4; i++)
        {
            TaffyTree<TestNodeContext> taffy = new();
            NodeId node = taffy.NewLeaf(new Style { Padding = SingleSide(i) });

            taffy.ComputeLayout(node, new Size<AvailableSpace>(AvailableSpace.Definite(100f), AvailableSpace.Definite(100f)));

            Layout layout = taffy.Layout(node);
            Assert.Equal(0f, layout.Size.Width * layout.Size.Height);
        }
    }

    [Fact(Skip = IgnoredUpstream)]
    public void border_and_padding_on_a_single_axis_doesnt_increase_size()
    {
        for (int i = 0; i < 4; i++)
        {
            TaffyTree<TestNodeContext> taffy = new();
            Rect<LengthPercentage> rect = SingleSide(i);
            NodeId node = taffy.NewLeaf(new Style { Border = rect, Padding = rect });

            taffy.ComputeLayout(node, new Size<AvailableSpace>(AvailableSpace.Definite(100f), AvailableSpace.Definite(100f)));
            Layout layout = taffy.Layout(node);
            Assert.Equal(0f, layout.Size.Width * layout.Size.Height);
        }
    }

    [Fact(Skip = IgnoredUpstream)]
    public void vertical_border_and_padding_percentage_values_use_available_space_correctly()
    {
        TaffyTree<TestNodeContext> taffy = new();

        NodeId node = taffy.NewLeaf(new Style
        {
            Padding = new Rect<LengthPercentage>(
                LengthPercentage.Percent(1f),
                LengthPercentage.Zero,
                LengthPercentage.Percent(1f),
                LengthPercentage.Zero),
        });

        taffy.ComputeLayout(node, new Size<AvailableSpace>(AvailableSpace.Definite(200f), AvailableSpace.Definite(100f)));

        Layout layout = taffy.Layout(node);
        Assert.Equal(200f, layout.Size.Width);
        Assert.Equal(200f, layout.Size.Height);
    }
}
