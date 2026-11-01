namespace TaffySharp;

/// <summary>The state shared by all boxes in a block formatting context (currently its floats).</summary>
public sealed class BlockFormattingContext
{
    internal readonly FloatContext FloatContext = new();

    public BlockFormattingContext()
    {
    }

    /// <summary>Create the <see cref="BlockContext"/> for the root of this block formatting context.</summary>
    public BlockContext RootBlockContext() => new(this, 0f, new float[2], new float[2], true, new bool[2]);
}

/// <summary>
/// The view of a <see cref="BlockFormattingContext"/> from one block container within it: the container's offset
/// from the formatting context root and its horizontal insets.
/// </summary>
public sealed class BlockContext
{
    private readonly BlockFormattingContext _bfc;
    private readonly float _yOffset;
    private readonly float[] _insets;
    private readonly float[] _contentBoxInsets;
    private float _floatContentContribution = float.NegativeInfinity;
    private readonly bool _isRoot;
    private bool[] _adjoiningFloats;
    private bool[]? _topAdjoiningFloats;

    internal BlockContext(BlockFormattingContext bfc, float yOffset, float[] insets, float[] contentBoxInsets, bool isRoot, bool[] adjoiningFloats)
    {
        _bfc = bfc;
        _yOffset = yOffset;
        _insets = insets;
        _contentBoxInsets = contentBoxInsets;
        _isRoot = isRoot;
        _adjoiningFloats = adjoiningFloats;
    }

    /// <summary>Create a context for a child block container of this context's container.</summary>
    public BlockContext SubContext(float additionalYOffset, float[] insets)
    {
        float[] newInsets = { _insets[0] + insets[0], _insets[1] + insets[1] };
        // Floats adjoining the parent's current strut also adjoin this block's top strut
        // (if this block's top margin collapses with its first child's, which is checked separately)
        return new BlockContext(_bfc, _yOffset + additionalYOffset, newInsets, (float[])newInsets.Clone(), false, (bool[])_adjoiningFloats.Clone());
    }

    /// <summary>Whether this is the root of its block formatting context.</summary>
    public bool IsBfcRoot() => _isRoot;

    public void SetWidth(float availableWidth) => _bfc.FloatContext.SetWidth(availableWidth);

    public void ApplyContentBoxInset(float[] contentBoxXInsets)
    {
        _contentBoxInsets[0] = _insets[0] + contentBoxXInsets[0];
        _contentBoxInsets[1] = _insets[1] + contentBoxXInsets[1];
    }

    public bool HasFloats() => _bfc.FloatContext.HasFloats();

    public bool HasActiveFloats(float minY) => _bfc.FloatContext.HasActiveFloats(minY + _yOffset);

    public Point<float> PlaceFloatedBox(Size<float> floatedBox, float minY, FloatDirection direction, Clear clear, bool adjoinsUnresolvedStrut)
    {
        if (adjoinsUnresolvedStrut)
        {
            _adjoiningFloats[(int)direction] = true;
        }

        Point<float> pos = _bfc.FloatContext.PlaceFloatedBox(floatedBox, minY + _yOffset, _contentBoxInsets, direction, clear);
        pos.Y -= _yOffset;
        pos.X -= _insets[0];
        _floatContentContribution = MathHelpers.Max(_floatContentContribution, pos.Y + floatedBox.Height);
        return pos;
    }

    public ContentSlot FindContentSlot(float minY, Clear clear, int? after)
    {
        ContentSlot slot = _bfc.FloatContext.FindContentSlot(minY + _yOffset, _contentBoxInsets, clear, after);
        slot.Y -= _yOffset;
        slot.X -= _insets[0];
        return slot;
    }

    public BfcSlot FindBfcSlot(float minY, float[] margins, Direction direction, Clear clear, int? after)
    {
        BfcSlot slot = _bfc.FloatContext.FindBfcSlot(minY + _yOffset, _contentBoxInsets, margins, direction, clear, after);
        slot.Y -= _yOffset;
        slot.X -= _insets[0];
        return slot;
    }

    public float? ClearedThreshold(Clear clear) => _bfc.FloatContext.ClearedThreshold(clear) - _yOffset;

    /// <summary>Whether a float on the side(s) cleared by <paramref name="clear"/> adjoins the current margin-collapse strut.</summary>
    public bool HasAdjoiningFloat(Clear clear) => clear switch
    {
        Clear.Left => _adjoiningFloats[0],
        Clear.Right => _adjoiningFloats[1],
        Clear.Both => _adjoiningFloats[0] || _adjoiningFloats[1],
        _ => false,
    };

    internal void MergeAdjoiningFloats(bool[] flags)
    {
        _adjoiningFloats[0] |= flags[0];
        _adjoiningFloats[1] |= flags[1];
    }

    internal void CommitStrut()
    {
        _topAdjoiningFloats ??= _adjoiningFloats;
        _adjoiningFloats = new bool[2];
    }

    internal bool[] TopAdjoiningFloats() => _topAdjoiningFloats ?? _adjoiningFloats;

    public void AddChildFloatedContentHeightContribution(float childContribution) =>
        _floatContentContribution = MathHelpers.Max(_floatContentContribution, childContribution);

    public float FloatedContentHeightContribution() => _floatContentContribution;
}

/// <summary>Per-child data used by the block layout algorithm.</summary>
internal sealed class BlockItem
{
    public NodeId NodeId;
    public uint Order;
    public bool IsTable;
    public bool IsCompressibleReplaced;
    public bool IsInSameBfc;
    public Float Float;
    public Clear Clear;
    public Size<Dimension> SizeStyle;
    public Size<float?> Size;
    public Size<float?> MinSize;
    public Size<float?> MaxSize;
    public Point<Overflow> Overflow;
    public Contain Contain;
    public float ScrollbarWidth;
    public Position Position;
    public Direction Direction;
    public AlignItems? AlignSelf;
    public AlignItems? JustifySelf;
    public Rect<LengthPercentageAuto> Inset;
    public Rect<LengthPercentageAuto> Margin;
    public Rect<float> Padding;
    public Rect<float> Border;
    public Size<float> PaddingBorderSum;
    public Size<float> ComputedSize;
    public Point<float> StaticPosition;
    public bool CanBeCollapsedThrough;
    public Layout? FinalLayout;
    public OofCandidates OofCandidates;
}

public static partial class Compute
{
    /// <summary>
    /// The offset of the in-flow content of a block container with the given <c>align-content</c>
    /// (the whole stack of children is a single alignment subject).
    /// </summary>
    public static float ComputeBlockAlignContentOffset(AlignContent alignContent, float freeSpace)
    {
        AlignContentKeyword keyword = CommonAlignment.ApplyAlignmentFallback(freeSpace, 1, alignContent);
        return CommonAlignment.ComputeAlignmentOffset(freeSpace, 1, 0f, keyword, false, true);
    }

