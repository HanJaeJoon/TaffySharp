// The maximum scroll offset is the content size less the size of the padding
// box, so it must not depend on which side a border sits on, and must agree
// across the layout algorithms.
namespace TaffySharp.Tests.HandWritten;

public class ScrollSizeTests
{
    private const float Content = 1000f;
    private const float Container = 200f;
    private const float Border = 20f;
    private const float Padding = 20f;

    private static readonly Display[] Displays = { Display.Block, Display.Flex, Display.Grid };

    private static readonly (float Top, float Bottom)[] BorderEdges = { (Border, 0f), (0f, Border), (Border, Border), (0f, 0f) };

    private static readonly (float Top, float Bottom)[] PaddingEdges = { (Padding, 0f), (0f, Padding), (Padding, Padding), (0f, 0f) };

    private static void AssertEqual<T>(T expected, T actual, string message) =>
        Assert.True(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, actual {actual}");

    private static Layout ScrollerWithPadding(Display display, Rect<LengthPercentage> border, Rect<LengthPercentage> padding)
    {
        TaffyTree<TestNodeContext> tree = new();
        NodeId child = tree.NewLeaf(new Style { Size = Size.FromLengths(100f, Content) });
        NodeId node = tree.NewWithChildren(
            new Style
            {
                Display = display,
                BoxSizing = BoxSizing.BorderBox,
                Size = Size.FromLengths(300f, Container),
                Border = border,
                Padding = padding,
                Overflow = new Point<Overflow>(Overflow.Scroll, Overflow.Scroll),
            },
            child);

        tree.ComputeLayout(node, Size.MaxContent);
        return tree.Layout(node);
    }

    private static Layout Scroller(Display display, Rect<LengthPercentage> border) => ScrollerWithPadding(display, border, Edge(0f, 0f));

    private static Rect<LengthPercentage> Edge(float top, float bottom) => new(
        LengthPercentage.Length(0f),
        LengthPercentage.Length(0f),
        LengthPercentage.Length(top),
        LengthPercentage.Length(bottom));

    [Fact]
    public void scroll_height_does_not_depend_on_which_edge_the_border_is_on()
    {
        foreach (Display display in Displays)
        {
            Layout top = Scroller(display, Edge(Border, 0f));
            Layout bottom = Scroller(display, Edge(0f, Border));

            Assert.True(
                top.ScrollHeight() == bottom.ScrollHeight(),
                $"{display}: border-top gave {top.ScrollHeight()} and border-bottom gave {bottom.ScrollHeight()}");
        }
    }

    [Fact]
    public void scroll_height_is_the_content_beyond_the_padding_box()
    {
        foreach (Display display in Displays)
        {
            foreach ((float top, float bottom) in BorderEdges)
            {
                Layout layout = Scroller(display, Edge(top, bottom));
                float paddingBox = Container - top - bottom;

                AssertEqual(Content - paddingBox, layout.ScrollHeight(), $"{display} with border {top}/{bottom}");
            }
        }
    }

    [Fact]
    public void scrollable_overflow_excludes_the_containers_own_border()
    {
        foreach (Display display in Displays)
        {
            foreach ((float top, float bottom) in BorderEdges)
            {
                Layout layout = Scroller(display, Edge(top, bottom));

                AssertEqual(Content, layout.ScrollableOverflowRect.Bottom, $"{display} with border {top}/{bottom}");
            }
        }
    }

    [Fact]
    public void scrollable_overflow_includes_the_containers_own_padding()
    {
        foreach (Display display in Displays)
        {
            foreach ((float top, float bottom) in PaddingEdges)
            {
                Layout layout = ScrollerWithPadding(display, Edge(0f, 0f), Edge(top, bottom));

                AssertEqual(Content + top + bottom, layout.ScrollableOverflowRect.Bottom, $"{display} with padding {top}/{bottom}");
            }
        }
    }

    [Fact]
    public void scrollable_overflow_excludes_the_own_padding_of_a_non_scroll_container()
    {
        foreach (Display display in Displays)
        {
            foreach ((float top, float bottom) in PaddingEdges)
            {
                TaffyTree<TestNodeContext> tree = new();
                NodeId child = tree.NewLeaf(new Style { Size = Size.FromLengths(100f, Content) });
                NodeId node = tree.NewWithChildren(
                    new Style
                    {
                        Display = display,
                        BoxSizing = BoxSizing.BorderBox,
                        Size = Size.FromLengths(300f, Container),
                        Padding = Edge(top, bottom),
                    },
                    child);

                tree.ComputeLayout(node, Size.MaxContent);
                Layout layout = tree.Layout(node);

                // A box that is not a scroll container does not extend its scrollable overflow
                // region by its own padding: only the content contributes.
                AssertEqual(top + Content, layout.ScrollableOverflowRect.Bottom, $"{display} with padding {top}/{bottom}");
            }
        }
    }

    private static Layout MeasuredLeaf(Overflow overflow, Rect<LengthPercentage> padding)
    {
        TaffyTree<TestNodeContext> tree = new();
        NodeId node = tree.NewLeafWithContext(
            new Style
            {
                BoxSizing = BoxSizing.BorderBox,
                Size = Size.FromLengths(300f, Container),
                Padding = padding,
                Overflow = new Point<Overflow>(overflow, overflow),
            },
            TestNodeContext.Fixed(100f, Content));

        tree.ComputeLayoutWithMeasure(node, Size.MaxContent, TestHelpers.MeasureFunction);
        return tree.Layout(node);
    }

    [Fact]
    public void leaf_content_size_includes_the_containers_own_padding()
    {
        foreach ((float top, float bottom) in PaddingEdges)
        {
            Layout layout = MeasuredLeaf(Overflow.Scroll, Edge(top, bottom));

            AssertEqual(top + Content + bottom, layout.ScrollableOverflowRect.Bottom, $"Leaf with padding {top}/{bottom}");
        }
    }

    [Fact]
    public void leaf_content_size_excludes_the_own_padding_of_a_non_scroll_container()
    {
        foreach ((float top, float bottom) in PaddingEdges)
        {
            Layout layout = MeasuredLeaf(Overflow.Visible, Edge(top, bottom));

            // A box that is not a scroll container does not extend its scrollable overflow
            // region by its own padding: only the content contributes.
            AssertEqual(top + Content, layout.ScrollableOverflowRect.Bottom, $"Leaf with padding {top}/{bottom}");
        }
    }

    [Fact]
    public void scroll_height_accounts_for_the_containers_own_padding()
    {
        foreach (Display display in Displays)
        {
            foreach ((float top, float bottom) in PaddingEdges)
            {
                Layout layout = ScrollerWithPadding(display, Edge(0f, 0f), Edge(top, bottom));

                // The scrollable overflow region runs from the padding-box origin to the end of
                // the content plus the container's end-side padding.
                float expected = (top + Content + bottom) - Container;
                AssertEqual(expected, layout.ScrollHeight(), $"{display} with padding {top}/{bottom}");
            }
        }
    }

    // Items in overflowing rows must contribute their position within the container (not just
    // their size) to the container's content size.
    [Fact]
    public void grid_scrollable_overflow_includes_item_positions_in_overflowing_tracks()
    {
        TaffyTree<TestNodeContext> tree = new();
        Func<Style> child = () => new Style { Size = Size.FromLengths(100f, 500f) };
        NodeId c1 = tree.NewLeaf(child());
        NodeId c2 = tree.NewLeaf(child());
        NodeId node = tree.NewWithChildren(
            new Style
            {
                Display = Display.Grid,
                Size = Size.FromLengths(300f, Container),
                Overflow = new Point<Overflow>(Overflow.Scroll, Overflow.Scroll),
            },
            c1,
            c2);

        tree.ComputeLayout(node, Size.MaxContent);
        Layout layout = tree.Layout(node);

        Assert.Equal(1000f, layout.ScrollableOverflowRect.Bottom);
        Assert.Equal(1000f - Container, layout.ScrollHeight());
    }

    [Fact]
    public void scroll_height_agrees_with_borders_and_padding_combined()
    {
        foreach (Display display in Displays)
        {
            Layout layout = ScrollerWithPadding(display, Edge(Border, Border), Edge(Padding, Padding));

            float paddingBox = Container - Border - Border;
            float expected = (Padding + Content + Padding) - paddingBox;
            AssertEqual(expected, layout.ScrollHeight(), $"{display}");
        }
    }
}
