namespace TaffySharp.Tests.HandWritten;

public class RelayoutTests
{
    [Fact]
    public void relayout()
    {
        TaffyTree<TestNodeContext> taffy = new();
        NodeId node1 = taffy.NewLeaf(new Style { Size = Size.FromLengths(8f, 80f) });
        NodeId node0 = taffy.NewWithChildren(
            new Style
            {
                AlignSelf = AlignItems.Center,
                Size = new Size<Dimension>(Dimension.Auto, Dimension.Auto),
                // Size = new Size<Dimension>(Dimension.Percent(1f), Dimension.Percent(1f)),
            },
            node1);
        NodeId node = taffy.NewWithChildren(
            new Style { Size = new Size<Dimension>(Dimension.Percent(1f), Dimension.Percent(1f)) },
            node0);
        taffy.ComputeLayout(node, new Size<AvailableSpace>(AvailableSpace.Definite(100f), AvailableSpace.Definite(100f)));
        Point<float> initial = taffy.Layout(node).Location;
        Point<float> initial0 = taffy.Layout(node0).Location;
        Point<float> initial1 = taffy.Layout(node1).Location;
        for (int i = 1; i < 10; i++)
        {
            taffy.ComputeLayout(node, new Size<AvailableSpace>(AvailableSpace.Definite(100f), AvailableSpace.Definite(100f)));
            Assert.Equal(initial, taffy.Layout(node).Location);
            Assert.Equal(initial0, taffy.Layout(node0).Location);
            Assert.Equal(initial1, taffy.Layout(node1).Location);
        }
    }

    private static Style HiddenStyle() => new() { Display = Display.None, Size = Size.FromLengths(100f, 100f) };

    private static Style SizedStyle(Display display) => new() { Display = display, Size = Size.FromLengths(100f, 100f) };

    private static void AssertOriginAndSize(Layout layout, float width, float height)
    {
        Assert.Equal(0f, layout.Location.X);
        Assert.Equal(0f, layout.Location.Y);
        Assert.Equal(width, layout.Size.Width);
        Assert.Equal(height, layout.Size.Height);
    }

    [Fact]
    public void toggle_root_display_none()
    {
        Style hiddenStyle = HiddenStyle();

        Style flexStyle = SizedStyle(Display.Flex);

        // Setup
        TaffyTree<TestNodeContext> taffy = new();
        NodeId node = taffy.NewLeaf(hiddenStyle.Clone());

        // Layout 1 (None)
        taffy.ComputeLayout(node, Size.MaxContent);
        AssertOriginAndSize(taffy.Layout(node), 0f, 0f);

        // Layout 2 (Flex)
        taffy.SetStyle(node, flexStyle);
        taffy.ComputeLayout(node, Size.MaxContent);
        AssertOriginAndSize(taffy.Layout(node), 100f, 100f);

        // Layout 3 (None)
        taffy.SetStyle(node, hiddenStyle);
        taffy.ComputeLayout(node, Size.MaxContent);
        AssertOriginAndSize(taffy.Layout(node), 0f, 0f);
    }

    [Fact]
    public void toggle_root_display_none_with_children()
    {
        TaffyTree<TestNodeContext> taffy = new();

        NodeId child = taffy.NewLeaf(new Style { Size = Size.FromLengths(800f, 100f) });

        NodeId parent = taffy.NewWithChildren(new Style { Size = Size.FromLengths(800f, 100f) }, child);

        NodeId root = taffy.NewWithChildren(new Style(), parent);
        taffy.ComputeLayout(root, Size.MaxContent);
        Assert.Equal(800f, taffy.Layout(child).Size.Width);
        Assert.Equal(100f, taffy.Layout(child).Size.Height);

        taffy.SetStyle(root, new Style { Display = Display.None });
        taffy.ComputeLayout(root, Size.MaxContent);
        Assert.Equal(0f, taffy.Layout(child).Size.Width);
        Assert.Equal(0f, taffy.Layout(child).Size.Height);

        taffy.SetStyle(root, new Style());
        taffy.ComputeLayout(root, Size.MaxContent);
        Assert.Equal(800f, taffy.Layout(parent).Size.Width);
        Assert.Equal(100f, taffy.Layout(parent).Size.Height);
        Assert.Equal(800f, taffy.Layout(child).Size.Width);
        Assert.Equal(100f, taffy.Layout(child).Size.Height);
    }