    /// <summary>Computes the layout of a block container and its direct children.</summary>
    public static LayoutOutput ComputeBlockLayout(ILayoutBlockContainer tree, NodeId nodeId, LayoutInput inputs, BlockContext? blockCtx)
    {
        Size<float?> knownDimensions = inputs.KnownDimensions;
        Size<float?> parentSize = inputs.ParentSize;
        RunMode runMode = inputs.RunMode;
        Func<object, float, float> calc = tree.ResolveCalcValue;
        IBlockContainerStyle style = tree.GetBlockContainerStyle(nodeId);

        // Pull these out earlier to avoid borrowing issues
        Point<Overflow> overflow = style.Overflow;
        bool isScrollContainer = overflow.X.IsScrollContainer() || overflow.Y.IsScrollContainer();
        Contain contain = style.Contain;
        // css-align-3 5.1.1: a non-`normal` `align-content` makes a block container establish an
        // independent formatting context. Layout and paint containment do too.
        bool establishesNewBfc = isScrollContainer
            || style.AlignContent.Keyword != AlignContentKeyword.Normal
            || contain.EstablishesIndependentFormattingContext();
        float? aspectRatio = style.AspectRatio;
        Rect<float> padding = style.Padding.ResolveOrZero(parentSize.Width, calc);
        Rect<float> border = style.Border.ResolveOrZero(parentSize.Width, calc);
        Size<float> paddingBorderSize = padding.Add(border).SumAxes();
        Size<float> boxSizingAdjustment = style.BoxSizing == BoxSizing.ContentBox ? paddingBorderSize : Size.Zero;

        Size<float?> minSize = style.MinSize
            .MaybeResolve(parentSize, calc)
            .MaybeApplyAspectRatio(aspectRatio)
            .MaybeAdd(boxSizingAdjustment);
        Size<float?> maxSize = style.MaxSize
            .MaybeResolve(parentSize, calc)
            .MaybeApplyAspectRatio(aspectRatio)
            .MaybeAdd(boxSizingAdjustment);
        Size<float?> clampedStyleSize = inputs.SizingMode == SizingMode.InherentSize
            ? style.Size
                .MaybeResolve(parentSize, calc)
                .MaybeApplyAspectRatio(aspectRatio)
                .MaybeAdd(boxSizingAdjustment)
                .MaybeClamp(minSize, maxSize)
            : Size.None;

        // If both min and max in a given axis are set and max <= min then this determines the size in that axis
        Size<float?> minMaxDefiniteSize = minSize.ZipMap(maxSize, (min, max) => min is float mn && max is float mx && mx <= mn ? mn : (float?)null);

        Size<float?> styledBasedKnownDimensions =
            knownDimensions.Or(minMaxDefiniteSize).Or(clampedStyleSize).MaybeMax(paddingBorderSize);

        // Short-circuit layout if the container's size is fully determined by the container's size and the run mode
        // is ComputeSize (and thus the container's size is all that we're interested in)
        if (runMode == RunMode.ComputeSize)
        {
            if (styledBasedKnownDimensions.Width is float w && styledBasedKnownDimensions.Height is float h)
            {
                return LayoutOutput.FromOuterSize(new Size<float>(w, h));
            }

            // We can also short-circuit if the width is known and only the width has been requested.
            if (inputs.Axis == RequestedAxis.Horizontal && styledBasedKnownDimensions.Width is float width)
            {
                return LayoutOutput.FromOuterSize(new Size<float>(width, 0f));
            }
        }

        LayoutInput innerInputs = inputs;
        innerInputs.KnownDimensions = styledBasedKnownDimensions;

        // Use the block formatting context that was passed in, or else create a new one
        LayoutOutput output;
        if (blockCtx != null && !establishesNewBfc)
        {
            output = ComputeBlockInner(tree, nodeId, innerInputs, blockCtx);
        }
        else
        {
            BlockFormattingContext rootBfc = new();
            BlockContext rootCtx = rootBfc.RootBlockContext();
            output = ComputeBlockInner(tree, nodeId, innerInputs, rootCtx);
        }

        // Layout containment suppresses the box's baseline for baseline-alignment purposes
        if (contain.SuppressesBaseline())
        {
            output.Baselines = Baselines.None;
        }

        return output;
    }

