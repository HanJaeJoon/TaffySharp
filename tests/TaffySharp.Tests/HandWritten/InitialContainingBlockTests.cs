// Tests for the initial containing block: boxes with no nearer containing block are positioned
// against a viewport-sized rectangle at the canvas origin, not against the root element's box.
namespace TaffySharp.Tests.HandWritten;

public class InitialContainingBlockTests
{
    private static readonly Size<AvailableSpace> Viewport = new(AvailableSpace.Definite(800f), AvailableSpace.Definite(600f));

    private static Style RootStyle() => new()
    {
        Display = Display.Block,
        Margin = new Rect<LengthPercentageAuto>(
            LengthPercentageAuto.Length(10f),
            LengthPercentageAuto.Length(10f),
            LengthPercentageAuto.Length(20f),
            LengthPercentageAuto.Length(20f)),
        Border = new Rect<LengthPercentage>(
            LengthPercentage.Length(5f),
            LengthPercentage.Length(5f),
            LengthPercentage.Length(5f),
            LengthPercentage.Length(5f)),
    };

    private static Style OofStyle(Position position, Rect<LengthPercentageAuto> inset, Size<Dimension> size) => new()
    {
        Position = position,
        Inset = inset,
        Size = size,
    };

    private static void AssertEqual<T>(T expected, T actual, string message) =>
        Assert.True(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, actual {actual}");

    [Fact]
    public void root_is_placed_at_its_margin()
    {
        TaffyTree tree = new();
        NodeId root = tree.NewLeaf(RootStyle());
        tree.ComputeLayout(root, Viewport);

        Layout layout = tree.Layout(root);
        Assert.Equal(new Point<float>(10f, 20f), layout.Location);
        Assert.Equal(780f, layout.Size.Width);
    }

    [Fact]
    public void inset_zero_fills_viewport()
    {
        foreach (Position position in new[] { Position.Absolute, Position.Fixed })
        {
            TaffyTree tree = new();
            NodeId oof = tree.NewLeaf(OofStyle(
                position,
                new Rect<LengthPercentageAuto>(
                    LengthPercentageAuto.Length(0f),
                    LengthPercentageAuto.Length(0f),
                    LengthPercentageAuto.Length(0f),
                    LengthPercentageAuto.Length(0f)),
                new Size<Dimension>(Dimension.Auto, Dimension.Auto)));
            NodeId root = tree.NewWithChildren(RootStyle(), oof);
            tree.ComputeLayout(root, Viewport);

            // Location is relative to the root's border box, which sits at (10, 20) in the viewport
            Layout layout = tree.Layout(oof);
            AssertEqual(new Point<float>(-10f, -20f), layout.Location, $"{position}");
            AssertEqual(new Size<float>(800f, 600f), layout.Size, $"{position}");
        }
    }

    [Fact]
    public void bottom_right_insets_resolve_against_viewport()
    {
        foreach (Position position in new[] { Position.Absolute, Position.Fixed })
        {
            TaffyTree tree = new();
            NodeId oof = tree.NewLeaf(OofStyle(
                position,
                new Rect<LengthPercentageAuto>(
                    LengthPercentageAuto.Auto,
                    LengthPercentageAuto.Length(0f),
                    LengthPercentageAuto.Auto,
                    LengthPercentageAuto.Length(0f)),
                Size.FromLengths(50f, 50f)));
            // The root has no in-flow content, so its border box is much shorter than the viewport
            NodeId root = tree.NewWithChildren(RootStyle(), oof);
            tree.ComputeLayout(root, Viewport);

            Layout layout = tree.Layout(oof);
            AssertEqual(new Point<float>(800f - 50f - 10f, 600f - 50f - 20f), layout.Location, $"{position}");
        }
    }

    [Fact]
    public void static_position_is_unaffected()
    {
        TaffyTree tree = new();
        NodeId oof = tree.NewLeaf(OofStyle(
            Position.Absolute,
            new Rect<LengthPercentageAuto>(
                LengthPercentageAuto.Auto,
                LengthPercentageAuto.Auto,
                LengthPercentageAuto.Auto,
                LengthPercentageAuto.Auto),
            Size.FromLengths(50f, 50f)));
        NodeId root = tree.NewWithChildren(RootStyle(), oof);
        tree.ComputeLayout(root, Viewport);

        Assert.Equal(new Point<float>(5f, 5f), tree.Layout(oof).Location);
    }

    [Fact]
    public void fixed_root_is_positioned_by_its_insets()
    {
        // `position: fixed` roots cannot be covered by the generated tests (Chrome positions them
        // against the browser window rather than the `.viewport` wrapper), so check that they are
        // positioned like absolutely positioned roots against the initial containing block.
        TaffyTree tree = new();
        NodeId root = tree.NewLeaf(new Style
        {
            Display = Display.Block,
            Position = Position.Fixed,
            Inset = new Rect<LengthPercentageAuto>(
                LengthPercentageAuto.Auto,
                LengthPercentageAuto.Length(10f),
                LengthPercentageAuto.Percent(0.1f),
                LengthPercentageAuto.Auto),
            Size = Size.FromLengths(100f, 50f),
        });
        tree.ComputeLayout(root, Viewport);

        Layout layout = tree.Layout(root);
        Assert.Equal(new Point<float>(800f - 100f - 10f, 60f), layout.Location);
        Assert.Equal(new Size<float>(100f, 50f), layout.Size);
    }
}