    [Fact]
    public void toggle_flex_child_display_none()
    {
        Style hiddenStyle = HiddenStyle();

        Style flexStyle = SizedStyle(Display.Flex);

        // Setup
        TaffyTree<TestNodeContext> taffy = new();
        NodeId node = taffy.NewLeaf(hiddenStyle.Clone());
        NodeId root = taffy.NewWithChildren(flexStyle.Clone(), node);

        // Layout 1 (None)
        taffy.ComputeLayout(root, Size.MaxContent);
        AssertOriginAndSize(taffy.Layout(node), 0f, 0f);

        // Layout 2 (Flex)
        taffy.SetStyle(node, flexStyle);
        taffy.ComputeLayout(root, Size.MaxContent);
        AssertOriginAndSize(taffy.Layout(node), 100f, 100f);

        // Layout 3 (None)
        taffy.SetStyle(node, hiddenStyle);
        taffy.ComputeLayout(root, Size.MaxContent);
        AssertOriginAndSize(taffy.Layout(node), 0f, 0f);
    }

    [Fact]
    public void toggle_flex_container_display_none()
    {
        Style hiddenStyle = HiddenStyle();

        Style flexStyle = SizedStyle(Display.Flex);

        // Setup
        TaffyTree<TestNodeContext> taffy = new();
        NodeId node = taffy.NewLeaf(hiddenStyle.Clone());
        NodeId root = taffy.NewWithChildren(hiddenStyle.Clone(), node);

        // Layout 1 (None)
        taffy.ComputeLayout(root, Size.MaxContent);
        AssertOriginAndSize(taffy.Layout(root), 0f, 0f);

        // Layout 2 (Flex)
        taffy.SetStyle(root, flexStyle);
        taffy.ComputeLayout(root, Size.MaxContent);
        AssertOriginAndSize(taffy.Layout(root), 100f, 100f);

        // Layout 3 (None)
        taffy.SetStyle(root, hiddenStyle);
        taffy.ComputeLayout(root, Size.MaxContent);
        AssertOriginAndSize(taffy.Layout(root), 0f, 0f);
    }

    [Fact]
    public void toggle_grid_child_display_none()
    {
        Style hiddenStyle = HiddenStyle();

        Style gridStyle = SizedStyle(Display.Grid);

        // Setup
        TaffyTree<TestNodeContext> taffy = new();
        NodeId node = taffy.NewLeaf(hiddenStyle.Clone());
        NodeId root = taffy.NewWithChildren(gridStyle.Clone(), node);

        // Layout 1 (None)
        taffy.ComputeLayout(root, Size.MaxContent);
        AssertOriginAndSize(taffy.Layout(node), 0f, 0f);

        // Layout 2 (Flex)
        taffy.SetStyle(node, gridStyle);
        taffy.ComputeLayout(root, Size.MaxContent);
        AssertOriginAndSize(taffy.Layout(node), 100f, 100f);

        // Layout 3 (None)
        taffy.SetStyle(node, hiddenStyle);
        taffy.ComputeLayout(root, Size.MaxContent);
        AssertOriginAndSize(taffy.Layout(node), 0f, 0f);
    }

    [Fact]
    public void toggle_grid_container_display_none()
    {
        Style hiddenStyle = HiddenStyle();

        Style gridStyle = SizedStyle(Display.Grid);

        // Setup
        TaffyTree<TestNodeContext> taffy = new();
        NodeId node = taffy.NewLeaf(hiddenStyle.Clone());
        NodeId root = taffy.NewWithChildren(hiddenStyle.Clone(), node);

        // Layout 1 (None)
        taffy.ComputeLayout(root, Size.MaxContent);
        AssertOriginAndSize(taffy.Layout(root), 0f, 0f);

        // Layout 2 (Flex)
        taffy.SetStyle(root, gridStyle);
        taffy.ComputeLayout(root, Size.MaxContent);
        AssertOriginAndSize(taffy.Layout(root), 100f, 100f);

        // Layout 3 (None)
        taffy.SetStyle(root, hiddenStyle);
        taffy.ComputeLayout(root, Size.MaxContent);
        AssertOriginAndSize(taffy.Layout(root), 0f, 0f);
    }

