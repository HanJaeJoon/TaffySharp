namespace TaffySharp.Tests.HandWritten;

public class MeasureTests
{
    private static TestNodeContext HundredHundred() => TestNodeContext.Fixed(100f, 100f);

    private static TestNodeContext HundredFifty() => TestNodeContext.Fixed(100f, 50f);

    private static TestNodeContext FiftyFifty() => TestNodeContext.Fixed(50f, 50f);

    [Fact]
    public void measure_root()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId node = taffy.NewLeafWithContext(new Style(), HundredHundred());
        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);

        Assert.Equal(100f, taffy.Layout(node).Size.Width);
        Assert.Equal(100f, taffy.Layout(node).Size.Height);
    }

    [Fact]
    public void measure_child()
    {
        TaffyTree<TestNodeContext> taffy = new();

        NodeId child = taffy.NewLeafWithContext(new Style(), HundredHundred());

        NodeId node = taffy.NewWithChildren(new Style(), child);
        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);

        Assert.Equal(100f, taffy.Layout(node).Size.Width);
        Assert.Equal(100f, taffy.Layout(node).Size.Height);

        Assert.Equal(100f, taffy.Layout(child).Size.Width);
        Assert.Equal(100f, taffy.Layout(child).Size.Height);
    }

    [Fact]
    public void measure_child_constraint()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeafWithContext(new Style(), HundredHundred());

        NodeId node = taffy.NewWithChildren(
            new Style { Size = new Size<Dimension>(Dimension.Length(50f), Dimension.Auto) },
            child);

        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);

        // Parent
        Assert.Equal(50f, taffy.Layout(node).Size.Width);
        Assert.Equal(100f, taffy.Layout(node).Size.Height);
        // Child
        Assert.Equal(100f, taffy.Layout(child).Size.Width);
        Assert.Equal(100f, taffy.Layout(child).Size.Height);
    }

    [Fact]
    public void measure_child_constraint_padding_parent()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeafWithContext(new Style(), HundredHundred());

        NodeId node = taffy.NewWithChildren(
            new Style
            {
                Size = new Size<Dimension>(Dimension.Length(50f), Dimension.Auto),
                Padding = new Rect<LengthPercentage>(
                    LengthPercentage.Length(10f),
                    LengthPercentage.Length(10f),
                    LengthPercentage.Length(10f),
                    LengthPercentage.Length(10f)),
            },
            child);
        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);

        Assert.Equal(0f, taffy.Layout(node).Location.X);
        Assert.Equal(0f, taffy.Layout(node).Location.Y);
        Assert.Equal(50f, taffy.Layout(node).Size.Width);
        Assert.Equal(120f, taffy.Layout(node).Size.Height);

        Assert.Equal(10f, taffy.Layout(child).Location.X);
        Assert.Equal(10f, taffy.Layout(child).Location.Y);
        Assert.Equal(100f, taffy.Layout(child).Size.Width);
        Assert.Equal(100f, taffy.Layout(child).Size.Height);
    }

    [Fact]
    public void measure_child_with_flex_grow()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child0 = taffy.NewLeaf(new Style { Size = Size.FromLengths(50f, 50f) });

        NodeId child1 = taffy.NewLeafWithContext(new Style { FlexGrow = 1f }, FiftyFifty());

        NodeId node = taffy.NewWithChildren(
            new Style { Size = new Size<Dimension>(Dimension.Length(100f), Dimension.Auto) },
            child0,
            child1);

        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);

        Assert.Equal(50f, taffy.Layout(child1).Size.Width);
        Assert.Equal(50f, taffy.Layout(child1).Size.Height);
    }

    [Fact]
    public void measure_child_with_flex_shrink()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child0 = taffy.NewLeaf(new Style
        {
            Size = Size.FromLengths(50f, 50f),
            FlexShrink = 0f,
        });

        NodeId child1 = taffy.NewLeafWithContext(new Style(), HundredFifty());

        NodeId node = taffy.NewWithChildren(
            new Style { Size = new Size<Dimension>(Dimension.Length(100f), Dimension.Auto) },
            child0,
            child1);

        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);

        Assert.Equal(100f, taffy.Layout(child1).Size.Width);
        Assert.Equal(50f, taffy.Layout(child1).Size.Height);
    }

    [Fact]
    public void remeasure_child_after_growing()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child0 = taffy.NewLeaf(new Style { Size = Size.FromLengths(50f, 50f) });

        NodeId child1 = taffy.NewLeafWithContext(new Style { FlexGrow = 1f }, TestNodeContext.AspectRatio(10f, 2f));

        NodeId node = taffy.NewWithChildren(
            new Style
            {
                Size = new Size<Dimension>(Dimension.Length(100f), Dimension.Auto),
                AlignItems = AlignItems.Start,
            },
            child0,
            child1);

        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);

        Assert.Equal(50f, taffy.Layout(child1).Size.Width);
        Assert.Equal(100f, taffy.Layout(child1).Size.Height);
    }

    [Fact]
    public void remeasure_child_after_shrinking()
    {
        TaffyTree<TestNodeContext> taffy = new();

        NodeId child0 = taffy.NewLeaf(new Style
        {
            Size = Size.FromLengths(50f, 50f),
            FlexShrink = 0f,
        });

        NodeId child1 = taffy.NewLeafWithContext(new Style(), TestNodeContext.AspectRatio(100f, 2f));

        NodeId node = taffy.NewWithChildren(
            new Style
            {
                Size = new Size<Dimension>(Dimension.Length(100f), Dimension.Auto),
                AlignItems = AlignItems.Start,
            },
            child0,
            child1);

        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);

        Assert.Equal(100f, taffy.Layout(child1).Size.Width);
        Assert.Equal(200f, taffy.Layout(child1).Size.Height);
    }

    [Fact]
    public void remeasure_child_after_stretching()
    {
        TaffyTree taffy = new();

        static LayoutOutput CustomMeasureFunction(LayoutInput inputs, NodeId nodeId, object? nodeContext, Style style) =>
            Compute.ComputeLeafLayout(
                inputs,
                style,
                (_, _) => 0f,
                (knownDimensions, availableSpace) =>
                {
                    float height = knownDimensions.Height ?? 50f;
                    float width = knownDimensions.Width ?? height;
                    return new Size<float>(width, height);
                });

        NodeId child = taffy.NewLeafWithContext(new Style(), new object());

        NodeId node = taffy.NewWithChildren(
            new Style { Size = Size.FromLengths(100f, 100f) },
            child);

        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, CustomMeasureFunction);

        Assert.Equal(100f, taffy.Layout(child).Size.Width);
        Assert.Equal(100f, taffy.Layout(child).Size.Height);
    }

    [Fact]
    public void width_overrides_measure()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeafWithContext(
            new Style { Size = new Size<Dimension>(Dimension.Length(50f), Dimension.Auto) },
            HundredHundred());

        NodeId node = taffy.NewWithChildren(new Style(), child);
        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);

        Assert.Equal(50f, taffy.Layout(child).Size.Width);
        Assert.Equal(100f, taffy.Layout(child).Size.Height);
    }

    [Fact]
    public void height_overrides_measure()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeafWithContext(
            new Style { Size = new Size<Dimension>(Dimension.Auto, Dimension.Length(50f)) },
            HundredHundred());

        NodeId node = taffy.NewWithChildren(new Style(), child);
        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);

        Assert.Equal(100f, taffy.Layout(child).Size.Width);
        Assert.Equal(50f, taffy.Layout(child).Size.Height);
    }

    [Fact]
    public void flex_basis_overrides_measure()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child0 = taffy.NewLeaf(new Style { FlexBasis = Dimension.Length(50f), FlexGrow = 1f });

        NodeId child1 = taffy.NewLeafWithContext(
            new Style { FlexBasis = Dimension.Length(50f), FlexGrow = 1f },
            HundredHundred());

        NodeId node = taffy.NewWithChildren(
            new Style { Size = Size.FromLengths(200f, 100f) },
            child0,
            child1);

        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);

        Assert.Equal(100f, taffy.Layout(child0).Size.Width);
        Assert.Equal(100f, taffy.Layout(child0).Size.Height);
        Assert.Equal(100f, taffy.Layout(child1).Size.Width);
        Assert.Equal(100f, taffy.Layout(child1).Size.Height);
    }

    [Fact]
    public void stretch_overrides_measure()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeafWithContext(new Style(), FiftyFifty());

        NodeId node = taffy.NewWithChildren(
            new Style { Size = Size.FromLengths(100f, 100f) },
            child);

        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);

        Assert.Equal(50f, taffy.Layout(child).Size.Width);
        Assert.Equal(100f, taffy.Layout(child).Size.Height);
    }

    [Fact]
    public void explicit_min_main_size_skips_min_content_measurement()
    {
        static LayoutOutput AssertNotMinContent(LayoutInput inputs, NodeId nodeId, object? context, Style style)
        {
            Assert.NotEqual(AvailableSpace.MinContent, inputs.AvailableSpace.Width);
            return LayoutOutput.FromOuterSize(new Size<float>(10f, 10f));
        }

        TaffyTree taffy = new();
        NodeId child = taffy.NewLeafWithContext(
            new Style { MinSize = new Size<LengthPercentageAuto>(LengthPercentageAuto.Length(10f), LengthPercentageAuto.Auto) },
            new object());
        NodeId root = taffy.NewWithChildren(new Style(), child);

        taffy.ComputeLayoutWithMeasure(root, Size.MaxContent, AssertNotMinContent);
    }

    [Fact]
    public void measure_absolute_child()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeafWithContext(new Style { Position = Position.Absolute }, FiftyFifty());

        NodeId node = taffy.NewWithChildren(
            new Style { Size = Size.FromLengths(100f, 100f) },
            child);

        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);

        Assert.Equal(50f, taffy.Layout(child).Size.Width);
        Assert.Equal(50f, taffy.Layout(child).Size.Height);
    }

    [Fact]
    public void ignore_invalid_measure()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeaf(new Style { FlexGrow = 1f });

        NodeId node = taffy.NewWithChildren(
            new Style { Size = Size.FromLengths(100f, 100f) },
            child);

        taffy.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);

        Assert.Equal(100f, taffy.Layout(child).Size.Width);
        Assert.Equal(100f, taffy.Layout(child).Size.Height);
    }
}
