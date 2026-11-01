namespace TaffySharp;

/// <summary>Computes the flexbox layout algorithm on a tree (Rust <c>compute::flexbox</c>).</summary>
internal static partial class FlexboxAlgorithm
{
    /// <summary>Computes the layout of a box according to the flexbox algorithm.</summary>
    public static LayoutOutput ComputeFlexboxLayout(ILayoutFlexboxContainer tree, NodeId node, LayoutInput inputs)
    {
        Size<float?> knownDimensions = inputs.KnownDimensions;
        Size<float?> parentSize = inputs.ParentSize;
        RunMode runMode = inputs.RunMode;
        Func<object, float, float> calc = tree.ResolveCalcValue;

        IFlexboxContainerStyle style = tree.GetFlexboxContainerStyle(node);

        // Pull these out earlier to avoid borrowing issues
        Contain contain = style.Contain;
        float? aspectRatio = style.AspectRatio;
        Rect<float> padding = style.Padding.ResolveOrZero(parentSize.Width, calc);
        Rect<float> border = style.Border.ResolveOrZero(parentSize.Width, calc);
        Size<float> paddingBorderSum = padding.SumAxes().Add(border.SumAxes());
        Size<float> boxSizingAdjustment = style.BoxSizing == BoxSizing.ContentBox ? paddingBorderSum : Size.Zero;

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
        Size<float?> minMaxDefiniteSize = minSize.ZipMap(
            maxSize,
            (min, max) => min is float mn && max is float mx && mx <= mn ? mn : (float?)null);

        // The size of the container should be floored by the padding and border
        Size<float?> styledBasedKnownDimensions =
            knownDimensions.Or(minMaxDefiniteSize.Or(clampedStyleSize).MaybeMax(paddingBorderSum));

        // Short-circuit layout if the container's size is fully determined by the container's size and the run mode
        // is ComputeSize (and thus the container's size is all that we're interested in)
        if (runMode == RunMode.ComputeSize)
        {
            if (styledBasedKnownDimensions.Width is float width && styledBasedKnownDimensions.Height is float height)
            {
                return LayoutOutput.FromOuterSize(new Size<float>(width, height));
            }

            // We can also short-circuit if the width is known and only the width has been requested.
            if (inputs.Axis == RequestedAxis.Horizontal && styledBasedKnownDimensions.Width is float onlyWidth)
            {
                return LayoutOutput.FromOuterSize(new Size<float>(onlyWidth, 0f));
            }
        }

        // Normalize the definiteness flags: they only apply to dimensions which were passed in as known
        // by the parent. Dimensions resolved from the node's own style are always definite.
        Size<bool> knownDimensionsAreDefinite = inputs.KnownDimensionsAreDefinite
            .ZipMap(knownDimensions, (isDefinite, knownDimension) => isDefinite || knownDimension is null);

        LayoutInput preliminaryInputs = inputs;
        preliminaryInputs.KnownDimensions = styledBasedKnownDimensions;
        preliminaryInputs.KnownDimensionsAreDefinite = knownDimensionsAreDefinite;
        LayoutOutput output = ComputePreliminary(tree, node, preliminaryInputs);

        // Layout containment suppresses the box's baseline for baseline-alignment purposes
        if (contain.SuppressesBaseline())
        {
            output.Baselines = Baselines.None;
        }

        return output;
    }