    [Fact]
    public void relayout_is_stable_with_rounding()
    {
        TaffyTree<TestNodeContext> taffy = new();
        taffy.EnableRounding();

        // <div style="width: 1920px; height: 1080px">
        //     <div style="width: 100%; left: 1.5px">
        //         <div style="width: 150px; justify-content: end">
        //             <div style="min-width: 300px" />
        //         </div>
        //     </div>
        // </div>

        NodeId inner = taffy.NewLeaf(new Style
        {
            MinSize = new Size<LengthPercentageAuto>(LengthPercentageAuto.Length(300f), LengthPercentageAuto.Auto),
        });
        NodeId wrapper = taffy.NewWithChildren(
            new Style
            {
                Size = new Size<Dimension>(Dimension.Length(150f), Dimension.Auto),
                JustifyContent = AlignContent.End,
            },
            inner);
        NodeId outer = taffy.NewWithChildren(
            new Style
            {
                Size = new Size<Dimension>(Dimension.Percent(1f), Dimension.Auto),
                Position = Position.Relative,
                Inset = new Rect<LengthPercentageAuto>(
                    LengthPercentageAuto.Length(1.5f),
                    LengthPercentageAuto.Auto,
                    LengthPercentageAuto.Auto,
                    LengthPercentageAuto.Auto),
            },
            wrapper);
        NodeId root = taffy.NewWithChildren(new Style { Size = Size.FromLengths(1920f, 1080f) }, outer);

        // Compute and assert initial layout.

        taffy.ComputeLayout(root, Size.MaxContent);
        taffy.PrintTree(root);

        Layout initialRootLayout = taffy.Layout(root);
        Assert.Equal(0f, initialRootLayout.Location.X);
        Assert.Equal(0f, initialRootLayout.Location.Y);
        Assert.Equal(1920f, initialRootLayout.Size.Width);
        Assert.Equal(1080f, initialRootLayout.Size.Height);

        Layout initialOuterLayout = taffy.Layout(outer);
        Assert.Equal(2f, initialOuterLayout.Location.X);
        Assert.Equal(0f, initialOuterLayout.Location.Y);
        Assert.Equal(1920f, initialOuterLayout.Size.Width);
        Assert.Equal(1080f, initialOuterLayout.Size.Height);

        Layout initialWrapperLayout = taffy.Layout(wrapper);
        Assert.Equal(0f, initialWrapperLayout.Location.X);
        Assert.Equal(0f, initialWrapperLayout.Location.Y);
        Assert.Equal(150f, initialWrapperLayout.Size.Width);
        Assert.Equal(1080f, initialWrapperLayout.Size.Height);

        Layout initialInnerLayout = taffy.Layout(inner);
        Assert.Equal(-150f, initialInnerLayout.Location.X);
        Assert.Equal(0f, initialInnerLayout.Location.Y);
        Assert.Equal(300f, initialInnerLayout.Size.Width);
        Assert.Equal(1080f, initialInnerLayout.Size.Height);

        // Recompute and assert that new layout marks initial layout each time
        for (int i = 0; i < 5; i++)
        {
            taffy.MarkDirty(root);
            taffy.ComputeLayout(root, Size.MaxContent);
            taffy.PrintTree(root);

            Layout rootLayout = taffy.Layout(root);
            Assert.Equal(initialRootLayout.Location.X, rootLayout.Location.X);
            Assert.Equal(initialRootLayout.Location.Y, rootLayout.Location.Y);
            Assert.Equal(initialRootLayout.Size.Width, rootLayout.Size.Width);
            Assert.Equal(initialRootLayout.Size.Height, rootLayout.Size.Height);
            Layout outerLayout = taffy.Layout(outer);
            Assert.Equal(initialOuterLayout.Location.X, outerLayout.Location.X);
            Assert.Equal(initialOuterLayout.Location.Y, outerLayout.Location.Y);
            Assert.Equal(initialOuterLayout.Size.Width, outerLayout.Size.Width);
            Assert.Equal(initialOuterLayout.Size.Height, outerLayout.Size.Height);
            Layout wrapperLayout = taffy.Layout(wrapper);
            Assert.Equal(initialWrapperLayout.Location.X, wrapperLayout.Location.X);
            Assert.Equal(initialWrapperLayout.Location.X, wrapperLayout.Location.Y);
            Assert.Equal(initialWrapperLayout.Size.Width, wrapperLayout.Size.Width);
            Assert.Equal(initialWrapperLayout.Size.Height, wrapperLayout.Size.Height);
            Layout innerLayout = taffy.Layout(inner);
            Assert.Equal(initialInnerLayout.Location.X, innerLayout.Location.X);
            Assert.Equal(initialInnerLayout.Location.Y, innerLayout.Location.Y);
            Assert.Equal(initialInnerLayout.Size.Width, innerLayout.Size.Width);
            Assert.Equal(initialInnerLayout.Size.Height, innerLayout.Size.Height);
        }
    }

