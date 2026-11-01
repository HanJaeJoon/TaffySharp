namespace TaffySharp.Tests.HandWritten;

public class NegativeAvailableSpaceTests
{
    // Lays out a 20px wide container of the given display type with a single leaf whose
    // horizontal margins (40px) exceed the container width. Returns the leaf's final width
    // and every LayoutInput with a negative known dimension or available space that reached
    // the leaf's measure function.
    private static (float, List<string>) LayoutWithOversizedMargins(Display display, FlexDirection flexDirection)
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId leaf = taffy.NewLeafWithContext(
            new Style
            {
                Margin = new Rect<LengthPercentageAuto>(
                    LengthPercentageAuto.Length(30f),
                    LengthPercentageAuto.Length(10f),
                    LengthPercentageAuto.Zero,
                    LengthPercentageAuto.Zero),
                Size = new Size<Dimension>(Dimension.Auto, Dimension.Auto),
            },
            TestNodeContext.Zero());
        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = display,
                FlexDirection = flexDirection,
                Size = new Size<Dimension>(Dimension.Length(20f), Dimension.Auto),
                GridTemplateColumns = new List<GridTemplateComponent> { TrackSizingFunction.Length(20f) },
            },
            leaf);

        List<string> negativeInputs = new();
        taffy.ComputeLayoutWithMeasure(
            root,
            Size.MaxContent,
            (LayoutInput inputs, NodeId nodeId, TestNodeContext? context, Style style) =>
            {
                Size<float?> known = inputs.KnownDimensions;
                Size<AvailableSpace> avail = inputs.AvailableSpace;
                Func<float?, bool> isNegative = value => value is float v && v < 0f;
                Func<AvailableSpace, bool> isNegativeAvail = value => value.IsDefinite && value.Value < 0f;
                if (isNegative(known.Width)
                    || isNegative(known.Height)
                    || isNegativeAvail(avail.Width)
                    || isNegativeAvail(avail.Height))
                {
                    negativeInputs.Add($"known={known} available={avail}");
                }

                LayoutOutput output = TestHelpers.MeasureFunction(inputs, nodeId, context, style);
                return output;
            });

        return (taffy.Layout(leaf).Size.Width, negativeInputs);
    }

    private static void AssertFlooredAtZero(Display display, FlexDirection flexDirection)
    {
        (float leafWidth, List<string> negativeInputs) = LayoutWithOversizedMargins(display, flexDirection);
        Assert.True(negativeInputs.Count == 0, $"leaf received negative layout inputs: [{string.Join(", ", negativeInputs)}]");
        Assert.Equal(0f, leafWidth);
    }

    [Fact]
    public void block_child_margins_exceed_container_width()
    {
        AssertFlooredAtZero(Display.Block, FlexDirection.Row);
    }

    [Fact]
    public void flex_column_child_margins_exceed_container_width()
    {
        AssertFlooredAtZero(Display.Flex, FlexDirection.Column);
    }

    [Fact]
    public void grid_child_margins_exceed_container_width()
    {
        AssertFlooredAtZero(Display.Grid, FlexDirection.Row);
    }
}