    /// <summary>Compute a preliminary size for an item.</summary>
    private static LayoutOutput ComputePreliminary(ILayoutFlexboxContainer tree, NodeId node, LayoutInput inputs)
    {
        Size<float?> knownDimensions = inputs.KnownDimensions;
        Size<float?> parentSize = inputs.ParentSize;
        RunMode runMode = inputs.RunMode;
        Func<object, float, float> calc = tree.ResolveCalcValue;

        // Define some general constants we will need for the remainder of the algorithm.
        AlgoConstants constants = ComputeConstants(
            tree,
            tree.GetFlexboxContainerStyle(node),
            knownDimensions,
            inputs.KnownDimensionsAreDefinite,
            parentSize,
            inputs.AvailableSpace);

        // A container with no children has no flex items or lines, so its size follows directly
        // from its own style and the available space.
        if (tree.ChildCount(node) == 0)
        {
            return ComputeEmptyContainerLayout(tree, inputs, constants);
        }

        // 9. Flex Layout Algorithm

        // 9.1. Initial Setup

        // 1. Generate anonymous flex items as described in section 4 Flex Items.
        List<FlexItem> flexItems = GenerateAnonymousFlexItems(tree, node, constants);

        // 9.2. Line Length Determination

        // 2. Determine the available main and cross space for the flex items
        Size<AvailableSpace> availableSpace = DetermineAvailableSpace(knownDimensions, inputs.AvailableSpace, constants);

        // 3. Determine the flex base size and hypothetical main size of each item.
        DetermineFlexBaseSize(tree, constants, availableSpace, flexItems);

        // 4. Determine the main size of the flex container
        // This has already been done as part of ComputeConstants. The inner size is exposed as constants.NodeInnerSize.

        // 9.3. Main Size Determination

        // 5. Collect flex items into flex lines.
        List<FlexLine> flexLines = constants.IsBalance
            ? CollectBalancedFlexLines(constants, availableSpace, flexItems)
            : CollectFlexLines(constants, availableSpace, flexItems);

        // If container size is undefined, determine the container's main size
        // and then re-resolve gaps based on newly determined size
        if (constants.NodeInnerSize.Main(constants.Dir) is float innerMainSize)
        {
            float outerMainSize = innerMainSize + constants.ContentBoxInset.MainAxisSum(constants.Dir);
            constants.InnerContainerSize.SetMain(constants.Dir, innerMainSize);
            constants.ContainerSize.SetMain(constants.Dir, outerMainSize);
        }
        else
        {
            // Sets constants.ContainerSize and constants.InnerContainerSize
            DetermineContainerMainSize(tree, availableSpace, flexLines, constants);
            constants.NodeInnerSize.SetMain(constants.Dir, constants.InnerContainerSize.Main(constants.Dir));
            constants.NodeOuterSize.SetMain(constants.Dir, constants.ContainerSize.Main(constants.Dir));

            // Re-resolve percentage gaps
            IFlexboxContainerStyle style = tree.GetFlexboxContainerStyle(node);
            float innerContainerSize = constants.InnerContainerSize.Main(constants.Dir);
            float newGap = style.Gap.Main(constants.Dir).MaybeResolve(innerContainerSize, calc) ?? 0f;
            constants.Gap.SetMain(constants.Dir, newGap);
        }

        // If only the container's main size has been requested then we are done, as nothing below changes it. This
        // avoids measuring the cross size of every item.
        if (runMode == RunMode.ComputeSize && constants.Dir.MainAxisIsRequestedAxis(inputs.Axis))
        {
            Size<float> size = Size.Zero.WithMain(constants.Dir, constants.ContainerSize.Main(constants.Dir));
            return LayoutOutput.FromOuterSize(size);
        }

        // 6. Resolve the flexible lengths of all the flex items to find their used main size.
        foreach (FlexLine line in flexLines)
        {
            ResolveFlexibleLengths(line, constants);
        }

        // 9.4. Cross Size Determination

        // 7. Determine the hypothetical cross size of each item.
        foreach (FlexLine line in flexLines)
        {
            DetermineHypotheticalCrossSize(tree, line, constants, knownDimensions, availableSpace);
        }

        // Calculate child baselines. This function is internally smart and only computes child baselines
        // if they are necessary.
        CalculateChildrenBaseLines(tree, knownDimensions, availableSpace, flexLines, constants);

        // 8. Calculate the cross size of each flex line.
        CalculateCrossSize(flexLines, knownDimensions, constants);

        // 9. Handle 'align-content: stretch'.
        HandleAlignContentStretch(flexLines, knownDimensions, constants);

        // 10. Collapse visibility:collapse items.
        // TODO implement once (if ever) we support visibility:collapse

        // 11. Determine the used cross size of each flex item.
        DetermineUsedCrossSize(tree, flexLines, constants);

        // 9.5. Main-Axis Alignment

        // 12. Distribute any remaining free space.
        DistributeRemainingFreeSpace(flexLines, constants);

        // 9.6. Cross-Axis Alignment

        // 13. Resolve cross-axis auto margins (also includes 14).
        ResolveCrossAxisAutoMargins(flexLines, constants);

        // 15. Determine the flex container's used cross size.
        float totalLineCrossSize = DetermineContainerCrossSize(flexLines, knownDimensions, constants);

        // We have the container size.
        // If our caller does not care about performing layout we are done now.
        if (runMode == RunMode.ComputeSize)
        {
            return LayoutOutput.FromOuterSize(constants.ContainerSize);
        }

        // 16. Align all flex lines per align-content.
        AlignFlexLinesPerAlignContent(flexLines, constants, totalLineCrossSize);

        // Do a final layout pass and gather the resulting layouts
        Rect<float> inflowOverflowRect = FinalLayoutPass(tree, flexLines, constants);

        // Collect out-of-flow candidates in document order (direct out-of-flow children interleaved
        // with candidates bubbled from in-flow children's subtrees). These are laid out by the
        // out-of-flow positioning pass (ComputeOofLayout), which runs after this algorithm.
        OofCandidates candidates = OofCandidates.None;
        CollectOofCandidates(tree, node, constants, flexLines, ref candidates);

        bool isRtl = constants.LayoutDirection.IsRtl();
        Rect<float> absolutePositionInset = constants.Border.Add(new Rect<float>(
            isRtl ? constants.ScrollbarGutter.X : 0f,
            isRtl ? 0f : constants.ScrollbarGutter.X,
            0f,
            constants.ScrollbarGutter.Y));
        Size<float> absolutePositionArea = constants.ContainerSize.Sub(absolutePositionInset.SumAxes());
        Point<float> absolutePositionOffset = new(absolutePositionInset.Left, absolutePositionInset.Top);

        int len = tree.ChildCount(node);
        for (int order = 0; order < len; order++)
        {
            NodeId child = tree.GetChildId(node, order);
            if (tree.GetFlexboxChildStyle(child).BoxGenerationMode == BoxGenerationMode.None)
            {
                tree.SetUnroundedLayout(child, Layout.WithOrder((uint)order));
                tree.PerformChildLayout(child, Size.None, Size.None, Size.MaxContent, SizingMode.ContentSize, Line.False);
            }
        }

        // 8.5. Flex Container Baselines: calculate the flex container's first baseline
        // See https://www.w3.org/TR/css-flexbox-1/#flex-baselines
        // The baselines are generated from the startmost flex line, where "startmost" refers to the
        // line's visual position: for wrap-reverse containers the cross axis is flipped, so the
        // startmost line is the last line in flex-line order rather than the first.
        FlexLine? firstLine = flexLines.Count == 0 ? null : (constants.IsWrapReverse ? flexLines[flexLines.Count - 1] : flexLines[0]);
        float? firstVerticalBaseline = null;
        if (firstLine is not null && firstLine.Items.Count > 0)
        {
            List<FlexItem> items = firstLine.Items;
            if (constants.IsColumn)
            {
                // For column containers the baseline is generated from the startmost item in the line,
                // which for reverse-direction containers is the last item in flex order.
                FlexItem item = constants.Dir.IsReverse() ? items[items.Count - 1] : items[0];
                firstVerticalBaseline = item.Baseline;
            }
            else
            {
                FlexItem item = items.FirstOrDefault(i => i.ParticipatesInBaselineAlignment(constants.Dir)) ?? items[0];
                firstVerticalBaseline = item.Baseline;
            }
        }

        LayoutOutput output = LayoutOutput.FromSizesAndBaselines(
            constants.ContainerSize,
            inflowOverflowRect,
            Baselines.FromFirst(firstVerticalBaseline));
        output.OofCandidates = candidates;
        output.OofPositioningArea = new OofPositioningArea(absolutePositionArea, absolutePositionOffset);
        return output;
    }