    private static LayoutOutput ComputeBlockInner(ILayoutBlockContainer tree, NodeId nodeId, LayoutInput inputs, BlockContext blockCtx)
    {
        Size<float?> knownDimensions = inputs.KnownDimensions;
        Size<float?> parentSize = inputs.ParentSize;
        Size<AvailableSpace> availableSpace = inputs.AvailableSpace;
        RunMode runMode = inputs.RunMode;
        Line<bool> verticalMarginsAreCollapsible = inputs.VerticalMarginsAreCollapsible;
        Func<object, float, float> calc = tree.ResolveCalcValue;

        IBlockContainerStyle style = tree.GetBlockContainerStyle(nodeId);
        Rect<LengthPercentage> rawPadding = style.Padding;
        Rect<LengthPercentage> rawBorder = style.Border;
        Rect<LengthPercentageAuto> rawMargin = style.Margin;
        float? aspectRatio = style.AspectRatio;
        Rect<float> padding = rawPadding.ResolveOrZero(parentSize.Width, calc);
        Rect<float> border = rawBorder.ResolveOrZero(parentSize.Width, calc);
        Direction direction = style.Direction;

        // Scrollbar gutters are reserved when the `overflow` property is set to `Overflow::Scroll`.
        // However, the axis are switched (transposed) because a node that scrolls vertically needs
        // *horizontal* space to be reserved for a scrollbar
        float scrollbarWidth = style.ScrollbarWidth;
        Point<float> offsets = style.Overflow.Transpose().Map(o => o == Overflow.Scroll ? scrollbarWidth : 0f);
        Rect<float> scrollbarGutter = direction == Direction.Rtl
            ? new Rect<float>(offsets.X, 0f, 0f, offsets.Y)
            : new Rect<float>(0f, offsets.X, 0f, offsets.Y);

        Rect<float> paddingBorder = padding.Add(border);
        Size<float> paddingBorderSize = paddingBorder.SumAxes();
        Rect<float> contentBoxInset = paddingBorder.Add(scrollbarGutter);

        // Apply content box inset
        blockCtx.ApplyContentBoxInset(new[] { contentBoxInset.Left, contentBoxInset.Right });

        Size<float> boxSizingAdjustment = style.BoxSizing == BoxSizing.ContentBox ? paddingBorderSize : Size.Zero;
        Size<float?> size = style.Size
            .MaybeResolve(parentSize, calc)
            .MaybeApplyAspectRatio(aspectRatio)
            .MaybeAdd(boxSizingAdjustment);
        Size<float?> minSize = style.MinSize
            .MaybeResolve(parentSize, calc)
            .MaybeApplyAspectRatio(aspectRatio)
            .MaybeAdd(boxSizingAdjustment);
        Size<float?> maxSize = style.MaxSize
            .MaybeResolve(parentSize, calc)
            .MaybeApplyAspectRatio(aspectRatio)
            .MaybeAdd(boxSizingAdjustment);

        // css-sizing-4: a definite size in one axis transfers through `aspect-ratio` to make the other
        // definite. Only a newly-filled axis is adopted (and clamped); an incoming known size is left
        // as the parent resolved it (re-clamping would undo padding/border overrides).
        Size<float?> derived = knownDimensions.MaybeApplyAspectRatio(aspectRatio).MaybeClamp(minSize, maxSize);
        knownDimensions = new Size<float?>(knownDimensions.Width ?? derived.Width, knownDimensions.Height ?? derived.Height);

        Size<float?> percentageBasisDimensions = new(
            knownDimensions.Width,
            inputs.KnownDimensionsAreDefinite.Height ? knownDimensions.Height : null);
        Size<float?> containerContentBoxSize = percentageBasisDimensions.MaybeSub(contentBoxInset.SumAxes());

        Point<Overflow> overflow = style.Overflow;
        bool isScrollContainer = overflow.X.IsScrollContainer() || overflow.Y.IsScrollContainer();
        bool establishesNewBfc = isScrollContainer
            || style.AlignContent.Keyword != AlignContentKeyword.Normal
            || style.Contain.EstablishesIndependentFormattingContext();

        // Determine margin collapsing behaviour
        //
        // Whether a child's margins could collapse through each of this box's block-axis edges: the
        // box does not establish a new BFC and has neither border nor padding on that side.
        bool isOutOfFlow = style.Position.IsOutOfFlow();
        Line<bool> childrenMarginsAdjoinOwnEdges = new(
            verticalMarginsAreCollapsible.Start && !establishesNewBfc && !isOutOfFlow && padding.Top == 0f && border.Top == 0f,
            verticalMarginsAreCollapsible.End && !establishesNewBfc && !isOutOfFlow && padding.Bottom == 0f && border.Bottom == 0f);
        Line<bool> ownMarginsCollapseWithChildren = new(
            childrenMarginsAdjoinOwnEdges.Start,
            childrenMarginsAdjoinOwnEdges.End && size.Height is null);
        bool hasStylesPreventingBeingCollapsedThrough = !style.IsBlock
            || blockCtx.IsBfcRoot()
            || establishesNewBfc
            || isOutOfFlow
            || padding.Top > 0f
            || padding.Bottom > 0f
            || border.Top > 0f
            || border.Bottom > 0f;
        TextAlign textAlign = style.TextAlign;
        AlignContent alignContent = style.AlignContent;
        AlignItems justifyItems = style.JustifyItems;

        // 1. Generate items
        List<BlockItem> items = GenerateBlockItemList(tree, nodeId, containerContentBoxSize);

        // 2. Compute container width
        float containerOuterWidth;
        if (knownDimensions.Width is float knownWidth)
        {
            containerOuterWidth = knownWidth;
        }
        else
        {
            AvailableSpace availableWidth = availableSpace.Width.MaybeSub(contentBoxInset.HorizontalAxisSum());
            float intrinsicWidth = DetermineContentBasedContainerWidth(tree, items, availableWidth) + contentBoxInset.HorizontalAxisSum();
            containerOuterWidth = intrinsicWidth.MaybeClamp(minSize.Width, maxSize.Width).MaybeMax(paddingBorderSize.Width);
        }

        // Short-circuit if computing size and both dimensions known
        if (runMode == RunMode.ComputeSize && knownDimensions.Height is float knownHeight)
        {
            return LayoutOutput.FromOuterSize(new Size<float>(containerOuterWidth, knownHeight));
        }

        // We can also short-circuit if the width is known and only the width has been requested.
        if (runMode == RunMode.ComputeSize && inputs.Axis == RequestedAxis.Horizontal)
        {
            return LayoutOutput.FromOuterSize(new Size<float>(containerOuterWidth, 0f));
        }

        // Under `SizingMode::ContentSize` the container's own `height`/`min-height` are ignored,
        // so they cannot serve as the percentage basis for children either.
        float? containerPercentageResolutionHeight = percentageBasisDimensions.Height
            ?? (inputs.SizingMode == SizingMode.InherentSize ? size.Height.MaybeMax(minSize.Height) : null);

        // 3. Perform final item layout and return content height
        //
        // Percentage padding and borders resolve against the *containing block's* width
        // (`parent_size`), not the box's own width. Fall back to the box's own width when the
        // parent size is unknown (e.g. at the root of the layout tree).
        float percentageResolutionWidth = parentSize.Width ?? containerOuterWidth;
        Rect<float> resolvedPadding = rawPadding.ResolveOrZero(percentageResolutionWidth, calc);
        Rect<float> resolvedBorder = rawBorder.ResolveOrZero(percentageResolutionWidth, calc);
        Rect<float> resolvedContentBoxInset = resolvedPadding.Add(resolvedBorder).Add(scrollbarGutter);

        (Rect<float> inflowOverflowRect, float intrinsicOuterHeight, CollapsibleMarginSet firstChildTopMarginSet,
            CollapsibleMarginSet lastChildBottomMarginSet, float? firstBaseline) = PerformFinalLayoutOnInFlowChildren(
            tree,
            runMode,
            items,
            containerOuterWidth,
            containerPercentageResolutionHeight,
            contentBoxInset,
            resolvedContentBoxInset,
            resolvedBorder,
            textAlign,
            justifyItems,
            direction,
            ownMarginsCollapseWithChildren,
            childrenMarginsAdjoinOwnEdges,
            isScrollContainer,
            blockCtx);

        // Root BFCs contain floats
        if (blockCtx.IsBfcRoot() || establishesNewBfc)
        {
            intrinsicOuterHeight = MathHelpers.Max(intrinsicOuterHeight, blockCtx.FloatedContentHeightContribution());
        }

        float containerOuterHeight = (knownDimensions.Height ?? intrinsicOuterHeight.MaybeClamp(minSize.Height, maxSize.Height))
            .MaybeMax(paddingBorderSize.Height);
        Size<float> finalOuterSize = new(containerOuterWidth, containerOuterHeight);

        // CSS2 8.3.1: the bottom margin of a block with `height: auto` collapses with its last
        // in-flow child's bottom margin only if the box's `min-height` is less than the box's
        // used height. When `min-height` determines the used height, the last child's bottom
        // margin stays inside the box. (`max-height` has no such effect.)
        bool heightConstrainedByMinHeight = minSize.Height is float minHeight && minHeight > 0f && minHeight >= containerOuterHeight;
        bool ownBottomMarginCollapsesWithChildren = ownMarginsCollapseWithChildren.End && !heightConstrainedByMinHeight;

        // Apply `align-content` to in-flow non-floated items if requested. The per-item layouts were
        // held back in `FinalLayout` so that this step can shift `Location.Y` before tree commit.
        //
        // For block layout the entire stack of in-flow children is treated as a single alignment
        // subject (`num_items = 1`), so distribution keywords use the single-subject fallback and the
        // whole group shifts by one offset.
        if (alignContent.Keyword != AlignContentKeyword.Normal)
        {
            float containerInnerHeight = containerOuterHeight - resolvedContentBoxInset.VerticalAxisSum();
            float inflowContentHeight = intrinsicOuterHeight - resolvedContentBoxInset.VerticalAxisSum();
            float freeSpace = containerInnerHeight - inflowContentHeight;
            float groupOffset = ComputeBlockAlignContentOffset(alignContent, freeSpace);
            firstBaseline = firstBaseline + groupOffset;
            foreach (BlockItem item in items)
            {
                if (item.FinalLayout is Layout layout)
                {
                    layout.Location.Y += groupOffset;
                    item.FinalLayout = layout;
                }

                // The static positions of out-of-flow children move with the in-flow content
                if (item.Position.IsOutOfFlow())
                {
                    item.StaticPosition.Y += groupOffset;
                }
            }

            bool anyInFlow = items.Any(item => item.FinalLayout.HasValue);
            if (anyInFlow)
            {
                inflowOverflowRect = Rect.Zero;
                foreach (BlockItem item in items)
                {
                    if (item.FinalLayout is Layout layout)
                    {
                        Point<float> contributionLocation = direction.IsRtl()
                            ? new Point<float>(
                                containerOuterWidth - (layout.Location.X + layout.Size.Width) - resolvedBorder.Right,
                                layout.Location.Y - resolvedBorder.Top)
                            : new Point<float>(layout.Location.X - resolvedBorder.Left, layout.Location.Y - resolvedBorder.Top);
                        inflowOverflowRect = inflowOverflowRect.Union(ScrollableOverflow.ComputeScrollableOverflowContribution(
                            contributionLocation,
                            layout.Size,
                            layout.ScrollableOverflowRect,
                            item.Overflow,
                            item.Contain,
                            isScrollContainer));
                    }
                }
            }
        }

        // Determine whether this node can be collapsed through
        bool allInFlowChildrenCanBeCollapsedThrough = items.All(item =>
            item.Float.IsFloated() || item.Position.IsOutOfFlow() || item.CanBeCollapsedThrough);

        // CSS2 8.3.1: a box's top and bottom margins collapse through it only if its *used* height
        // is zero, so `height`/`min-height`/`max-height` are judged by the height they resolve to.
        bool canBeCollapsedThrough = !hasStylesPreventingBeingCollapsedThrough
            && containerOuterHeight == 0f
            && allInFlowChildrenCanBeCollapsedThrough;

        LayoutOutput output = new()
        {
            Size = finalOuterSize,
            ScrollableOverflowRect = Rect.Zero,
            Baselines = Baselines.FromFirst(firstBaseline),
            TopMargin = ownMarginsCollapseWithChildren.Start
                ? firstChildTopMarginSet
                : CollapsibleMarginSet.FromMargin(rawMargin.Top.ResolveOrZero(parentSize.Width, calc)),
            BottomMargin = ownBottomMarginCollapsesWithChildren
                ? lastChildBottomMarginSet
                : CollapsibleMarginSet.FromMargin(rawMargin.Bottom.ResolveOrZero(parentSize.Width, calc)),
            MarginsCanCollapseThrough = canBeCollapsedThrough,
            OofCandidates = OofCandidates.None,
            OofPositioningArea = null,
        };

        // Short-circuit if computing size.
        //
        // Note: it is important that we return the margin-collapsing related outputs here as Parent block containers
        // rely on the `TopMargin`/`BottomMargin` of their children to compute their own intrinsic height.
        if (runMode == RunMode.ComputeSize)
        {
            return output;
        }

        // Commit deferred child layouts to the tree.
        foreach (BlockItem item in items)
        {
            if (item.FinalLayout is Layout layout)
            {
                tree.SetUnroundedLayout(item.NodeId, layout);
            }
        }

        // 4. Collect out-of-flow candidates in document order (direct out-of-flow children plus
        // candidates bubbled from in-flow children's subtrees). These are laid out by the
        // out-of-flow positioning pass, which runs after this algorithm.
        OofCandidates candidates = OofCandidates.None;
        foreach (BlockItem item in items)
        {
            if (item.Position.IsOutOfFlow())
            {
                // The static-position rectangle of an out-of-flow child of a block container spans
                // the container's content box in the inline axis, and is a zero-height line at the
                // child's hypothetical position in the block axis. The child's self-alignment
                // properties determine how it is aligned within that rectangle.
                Line<float> inlineArea = new(resolvedContentBoxInset.Left, containerOuterWidth - resolvedContentBoxInset.Right);
                Line<float> blockArea = new(item.StaticPosition.Y, item.StaticPosition.Y);
                // A `justify_self` of `None` takes the container's `justify-items`. Block containers have
                // no `align-items`, so an `align_self` of `None` behaves as `normal`.
                AlignItems justifySelf = (item.JustifySelf ?? justifyItems).ResolveSelfRelative(item.Direction, direction, true);
                AlignItems alignSelf = (item.AlignSelf ?? AlignItems.Normal).ResolveSelfRelative(item.Direction, direction, false);
                candidates.Push(new OofCandidate
                {
                    Node = item.NodeId,
                    Order = item.Order,
                    Position = item.Position,
                    StaticPosition = new Point<AxisStaticPosition>(
                        AxisStaticPosition.FromAlignment(justifySelf, inlineArea, direction.IsRtl()),
                        AxisStaticPosition.FromAlignment(alignSelf, blockArea, false)),
                });
            }
            else if (!item.OofCandidates.IsEmpty)
            {
                // Translate anchors from item-relative to container-relative coordinates
                if (item.FinalLayout is Layout layout)
                {
                    item.OofCandidates.Translate(layout.Location);
                }

                candidates.Append(ref item.OofCandidates);
            }
        }

        Rect<float> absolutePositionInset = resolvedBorder.Add(scrollbarGutter);
        Size<float> absolutePositionArea = finalOuterSize.Sub(absolutePositionInset.SumAxes());
        Point<float> absolutePositionOffset = new(absolutePositionInset.Left, absolutePositionInset.Top);
        output.OofCandidates = candidates;
        output.OofPositioningArea = new OofPositioningArea(absolutePositionArea, absolutePositionOffset);

        // A scroll container's own padding at the end of the content is part of its scrollable
        // overflow region, so it is included in the in-flow overflow rect. Boxes that are not
        // scroll containers do not extend their overflow region by their own padding.
        if (isScrollContainer)
        {
            inflowOverflowRect.Right += direction.IsRtl() ? resolvedPadding.Left : resolvedPadding.Right;
            inflowOverflowRect.Bottom += resolvedPadding.Bottom;
        }

        output.ScrollableOverflowRect = inflowOverflowRect;

        // 5. Perform hidden layout on hidden children
        int len = tree.ChildCount(nodeId);
        for (int order = 0; order < len; order++)
        {
            NodeId child = tree.GetChildId(nodeId, order);
            if (tree.GetBlockChildStyle(child).BoxGenerationMode == BoxGenerationMode.None)
            {
                tree.SetUnroundedLayout(child, Layout.WithOrder((uint)order));
                tree.PerformChildLayout(child, Size.None, Size.None, Size.MaxContent, SizingMode.InherentSize, Line.False);
            }
        }

        return output;
    }

