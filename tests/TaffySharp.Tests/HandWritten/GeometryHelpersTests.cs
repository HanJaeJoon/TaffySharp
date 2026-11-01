namespace TaffySharp.Tests.HandWritten;

public class GeometryHelpersTests
{
    [Fact]
    public void length_percentage_helpers_build_the_expected_values()
    {
        Assert.Equal(
            new Rect<LengthPercentage>(LengthPercentage.Length(1f), LengthPercentage.Length(2f), LengthPercentage.Length(3f), LengthPercentage.Length(4f)),
            Rect.LengthPercentageFromLength(1f, 2f, 3f, 4f));
        Assert.Equal(
            new Rect<LengthPercentage>(LengthPercentage.Percent(0.1f), LengthPercentage.Percent(0.2f), LengthPercentage.Percent(0.3f), LengthPercentage.Percent(0.4f)),
            Rect.LengthPercentageFromPercent(0.1f, 0.2f, 0.3f, 0.4f));
        Assert.Equal(
            new Rect<LengthPercentageAuto>(LengthPercentageAuto.Length(1f), LengthPercentageAuto.Length(2f), LengthPercentageAuto.Length(3f), LengthPercentageAuto.Length(4f)),
            Rect.LengthPercentageAutoFromLength(1f, 2f, 3f, 4f));
        Assert.Equal(
            new Rect<LengthPercentageAuto>(LengthPercentageAuto.Percent(0.1f), LengthPercentageAuto.Percent(0.2f), LengthPercentageAuto.Percent(0.3f), LengthPercentageAuto.Percent(0.4f)),
            Rect.LengthPercentageAutoFromPercent(0.1f, 0.2f, 0.3f, 0.4f));
        Assert.Equal(new Size<LengthPercentage>(LengthPercentage.Length(5f), LengthPercentage.Length(6f)), Size.LengthPercentageFromLengths(5f, 6f));
        Assert.Equal(new Size<LengthPercentage>(LengthPercentage.Percent(0.5f), LengthPercentage.Percent(0.6f)), Size.LengthPercentageFromPercent(0.5f, 0.6f));
    }

    [Fact]
    public void helpers_assign_to_padding_border_margin_inset_and_gap()
    {
        TaffyTree taffy = new();
        NodeId first = taffy.NewLeaf(new Style
        {
            Size = Size.FromLengths(10f, 10f),
            Margin = Rect.LengthPercentageAutoFromLength(1f, 2f, 3f, 4f),
        });
        NodeId second = taffy.NewLeaf(new Style { Size = Size.FromLengths(10f, 10f) });
        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Position = Position.Relative,
                Padding = Rect.LengthPercentageFromLength(16f, 16f, 16f, 16f),
                Border = Rect.LengthPercentageFromLength(1f, 1f, 1f, 1f),
                Gap = Size.LengthPercentageFromLengths(8f, 0f),
            },
            first,
            second);
        NodeId tooltip = taffy.NewLeaf(new Style
        {
            Position = Position.Absolute,
            Size = Size.FromLengths(5f, 5f),
            Inset = Rect.LengthPercentageAutoFromLength(2f, 0f, 3f, 0f),
        });
        taffy.AddChild(root, tooltip);

        taffy.ComputeLayout(root, Size.MaxContent);

        Assert.Equal(new Rect<float>(16f, 16f, 16f, 16f), taffy.Layout(root).Padding);
        Assert.Equal(new Rect<float>(1f, 1f, 1f, 1f), taffy.Layout(root).Border);
        Assert.Equal(new Point<float>(18f, 20f), taffy.Layout(first).Location);
        // first (1 + 10 + 2 margins) + 8 gap
        Assert.Equal(new Point<float>(38f, 17f), taffy.Layout(second).Location);
        // Insets are measured from the padding box, inside the 1px border
        Assert.Equal(new Point<float>(3f, 4f), taffy.Layout(tooltip).Location);
    }
}