    // Lays out the tree that `build` makes with `before`, restyles the node that `build` returns first
    // with `after` and lays the tree out again. Returns the unrounded layout of every node that `build`
    // returns, alongside those of a fresh tree built with `after` directly. `build` returns the root last.
    private static (List<Layout> Relaid, List<Layout> Fresh) RelayoutAndFresh(
        Style before,
        Style after,
        Func<TaffyTree<TestNodeContext>, Style, List<NodeId>> build)
    {
        Func<TaffyTree<TestNodeContext>, List<NodeId>, List<Layout>> layouts =
            (tree, ids) => ids.Select(id => tree.UnroundedLayout(id)).ToList();

        TaffyTree<TestNodeContext> tree = new();
        List<NodeId> ids = build(tree, before);
        NodeId root = ids[ids.Count - 1];
        tree.ComputeLayout(root, Size.MaxContent);
        tree.SetStyle(ids[0], after.Clone());
        tree.ComputeLayout(root, Size.MaxContent);

        TaffyTree<TestNodeContext> fresh = new();
        List<NodeId> freshIds = build(fresh, after);
        fresh.ComputeLayout(freshIds[freshIds.Count - 1], Size.MaxContent);

        return (layouts(tree, ids), layouts(fresh, freshIds));
    }

    // <div style="display: block; width: 100px; height: 200px">
    //   <div style="display: block; height: 50px">
    //     <div style="display: block; height: 10px; margin-top: 20px"></div>
    //   </div>
    // </div>
    //
    // The outer box then becomes `container`. The inner box's margin collapses through the middle box
    // only while the middle box is in a block formatting context with its parent: as a flex or grid item
    // the middle box establishes an independent formatting context, which contains the margin.
    private static void RelayoutBlockParentAs(Style container)
    {
        Size<Dimension> size = Size.FromLengths(100f, 200f);
        Style block = new() { Display = Display.Block, Size = size };
        container.Size = size;

        (List<Layout> relaid, List<Layout> fresh) = RelayoutAndFresh(block, container, (tree, rootStyle) =>
        {
            NodeId inner = tree.NewLeaf(new Style
            {
                Display = Display.Block,
                Size = new Size<Dimension>(Dimension.Auto, Dimension.Length(10f)),
                Margin = new Rect<LengthPercentageAuto>(
                    LengthPercentageAuto.Zero,
                    LengthPercentageAuto.Zero,
                    LengthPercentageAuto.Length(20f),
                    LengthPercentageAuto.Zero),
            });
            NodeId middle = tree.NewWithChildren(
                new Style
                {
                    Display = Display.Block,
                    Size = new Size<Dimension>(Dimension.Auto, Dimension.Length(50f)),
                },
                inner);
            NodeId root = tree.NewWithChildren(rootStyle, middle);
            return new List<NodeId> { root, middle, inner, root };
        });

        Assert.True(fresh[1].Location.Y == 0f, $"the item is at the top of its container: actual {fresh[1].Location.Y}");
        Assert.True(fresh[2].Location.Y == 20f, $"the item contains its child's margin: actual {fresh[2].Location.Y}");
        Assert.Equal(fresh, relaid);
    }