    /// <summary>Create a <see cref="BlockItem"/> for each child of the container that generates a box.</summary>
    private static List<BlockItem> GenerateBlockItemList(ILayoutBlockContainer tree, NodeId node, Size<float?> nodeInnerSize)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;
        List<BlockItem> items = new();
        uint order = 0;
        foreach (NodeId childNodeId in tree.ChildIds(node))
        {
            IBlockItemStyle childStyle = tree.GetBlockChildStyle(childNodeId);
            if (childStyle.BoxGenerationMode == BoxGenerationMode.None)
            {
                continue;
            }

            float? aspectRatio = childStyle.AspectRatio;
            Rect<float> padding = childStyle.Padding.ResolveOrZero(nodeInnerSize.Width, calc);
            Rect<float> border = childStyle.Border.ResolveOrZero(nodeInnerSize.Width, calc);
            Size<float> pbSum = padding.Add(border).SumAxes();
            Size<float> boxSizingAdjustment = childStyle.BoxSizing == BoxSizing.ContentBox ? pbSum : Size.Zero;
            Position position = childStyle.Position;
            Point<Overflow> overflow = childStyle.Overflow;
            Float floatValue = childStyle.Float;
            bool isNotFloated = floatValue == Float.None;
            bool isBlock = childStyle.IsBlock;
            bool isTable = childStyle.IsTable;
            bool isScrollContainer = overflow.X.IsScrollContainer() || overflow.Y.IsScrollContainer();
            Contain contain = childStyle.Contain;
            bool isInSameBfc = isBlock
                && !isTable
                && !position.IsOutOfFlow()
                && isNotFloated
                && !isScrollContainer
                && !contain.EstablishesIndependentFormattingContext()
                && childStyle.AlignContent.Keyword == AlignContentKeyword.Normal;

            Size<Dimension> sizeStyle = childStyle.Size;
            items.Add(new BlockItem
            {
                NodeId = childNodeId,
                Order = order,
                IsTable = isTable,
                IsCompressibleReplaced = childStyle.IsCompressibleReplaced,
                IsInSameBfc = isInSameBfc,
                Float = floatValue,
                Clear = childStyle.Clear,
                SizeStyle = sizeStyle,
                Size = sizeStyle.MaybeResolve(nodeInnerSize, calc).MaybeApplyAspectRatio(aspectRatio).MaybeAdd(boxSizingAdjustment),
                MinSize = childStyle.MinSize.MaybeResolve(nodeInnerSize, calc).MaybeApplyAspectRatio(aspectRatio).MaybeAdd(boxSizingAdjustment),
                MaxSize = childStyle.MaxSize.MaybeResolve(nodeInnerSize, calc).MaybeApplyAspectRatio(aspectRatio).MaybeAdd(boxSizingAdjustment),
                Overflow = overflow,
                Contain = contain,
                ScrollbarWidth = childStyle.ScrollbarWidth,
                Position = position,
                Direction = childStyle.Direction,
                AlignSelf = childStyle.AlignSelf,
                JustifySelf = childStyle.JustifySelf,
                Inset = childStyle.Inset,
                Margin = childStyle.Margin,
                Padding = padding,
                Border = border,
                PaddingBorderSum = pbSum,
                // Fields to be computed later (for now we initialise with dummy values)
                ComputedSize = Size.Zero,
                StaticPosition = Point.Zero,
                CanBeCollapsedThrough = false,
                FinalLayout = null,
                OofCandidates = OofCandidates.None,
            });
            order++;
        }