    /// <summary>The layout of a flex container without children.</summary>
    private static LayoutOutput ComputeEmptyContainerLayout(ILayoutFlexboxContainer tree, LayoutInput inputs, AlgoConstants constants)
    {
        Size<float?> knownDimensions = inputs.KnownDimensions;
        FlexDirection dir = constants.Dir;

        Size<AvailableSpace> availableSpace = DetermineAvailableSpace(knownDimensions, inputs.AvailableSpace, constants);
        if (constants.NodeInnerSize.Main(dir) is float innerMainSize)
        {
            float outerMainSize = innerMainSize + constants.ContentBoxInset.MainAxisSum(dir);
            constants.InnerContainerSize.SetMain(dir, innerMainSize);
            constants.ContainerSize.SetMain(dir, outerMainSize);
        }
        else
        {
            DetermineContainerMainSize(tree, availableSpace, new List<FlexLine>(), constants);
        }

        if (inputs.RunMode == RunMode.ComputeSize && dir.MainAxisIsRequestedAxis(inputs.Axis))
        {
            Size<float> size = Size.Zero.WithMain(dir, constants.ContainerSize.Main(dir));
            return LayoutOutput.FromOuterSize(size);
        }

        DetermineContainerCrossSize(new List<FlexLine>(), knownDimensions, constants);

        // With no children there are no out-of-flow candidates, so the positioning area's value is
        // never read. It still has to be set: that is what makes ComputeOofLayout clear a
        // stale hoisted-children list from a run in which the container still had descendants.
        LayoutOutput output = LayoutOutput.FromOuterSize(constants.ContainerSize);
        output.OofPositioningArea = new OofPositioningArea(Size.Zero, Point.Zero);
        return output;
    }

    /// <summary><c>normal</c> behaves as <c>stretch</c> for flex items.</summary>
    private static AlignItems ResolveNormalAlignment(AlignItems alignment) =>
        alignment.Keyword == AlignItemsKeyword.Normal ? AlignItems.Stretch : alignment;

    /// <summary>Compute constants that can be reused during the flexbox algorithm.</summary>
    private static AlgoConstants ComputeConstants(
        ILayoutFlexboxContainer tree,
        IFlexboxContainerStyle style,
        Size<float?> knownDimensions,
        Size<bool> knownDimensionsAreDefinite,
        Size<float?> parentSize,
        Size<AvailableSpace> availableSpace)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;
        FlexDirection dir = style.FlexDirection;
        bool isRow = dir.IsRow();
        bool isColumn = dir.IsColumn();
        FlexWrap flexWrap = style.FlexWrap;
        bool isWrap = flexWrap.IsMultiLine();
        bool isWrapReverse = flexWrap.IsReverse();
        bool isBalance = flexWrap.IsBalance();
        ushort? lineCount = isWrap ? (ushort)Math.Max((int)style.FlexLineCount, 1) : null;

        float? aspectRatio = style.AspectRatio;
        Rect<float> padding = style.Padding.ResolveOrZero(parentSize.Width, calc);
        Rect<float> border = style.Border.ResolveOrZero(parentSize.Width, calc);
        Size<float> paddingBorderSum = padding.SumAxes().Add(border.SumAxes());
        Size<float> boxSizingAdjustment = style.BoxSizing == BoxSizing.ContentBox ? paddingBorderSum : Size.Zero;

        // `normal` behaves as `stretch` for flex items. This is resolved once here so that
        // items with an AlignSelf of null, which defer to this value, are also resolved.
        AlignItems alignItems = ResolveNormalAlignment(style.AlignItems);

        // `normal` behaves as `stretch` for flex containers. In the main axis `stretch` behaves as
        // `flex-start`, so a `normal` justify-content is resolved straight to `flex-start`.
        // <https://www.w3.org/TR/css-align-3/#distribution-flex>
        AlignContent alignContent = style.AlignContent.Keyword == AlignContentKeyword.Normal ? AlignContent.Stretch : style.AlignContent;
        AlignContent justifyContent = style.JustifyContent.Keyword == AlignContentKeyword.Normal ? AlignContent.FlexStart : style.JustifyContent;

        Direction layoutDirection = style.Direction;

        // Scrollbar gutters are reserved when the `overflow` property is set to `Overflow.Scroll`.
        // However, the axis are switched (transposed) because a node that scrolls vertically needs
        // *horizontal* space to be reserved for a scrollbar
        float scrollbarWidth = style.ScrollbarWidth;
        Point<Overflow> overflow = style.Overflow;
        Point<float> scrollbarGutter = overflow.Transpose().Map(o => o == Overflow.Scroll ? scrollbarWidth : 0f);
        bool isScrollContainer = overflow.X.IsScrollContainer() || overflow.Y.IsScrollContainer();

