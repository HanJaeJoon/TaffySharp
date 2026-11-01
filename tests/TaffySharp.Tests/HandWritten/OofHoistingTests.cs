// Tests for out-of-flow hoisting: absolute/fixed boxes are laid out by their containing
// block (nearest positioned ancestor, or the root) rather than by their DOM parent, and
// their Layout.Location is relative to that containing block.
namespace TaffySharp.Tests.HandWritten;

public class OofHoistingTests
{
    private static Style LeafStyle(float width, float height) => new() { Size = Size.FromLengths(width, height) };

    // Inset or margin with the given left/top lengths and auto right/bottom
    private static Rect<LengthPercentageAuto> LeftTop(float left, float top) => new(
        LengthPercentageAuto.Length(left),
        LengthPercentageAuto.Auto,
        LengthPercentageAuto.Length(top),
        LengthPercentageAuto.Auto);

    private static void AssertEqual<T>(T expected, T actual, string message) =>
        Assert.True(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, actual {actual}");

    // An absolute box whose parent is static is positioned relative to the nearest
    // positioned ancestor, with a containing-block-relative location.
    [Fact]
    public void absolute_skips_static_parent()
    {
        foreach (Display display in new[] { Display.Block, Display.Flex, Display.Grid })
        {
            TaffyTree tree = new();
            NodeId abs = tree.NewLeaf(new Style
            {
                Position = Position.Absolute,
                Inset = LeftTop(10f, 20f),
                Size = Size.FromLengths(30f, 30f),
            });
            // Static in-flow parent, offset from the containing block's origin
            NodeId staticParent = tree.NewWithChildren(
                new Style
                {
                    Display = display,
                    Margin = LeftTop(40f, 40f),
                    Size = Size.FromLengths(50f, 50f),
                },
                abs);
            NodeId cb = tree.NewWithChildren(
                new Style
                {
                    Display = display,
                    Position = Position.Relative,
                    Size = Size.FromLengths(200f, 200f),
                },
                staticParent);

            tree.ComputeLayout(cb, Size.MaxContent);

            // Location is relative to the containing block, not the static parent
            Layout layout = tree.Layout(abs);
            AssertEqual(new Point<float>(10f, 20f), layout.Location, $"{display}");
            AssertEqual(new Size<float>(30f, 30f), layout.Size, $"{display}");
        }
    }

    // The containing block's hoisted children are recorded in document order: direct
    // out-of-flow children interleaved with boxes hoisted out of in-flow children's subtrees.
    [Fact]
    public void hoisted_children_are_in_document_order()
    {
        foreach (Display display in new[] { Display.Block, Display.Flex, Display.Grid })
        {
            TaffyTree tree = new();
            Style absStyle = new()
            {
                Position = Position.Absolute,
                Size = Size.FromLengths(10f, 10f),
            };
            Style inflowStyle = new() { Display = display, Size = Size.FromLengths(20f, 20f) };

            // Children of `cb`, in document order:
            //   [direct_a, static_b(> nested_b), static_c(> nested_c), direct_d, static_e(> nested_e)]
            NodeId directA = tree.NewLeaf(absStyle.Clone());
            NodeId nestedB = tree.NewLeaf(absStyle.Clone());
            NodeId staticB = tree.NewWithChildren(inflowStyle.Clone(), nestedB);
            NodeId nestedC = tree.NewLeaf(absStyle.Clone());
            NodeId staticC = tree.NewWithChildren(inflowStyle.Clone(), nestedC);
            NodeId directD = tree.NewLeaf(absStyle.Clone());
            NodeId nestedE = tree.NewLeaf(absStyle.Clone());
            NodeId staticE = tree.NewWithChildren(inflowStyle.Clone(), nestedE);
            NodeId cb = tree.NewWithChildren(
                new Style
                {
                    Display = display,
                    Position = Position.Relative,
                    FlexWrap = FlexWrap.WrapReverse,
                    FlexDirection = FlexDirection.RowReverse,
                    Size = Size.FromLengths(50f, 200f),
                },
                directA,
                staticB,
                staticC,
                directD,
                staticE);

            tree.ComputeLayout(cb, Size.MaxContent);

            NodeId[] expected = { directA, nestedB, nestedC, directD, nestedE };
            Assert.True(
                expected.SequenceEqual(tree.HoistedChildren(cb)),
                $"{display}: expected [{string.Join(", ", expected)}], actual [{string.Join(", ", tree.HoistedChildren(cb))}]");
        }
    }

    // An absolute box with auto insets is placed at its static position: where it would
    // have been placed in the normal flow of its DOM parent, expressed relative to its
    // containing block.
    [Fact]
    public void static_position_is_containing_block_relative()
    {
        foreach (Display display in new[] { Display.Block, Display.Flex, Display.Grid })
        {
            TaffyTree tree = new();
            NodeId abs = tree.NewLeaf(new Style
            {
                Position = Position.Absolute,
                Size = Size.FromLengths(30f, 30f),
            });
            NodeId sibling = tree.NewLeaf(LeafStyle(50f, 50f));
            NodeId staticParent = tree.NewWithChildren(
                new Style
                {
                    Display = display,
                    Margin = LeftTop(40f, 40f),
                    Size = Size.FromLengths(100f, 100f),
                },
                sibling,
                abs);
            NodeId cb = tree.NewWithChildren(
                new Style
                {
                    Display = display,
                    Position = Position.Relative,
                    Size = Size.FromLengths(200f, 200f),
                },
                staticParent);

            tree.ComputeLayout(cb, Size.MaxContent);

            // Static position is offset by the static parent's location within the CB
            Layout layout = tree.Layout(abs);
            Point<float> expected = display switch
            {
                // Block: below the in-flow sibling
                Display.Block => new Point<float>(40f, 90f),
                // Flex/grid: placed as if it were the sole item, at the content-box start
                Display.Flex => new Point<float>(40f, 40f),
                Display.Grid => new Point<float>(40f, 40f),
                _ => throw new InvalidOperationException("unreachable"),
            };
            AssertEqual(expected, layout.Location, $"{display}");
        }
    }

    // A fixed box bubbles past positioned (non-transformed) ancestors all the way to the root.
    [Fact]
    public void fixed_hoists_to_root()
    {
        TaffyTree tree = new();
        NodeId @fixed = tree.NewLeaf(new Style
        {
            Position = Position.Fixed,
            Inset = LeftTop(5f, 5f),
            Size = Size.FromLengths(10f, 10f),
        });
        NodeId positionedParent = tree.NewWithChildren(
            new Style
            {
                Position = Position.Relative,
                Margin = LeftTop(50f, 50f),
                Size = Size.FromLengths(50f, 50f),
            },
            @fixed);
        NodeId root = tree.NewWithChildren(new Style { Size = Size.FromLengths(200f, 200f) }, positionedParent);

        tree.ComputeLayout(root, Size.MaxContent);

        // Location is relative to the root, ignoring the positioned (but not fixed-CB) parent
        Assert.Equal(new Point<float>(5f, 5f), tree.Layout(@fixed).Location);
    }

    // An absolute descendant of an absolute box resolves against the absolute box
    // (which is positioned and therefore a containing block).
    [Fact]
    public void absolute_within_absolute()
    {
        TaffyTree tree = new();
        NodeId inner = tree.NewLeaf(new Style
        {
            Position = Position.Absolute,
            Inset = new Rect<LengthPercentageAuto>(
                LengthPercentageAuto.Auto,
                LengthPercentageAuto.Length(0f),
                LengthPercentageAuto.Auto,
                LengthPercentageAuto.Length(0f)),
            Size = Size.FromLengths(10f, 10f),
        });
        NodeId outer = tree.NewWithChildren(
            new Style
            {
                Position = Position.Absolute,
                Inset = LeftTop(20f, 20f),
                Size = Size.FromLengths(50f, 50f),
            },
            inner);
        NodeId root = tree.NewWithChildren(new Style { Size = Size.FromLengths(200f, 200f) }, outer);

        tree.ComputeLayout(root, Size.MaxContent);

        Assert.Equal(new Point<float>(20f, 20f), tree.Layout(outer).Location);
        // Relative to `outer`, its containing block
        Assert.Equal(new Point<float>(40f, 40f), tree.Layout(inner).Location);
    }

    // A fixed descendant surfaced while laying out an absolute box (at the abspos box's
    // containing block) is re-swept and continues bubbling to the root.
    [Fact]
    public void fixed_within_absolute_hoists_to_root()
    {
        TaffyTree tree = new();
        NodeId @fixed = tree.NewLeaf(new Style
        {
            Position = Position.Fixed,
            Inset = LeftTop(1f, 2f),
            Size = Size.FromLengths(10f, 10f),
        });
        NodeId abs = tree.NewWithChildren(
            new Style
            {
                Position = Position.Absolute,
                Inset = LeftTop(20f, 20f),
                Size = Size.FromLengths(50f, 50f),
            },
            @fixed);
        NodeId cb = tree.NewWithChildren(
            new Style
            {
                Position = Position.Relative,
                Margin = LeftTop(30f, 30f),
                Size = Size.FromLengths(100f, 100f),
            },
            abs);
        NodeId root = tree.NewWithChildren(new Style { Size = Size.FromLengths(200f, 200f) }, cb);

        tree.ComputeLayout(root, Size.MaxContent);

        Assert.Equal(new Point<float>(20f, 20f), tree.Layout(abs).Location);
        // Root-relative, unaffected by `cb` or `abs` offsets
        Assert.Equal(new Point<float>(1f, 2f), tree.Layout(@fixed).Location);
    }

    // Hoisting still works when the intermediate (static) ancestors hit the layout cache:
    // the cached LayoutOutput re-propagates the candidates without descending into
    // the skipped subtree.
    [Fact]
    public void hoisting_survives_cache_hits()
    {
        TaffyTree tree = new();
        NodeId abs = tree.NewLeaf(new Style
        {
            Position = Position.Absolute,
            Inset = LeftTop(10f, 20f),
            Size = Size.FromLengths(30f, 30f),
        });
        NodeId staticAncestor = tree.NewWithChildren(LeafStyle(50f, 50f), abs);
        for (int i = 0; i < 3; i++)
        {
            staticAncestor = tree.NewWithChildren(LeafStyle(50f, 50f), staticAncestor);
        }

        NodeId cb = tree.NewWithChildren(
            new Style
            {
                Position = Position.Relative,
                Size = Size.FromLengths(200f, 200f),
            },
            staticAncestor);

        tree.ComputeLayout(cb, Size.MaxContent);
        Assert.Equal(new Point<float>(10f, 20f), tree.Layout(abs).Location);

        // Recompute without dirtying anything: everything hits the cache
        tree.ComputeLayout(cb, Size.MaxContent);
        Assert.Equal(new Point<float>(10f, 20f), tree.Layout(abs).Location);

        // Relayout with the same constraints: intermediate nodes hit the cache
        tree.MarkDirty(cb);
        tree.ComputeLayout(cb, Size.MaxContent);
        Assert.Equal(new Point<float>(10f, 20f), tree.Layout(abs).Location);

        // Changing the abspos box's styles relays it out through its containing block
        Style newStyle = tree.Style(abs).Clone();
        Rect<LengthPercentageAuto> inset = newStyle.Inset;
        inset.Left = LengthPercentageAuto.Length(15f);
        newStyle.Inset = inset;
        tree.SetStyle(abs, newStyle);
        tree.ComputeLayout(cb, Size.MaxContent);
        Assert.Equal(new Point<float>(15f, 20f), tree.Layout(abs).Location);
    }

    // When the root's layout is served from the cache, the root positioning pass must not
    // re-add fixed boxes to the root's (still valid) hoisted-children list.
    [Fact]
    public void cached_root_does_not_duplicate_hoisted_children()
    {
        TaffyTree tree = new();
        NodeId @fixed = tree.NewLeaf(new Style
        {
            Position = Position.Fixed,
            Size = Size.FromLengths(30f, 30f),
        });
        NodeId inner = tree.NewWithChildren(LeafStyle(50f, 50f), @fixed);
        NodeId root = tree.NewWithChildren(LeafStyle(200f, 200f), inner);

        tree.ComputeLayout(root, Size.MaxContent);
        Assert.Equal(new[] { @fixed }, tree.HoistedChildren(root));

        tree.ComputeLayout(root, Size.MaxContent);
        Assert.Equal(new[] { @fixed }, tree.HoistedChildren(root));

        tree.MarkDirty(root);
        tree.ComputeLayout(root, Size.MaxContent);
        Assert.Equal(new[] { @fixed }, tree.HoistedChildren(root));
    }

    // Rounding recurses into hoisted boxes via their containing block, so a hoisted box's
    // rounded location is computed against the containing block's cumulative offset.
    [Fact]
    public void rounding_uses_containing_block_offsets()
    {
        TaffyTree tree = new();
        tree.EnableRounding();
        NodeId abs = tree.NewLeaf(new Style
        {
            Position = Position.Absolute,
            Inset = LeftTop(10.6f, 10.6f),
            Size = Size.FromLengths(10f, 10f),
        });
        NodeId staticParent = tree.NewWithChildren(LeafStyle(50f, 50f), abs);
        NodeId cb = tree.NewWithChildren(
            new Style
            {
                Position = Position.Relative,
                Margin = LeftTop(20.3f, 20.3f),
                Size = Size.FromLengths(100f, 100f),
            },
            staticParent);
        NodeId root = tree.NewWithChildren(new Style { Size = Size.FromLengths(200f, 200f) }, cb);

        tree.ComputeLayout(root, Size.MaxContent);

        // cb rounds to x=20; abs at cumulative 20.3+10.6=30.9 rounds to 31, i.e. 11 relative to cb
        Assert.Equal(new Point<float>(20f, 20f), tree.Layout(cb).Location);
        Assert.Equal(new Point<float>(11f, 11f), tree.Layout(abs).Location);
    }

    // When an intermediate ancestor becomes positioned (with a hot cache), it claims
    // the hoisted box away from the outer containing block, and vice versa when it
    // becomes static again.
    [Fact]
    public void claim_change_relayouts_with_hot_cache()
    {
        TaffyTree tree = new();
        NodeId abs = tree.NewLeaf(new Style
        {
            Position = Position.Absolute,
            Inset = LeftTop(10f, 20f),
            Size = Size.FromLengths(30f, 30f),
        });
        Style middleStyle = new()
        {
            Margin = LeftTop(40f, 40f),
            Size = Size.FromLengths(50f, 50f),
        };
        NodeId middle = tree.NewWithChildren(middleStyle.Clone(), abs);
        NodeId cb = tree.NewWithChildren(
            new Style
            {
                Position = Position.Relative,
                Size = Size.FromLengths(200f, 200f),
            },
            middle);

        tree.ComputeLayout(cb, Size.MaxContent);
        Assert.Equal(new Point<float>(10f, 20f), tree.Layout(abs).Location);

        // Make the middle node positioned: it now claims the abspos box, whose
        // location becomes relative to the middle node.
        Style positionedMiddle = middleStyle.Clone();
        positionedMiddle.Position = Position.Relative;
        tree.SetStyle(middle, positionedMiddle);
        tree.ComputeLayout(cb, Size.MaxContent);
        Assert.Equal(new Point<float>(10f, 20f), tree.Layout(abs).Location);
        // Page position moved by the middle node's offset
        Assert.Equal(new Point<float>(40f, 40f), tree.Layout(middle).Location);

        // Back to static: the outer containing block claims it again.
        tree.SetStyle(middle, middleStyle);
        tree.ComputeLayout(cb, Size.MaxContent);
        Assert.Equal(new Point<float>(10f, 20f), tree.Layout(abs).Location);
        Assert.Equal(new Point<float>(40f, 40f), tree.Layout(middle).Location);
    }

    // A fixed box hoisted to the root moves to an intermediate ancestor when that
    // ancestor starts claiming fixed boxes (position change with a hot cache), and back.
    [Fact]
    public void fixed_claim_change_relayouts_with_hot_cache()
    {
        TaffyTree tree = new();
        NodeId @fixed = tree.NewLeaf(new Style
        {
            Position = Position.Fixed,
            Inset = LeftTop(5f, 5f),
            Size = Size.FromLengths(10f, 10f),
        });
        Style absStyle = new()
        {
            Position = Position.Absolute,
            Inset = LeftTop(50f, 50f),
            Size = Size.FromLengths(80f, 80f),
        };
        NodeId abs = tree.NewWithChildren(absStyle.Clone(), @fixed);
        NodeId root = tree.NewWithChildren(new Style { Size = Size.FromLengths(200f, 200f) }, abs);

        tree.ComputeLayout(root, Size.MaxContent);
        // Fixed box is claimed by the root, not the abspos ancestor
        Assert.Equal(new Point<float>(5f, 5f), tree.Layout(@fixed).Location);
        Assert.Equal(new Point<float>(50f, 50f), tree.Layout(abs).Location);

        // Dirty only the fixed box: it must still be re-laid out via the root
        Style newFixedStyle = tree.Style(@fixed).Clone();
        Rect<LengthPercentageAuto> inset = newFixedStyle.Inset;
        inset.Left = LengthPercentageAuto.Length(7f);
        newFixedStyle.Inset = inset;
        tree.SetStyle(@fixed, newFixedStyle);
        tree.ComputeLayout(root, Size.MaxContent);
        Assert.Equal(new Point<float>(7f, 5f), tree.Layout(@fixed).Location);
    }

    // Content changes inside a hoisted subtree relayout correctly through the
    // containing block even when unrelated siblings hit the cache.
    [Fact]
    public void content_change_inside_hoisted_subtree()
    {
        TaffyTree tree = new();
        NodeId inner = tree.NewLeaf(LeafStyle(10f, 10f));
        NodeId abs = tree.NewWithChildren(
            new Style
            {
                Position = Position.Absolute,
                Inset = LeftTop(10f, 10f),
            },
            inner);
        NodeId staticParent = tree.NewWithChildren(LeafStyle(50f, 50f), abs);
        NodeId cb = tree.NewWithChildren(
            new Style
            {
                Position = Position.Relative,
                Size = Size.FromLengths(200f, 200f),
            },
            staticParent);

        tree.ComputeLayout(cb, Size.MaxContent);
        Assert.Equal(new Size<float>(10f, 10f), tree.Layout(abs).Size);

        // Grow the inner leaf: the hoisted abspos box must resize
        tree.SetStyle(inner, LeafStyle(20f, 30f));
        tree.ComputeLayout(cb, Size.MaxContent);
        Assert.Equal(new Size<float>(20f, 30f), tree.Layout(abs).Size);
    }

    // Hiding an ancestor of an out-of-flow box zeroes the box's rounded layout, whether or not
    // the hidden ancestor is the box's containing block.
    [Fact]
    public void hiding_an_ancestor_zeroes_out_of_flow_layout()
    {
        foreach (Position position in new[] { Position.Absolute, Position.Fixed })
        {
            foreach (Position parentPosition in new[] { Position.Static, Position.Relative })
            {
                TaffyTree tree = new();
                Style oofStyle = LeafStyle(30f, 30f);
                oofStyle.Position = position;
                NodeId oof = tree.NewLeaf(oofStyle);
                Style parentStyle = LeafStyle(50f, 50f);
                parentStyle.Position = parentPosition;
                NodeId parent = tree.NewWithChildren(parentStyle.Clone(), oof);
                NodeId root = tree.NewWithChildren(LeafStyle(200f, 200f), parent);

                tree.ComputeLayout(root, Size.MaxContent);
                Assert.Equal(new Size<float>(30f, 30f), tree.Layout(oof).Size);

                Style hiddenParentStyle = parentStyle;
                hiddenParentStyle.Display = Display.None;
                tree.SetStyle(parent, hiddenParentStyle);
                tree.ComputeLayout(root, Size.MaxContent);
                AssertEqual(Size.Zero, tree.UnroundedLayout(oof).Size, $"{position} in {parentPosition}");
                AssertEqual(Size.Zero, tree.Layout(oof).Size, $"{position} in {parentPosition}");
            }
        }
    }
}
