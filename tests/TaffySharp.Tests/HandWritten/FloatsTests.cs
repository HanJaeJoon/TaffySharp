namespace TaffySharp.Tests.HandWritten;

public class FloatsTests
{
    // Regression test for https://wpt.live/css/CSS2/floats-clear/floats-146.xht
    //
    // A right float placed after two left floats must not be placed higher than the
    // outer top of the earlier floats (CSS2 float rule 5). This scenario previously
    // panicked in FloatContext.SubdivideSegment when the float's end coincided
    // with an existing segment boundary due to floating point imprecision.
    [Fact]
    public void float_no_higher_than_earlier_floats()
    {
        TaffyTree<TestNodeContext> taffy = new();

        Func<float, float, Float, Style> block = (width, height, @float) => new Style
        {
            Display = Display.Block,
            Float = @float,
            Size = Size.FromLengths(width, height),
        };

        // Spacer block advancing the flow position to a fractional y offset (43.2)
        NodeId spacer = taffy.NewLeaf(block(400f, 43.2f, Float.None));
        NodeId floatA = taffy.NewLeaf(block(258f, 42f, Float.Left));
        NodeId floatB = taffy.NewLeaf(block(298f, 42f, Float.Left));
        NodeId floatC = taffy.NewLeaf(block(98f, 42f, Float.Right));

        NodeId root = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Block,
                Size = new Size<Dimension>(Dimension.Length(400f), Dimension.Auto),
            },
            spacer,
            floatA,
            floatB,
            floatC);

        taffy.ComputeLayout(root, Size.MaxContent);

        Layout layoutA = taffy.Layout(floatA);
        Layout layoutB = taffy.Layout(floatB);
        Layout layoutC = taffy.Layout(floatC);

        // A is placed below the spacer
        Assert.Equal(new Point<float>(0f, 43f), layoutA.Location);
        // B does not fit next to A, so is placed below it
        Assert.Equal(new Point<float>(0f, 85f), layoutB.Location);
        // C must not be placed higher than B (it fits next to B on the right)
        Assert.Equal(new Point<float>(302f, 85f), layoutC.Location);
    }

    // Regression test for https://wpt.live/css/CSS2/floats-clear/margin-collapse-033.xht
    //
    // Floats that occupy no horizontal space (e.g. a zero-width float) still affect
    // `clear`: a cleared block must be placed past the bottom of all floats on the
    // relevant side, regardless of their width.
    [Fact]
    public void clear_past_zero_width_float()
    {
        TaffyTree<TestNodeContext> taffy = new();

        // float: left with auto width resolving to 0
        NodeId floated = taffy.NewLeaf(new Style
        {
            Display = Display.Block,
            Float = Float.Left,
            Size = new Size<Dimension>(Dimension.Auto, Dimension.Length(1f)),
        });
        NodeId cleared = taffy.NewLeaf(new Style { Display = Display.Block, Clear = Clear.Left });
        NodeId sibling = taffy.NewLeaf(new Style
        {
            Display = Display.Block,
            Margin = new Rect<LengthPercentageAuto>(
                LengthPercentageAuto.Zero,
                LengthPercentageAuto.Zero,
                LengthPercentageAuto.Length(99f),
                LengthPercentageAuto.Zero),
        });

        NodeId container = taffy.NewWithChildren(
            new Style
            {
                Display = Display.Block,
                Size = new Size<Dimension>(Dimension.Length(100f), Dimension.Auto),
            },
            floated,
            cleared,
            sibling);
        NodeId root = taffy.NewWithChildren(new Style { Display = Display.Block }, container);

        taffy.ComputeLayout(root, Size.MaxContent);

        // The cleared (self-collapsing) block is placed below the float (y=1). Its clearance
        // prevents the sibling's 99px margin from collapsing with preceding margins, so the
        // container's height is 1 + 99 = 100.
        Assert.Equal(100f, taffy.Layout(container).Size.Height);
    }

    private static Style FloatBlock(float width, float height, Float @float) => new()
    {
        Display = Display.Block,
        Float = @float,
        Size = Size.FromLengths(width, height),
    };

    private static Style RootStyle(float width) => new()
    {
        Display = Display.Block,
        Size = new Size<Dimension>(Dimension.Length(width), Dimension.Auto),
    };

    // Regression test for https://wpt.live/css/CSS2/floats/floats-rule3-outside-left-001.xht
    //
    // CSS2 float rule 7: a float that is wider than its containing block and has no other
    // float beside it may overflow the containing block's edge rather than being pushed down.
    [Fact]
    public void oversized_float_overflows_containing_block()
    {
        TaffyTree<TestNodeContext> taffy = new();

        NodeId floatA = taffy.NewLeaf(FloatBlock(150f, 50f, Float.Left));
        NodeId root = taffy.NewWithChildren(RootStyle(100f), floatA);

        taffy.ComputeLayout(root, Size.MaxContent);

        Assert.Equal(new Point<float>(0f, 0f), taffy.Layout(floatA).Location);
    }

    // CSS2 float rules 3 & 7: a float with another float beside it must not overlap that float
    // (rule 3) nor extend past the containing block's opposite edge (rule 7) - it must move down.
    [Fact]
    public void float_beside_existing_float_moves_down_instead_of_overflowing()
    {
        TaffyTree<TestNodeContext> taffy = new();

        // Rule 3: an opposite-direction float that doesn't fit must move down, not overlap
        NodeId left = taffy.NewLeaf(FloatBlock(60f, 50f, Float.Left));
        NodeId right = taffy.NewLeaf(FloatBlock(60f, 50f, Float.Right));
        // Rule 7: a same-direction float that doesn't fit must move down, not overflow the
        // containing block's trailing edge
        NodeId left2 = taffy.NewLeaf(FloatBlock(60f, 50f, Float.Left));

        NodeId root = taffy.NewWithChildren(RootStyle(100f), left, right, left2);

        taffy.ComputeLayout(root, Size.MaxContent);

        Assert.Equal(new Point<float>(0f, 0f), taffy.Layout(left).Location);
        Assert.Equal(new Point<float>(40f, 50f), taffy.Layout(right).Location);
        Assert.Equal(new Point<float>(0f, 100f), taffy.Layout(left2).Location);
    }

    // Regression test for auto-margins-used-values-with-floats.tentative.html.
    [Fact]
    public void auto_margins_include_float_avoidance_offsets()
    {
        foreach (Direction direction in new[] { Direction.Ltr, Direction.Rtl })
        {
            foreach ((float leftFloatWidth, float rightFloatWidth) in new[] { (0f, 20f), (20f, 0f), (20f, 20f), (0f, 0f) })
            {
                TaffyTree<TestNodeContext> taffy = new();
                NodeId leftFloat = taffy.NewLeaf(FloatBlock(leftFloatWidth, 40f, Float.Left));
                NodeId rightFloat = taffy.NewLeaf(FloatBlock(rightFloatWidth, 40f, Float.Right));
                (bool LeftAuto, bool RightAuto)[] autoSides = { (true, true), (true, false), (false, true) };
                List<NodeId> boxes = autoSides
                    .Select(sides => taffy.NewLeaf(new Style
                    {
                        Display = Display.FlowRoot,
                        Direction = direction,
                        Size = Size.FromLengths(40f, 10f),
                        Margin = new Rect<LengthPercentageAuto>(
                            sides.LeftAuto ? LengthPercentageAuto.Auto : LengthPercentageAuto.Zero,
                            sides.RightAuto ? LengthPercentageAuto.Auto : LengthPercentageAuto.Zero,
                            LengthPercentageAuto.Auto,
                            LengthPercentageAuto.Auto),
                    }))
                    .ToList();
                NodeId[] children = new[] { leftFloat, rightFloat }.Concat(boxes).ToArray();
                NodeId root = taffy.NewWithChildren(
                    new Style
                    {
                        Display = Display.FlowRoot,
                        Direction = direction,
                        Size = new Size<Dimension>(Dimension.Length(100f), Dimension.Auto),
                        Padding = new Rect<LengthPercentage>(
                            LengthPercentage.Length(5f),
                            LengthPercentage.Length(5f),
                            LengthPercentage.Length(5f),
                            LengthPercentage.Length(5f)),
                    },
                    children);

                taffy.ComputeLayout(root, Size.MaxContent);

                for (int index = 0; index < boxes.Count; index++)
                {
                    (bool leftAuto, bool rightAuto) = autoSides[index];
                    Layout layout = taffy.Layout(boxes[index]);
                    float freeSpace = 90f - leftFloatWidth - rightFloatWidth - 40f;
                    float alignmentMargin = freeSpace / ((leftAuto ? 1 : 0) + (rightAuto ? 1 : 0));
                    float leftGap = leftAuto ? alignmentMargin : 0f;
                    Assert.Equal(new Point<float>(5f + leftFloatWidth + leftGap, 5f + index * 10f), layout.Location);
                    Assert.Equal(new Size<float>(40f, 10f), layout.Size);
                    Assert.Equal(leftAuto ? leftFloatWidth + alignmentMargin : 0f, layout.Margin.Left);
                    Assert.Equal(rightAuto ? rightFloatWidth + alignmentMargin : 0f, layout.Margin.Right);
                    Assert.Equal(0f, layout.Margin.Top);
                    Assert.Equal(0f, layout.Margin.Bottom);
                }
            }
        }
    }

    [Fact]
    public void float_avoiding_auto_margins_in_nested_block_ignore_relative_insets_and_clearance()
    {
        foreach (Direction direction in new[] { Direction.Ltr, Direction.Rtl })
        {
            TaffyTree<TestNodeContext> taffy = new();
            NodeId leftFloat = taffy.NewLeaf(FloatBlock(40f, 40f, Float.Left));
            NodeId rightFloat = taffy.NewLeaf(FloatBlock(60f, 40f, Float.Right));
            Style boxStyle = new()
            {
                Display = Display.FlowRoot,
                Direction = direction,
                Size = Size.FromLengths(40f, 10f),
                Margin = new Rect<LengthPercentageAuto>(
                    LengthPercentageAuto.Auto,
                    LengthPercentageAuto.Auto,
                    LengthPercentageAuto.Zero,
                    LengthPercentageAuto.Zero),
            };
            Style relativeStyle = boxStyle.Clone();
            relativeStyle.Position = Position.Relative;
            relativeStyle.Inset = new Rect<LengthPercentageAuto>(
                LengthPercentageAuto.Length(7f),
                LengthPercentageAuto.Auto,
                LengthPercentageAuto.Auto,
                LengthPercentageAuto.Auto);
            NodeId relativeBox = taffy.NewLeaf(relativeStyle);
            Style clearedStyle = boxStyle;
            clearedStyle.Clear = Clear.Both;
            NodeId clearedBox = taffy.NewLeaf(clearedStyle);
            NodeId nested = taffy.NewWithChildren(
                new Style
                {
                    Display = Display.Block,
                    Direction = direction,
                    Margin = new Rect<LengthPercentageAuto>(
                        LengthPercentageAuto.Length(20f),
                        LengthPercentageAuto.Length(30f),
                        LengthPercentageAuto.Zero,
                        LengthPercentageAuto.Zero),
                    Padding = new Rect<LengthPercentage>(
                        LengthPercentage.Length(5f),
                        LengthPercentage.Length(7f),
                        LengthPercentage.Zero,
                        LengthPercentage.Zero),
                    Border = new Rect<LengthPercentage>(
                        LengthPercentage.Length(3f),
                        LengthPercentage.Length(5f),
                        LengthPercentage.Zero,
                        LengthPercentage.Zero),
                },
                relativeBox,
                clearedBox);
            NodeId root = taffy.NewWithChildren(
                new Style
                {
                    Display = Display.FlowRoot,
                    Direction = direction,
                    Size = new Size<Dimension>(Dimension.Length(200f), Dimension.Auto),
                    Padding = new Rect<LengthPercentage>(
                        LengthPercentage.Length(10f),
                        LengthPercentage.Length(10f),
                        LengthPercentage.Length(10f),
                        LengthPercentage.Length(10f)),
                },
                leftFloat,
                rightFloat,
                nested);

            taffy.ComputeLayout(root, Size.MaxContent);

            Layout relativeLayout = taffy.Layout(relativeBox);
            Assert.Equal(new Point<float>(47f, 0f), relativeLayout.Location);
            Assert.Equal(32f, relativeLayout.Margin.Left);
            Assert.Equal(38f, relativeLayout.Margin.Right);
            Layout clearedLayout = taffy.Layout(clearedBox);
            Assert.Equal(new Point<float>(43f, 40f), clearedLayout.Location);
            Assert.Equal(35f, clearedLayout.Margin.Left);
            Assert.Equal(35f, clearedLayout.Margin.Right);
            Layout nestedLayout = taffy.Layout(nested);
            Assert.Equal(new Point<float>(30f, 10f), nestedLayout.Location);
            Assert.Equal(20f, nestedLayout.Margin.Left);
            Assert.Equal(30f, nestedLayout.Margin.Right);
        }
    }
}
