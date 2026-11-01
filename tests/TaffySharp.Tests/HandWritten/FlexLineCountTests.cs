namespace TaffySharp.Tests.HandWritten;

public class FlexLineCountTests
{
    private const string Text = "HHHHHHHHHH\u200BHHHHHHHHHH\u200BHHHHHHHHHH";

    private static Style ContainerStyle(FlexWrap flexWrap) => new()
    {
        Display = Display.Flex,
        FlexDirection = FlexDirection.Column,
        FlexWrap = flexWrap,
        FlexLineCount = 2,
        Size = new Size<Dimension>(Dimension.Length(210f), Dimension.Length(100f)),
        Gap = new Size<LengthPercentage>(LengthPercentage.Length(10f), LengthPercentage.Zero),
        AlignItems = AlignItems.Start,
        AlignContent = AlignContent.Start,
    };

    private static Size<float> MeasureTextChild(FlexWrap flexWrap)
    {
        TaffyTree<TestNodeContext> taffy = new();
        TestNodeContext text = TestNodeContext.AhemText(Text, WritingMode.Horizontal);
        NodeId child = taffy.NewLeafWithContext(new Style(), text);
        NodeId container = taffy.NewWithChildren(ContainerStyle(flexWrap), child);
        taffy.ComputeLayoutWithMeasure(container, Size.MaxContent, TestHelpers.MeasureFunction);
        return taffy.Layout(child).Size;
    }

    // A multi-line container with `flex-line-count: 2` measures content against the cross-axis
    // available space divided between the requested number of lines (after subtracting the
    // cross-axis gaps): (210 - 10) / 2 = 100.
    [Fact]
    public void wrap_line_count_divides_cross_available_space()
    {
        Assert.Equal(new Size<float>(100f, 30f), MeasureTextChild(FlexWrap.Wrap));
    }

    [Fact]
    public void wrap_reverse_line_count_divides_cross_available_space()
    {
        Assert.Equal(new Size<float>(100f, 30f), MeasureTextChild(FlexWrap.WrapReverse));
    }

    // `flex-line-count` has no effect on `nowrap` containers.
    [Fact]
    public void nowrap_line_count_has_no_effect()
    {
        Assert.Equal(new Size<float>(210f, 20f), MeasureTextChild(FlexWrap.NoWrap));
    }

    private static Size<float> MeasureFitContentChild(FlexWrap flexWrap)
    {
        TaffyTree<TestNodeContext> taffy = new();
        TestNodeContext text = TestNodeContext.AhemText(Text, WritingMode.Horizontal);
        Style childStyle = new() { Size = new Size<Dimension>(Dimension.FitContent, Dimension.Auto) };
        NodeId child = taffy.NewLeafWithContext(childStyle, text);
        NodeId container = taffy.NewWithChildren(ContainerStyle(flexWrap), child);
        taffy.ComputeLayoutWithMeasure(container, Size.MaxContent, TestHelpers.MeasureFunction);
        return taffy.Layout(child).Size;
    }

    // A bare `fit-content` cross size is also measured against the divided cross-axis
    // available space, not the full container cross size.
    [Fact]
    public void wrap_line_count_divides_fit_content_cross_size()
    {
        Assert.Equal(new Size<float>(100f, 30f), MeasureFitContentChild(FlexWrap.Wrap));
    }

    [Fact]
    public void nowrap_line_count_does_not_divide_fit_content_cross_size()
    {
        Assert.Equal(new Size<float>(210f, 20f), MeasureFitContentChild(FlexWrap.NoWrap));
    }
}
