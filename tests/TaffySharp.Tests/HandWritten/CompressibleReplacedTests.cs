// Sizing of compressible replaced boxes that are not replaced elements
// (ItemIsCompressibleReplaced = true, e.g. form controls such as <input>).
//
// Like replaced elements, block-level form controls with `width: auto` take their intrinsic
// width rather than being stretched to the container (css-sizing-3 "compressible replaced
// elements"). Unlike replaced elements, an absolutely positioned form control with both insets
// set *is* stretched between them (CSS2 section 10.3.7 applies, not section 10.3.8).
namespace TaffySharp.Tests.HandWritten;

public class CompressibleReplacedTests
{
    // A 150x20 text input (height = width * 20/150)
    private static TestNodeContext Input() => TestNodeContext.AspectRatio(150f, 20f / 150f);

    private static Layout LayoutChild(Style container, Style childStyle)
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeafWithContext(childStyle, Input());
        NodeId root = taffy.NewWithChildren(container, child);
        taffy.ComputeLayoutWithMeasure(root, Size.MaxContent, TestHelpers.MeasureFunction);
        return taffy.Layout(child);
    }

    private static Style BlockContainer() => new()
    {
        Display = Display.Block,
        Size = new Size<Dimension>(Dimension.Length(400f), Dimension.Auto),
    };

    private static Style PositionedContainer() => new()
    {
        Display = Display.Block,
        Position = Position.Relative,
        Size = Size.FromLengths(400f, 300f),
    };

    private static Rect<LengthPercentageAuto> AllInsets(float value) => new(
        LengthPercentageAuto.Length(value),
        LengthPercentageAuto.Length(value),
        LengthPercentageAuto.Length(value),
        LengthPercentageAuto.Length(value));

    [Fact]
    public void block_level_auto_width_uses_intrinsic_size()
    {
        Layout layout = LayoutChild(
            BlockContainer(),
            new Style { Display = Display.Block, ItemIsCompressibleReplaced = true });
        Assert.Equal(new Size<float>(150f, 20f), layout.Size);
    }

    [Fact]
    public void block_level_justify_self_stretch_stretches()
    {
        Layout layout = LayoutChild(
            BlockContainer(),
            new Style
            {
                Display = Display.Block,
                ItemIsCompressibleReplaced = true,
                JustifySelf = AlignItems.Stretch,
            });
        Assert.Equal(400f, layout.Size.Width);
    }

    [Fact]
    public void absolute_auto_size_is_stretched_between_insets()
    {
        Layout layout = LayoutChild(
            PositionedContainer(),
            new Style
            {
                Position = Position.Absolute,
                ItemIsCompressibleReplaced = true,
                Inset = AllInsets(10f),
            });
        Assert.Equal(new Size<float>(380f, 280f), layout.Size);
        Assert.Equal(new Point<float>(10f, 10f), layout.Location);
    }

    // A `width: 100px; max-width: 100%` item in a zero-width grid's `1fr` column.
    private static float GridItemWidthWithPercentMaxWidth(bool replaced, bool compressibleReplaced)
    {
        Style container = new()
        {
            Display = Display.Grid,
            Size = new Size<Dimension>(Dimension.Length(0f), Dimension.Auto),
            GridTemplateColumns = new List<GridTemplateComponent> { TrackSizingFunction.Fr(1f) },
        };
        Style child = new()
        {
            ItemIsReplaced = replaced,
            ItemIsCompressibleReplaced = compressibleReplaced,
            Size = Size.FromLengths(100f, 20f),
            MinSize = new Size<LengthPercentageAuto>(LengthPercentageAuto.Length(0f), LengthPercentageAuto.Auto),
            MaxSize = new Size<LengthPercentageAuto>(LengthPercentageAuto.Percent(1f), LengthPercentageAuto.Auto),
        };
        return LayoutChild(container, child).Size.Width;
    }

    [Fact]
    public void grid_percent_max_width_does_not_compress_minimum_contribution()
    {
        Assert.Equal(100f, GridItemWidthWithPercentMaxWidth(false, true));
    }

    [Fact]
    public void grid_percent_max_width_compresses_replaced_minimum_contribution()
    {
        Assert.Equal(0f, GridItemWidthWithPercentMaxWidth(true, false));
    }

    [Fact]
    public void replaced_implies_compressible_replaced()
    {
        ICoreStyle style = new Style { ItemIsReplaced = true };
        Assert.True(style.IsCompressibleReplaced);
        Assert.True(style.IsReplaced);
        style = new Style { ItemIsCompressibleReplaced = true };
        Assert.True(style.IsCompressibleReplaced);
        Assert.False(style.IsReplaced);
    }
}
