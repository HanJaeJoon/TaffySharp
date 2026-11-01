// The scrollable overflow rectangle captures start-side (negative) overflow as well as
// end-side overflow. Boxes wholly in the unreachable scrollable overflow region are
// excluded for scroll containers, while boxes only partially in the unreachable region
// contribute their whole border box.
namespace TaffySharp.Tests.HandWritten;

public class ScrollableOverflowTests
{
    private const float Container = 200f;
    private const float Child = 50f;

    private static readonly Display[] Displays = { Display.Block, Display.Flex, Display.Grid };

    private static void AssertEqual<T>(T expected, T actual, string message) =>
        Assert.True(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, actual {actual}");

    private static LengthPercentageAuto LengthOrAuto(float? value) => value is float v ? LengthPercentageAuto.Length(v) : LengthPercentageAuto.Auto;

    private static Layout ContainerWithAbsoluteChild(
        Display display,
        Overflow overflow,
        Direction direction,
        float? insetLeft,
        float? insetRight)
    {
        TaffyTree<TestNodeContext> tree = new();
        NodeId child = tree.NewLeaf(new Style
        {
            Position = Position.Absolute,
            Inset = new Rect<LengthPercentageAuto>(
                LengthOrAuto(insetLeft),
                LengthOrAuto(insetRight),
                LengthPercentageAuto.Length(0f),
                LengthPercentageAuto.Auto),
            Size = Size.FromLengths(Child, Child),
        });
        NodeId node = tree.NewWithChildren(
            new Style
            {
                Display = display,
                Direction = direction,
                Position = Position.Relative,
                Size = Size.FromLengths(Container, Container),
                Overflow = new Point<Overflow>(overflow, overflow),
            },
            child);

        tree.ComputeLayout(node, Size.MaxContent);
        return tree.Layout(node);
    }

    [Fact]
    public void wholly_unreachable_boxes_are_excluded_for_scroll_containers()
    {
        foreach (Display display in Displays)
        {
            // Child entirely before the scroll origin (x from -100 to -50)
            Layout layout = ContainerWithAbsoluteChild(display, Overflow.Scroll, Direction.Ltr, -100f, null);

            AssertEqual(Rect.Zero, layout.ScrollableOverflowRect, $"{display}");
            AssertEqual(0f, layout.ScrollWidth(), $"{display}");
        }
    }

    [Fact]
    public void wholly_unreachable_boxes_contribute_to_non_scroll_containers()
    {
        foreach (Display display in Displays)
        {
            // A box that is not a scroll container has no unreachable region of its own, so
            // start-side overflow is captured in its scrollable overflow rect.
            Layout layout = ContainerWithAbsoluteChild(display, Overflow.Visible, Direction.Ltr, -100f, null);

            AssertEqual(-100f, layout.ScrollableOverflowRect.Left, $"{display}");
        }
    }

    [Fact]
    public void partially_unreachable_boxes_contribute_their_whole_border_box()
    {
        foreach (Display display in Displays)
        {
            // Child straddling the scroll origin (x from -25 to 25): its whole border box is
            // part of the scrollable overflow area, even though the negative part is unreachable.
            Layout layout = ContainerWithAbsoluteChild(display, Overflow.Scroll, Direction.Ltr, -25f, null);

            AssertEqual(-25f, layout.ScrollableOverflowRect.Left, $"{display}");
            AssertEqual(25f, layout.ScrollableOverflowRect.Right, $"{display}");
            // Only the reachable extent counts towards the scroll width
            AssertEqual(0f, layout.ScrollWidth(), $"{display}");
        }
    }

    [Fact]
    public void end_side_overflow_is_captured_and_scrollable()
    {
        foreach (Display display in Displays)
        {
            // Child extending past the end edge (x from 200 to 250)
            Layout layout = ContainerWithAbsoluteChild(display, Overflow.Scroll, Direction.Ltr, Container, null);

            AssertEqual(Container + Child, layout.ScrollableOverflowRect.Right, $"{display}");
            AssertEqual(Child, layout.ScrollWidth(), $"{display}");
        }
    }

    [Fact]
    public void absolute_only_containers_include_scroll_container_end_padding()
    {
        foreach (Display display in Displays)
        {
            TaffyTree<TestNodeContext> tree = new();
            NodeId abs = tree.NewLeaf(new Style
            {
                Position = Position.Absolute,
                Inset = new Rect<LengthPercentageAuto>(
                    LengthPercentageAuto.Length(0f),
                    LengthPercentageAuto.Auto,
                    LengthPercentageAuto.Length(0f),
                    LengthPercentageAuto.Auto),
                Size = Size.FromLengths(5f, 5f),
            });
            NodeId node = tree.NewWithChildren(
                new Style
                {
                    Display = display,
                    Position = Position.Relative,
                    Overflow = new Point<Overflow>(Overflow.Scroll, Overflow.Scroll),
                    Padding = new Rect<LengthPercentage>(
                        LengthPercentage.Length(0f),
                        LengthPercentage.Length(20f),
                        LengthPercentage.Length(0f),
                        LengthPercentage.Length(30f)),
                    Size = Size.FromLengths(Container, Container),
                },
                abs);

            tree.ComputeLayout(node, Size.MaxContent);
            Layout layout = tree.Layout(node);

            AssertEqual(20f, layout.ScrollableOverflowRect.Right, $"{display}");
            AssertEqual(30f, layout.ScrollableOverflowRect.Bottom, $"{display}");
        }
    }

    private static Layout NestedOverflowLayout(Display display, Point<Overflow> childOverflow)
    {
        TaffyTree<TestNodeContext> tree = new();
        // Grandchild overflowing the child in the y axis (extends to y=300 in a 200-high child)
        NodeId grandchild = tree.NewLeaf(new Style
        {
            Position = Position.Absolute,
            Inset = new Rect<LengthPercentageAuto>(
                LengthPercentageAuto.Length(0f),
                LengthPercentageAuto.Auto,
                LengthPercentageAuto.Length(Container),
                LengthPercentageAuto.Auto),
            Size = Size.FromLengths(Child, Child),
        });
        NodeId child = tree.NewWithChildren(
            new Style
            {
                Display = display,
                Position = Position.Relative,
                Size = Size.FromLengths(Container, Container),
                Overflow = childOverflow,
            },
            grandchild);
        NodeId node = tree.NewWithChildren(
            new Style { Display = display, Size = Size.FromLengths(Container, Container) },
            child);

        tree.ComputeLayout(node, Size.MaxContent);
        return tree.Layout(node);
    }

    [Fact]
    public void scroll_container_in_one_axis_clips_both_axes()
    {
        foreach (Display display in Displays)
        {
            // Hidden/Scroll in either axis makes the child a scroll container, which clips
            // overflow in both axes: the grandchild's y overflow must not propagate.
            Layout layout = NestedOverflowLayout(display, new Point<Overflow>(Overflow.Hidden, Overflow.Visible));

            AssertEqual(Container, layout.ScrollableOverflowRect.Bottom, $"{display}");
        }
    }

    [Fact]
    public void clip_in_one_axis_does_not_clip_the_other()
    {
        foreach (Display display in Displays)
        {
            // Clip does not establish a scroll container: overflow in the other (visible) axis
            // still propagates.
            Layout layout = NestedOverflowLayout(display, new Point<Overflow>(Overflow.Clip, Overflow.Visible));

            AssertEqual(Container + Child, layout.ScrollableOverflowRect.Bottom, $"{display}");
        }
    }

    [Fact]
    public void wholly_unreachable_boxes_are_excluded_for_rtl_scroll_containers()
    {
        foreach (Display display in Displays)
        {
            // In RTL the scroll origin is the top-right corner: a child entirely past the
            // *right* edge is wholly unreachable.
            Layout layout = ContainerWithAbsoluteChild(display, Overflow.Scroll, Direction.Rtl, null, -100f);

            AssertEqual(Rect.Zero, layout.ScrollableOverflowRect, $"{display}");
            AssertEqual(0f, layout.ScrollWidth(), $"{display}");
        }
    }

    [Fact]
    public void end_side_overflow_is_captured_for_rtl_scroll_containers()
    {
        foreach (Display display in Displays)
        {
            // In RTL, overflow past the *left* edge is reachable end-side overflow.
            Layout layout = ContainerWithAbsoluteChild(display, Overflow.Scroll, Direction.Rtl, -Child, null);

            AssertEqual(Container + Child, layout.ScrollableOverflowRect.Right, $"{display}");
            AssertEqual(Child, layout.ScrollWidth(), $"{display}");
        }
    }
}