        return items;
    }

    /// <summary>Resolve a <c>stretch</c> height against the container's inner height (minus the item's non-collapsing margins).</summary>
    private static float? ResolveStretchHeight(Dimension heightStyle, float? containerInnerHeight, Rect<float> itemMargin, Line<bool> marginsTreatedAsZero)
    {
        if (!heightStyle.IsStretch() || containerInnerHeight is not float height)
        {
            return null;
        }

        if (!marginsTreatedAsZero.Start)
        {
            height -= itemMargin.Top;
        }

        if (!marginsTreatedAsZero.End)
        {
            height -= itemMargin.Bottom;
        }

        return height;
    }

    /// <summary>The offset of a relatively positioned item from its static position.</summary>
    private static Point<float> ResolveRelativeOffset(BlockItem item, Size<float?> parentSize, Direction direction, ILayoutPartialTree tree)
    {
        if (item.Position != Position.Relative)
        {
            return Point.Zero;
        }

        Func<object, float, float> calc = tree.ResolveCalcValue;
        float? left = item.Inset.Left.MaybeResolve(parentSize.Width, calc);
        float? right = item.Inset.Right.MaybeResolve(parentSize.Width, calc);
        float? top = item.Inset.Top.MaybeResolve(parentSize.Height, calc);
        float? bottom = item.Inset.Bottom.MaybeResolve(parentSize.Height, calc);
        float? x = direction.IsRtl() ? -right ?? left : left ?? -right;
        return new Point<float>(x ?? 0f, top ?? -bottom ?? 0f);
    }

    /// <summary>Compute the content-based width of a block container whose width is not known.</summary>
    private static float DetermineContentBasedContainerWidth(ILayoutPartialTree tree, List<BlockItem> items, AvailableSpace availableWidth)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;
        Size<AvailableSpace> availableSpace = new(availableWidth, AvailableSpace.MinContent);

        float maxChildWidth = 0f;
        FloatIntrinsicWidthCalculator floatContribution = new(availableWidth);
        foreach (BlockItem item in items)
        {
            if (item.Position.IsOutOfFlow())
            {
                continue;
            }

            Size<float?> knownDimensions = item.Size.MaybeClamp(item.MinSize, item.MaxSize);
            float itemXMarginSum = item.Margin.ResolveOrZero(availableSpace.Width.IntoOption(), calc).HorizontalAxisSum();

            float width;
            if (knownDimensions.Width is float knownWidth)
            {
                width = knownWidth;
            }
            else
            {
                SizingKeywordResolution? resolution = SizingKeyword.ResolveSizingKeyword(item.SizeStyle.Width, null, null, calc);
                AvailableSpace itemAvailableWidth = resolution is SizingKeywordResolution r
                    ? (r.IsExact ? AvailableSpace.Definite(r.ExactSize) : r.MeasureSpace)
                    : availableSpace.Width.MaybeSub(itemXMarginSum);
                width = tree.MeasureChildSize(
                    item.NodeId,
                    knownDimensions,
                    Size.None,
                    new Size<AvailableSpace>(itemAvailableWidth, availableSpace.Height),
                    SizingMode.InherentSize,
                    AbsoluteAxis.Horizontal,
                    // Must match the value passed when laying the item out (see `Cache`)
                    item.IsInSameBfc ? Line.True : Line.False);
            }

            width = MathHelpers.Max(width, item.PaddingBorderSum.Width) + itemXMarginSum;

            if (item.Float.FloatDirection() is FloatDirection floatDirection)
            {
                floatContribution.AddFloat(width, floatDirection, item.Clear);
                continue;
            }

            maxChildWidth = MathHelpers.Max(maxChildWidth, width);
        }

        return MathHelpers.Max(maxChildWidth, floatContribution.Result());
    }

    /// <summary>Compute each in-flow (and floated) child's final layout, returning the container's content height and margin sets.</summary>
    private static (Rect<float> InflowOverflowRect, float ContentHeight, CollapsibleMarginSet FirstChildTopMarginSet,
        CollapsibleMarginSet LastChildBottomMarginSet, float? FirstBaseline) PerformFinalLayoutOnInFlowChildren(
        ILayoutBlockContainer tree,
        RunMode runMode,
        List<BlockItem> items,
        float containerOuterWidth,
        float? containerPercentageResolutionHeight,
        Rect<float> contentBoxInset,
        Rect<float> resolvedContentBoxInset,
        Rect<float> resolvedBorder,
        TextAlign textAlign,
        AlignItems justifyItems,
        Direction direction,
        Line<bool> ownMarginsCollapseWithChildren,
        Line<bool> childrenMarginsAdjoinOwnEdges,
        bool isScrollContainer,
        BlockContext blockCtx)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;

        // Resolve container_inner_width for sizing child nodes using initial content_box_inset
        float containerInnerWidth = MathHelpers.Max(containerOuterWidth - resolvedContentBoxInset.HorizontalAxisSum(), 0f);
        containerPercentageResolutionHeight = containerPercentageResolutionHeight.MaybeSub(resolvedContentBoxInset.VerticalAxisSum());
        Size<float?> parentSize = new(containerInnerWidth, containerPercentageResolutionHeight);
        // Vertical available space in block flow is indefinite, NOT a min-content constraint:
        // MaxContent is taffy's representation of "indefinite".
        Size<AvailableSpace> availableSpace = new(AvailableSpace.Definite(containerInnerWidth), AvailableSpace.MaxContent);

        // TODO: handle nested blocks with different widths
        if (blockCtx.IsBfcRoot())
        {
            blockCtx.SetWidth(containerOuterWidth);
            blockCtx.ApplyContentBoxInset(new[] { resolvedContentBoxInset.Left, resolvedContentBoxInset.Right });
        }

        // If this block's top margin does not collapse with its children's then the position of its
        // top margin strut is resolved relative to it, and floats adjoining ancestor struts do not
        // adjoin this block's strut.
        if (!ownMarginsCollapseWithChildren.Start)
        {
            blockCtx.CommitStrut();
        }

        Rect<float> inflowOverflowRect = Rect.Zero;
        float committedYOffset = resolvedContentBoxInset.Top;
        float yOffsetForAbsolute = resolvedContentBoxInset.Top;
        CollapsibleMarginSet firstChildTopMarginSet = CollapsibleMarginSet.Zero;
        CollapsibleMarginSet activeCollapsibleMarginSet = CollapsibleMarginSet.Zero;
        bool isCollapsingWithFirstMarginSet = true;
        float? firstBaseline = null;
        // Whether the active margin set contains the margins of a self-collapsing element with
        // clearance. Such margins collapse with the margins of following siblings but the resulting
        // margin does not collapse with the bottom margin of the parent block.
        bool activeMarginSetHasClearance = false;

        bool hasActiveFloats = blockCtx.HasActiveFloats(committedYOffset);

        foreach (BlockItem item in items)
        {
            if (item.Position.IsOutOfFlow())
            {
                float x = direction == Direction.Rtl
                    ? containerOuterWidth - resolvedContentBoxInset.Right
                    : resolvedContentBoxInset.Left;
                item.StaticPosition = new Point<float>(x, yOffsetForAbsolute);
                continue;
            }

            Rect<float?> itemMargin = item.Margin.Map(margin => margin.ResolveToOption(containerInnerWidth, calc));
            Rect<float> itemNonAutoMargin = itemMargin.Map(m => m ?? 0f);
            float itemNonAutoXMarginSum = itemNonAutoMargin.HorizontalAxisSum();
            Size<float> scrollbarSize = new(
                item.Overflow.Y == Overflow.Scroll ? item.ScrollbarWidth : 0f,
                item.Overflow.X == Overflow.Scroll ? item.ScrollbarWidth : 0f);

            // Handle floated boxes
            if (item.Float.FloatDirection() is FloatDirection floatDirection)
            {
                hasActiveFloats = true;

                // A float with `width: auto` is shrink-to-fit (fit-content) sized: the available
                // space clamped between its min-content and max-content sizes.
                float floatAvailableWidth = MathHelpers.Max(containerInnerWidth - itemNonAutoXMarginSum, 0f);
                float? itemKnownWidth;
                AvailableSpace itemAvailableWidth;
                SizingKeywordResolution? floatResolution =
                    SizingKeyword.ResolveSizingKeyword(item.SizeStyle.Width, floatAvailableWidth, containerInnerWidth, calc);
                if (floatResolution is SizingKeywordResolution fr)
                {
                    itemKnownWidth = fr.IsExact ? fr.ExactSize : null;
                    itemAvailableWidth = fr.IsExact ? AvailableSpace.Definite(fr.ExactSize) : fr.MeasureSpace;
                }
                else
                {
                    itemKnownWidth = null;
                    itemAvailableWidth = AvailableSpace.Definite(floatAvailableWidth);
                }

                float? itemKnownHeight = ResolveStretchHeight(
                    item.SizeStyle.Height,
                    containerPercentageResolutionHeight,
                    itemNonAutoMargin,
                    // The margins of a float never collapse
                    Line.False);

                LayoutOutput floatLayout = tree.PerformChildLayout(
                    item.NodeId,
                    new Size<float?>(itemKnownWidth, itemKnownHeight),
                    parentSize,
                    new Size<AvailableSpace>(itemAvailableWidth, AvailableSpace.MaxContent),
                    SizingMode.InherentSize,
                    // A float establishes a new block formatting context: its margins do not
                    // collapse with the margins of its children
                    Line.False);
                item.OofCandidates = floatLayout.OofCandidates.Take();

                Size<float> marginBox = floatLayout.Size.Add(itemNonAutoMargin.SumAxes());

                // Floats that occur between collapsing margins are positioned as if they had an otherwise
                // empty anonymous block parent taking part in the flow, so the pending collapsible margins
                // contribute to the float's minimum y position (unless those margins collapse with the
                // container's own top margin, in which case they are applied outside the container).
                //
                // In the latter case the position of the float is not fully resolved: margins contributed
                // by later siblings can still collapse into the strut and move the container (and float).
                // Such floats force clearance on cleared elements whose margins adjoin the same strut.
                bool adjoinsUnresolvedStrut = isCollapsingWithFirstMarginSet && ownMarginsCollapseWithChildren.Start;
                float yOffsetForFloat = adjoinsUnresolvedStrut
                    ? committedYOffset
                    : committedYOffset + activeCollapsibleMarginSet.Resolve();
                Point<float> floatLocation = blockCtx.PlaceFloatedBox(marginBox, yOffsetForFloat, floatDirection, item.Clear, adjoinsUnresolvedStrut);

                // Convert the margin-box location returned by float placement into a border-box location
                // for the output Layout
                floatLocation.Y += itemNonAutoMargin.Top;
                floatLocation.X += itemNonAutoMargin.Left;

                // A relatively positioned float is placed as a float first, then offset by its
                // inset. The offset does not affect the placement of other content (CSS 2.2 9.4.3).
                Point<float> floatInsetOffset = ResolveRelativeOffset(item, parentSize, direction, tree);
                floatLocation.X += floatInsetOffset.X;
                floatLocation.Y += floatInsetOffset.Y;

                // Deferred to the post-loop pass in `ComputeBlockInner` (like in-flow items) so that
                // `align-content` can shift `Location.Y` before the layout is committed.
                item.FinalLayout = new Layout
                {
                    Order = item.Order,
                    Size = floatLayout.Size,
                    ScrollableOverflowRect = floatLayout.ScrollableOverflowRect,
                    ScrollbarSize = scrollbarSize,
                    Location = floatLocation,
                    Padding = item.Padding,
                    Border = item.Border,
                    Margin = itemNonAutoMargin,
                };

                // TODO: Should the overflow of floated boxes count as "inflow_overflow_rect"
                // or should it be counted separately?
                Point<float> floatContributionLocation = direction.IsRtl()
                    ? new Point<float>(
                        containerOuterWidth - (floatLocation.X + floatLayout.Size.Width) - resolvedBorder.Right,
                        floatLocation.Y - resolvedBorder.Top)
                    : new Point<float>(floatLocation.X - resolvedBorder.Left, floatLocation.Y - resolvedBorder.Top);
                inflowOverflowRect = inflowOverflowRect.Union(ScrollableOverflow.ComputeScrollableOverflowContribution(
                    floatContributionLocation,
                    floatLayout.Size,
                    floatLayout.ScrollableOverflowRect,
                    item.Overflow,
                    item.Contain,
                    isScrollContainer));

                continue;
            }

            // Handle non-floated boxes
            float yMarginOffset = 0f;
            bool itemAvoidsFloats = false;
            bool itemPushedBelowFloat = false;

            float stretchWidth;
            Point<float> floatAvoidingPosition;
            float floatAvoidingWidth;
            if (item.IsInSameBfc)
            {
                stretchWidth = MathHelpers.Max(containerInnerWidth - itemNonAutoXMarginSum, 0f);
                floatAvoidingPosition = new Point<float>(0f, 0f);
                floatAvoidingWidth = 0f;
            }
            else
            {
                // Set y_margin_offset (different bfc child)
                if (!isCollapsingWithFirstMarginSet || !ownMarginsCollapseWithChildren.Start)
                {
                    yMarginOffset = activeCollapsibleMarginSet.CollapseWithMargin(itemNonAutoMargin.Top).Resolve();
                }

                float minY = committedYOffset + yMarginOffset;

                // In addition to the running flag, check the float context directly:
                // floats placed by the subtree of a preceding in-flow sibling (in the same
                // BFC) are not reflected in the flag
                if (hasActiveFloats || blockCtx.HasActiveFloats(minY))
                {
                    float[] xMargins = { itemNonAutoMargin.Left, itemNonAutoMargin.Right };
                    // An auto width resolves to at least the negation of the margin sum
                    // (so that the margin box width is non-negative, per CSS2 10.3.3)
                    float minAutoWidth = -itemNonAutoXMarginSum;

                    // Find the highest slot (at or below `minY`) with enough horizontal space
                    // for the item's border box, which must not overlap any float
                    int? slotSegment = null;
                    BfcSlot slot;
                    while (true)
                    {
                        slot = blockCtx.FindBfcSlot(minY, xMargins, direction, item.Clear, slotSegment);
                        if (slot.SegmentId is not int segmentId)
                        {
                            break;
                        }

                        float width = (item.Size.Width ?? MathHelpers.Max(MathHelpers.Max(slot.StretchWidth, minAutoWidth), 0f))
                            .MaybeClamp(item.MinSize.Width, item.MaxSize.Width);
                        if (width <= slot.BorderWidth + FloatConstants.FitTolerance)
                        {
                            break;
                        }

                        slotSegment = segmentId;
                    }

                    // If the item had to move down to avoid floats then it "separates from the
                    // float": similarly to clearance, its top margin no longer collapses with
                    // the parent's margins.
                    if (slot.Y > minY)
                    {
                        itemPushedBelowFloat = true;
                    }

                    hasActiveFloats = slot.SegmentId.HasValue;
                    itemAvoidsFloats = true;
                    stretchWidth = MathHelpers.Max(MathHelpers.Max(slot.StretchWidth, minAutoWidth), 0f);
                    floatAvoidingPosition = new Point<float>(slot.X, slot.Y);
                    floatAvoidingWidth = slot.BorderWidth;
                }
                else
                {
                    stretchWidth = MathHelpers.Max(containerInnerWidth - itemNonAutoXMarginSum, 0f);
                    floatAvoidingPosition = new Point<float>(resolvedContentBoxInset.Left, minY);
                    floatAvoidingWidth = containerInnerWidth;
                }
            }

            // `justify-self` on an in-flow block-level box (css-align-3 6.1.1). `None` takes the
            // container's `justify-items`, and `normal` lays the box out according to the default
            // block layout rules. `stretch` does too: it only differs from `normal` for tables and
            // replaced boxes, which would otherwise resolve their own width.
            AlignItems justifySelf = (item.JustifySelf ?? justifyItems).ResolveSelfRelative(item.Direction, direction, true);
            bool isStretch = justifySelf.Keyword == AlignItemsKeyword.Stretch;
            // The (resolved) `justify-self` if it is neither `normal` nor `stretch`
            AlignItems? nonStretchJustifySelf = justifySelf.Keyword is AlignItemsKeyword.Normal or AlignItemsKeyword.Stretch
                ? null
                : justifySelf;

            // Unless stretched, tables and compressible replaced elements resolve their own width
            Size<float?> knownDimensions;
            if ((item.IsTable || item.IsCompressibleReplaced) && !isStretch)
            {
                knownDimensions = Size.None;
            }
            else
            {
                // The automatic width of a block-level box whose `justify-self` is neither `normal` nor `stretch`
                // is equivalent to `fit-content` rather than `stretch`
                Dimension widthStyle = nonStretchJustifySelf.HasValue && item.SizeStyle.Width.IsAuto()
                    ? Dimension.FitContent
                    : item.SizeStyle.Width;

                // Items with a sizing keyword width (min-content, max-content, fit-content,
                // fit-content(...), stretch) resolve their width either directly or by measuring
                // the item under the corresponding available space constraint
                float? keywordWidth = null;
                if (SizingKeyword.ResolveSizingKeyword(widthStyle, stretchWidth, containerInnerWidth, calc) is SizingKeywordResolution resolution)
                {
                    keywordWidth = resolution.IsExact
                        ? resolution.ExactSize
                        : tree.MeasureChildSize(
                            item.NodeId,
                            Size.None,
                            parentSize,
                            new Size<AvailableSpace>(resolution.MeasureSpace, AvailableSpace.MaxContent),
                            SizingMode.InherentSize,
                            AbsoluteAxis.Horizontal,
                            // Must match the value passed when laying the item out (see `Cache`)
                            item.IsInSameBfc ? Line.True : Line.False);
                }

                float? keywordHeight = ResolveStretchHeight(
                    item.SizeStyle.Height,
                    containerPercentageResolutionHeight,
                    itemNonAutoMargin,
                    childrenMarginsAdjoinOwnEdges);

                knownDimensions = new Size<float?>(
                    (item.Size.Width ?? keywordWidth ?? stretchWidth).MaybeClamp(item.MinSize.Width, item.MaxSize.Width),
                    item.Size.Height ?? keywordHeight)
                    .MaybeClamp(item.MinSize, item.MaxSize);
            }

            LayoutInput inputs = new()
            {
                RunMode = runMode,
                SizingMode = SizingMode.InherentSize,
                Axis = RequestedAxis.Both,
                KnownDimensions = knownDimensions,
                KnownDimensionsAreDefinite = new Size<bool>(true, true),
                ParentSize = parentSize,
                AvailableSpace = new Size<AvailableSpace>(AvailableSpace.Definite(stretchWidth), availableSpace.Height),
                VerticalMarginsAreCollapsible = item.IsInSameBfc ? Line.True : Line.False,
            };

            float? clearThreshold = blockCtx.ClearedThreshold(item.Clear);
            float clearPos = clearThreshold ?? float.NegativeInfinity;

            // The height consumed by floats within a same-BFC child, relative to the child's
            // border-top. Added to the parent's contribution once the child's position is known.
            float childFloatContribution = float.NegativeInfinity;

            LayoutOutput itemLayout;
            if (item.IsInSameBfc)
            {
                // Replaced elements may not have a known width (they are sized by their
                // measure function rather than stretch-sized)
                float width = MathHelpers.Max(knownDimensions.Width ?? stretchWidth, item.PaddingBorderSum.Width);
                // TODO: account for auto margins
                float insetLeft = itemNonAutoMargin.Left + contentBoxInset.Left;
                float insetRight = containerOuterWidth - width - insetLeft;
                float[] insets = { insetLeft, insetRight };

                // Compute child layout. The child's position is estimated from its top margin collapsed with
                // the preceding margins (the child's own collapsed-through margins are not yet known).
                float estimatedY = committedYOffset
                    + activeCollapsibleMarginSet.CollapseWithMargin(itemNonAutoMargin.Top).Resolve();
                BlockContext childBlockCtx = blockCtx.SubContext(MathHelpers.Max(estimatedY, clearPos), insets);
                itemLayout = tree.ComputeBlockChildLayout(item.NodeId, inputs, childBlockCtx);

                // Extract float contribution from child block context
                childFloatContribution = childBlockCtx.FloatedContentHeightContribution();
                // Floats placed while the position of the child's top margin strut was unresolved
                // also adjoin this block's current strut
                blockCtx.MergeAdjoiningFloats(childBlockCtx.TopAdjoiningFloats());
            }
            else
            {
                itemLayout = tree.ComputeChildLayout(item.NodeId, inputs);
            }

            item.OofCandidates = itemLayout.OofCandidates.Take();
            Size<float> finalSize = itemLayout.Size;
            CollapsibleMarginSet topMarginSet = itemLayout.TopMargin.CollapseWithMargin(itemMargin.Top ?? 0f);
            CollapsibleMarginSet bottomMarginSet = itemLayout.BottomMargin.CollapseWithMargin(itemMargin.Bottom ?? 0f);

            // Expand auto margins to fill available space
            // Note: Vertical auto-margins for relatively positioned block items simply resolve to 0.
            // See: https://www.w3.org/TR/CSS21/visudet.html#abs-non-replaced-width
            float freeXSpace = MathHelpers.Max(0f, stretchWidth - finalSize.Width);
            int autoMarginCount = (itemMargin.Left is null ? 1 : 0) + (itemMargin.Right is null ? 1 : 0);
            float xAxisAutoMarginSize = autoMarginCount > 0 ? freeXSpace / autoMarginCount : 0f;
            Rect<float> resolvedMargin = new(
                itemMargin.Left ?? xAxisAutoMarginSize,
                itemMargin.Right ?? xAxisAutoMarginSize,
                itemMargin.Top ?? 0f,
                itemMargin.Bottom ?? 0f);

            // Resolve item inset
            Point<float> insetOffset = ResolveRelativeOffset(item, parentSize, direction, tree);

            // Set y_margin_offset (same bfc child)
            if (item.IsInSameBfc && (!isCollapsingWithFirstMarginSet || !ownMarginsCollapseWithChildren.Start))
            {
                yMarginOffset = activeCollapsibleMarginSet.CollapseWithSet(topMarginSet).Resolve();
            }

            // Compute clearance (CSS2.2 9.5.2). Clearance is introduced if the hypothetical position of the
            // item's top border edge (the position it would have with normal margin collapsing) is not past
            // the bottom of the relevant floats. When clearance is introduced the item's border edge is
            // placed at `max(float bottom, hypothetical position)`.
            bool hasClearance = false;
            if (item.IsInSameBfc && clearThreshold is float threshold)
            {
                // The hypothetical position always includes the item's collapsed top margin set, even
                // when those margins collapse with the container's own top margin (and are thus applied
                // outside the container): in that case they still move the container (and hence the item)
                // relative to the floats.
                float hypotheticalY = committedYOffset + activeCollapsibleMarginSet.CollapseWithSet(topMarginSet).Resolve();
                // Clearance is forced (regardless of the hypothetical position) if a relevant float is
                // adjoining the margin-collapse strut that the item's top margin would collapse into:
                // if the margins were allowed to collapse they would pull the float down with the item,
                // so clearance is inserted to separate the two, placing the item just below the float.
                bool forcedClearance = blockCtx.HasAdjoiningFloat(item.Clear);
                if (forcedClearance || hypotheticalY < threshold)
                {
                    hasClearance = true;
                    // Clearance stops the item's top margin collapsing with preceding margins. If those
                    // preceding margins collapse with the container's own top margin they are applied
                    // outside the container (moving it down), so the item's cleared position within the
                    // container must be reduced by that amount to keep its absolute position correct.
                    float escapedMargin = isCollapsingWithFirstMarginSet && ownMarginsCollapseWithChildren.Start
                        ? activeCollapsibleMarginSet.Resolve()
                        : 0f;
                    yMarginOffset = threshold - committedYOffset - escapedMargin;
                }
            }

            item.ComputedSize = itemLayout.Size;
            item.CanBeCollapsedThrough = itemLayout.MarginsCanCollapseThrough && !hasClearance;
            if (item.IsInSameBfc)
            {
                float unclearedY = committedYOffset + activeCollapsibleMarginSet.Resolve();
                item.StaticPosition = new Point<float>(
                    direction == Direction.Rtl
                        ? containerOuterWidth - resolvedContentBoxInset.Right - finalSize.Width
                        : resolvedContentBoxInset.Left,
                    MathHelpers.Max(unclearedY, clearPos));
            }
            else
            {
                // TODO: handle inset and margins
                item.StaticPosition = new Point<float>(
                    direction == Direction.Rtl
                        ? floatAvoidingPosition.X + floatAvoidingWidth - finalSize.Width
                        : floatAvoidingPosition.X,
                    floatAvoidingPosition.Y);
            }

            Point<float> location;
            if (item.IsInSameBfc)
            {
                location = new Point<float>(
                    direction == Direction.Rtl
                        ? containerOuterWidth - resolvedContentBoxInset.Right - finalSize.Width - resolvedMargin.Right + insetOffset.X
                        : resolvedContentBoxInset.Left + insetOffset.X + resolvedMargin.Left,
                    committedYOffset + yMarginOffset + insetOffset.Y);
            }
            else
            {
                // When the item avoids floats, its non-auto margins are already accounted for in the
                // slot's border-box position/width (margins may overlap floats), so only the auto
                // portion of the resolved margin is added here.
                float extraMarginLeft = itemAvoidsFloats ? resolvedMargin.Left - itemNonAutoMargin.Left : resolvedMargin.Left;
                float extraMarginRight = itemAvoidsFloats ? resolvedMargin.Right - itemNonAutoMargin.Right : resolvedMargin.Right;
                // TODO: handle inset and margins
                location = new Point<float>(
                    direction == Direction.Rtl
                        ? floatAvoidingPosition.X + floatAvoidingWidth - finalSize.Width - extraMarginRight + insetOffset.X
                        : floatAvoidingPosition.X + extraMarginLeft + insetOffset.X,
                    floatAvoidingPosition.Y + insetOffset.Y);
            }

            // Floats within the child are positioned relative to the child's border-top, so the
            // child's (margin-collapsed) position, rather than its relative offset, is added.
            if (item.IsInSameBfc)
            {
                blockCtx.AddChildFloatedContentHeightContribution(location.Y - insetOffset.Y + childFloatContribution);
            }

            // Apply alignment. `justify-self` aligns the item's margin box within its alignment
            // container and takes precedence over legacy `text-align`, but auto margins (which have
            // already absorbed the free space) take precedence over `justify-self`.
            bool hasAutoXMargin = itemMargin.Left is null || itemMargin.Right is null;
            float itemOuterWidth = itemLayout.Size.Width + resolvedMargin.HorizontalAxisSum();
            if (nonStretchJustifySelf is AlignItems nonStretch && !hasAutoXMargin)
            {
                // The alignment container is the containing block, except that a BFC-establishing
                // box placed next to a float aligns within the space left over by the float (in
                // which case the slot is already inset by the item's margins).
                float containerStart;
                float alignFreeXSpace;
                if (itemAvoidsFloats)
                {
                    containerStart = floatAvoidingPosition.X - resolvedMargin.Left;
                    alignFreeXSpace = floatAvoidingWidth - itemLayout.Size.Width;
                }
                else
                {
                    containerStart = resolvedContentBoxInset.Left;
                    alignFreeXSpace = containerInnerWidth - itemOuterWidth;
                }

                AlignItemsKeyword keyword = nonStretch.Safety == AlignmentSafety.Safe && alignFreeXSpace < 0f
                    ? AlignItemsKeyword.Start
                    : nonStretch.Keyword;
                bool? alignToEnd = keyword switch
                {
                    AlignItemsKeyword.Center => null,
                    AlignItemsKeyword.End or AlignItemsKeyword.FlexEnd or AlignItemsKeyword.SelfEnd => true,
                    _ => false,
                };
                float offset = alignToEnd switch
                {
                    null => alignFreeXSpace / 2f,
                    bool end when end != direction.IsRtl() => alignFreeXSpace,
                    _ => 0f,
                };
                location.X = containerStart + resolvedMargin.Left + insetOffset.X + offset;
            }
            else if (!nonStretchJustifySelf.HasValue && itemOuterWidth < containerInnerWidth)
            {
                float textAlignFreeXSpace = containerInnerWidth - itemOuterWidth;
                switch (textAlign, direction)
                {
                    case (TextAlign.LegacyLeft, Direction.Rtl):
                        location.X -= textAlignFreeXSpace;
                        break;
                    case (TextAlign.LegacyRight, Direction.Ltr):
                        location.X += textAlignFreeXSpace;
                        break;
                    case (TextAlign.LegacyCenter, Direction.Ltr):
                        location.X += textAlignFreeXSpace / 2f;
                        break;
                    case (TextAlign.LegacyCenter, Direction.Rtl):
                        location.X -= textAlignFreeXSpace / 2f;
                        break;
                    default:
                        // Auto, or already aligned to the start side by default
                        break;
                }
            }

            // A block container's first baseline is the first baseline of its first in-flow child
            // that has one. The child's relative offset does not affect it.
            if (firstBaseline is null)
            {
                float? childBaseline = CommonAlignment.ChildFirstBaseline(itemLayout.Baselines.First, itemLayout.Size.Height, item.Overflow.Y);
                firstBaseline = location.Y - insetOffset.Y + childBaseline;
            }

            // Used auto margins include float-avoidance offsets, unlike the alignment margins above.
            Rect<float> usedMargin = resolvedMargin;
            if (itemAvoidsFloats)
            {
                if (itemMargin.Left is null)
                {
                    usedMargin.Left += floatAvoidingPosition.X - resolvedContentBoxInset.Left;
                }

                if (itemMargin.Right is null)
                {
                    usedMargin.Right += containerOuterWidth
                        - resolvedContentBoxInset.Right
                        - floatAvoidingPosition.X
                        - floatAvoidingWidth;
                }
            }

            // Defer `SetUnroundedLayout` to the post-loop pass in `ComputeBlockInner` so that
            // `align-content` can shift `Location.Y` before the layout is committed to the tree.
            item.FinalLayout = new Layout
            {
                Order = item.Order,
                Size = itemLayout.Size,
                ScrollableOverflowRect = itemLayout.ScrollableOverflowRect,
                ScrollbarSize = scrollbarSize,
                Location = location,
                Padding = item.Padding,
                Border = item.Border,
                Margin = usedMargin,
            };

            Point<float> contributionLocation = direction.IsRtl()
                ? new Point<float>(
                    containerOuterWidth - (location.X + finalSize.Width) - resolvedBorder.Right,
                    location.Y - resolvedBorder.Top)
                : new Point<float>(location.X - resolvedBorder.Left, location.Y - resolvedBorder.Top);
            inflowOverflowRect = inflowOverflowRect.Union(ScrollableOverflow.ComputeScrollableOverflowContribution(
                contributionLocation,
                finalSize,
                itemLayout.ScrollableOverflowRect,
                item.Overflow,
                item.Contain,
                isScrollContainer));

            // Update first_child_top_margin_set
            //
            // The top margin of an item with clearance does not collapse with the container's top margin,
            // so clearance terminates collapsing without contributing the item's own margins.
            if (isCollapsingWithFirstMarginSet && itemPushedBelowFloat)
            {
                // The item's top margin "separated from the float" and must not propagate to the parent
                isCollapsingWithFirstMarginSet = false;
            }

            if (isCollapsingWithFirstMarginSet && hasClearance)
            {
                isCollapsingWithFirstMarginSet = false;
            }
            else if (isCollapsingWithFirstMarginSet)
            {
                if (item.CanBeCollapsedThrough)
                {
                    firstChildTopMarginSet = firstChildTopMarginSet
                        .CollapseWithSet(topMarginSet)
                        .CollapseWithSet(bottomMarginSet);
                }
                else
                {
                    firstChildTopMarginSet = firstChildTopMarginSet.CollapseWithSet(topMarginSet);
                    isCollapsingWithFirstMarginSet = false;
                }
            }

            // Update active_collapsible_margin_set
            if (item.CanBeCollapsedThrough)
            {
                activeCollapsibleMarginSet = activeCollapsibleMarginSet
                    .CollapseWithSet(topMarginSet)
                    .CollapseWithSet(bottomMarginSet);
                yOffsetForAbsolute = committedYOffset + itemLayout.Size.Height + yMarginOffset;
            }
            else
            {
                committedYOffset = location.Y - insetOffset.Y + itemLayout.Size.Height;
                // A self-collapsing item with clearance is not collapsed through (its margins do not collapse
                // with margins of preceding siblings), but its top and bottom margins still collapse with each
                // other and with the margins of following siblings.
                if (hasClearance && itemLayout.MarginsCanCollapseThrough)
                {
                    // The element's border edge stays at the cleared position, but its collapsed margin
                    // extends below it: the border edge sits `top margin` inside the collapsed margin, so
                    // following content is offset by `collapsed margin - top margin` from the border edge.
                    committedYOffset -= topMarginSet.Resolve();
                    activeCollapsibleMarginSet = topMarginSet.CollapseWithSet(bottomMarginSet);
                    activeMarginSetHasClearance = true;
                }
                else
                {
                    activeCollapsibleMarginSet = bottomMarginSet;
                    activeMarginSetHasClearance = false;
                }

                yOffsetForAbsolute = committedYOffset + activeCollapsibleMarginSet.Resolve();
                // Committing in-flow content resolves the position of the current margin-collapse strut,
                // so floats placed before this point no longer force clearance
                blockCtx.CommitStrut();
            }
        }

        // The margins of a self-collapsing element with clearance do not collapse with the bottom
        // margin of the parent block: they extend the parent's content height instead of escaping it
        CollapsibleMarginSet lastChildBottomMarginSet = activeMarginSetHasClearance ? CollapsibleMarginSet.Zero : activeCollapsibleMarginSet;
        float bottomYMarginOffset;
        if (activeMarginSetHasClearance)
        {
            bottomYMarginOffset = activeCollapsibleMarginSet.Resolve();
        }
        else if (ownMarginsCollapseWithChildren.End)
        {
            bottomYMarginOffset = 0f;
        }
        else
        {
            bottomYMarginOffset = lastChildBottomMarginSet.Resolve();
        }

        committedYOffset += resolvedContentBoxInset.Bottom + bottomYMarginOffset;
        float contentHeight = MathHelpers.Max(0f, committedYOffset);
        return (inflowOverflowRect, contentHeight, firstChildTopMarginSet, lastChildBottomMarginSet, firstBaseline);
    }
}