        Rect<float> contentBoxInset = padding.Add(border);
        contentBoxInset.Bottom += scrollbarGutter.Y;
        if (layoutDirection == Direction.Ltr)
        {
            contentBoxInset.Right += scrollbarGutter.X;
        }
        else
        {
            contentBoxInset.Left += scrollbarGutter.X;
        }

        Size<float?> nodeOuterSize = knownDimensions;
        Size<float?> nodeInnerSize = nodeOuterSize.MaybeSub(contentBoxInset.SumAxes());
        bool knownMainSizeIsDefinite = knownDimensionsAreDefinite.Main(dir);
        bool hasDefiniteMainSize = knownMainSizeIsDefinite && knownDimensions.Main(dir).HasValue;
        bool hasDefiniteCrossSize = knownDimensionsAreDefinite.Cross(dir) && knownDimensions.Cross(dir).HasValue;
        bool crossAxisAvailableSpaceIsDefinite = hasDefiniteCrossSize || availableSpace.Cross(dir).IsDefinite;
        Size<float> gap = style.Gap.ResolveOrZero(nodeInnerSize.Or(new Size<float?>(0f, 0f)), calc);

        return new AlgoConstants
        {
            Dir = dir,
            LayoutDirection = layoutDirection,
            IsRow = isRow,
            IsColumn = isColumn,
            IsWrap = isWrap,
            IsWrapReverse = isWrapReverse,
            IsBalance = isBalance,
            LineCount = lineCount,
            MinSize = style.MinSize
                .MaybeResolve(parentSize, calc)
                .MaybeApplyAspectRatio(aspectRatio)
                .MaybeAdd(boxSizingAdjustment),
            MaxSize = style.MaxSize
                .MaybeResolve(parentSize, calc)
                .MaybeApplyAspectRatio(aspectRatio)
                .MaybeAdd(boxSizingAdjustment),
            Border = border,
            Gap = gap,
            ContentBoxInset = contentBoxInset,
            ScrollbarGutter = scrollbarGutter,
            IsScrollContainer = isScrollContainer,
            AlignItems = alignItems,
            AlignContent = alignContent,
            JustifyContent = justifyContent,
            NodeOuterSize = nodeOuterSize,
            NodeInnerSize = nodeInnerSize,
            KnownMainSizeIsDefinite = knownMainSizeIsDefinite,
            HasDefiniteMainSize = hasDefiniteMainSize,
            HasDefiniteCrossSize = hasDefiniteCrossSize,
            CrossAxisAvailableSpaceIsDefinite = crossAxisAvailableSpaceIsDefinite,
            ContainerSize = Size.Zero,
            InnerContainerSize = Size.Zero,
        };
    }

    /// <summary>Resolve the relative inset of an item into main/cross offsets (honouring the direction).</summary>
    private static Size<float> ResolveRelativeInset(Rect<float?> inset, AlgoConstants constants)
    {
        FlexDirection direction = constants.Dir;
        bool isRtlRow = direction.IsRow() && constants.LayoutDirection.IsRtl();
        bool isRtlColumn = direction.IsColumn() && constants.LayoutDirection.IsRtl();

        float main = isRtlRow
            ? inset.MainEnd(direction) ?? -inset.MainStart(direction) ?? 0f
            : inset.MainStart(direction) ?? -inset.MainEnd(direction) ?? 0f;
        float cross = isRtlColumn
            ? -inset.CrossEnd(direction) ?? inset.CrossStart(direction) ?? 0f
            : inset.CrossStart(direction) ?? -inset.CrossEnd(direction) ?? 0f;

        Size<float> output = Size.Zero;
        output.SetMain(direction, main);
        output.SetCross(direction, cross);
        return output;
    }

    /// <summary>Generate anonymous flex items.</summary>
    private static List<FlexItem> GenerateAnonymousFlexItems(ILayoutFlexboxContainer tree, NodeId node, AlgoConstants constants)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;

        // Percentage sizes of items resolve against the container's inner size, but only if that size
        // is definite. A known main size which is derived from the container's own content is treated
        // as indefinite here.
        Size<float?> percentResolutionSize = constants.KnownMainSizeIsDefinite
            ? constants.NodeInnerSize
            : constants.NodeInnerSize.WithMain(constants.Dir, null);

        List<FlexItem> flexItems = new(tree.ChildCount(node));
        int index = -1;
        foreach (NodeId child in tree.ChildIds(node))
        {
            index++;
            IFlexboxItemStyle childStyle = tree.GetFlexboxChildStyle(child);
            if (childStyle.Position.IsOutOfFlow() || childStyle.BoxGenerationMode == BoxGenerationMode.None)
            {
                continue;
            }

            float? aspectRatio = childStyle.AspectRatio;
            Rect<float> padding = childStyle.Padding.ResolveOrZero(constants.NodeInnerSize.Width, calc);
            Rect<float> border = childStyle.Border.ResolveOrZero(constants.NodeInnerSize.Width, calc);
            Size<float> boxSizingAdjustment = childStyle.BoxSizing == BoxSizing.ContentBox ? padding.Add(border).SumAxes() : Size.Zero;
            Rect<LengthPercentageAuto> marginStyle = childStyle.Margin;

            flexItems.Add(new FlexItem
            {
                Node = child,
                Order = (uint)index,
                Size = childStyle.Size
                    .MaybeResolve(percentResolutionSize, calc)
                    .MaybeApplyAspectRatio(aspectRatio)
                    .MaybeAdd(boxSizingAdjustment),
                SizeStyle = childStyle.Size,
                MinSize = childStyle.MinSize.MaybeResolve(percentResolutionSize, calc).MaybeAdd(boxSizingAdjustment),
                MaxSize = childStyle.MaxSize.MaybeResolve(percentResolutionSize, calc).MaybeAdd(boxSizingAdjustment),
                AspectRatio = aspectRatio,
                RelativeInset = childStyle.Position == Position.Relative
                    ? ResolveRelativeInset(
                        childStyle.Inset.ZipSize<float?, float?>(constants.NodeInnerSize, (p, s) => p.MaybeResolve(s, calc)),
                        constants)
                    : Size.Zero,
                Margin = marginStyle.ResolveOrZero(constants.NodeInnerSize.Width, calc),
                MarginIsAuto = marginStyle.Map(m => m.IsAuto()),
                Padding = padding,
                Border = border,
                AlignSelf = ResolveNormalAlignment(childStyle.AlignSelf ?? constants.AlignItems)
                    .ResolveSelfRelative(childStyle.Direction, constants.LayoutDirection, constants.IsColumn),
                Overflow = childStyle.Overflow,
                Contain = childStyle.Contain,
                ScrollbarWidth = childStyle.ScrollbarWidth,
                FlexGrow = childStyle.FlexGrow,
                FlexShrink = childStyle.FlexShrink,
                OofCandidates = OofCandidates.None,
            });
        }

        return flexItems;
    }

    /// <summary>Determine the available main and cross space for the flex items.</summary>
    private static Size<AvailableSpace> DetermineAvailableSpace(
        Size<float?> knownDimensions,
        Size<AvailableSpace> outerAvailableSpace,
        AlgoConstants constants)
    {
        // Note: min/max/preferred size styles have already been applied to knownDimensions in the compute function above
        AvailableSpace width = knownDimensions.Width is float nodeWidth
            ? AvailableSpace.Definite(MathHelpers.Max(nodeWidth - constants.ContentBoxInset.HorizontalAxisSum(), 0f))
            : outerAvailableSpace.Width.MaybeSub(constants.ContentBoxInset.HorizontalAxisSum()).MaybeMax(0f);

        AvailableSpace height = knownDimensions.Height is float nodeHeight
            ? AvailableSpace.Definite(MathHelpers.Max(nodeHeight - constants.ContentBoxInset.VerticalAxisSum(), 0f))
            : outerAvailableSpace.Height.MaybeSub(constants.ContentBoxInset.VerticalAxisSum()).MaybeMax(0f);

        return new Size<AvailableSpace>(width, height);
    }

    /// <summary>Determine the flex base size and hypothetical main size of each item.</summary>
    private static void DetermineFlexBaseSize(
        ILayoutFlexboxContainer tree,
        AlgoConstants constants,
        Size<AvailableSpace> availableSpace,
        List<FlexItem> flexItems)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;
        FlexDirection dir = constants.Dir;

        foreach (FlexItem child in flexItems)
        {
            IFlexboxItemStyle childStyle = tree.GetFlexboxChildStyle(child.Node);

            // Parent size for child sizing
            float? crossAxisParentSize = constants.NodeInnerSize.Cross(dir);
            Size<float?> childParentSize = Size.FromCross(dir, crossAxisParentSize);

            // Available space for child sizing (excluding the child's margins)
            // Min/max sizes transferred through the aspect ratio are taken into account here
            // https://github.com/w3c/csswg-drafts/issues/10997
            Size<float?> transferredMinSize = child.MinSize.MaybeApplyAspectRatio(child.AspectRatio);
            Size<float?> transferredMaxSize = child.MaxSize.MaybeApplyAspectRatio(child.AspectRatio);
            float? childMinCross = transferredMinSize.Cross(dir);
            float? childMaxCross = transferredMaxSize.Cross(dir);

            // Clamp available space by min- and max- size
            AvailableSpace availableCross = availableSpace.Cross(dir);
            AvailableSpace crossAxisAvailableSpace;
            if (availableCross.IsDefinite)
            {
                crossAxisAvailableSpace = AvailableSpace.Definite(
                    MathHelpers.Max(
                        constants.DividedCrossSpace(crossAxisParentSize ?? availableCross.Value) - child.Margin.CrossAxisSum(dir),
                        0f)
                    .MaybeClamp(childMinCross, childMaxCross));
            }
            else if (availableCross.IsMinContent)
            {
                crossAxisAvailableSpace = childMinCross is float min ? AvailableSpace.Definite(min) : AvailableSpace.MinContent;
            }
            else
            {
                crossAxisAvailableSpace = childMaxCross is float max ? AvailableSpace.Definite(max) : AvailableSpace.MaxContent;
            }

            // Known dimensions for child sizing
            bool childCrossSizeIsDefinite = child.Size.Cross(dir).HasValue;
            Size<float?> childKnownDimensions = child.Size.WithMain(dir, null);

            // Clamp the definite cross size by the cross min/max sizes so that sizes
            // transferred through an intrinsic aspect ratio (e.g. for replaced elements)
            // are based on the used cross size.
            childKnownDimensions.SetCross(
                dir,
                childKnownDimensions.Cross(dir).MaybeClamp(transferredMinSize.Cross(dir), transferredMaxSize.Cross(dir)));
            if (child.AlignSelf == AlignItems.Stretch
                && !child.MarginIsAuto.CrossStart(constants.Dir)
                && !child.MarginIsAuto.CrossEnd(constants.Dir)
                && childKnownDimensions.Cross(dir) is null)
            {
                childKnownDimensions.SetCross(dir, crossAxisAvailableSpace.IntoOption());

                // The cross size of a stretched item is definite if the container has a definite
                // cross size (https://www.w3.org/TR/css-flexbox-1/#definite-sizes)
                childCrossSizeIsDefinite = !constants.IsWrap && constants.HasDefiniteCrossSize && crossAxisParentSize.HasValue;
            }

            float boxSizingAdjustment = child.BoxSizingAdjustment(childStyle).Main(dir);

            // Percentage flex basis values resolve against the container's inner main size, but only
            // if that size is definite. A known main size which is derived from the container's own
            // content is treated as indefinite here.
            float? percentResolutionMainSize = constants.KnownMainSizeIsDefinite ? constants.NodeInnerSize.Main(dir) : null;
            Dimension flexBasisStyle = childStyle.FlexBasis;
            float? flexBasis = flexBasisStyle.MaybeResolve(percentResolutionMainSize, calc).MaybeAdd(boxSizingAdjustment);

            child.FlexBasis = ComputeFlexBasis();

            float ComputeFlexBasis()
            {
                // A. If the item has a definite used flex basis, that's the flex base size.

                // B. If the flex item has an intrinsic aspect ratio,
                //    a used flex basis of content, and a definite cross size,
                //    then the flex base size is calculated from its inner
                //    cross size and the flex item's intrinsic aspect ratio.
                float? mainSize = child.Size.Main(dir);
                float? mainStretchSize = percentResolutionMainSize.MaybeSub(child.Margin.MainAxisSum(dir)).MaybeMax(0f);

                // A flex basis that is a sizing keyword (min-content, max-content, fit-content,
                // fit-content(...), stretch) is used in place of the main size property: `stretch`
                // resolves to an exact (definite) size while the other keywords determine the
                // available space constraint the item is measured under. A keyword that cannot be
                // resolved in the current context behaves as `content`.
                AvailableSpace? keywordMainAvailableSpace;
                if (flexBasisStyle.IsContent())
                {
                    // A flex basis of `content` indicates an automatic size based on the item's
                    // content: the item is measured (ignoring its main size property) under the
                    // default constraint below
                    keywordMainAvailableSpace = null;
                }
                else if (flexBasisStyle.IsSizingKeyword())
                {
                    SizingKeywordResolution? resolution = SizingKeyword.ResolveSizingKeyword(
                        flexBasisStyle, mainStretchSize, percentResolutionMainSize, calc);
                    if (resolution is { IsExact: true } exact)
                    {
                        child.FlexBasisIsDefinite = true;
                        return exact.ExactSize;
                    }

                    keywordMainAvailableSpace = resolution?.MeasureSpace;
                }
                else
                {
                    if ((flexBasis ?? (flexBasisStyle.IsAuto() ? mainSize : null)) is float definiteBasis)
                    {
                        child.FlexBasisIsDefinite = true;
                        return definiteBasis;
                    }

                    // A main size that is a sizing keyword either resolves to an exact size or
                    // determines the available space constraint the item is measured under
                    if (flexBasisStyle.IsAuto())
                    {
                        SizingKeywordResolution? resolution = SizingKeyword.ResolveSizingKeyword(
                            child.SizeStyle.Main(dir), mainStretchSize, percentResolutionMainSize, calc);
                        if (resolution is { IsExact: true } exact)
                        {
                            child.FlexBasisIsDefinite = true;
                            return exact.ExactSize;
                        }

                        keywordMainAvailableSpace = resolution?.MeasureSpace;
                    }
                    else
                    {
                        keywordMainAvailableSpace = null;
                    }
                }

                // C. If the used flex basis is content or depends on its available space,
                //    and the flex container is being sized under a min-content or max-content
                //    constraint (e.g. when performing automatic table layout [CSS21]),
                //    size the item under that constraint. The flex base size is the item's
                //    resulting main size.
                // This is covered by the implementation of E below, which passes the available_space constraint
                // through to the child size computation. It may need a separate implementation if/when D is implemented.

                // D. Otherwise, if the used flex basis is content or depends on its
                //    available space, the available main size is infinite, and the flex item's
                //    inline axis is parallel to the main axis, lay the item out using the rules
                //    for a box in an orthogonal flow [CSS3-WRITING-MODES]. The flex base size
                //    is the item's max-content main size.
                // TODO if/when vertical writing modes are supported

                // If the item has an aspect ratio and a definite cross size then the flex base size
                // is calculated by transferring that cross size through the aspect ratio (case B
                // above), and is therefore definite.
                if (childCrossSizeIsDefinite && child.AspectRatio is float ratio && childKnownDimensions.Cross(dir) is float cross)
                {
                    child.FlexBasisIsDefinite = true;
                    return dir.IsRow() ? cross * ratio : cross / ratio;
                }

                // E. Otherwise, size the item into the available space using its used flex basis
                //    in place of its main size, treating a value of content as max-content.
                //    If a cross size is needed to determine the main size (e.g. when the
                //    flex item's main size is in its block axis) and the flex item's cross size
                //    is auto and not definite, in this calculation use fit-content as the
                //    flex item's cross size. The flex base size is the item's resulting main size.
                Size<AvailableSpace> childAvailableSpace = Size.MaxContent
                    .WithMain(
                        dir,
                        keywordMainAvailableSpace ?? (
                            // Map AvailableSpace.Definite to AvailableSpace.MaxContent
                            availableSpace.Main(dir).IsMinContent ? AvailableSpace.MinContent : AvailableSpace.MaxContent))
                    .WithCross(dir, crossAxisAvailableSpace);

                return tree.MeasureChildSize(
                    child.Node,
                    childKnownDimensions,
                    childParentSize,
                    childAvailableSpace,
                    SizingMode.ContentSize,
                    dir.MainAxis(),
                    Line.False);
            }

            // Floor flex-basis by the padding_border_sum (floors inner_flex_basis at zero)
            // This seems to be in violation of the spec which explicitly states that the content box should not be floored at zero
            // (like it usually is) when calculating the flex-basis. But including this matches both Chrome and Firefox's behaviour.
            //
            // TODO: resolve spec violation
            // Spec: https://www.w3.org/TR/css-flexbox-1/#intrinsic-item-contributions
            // Spec: https://www.w3.org/TR/css-flexbox-1/#change-2016-max-contribution
            float paddingBorderSum = child.Padding.MainAxisSum(constants.Dir) + child.Border.MainAxisSum(constants.Dir);
            child.FlexBasis = MathHelpers.Max(child.FlexBasis, paddingBorderSum);

            // The hypothetical main size is the item's flex base size clamped according to its
            // used min and max main sizes (and flooring the content box size at zero).

            child.InnerFlexBasis = child.FlexBasis - paddingBorderSum;

            Size<float?> paddingBorderAxesSums = child.Padding.Add(child.Border).SumAxes().AsOptions();

            // Note that it is important that the `parent_size` parameter in the main axis is not set for this
            // function call as it used for resolving percentages, and percentage size in an axis should not contribute
            // to a min-content contribution in that same axis. However the `parent_size` and `available_space` *should*
            // be set to their usual values in the cross axis so that wrapping content can wrap correctly.
            //
            // See https://drafts.csswg.org/css-sizing-3/#min-percentage-contribution
            Size<float?> automaticMinSize = new(
                child.Overflow.X.MaybeIntoAutomaticMinSize(),
                child.Overflow.Y.MaybeIntoAutomaticMinSize());
            float? styleMinMainSize = child.MinSize.Or(automaticMinSize).Main(dir);

            if (styleMinMainSize is float styleMin)
            {
                child.ResolvedMinimumMainSize = styleMin;
            }
            else
            {
                Size<AvailableSpace> minContentAvailableSpace = Size.MinContent.WithCross(dir, crossAxisAvailableSpace);
                float minContentMainSize = tree.MeasureChildSize(
                    child.Node,
                    childKnownDimensions,
                    childParentSize,
                    minContentAvailableSpace,
                    SizingMode.ContentSize,
                    dir.MainAxis(),
                    Line.False);

                // 4.5. Automatic Minimum Size of Flex Items
                // https://www.w3.org/TR/css-flexbox-1/#min-size-auto
                float clampedMinContentSize = minContentMainSize.MaybeMin(child.Size.Main(dir)).MaybeMin(transferredMaxSize.Main(dir));
                child.ResolvedMinimumMainSize = clampedMinContentSize.MaybeMax(paddingBorderAxesSums.Main(dir));
            }

            // Sizes transferred through the aspect ratio clamp the hypothetical main size,
            // but do not participate in resolving flexible lengths or clamping the final size.
            // https://github.com/w3c/csswg-drafts/issues/10997
            float hypotheticalInnerMinMain = child.ResolvedMinimumMainSize
                .MaybeMax(transferredMinSize.Main(constants.Dir))
                .MaybeMax(paddingBorderAxesSums.Main(constants.Dir));
            float hypotheticalInnerSize = child.FlexBasis.MaybeClamp(hypotheticalInnerMinMain, transferredMaxSize.Main(constants.Dir));
            float hypotheticalOuterSize = hypotheticalInnerSize + child.Margin.MainAxisSum(constants.Dir);

            child.HypotheticalInnerSize.SetMain(constants.Dir, hypotheticalInnerSize);
            child.HypotheticalOuterSize.SetMain(constants.Dir, hypotheticalOuterSize);
        }
    }

    /// <summary>A line of one item for each item (every wrapping opportunity is taken).</summary>
    private static List<FlexLine> OneItemPerLine(List<FlexItem> flexItems)
    {
        List<FlexLine> lines = new(flexItems.Count);
        foreach (FlexItem item in flexItems)
        {
            lines.Add(new FlexLine(new List<FlexItem>(1) { item }));
        }

        return lines;
    }

    /// <summary>
    /// The main axis space that items wrap against: definite main size, max main size, or the available space.
    /// </summary>
    private static AvailableSpace WrapMainAxisAvailableSpace(AlgoConstants constants, Size<AvailableSpace> availableSpace)
    {
        if (constants.MaxSize.Main(constants.Dir) is float maxSize)
        {
            float available = availableSpace.Main(constants.Dir).IntoOption() ?? maxSize;

            // If the container's main size is not definite then it is at most the max main size,
            // so the max size (and not the available space) is the limit that items wrap against.
            available = constants.HasDefiniteMainSize ? available : MathHelpers.Min(available, maxSize);
            return AvailableSpace.Definite(available.MaybeMax(constants.MinSize.Main(constants.Dir)));
        }

        // A column container's automatic main size is content-based, so definite available space
        // handed down by an ancestor does not constrain where its lines wrap. Automatic widths
        // resolve against available space, so rows do wrap against it.
        if (!constants.Dir.IsRow() && !constants.HasDefiniteMainSize && availableSpace.Main(constants.Dir).IsDefinite)
        {
            return AvailableSpace.MaxContent;
        }

        return availableSpace.Main(constants.Dir);
    }

    /// <summary>Collect flex items into flex lines.</summary>
    private static List<FlexLine> CollectFlexLines(AlgoConstants constants, Size<AvailableSpace> availableSpace, List<FlexItem> flexItems)
    {
        // Wrapping into multiple lines requires a definite main size. If the container's known main size
        // is derived from its own content (and is thus indefinite) then all items are collected into a
        // single flex line, matching how the container was sized under a min/max-content constraint.
        if (!constants.IsWrap || !constants.KnownMainSizeIsDefinite)
        {
            return new List<FlexLine>(1) { new(flexItems) };
        }

        AvailableSpace mainAxisAvailableSpace = WrapMainAxisAvailableSpace(constants, availableSpace);

        // If we're sizing under a max-content constraint then the flex items will never wrap
        // (at least for now - future extensions to the CSS spec may add provisions for forced wrap points)
        if (mainAxisAvailableSpace.IsMaxContent)
        {
            return new List<FlexLine>(1) { new(flexItems) };
        }

        // If flex-wrap is Wrap and we're sizing under a min-content constraint, then we take every possible wrapping opportunity
        // and place each item in it's own line
        if (mainAxisAvailableSpace.IsMinContent)
        {
            return OneItemPerLine(flexItems);
        }

        float mainAxisAvailable = mainAxisAvailableSpace.Value;
        List<FlexLine> lines = new(1);
        float mainAxisGap = constants.Gap.Main(constants.Dir);
        int start = 0;
        while (start < flexItems.Count)
        {
            // Find index of the first item in the next line
            // (or the last item if all remaining items are in the current line)
            int remaining = flexItems.Count - start;
            float lineLength = 0f;
            int index = remaining;
            for (int idx = 0; idx < remaining; idx++)
            {
                // Gaps only occur between items (not before the first one or after the last one)
                // So first item in the line does not contribute a gap to the line length
                float gapContribution = idx == 0 ? 0f : mainAxisGap;
                lineLength += flexItems[start + idx].HypotheticalOuterSize.Main(constants.Dir) + gapContribution;
                if (lineLength > mainAxisAvailable && idx != 0)
                {
                    index = idx;
                    break;
                }
            }

            lines.Add(new FlexLine(flexItems.GetRange(start, index)));
            start += index;
        }

        return lines;
    }

    /// <summary>Collect flex items into balanced flex lines (<c>flex-wrap: balance</c>).</summary>
    private static List<FlexLine> CollectBalancedFlexLines(AlgoConstants constants, Size<AvailableSpace> availableSpace, List<FlexItem> flexItems)
    {
        if (flexItems.Count == 0)
        {
            return new List<FlexLine>(0);
        }

        // If the container's known main size is derived from its own content (and is thus indefinite)
        // then items are balanced without a size limit, matching how the container was sized under a
        // min/max-content constraint.
        AvailableSpace mainAxisAvailableSpace = constants.KnownMainSizeIsDefinite
            ? WrapMainAxisAvailableSpace(constants, availableSpace)
            : AvailableSpace.MaxContent;

        // If we're sizing under a min-content constraint then we take every possible wrapping
        // opportunity and place each item in its own line, the same as greedy wrapping (the
        // min-content main size of a multi-line container is the size of its largest item)
        if (mainAxisAvailableSpace.IsMinContent)
        {
            return OneItemPerLine(flexItems);
        }

        float lineBreakSize = mainAxisAvailableSpace.IntoOption() ?? float.PositiveInfinity;
        int minLineCount = constants.LineCount ?? 1;
        List<float> itemSizes = flexItems.Select(item => item.HypotheticalOuterSize.Main(constants.Dir)).ToList();
        List<int> itemCounts = FlexboxBalance.BalancedLineItemCounts(
            itemSizes,
            lineBreakSize,
            constants.Gap.Main(constants.Dir),
            minLineCount);

        List<FlexLine> lines = new(itemCounts.Count);
        int start = 0;
        foreach (int count in itemCounts)
        {
            lines.Add(new FlexLine(flexItems.GetRange(start, count)));
            start += count;
        }

        System.Diagnostics.Debug.Assert(start == flexItems.Count);
        return lines;
    }

    /// <summary>Which of an item's known dimensions are definite, for the item's own percentage resolution.</summary>
    private static Size<bool> ItemKnownDimensionDefiniteness(AlgoConstants constants, FlexItem item)
    {
        FlexDirection dir = constants.Dir;
        bool mainIsDefinite = constants.HasDefiniteMainSize || item.FlexBasisIsDefinite;

        // An item's cross size is definite if it is stretched (it stretches to the flex line, whose
        // size is known by the time the item's final layout is performed), or if its cross size style
        // resolved to a definite size. Additionally, a non-stretched item's cross *width* is fit-content
        // sized, and a size resolved by fit-content sizing against definite available space is itself
        // definite (<https://www.w3.org/TR/css-sizing-3/#definite>). The same does not apply to cross
        // heights, which are content-based and therefore indefinite when not stretched.
        bool hasCrossAutoMargins = item.MarginIsAuto.CrossStart(dir) || item.MarginIsAuto.CrossEnd(dir);
        Dimension crossSize = item.SizeStyle.Cross(dir);
        bool isStretched = !hasCrossAutoMargins
            && (crossSize.IsStretch() || (item.AlignSelf == AlignItems.Stretch && crossSize.IsAuto()));
        bool crossIsDefinite = isStretched
            || item.Size.Cross(dir).HasValue
            || (!dir.IsRow() && constants.CrossAxisAvailableSpaceIsDefinite);
        return new Size<bool>(true, true).WithMain(dir, mainIsDefinite).WithCross(dir, crossIsDefinite);
    }

    /// <summary>The number of gaps between <paramref name="numItems"/> items, times the gap size.</summary>
    private static float SumAxisGaps(float gap, int numItems)
    {
        // Gaps only exist between items, so...
        if (numItems <= 1)
        {
            // ...if there are less than 2 items then there are no gaps
            return 0f;
        }

        // ...otherwise there are (num_items - 1) gaps
        return gap * (numItems - 1);
    }

    /// <summary>Rust <c>f32::is_normal</c>.</summary>
    private static bool IsNormal(float value) =>
        MathHelpers.IsFinite(value) && value != 0f && Math.Abs(value) >= 1.17549435E-38f;
}
