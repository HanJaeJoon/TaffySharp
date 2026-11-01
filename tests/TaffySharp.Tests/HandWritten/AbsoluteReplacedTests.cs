// Sizing of absolutely positioned replaced (ItemIsReplaced = true) boxes.
//
// An `auto` width/height of an absolutely positioned replaced element resolves to its
// intrinsic size rather than being stretched between its insets
// (https://www.w3.org/TR/CSS22/visudet.html#abs-replaced-width,
// https://www.w3.org/TR/CSS22/visudet.html#abs-replaced-height).
namespace TaffySharp.Tests.HandWritten;

public class AbsoluteReplacedTests
{
    // A 100x50 image (height = width * 0.5)
    private static TestNodeContext Image() => TestNodeContext.AspectRatio(100f, 0.5f);

    private static Style Container() => new()
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

    private static Layout LayoutChild(Style childStyle)
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId child = taffy.NewLeafWithContext(childStyle, Image());
        NodeId root = taffy.NewWithChildren(Container(), child);
        taffy.ComputeLayoutWithMeasure(root, Size.MaxContent, TestHelpers.MeasureFunction);
        return taffy.Layout(child);
    }

    [Fact]
    public void auto_size_uses_intrinsic_size_despite_insets()
    {
        Layout layout = LayoutChild(new Style
        {
            Position = Position.Absolute,
            ItemIsReplaced = true,
            Inset = AllInsets(10f),
        });
        Assert.Equal(new Size<float>(100f, 50f), layout.Size);
        Assert.Equal(new Point<float>(10f, 10f), layout.Location);
    }

    [Fact]
    public void auto_height_uses_aspect_ratio_despite_insets()
    {
        Layout layout = LayoutChild(new Style
        {
            Position = Position.Absolute,
            ItemIsReplaced = true,
            Inset = AllInsets(10f),
            Size = new Size<Dimension>(Dimension.Length(200f), Dimension.Auto),
        });
        Assert.Equal(new Size<float>(200f, 100f), layout.Size);
    }

    [Fact]
    public void non_replaced_auto_size_is_stretched_between_insets()
    {
        Layout layout = LayoutChild(new Style { Position = Position.Absolute, Inset = AllInsets(10f) });
        Assert.Equal(new Size<float>(380f, 280f), layout.Size);
    }
}