    // A flex container lays its items out with SizingMode.ContentSize and without collapsible
    // margins. The block container it was before used SizingMode.InherentSize and collapsible margins.
    [Fact]
    public void block_child_does_not_keep_collapsed_margin_as_flex_item()
    {
        RelayoutBlockParentAs(new Style
        {
            Display = Display.Flex,
            FlexDirection = FlexDirection.Column,
        });
    }

    // A grid container lays its items out with SizingMode.InherentSize, as the block container it
    // was before did: only VerticalMarginsAreCollapsible differs between the two layout inputs.
    [Fact]
    public void block_child_does_not_keep_collapsed_margin_as_grid_item()
    {
        RelayoutBlockParentAs(new Style { Display = Display.Grid });
    }

    // <div style="display: grid; width: 100px">
    //   <div style="display: grid; flex-grow: 1; min-width: 40px; height: 20px"></div>
    //   <div style="display: block; flex-grow: 1; height: 20px"></div>
    // </div>
    //
    // The outer box then becomes `display: flex`. The grid container measured the first item with
    // SizingMode.InherentSize, which applies the item's `min-width`. The flex container measures
    // the same item's flex base size with SizingMode.ContentSize, which does not: both items
    // have a flex base size of zero and grow equally.
    [Fact]
    public void flex_base_size_is_not_answered_by_an_inherent_size_measurement()
    {
        Func<Display, Style> container = display => new Style
        {
            Display = display,
            Size = new Size<Dimension>(Dimension.Length(100f), Dimension.Auto),
        };

        (List<Layout> relaid, List<Layout> fresh) = RelayoutAndFresh(container(Display.Grid), container(Display.Flex), (tree, rootStyle) =>
        {
            NodeId first = tree.NewLeaf(new Style
            {
                Display = Display.Grid,
                FlexGrow = 1f,
                Size = new Size<Dimension>(Dimension.Auto, Dimension.Length(20f)),
                MinSize = new Size<LengthPercentageAuto>(LengthPercentageAuto.Length(40f), LengthPercentageAuto.Auto),
            });
            NodeId second = tree.NewLeaf(new Style
            {
                Display = Display.Block,
                FlexGrow = 1f,
                Size = new Size<Dimension>(Dimension.Auto, Dimension.Length(20f)),
            });
            NodeId root = tree.NewWithChildren(rootStyle, first, second);
            return new List<NodeId> { root, first, second, root };
        });

        Assert.Equal(50f, fresh[1].Size.Width);
        Assert.Equal(50f, fresh[2].Size.Width);
        Assert.Equal(fresh, relaid);
    }

    [Fact]
    public void a_percentage_height_is_not_reused_after_its_containing_block_loses_its_height()
    {
        // <div style="display:flex;flex-direction:column;width:78px">
        //   <div style="width:78px;height:62px">
        //     <div style="display:flex;width:31px;height:86%"> <div style="height:19px"></div>
        // </div></div></div>, then the middle block's height becomes auto: the item's
        // percentage no longer resolves (CSS 2.1 section 10.5) and it is 19px tall, not 53.32px.
        Func<Dimension, Style> block = height => new Style
        {
            Display = Display.Block,
            Size = new Size<Dimension>(Dimension.Length(78f), height),
        };
        (List<Layout> incremental, List<Layout> fresh) =
            RelayoutAndFresh(block(Dimension.Length(62f)), block(Dimension.Auto), (tree, middleStyle) =>
            {
                NodeId leaf = tree.NewLeaf(new Style
                {
                    Display = Display.Block,
                    Size = new Size<Dimension>(Dimension.Auto, Dimension.Length(19f)),
                });
                NodeId item = tree.NewWithChildren(
                    new Style
                    {
                        Display = Display.Flex,
                        Size = new Size<Dimension>(Dimension.Length(31f), Dimension.Percent(0.86f)),
                    },
                    leaf);
                NodeId middle = tree.NewWithChildren(middleStyle, item);
                NodeId root = tree.NewWithChildren(
                    new Style
                    {
                        Display = Display.Flex,
                        FlexDirection = FlexDirection.Column,
                        Size = new Size<Dimension>(Dimension.Length(78f), Dimension.Auto),
                    },
                    middle);
                return new List<NodeId> { middle, item, leaf, root };
            });
        Assert.Equal(19f, fresh[1].Size.Height);
        Assert.Equal(fresh, incremental);
    }
}
