namespace TaffySharp;

internal static partial class FlexboxAlgorithm
{
    /// <summary>Determine the container's main size (if not already known).</summary>
    private static void DetermineContainerMainSize(
        ILayoutFlexboxContainer tree,
        Size<AvailableSpace> availableSpace,
        List<FlexLine> lines,
        AlgoConstants constants)
    {
        FlexDirection dir = constants.Dir;
        float mainContentBoxInset = constants.ContentBoxInset.MainAxisSum(constants.Dir);

        float outerMainSize = constants.NodeOuterSize.Main(constants.Dir) is float knownOuterMainSize
            ? knownOuterMainSize
            : ContentBasedOuterMainSize();

        float ContentBasedOuterMainSize()
        {
            AvailableSpace mainAvailableSpace = availableSpace.Main(dir);
            float mainAxisGap = constants.Gap.Main(constants.Dir);

            float ItemMainLength(FlexItem child)
            {
                float paddingBorderSum = child.Padding.Add(child.Border).MainAxisSum(constants.Dir);
                return MathHelpers.Max(
                    child.FlexBasis.MaybeMax(child.MinSize.Main(constants.Dir)) + child.Margin.MainAxisSum(constants.Dir),
                    paddingBorderSum);
            }

            float LongestLineLength()
            {
                float longest = 0f;
                bool any = false;
                foreach (FlexLine line in lines)
                {
                    float lineMainAxisGap = SumAxisGaps(mainAxisGap, line.Items.Count);
                    float totalTargetSize = 0f;
                    foreach (FlexItem child in line.Items)
                    {
                        totalTargetSize += ItemMainLength(child);
                    }

                    float lineLength = totalTargetSize + lineMainAxisGap;
                    if (!any || lineLength.CompareTo(longest) >= 0)
                    {
                        longest = lineLength;
                        any = true;
                    }
                }

                return longest;
            }

            if (mainAvailableSpace.IsDefinite)
            {
                float mainAxisAvailableSpace = mainAvailableSpace.Value;
                float size = LongestLineLength() + mainContentBoxInset;

                // A balanced container can produce multiple lines that all fit within the
                // available space (via `flex-line-count`), in which case fit-content sizing
                // uses its max-content size: the longest line when items are balanced across
                // the minimum line count without a size limit.
                if (constants.IsBalance)
                {
                    int minLineCount = constants.LineCount ?? 1;
                    int itemCount = 0;
                    foreach (FlexLine line in lines)
                    {
                        itemCount += line.Items.Count;
                    }

                    if (itemCount == 0)
                    {
                        return size;
                    }

                    List<float> itemLengths = new(itemCount);
                    foreach (FlexLine line in lines)
                    {
                        foreach (FlexItem child in line.Items)
                        {
                            itemLengths.Add(ItemMainLength(child));
                        }
                    }

                    List<int> itemCounts = FlexboxBalance.BalancedLineItemCounts(
                        itemLengths,
                        float.PositiveInfinity,
                        mainAxisGap,
                        Math.Max(minLineCount, 1));
                    float widestLineLength = 0f;
                    int index = 0;
                    foreach (int count in itemCounts)
                    {
                        float lineLength = 0f;
                        for (int i = index; i < index + count; i++)
                        {
                            lineLength += itemLengths[i];
                        }

                        lineLength += SumAxisGaps(mainAxisGap, count);
                        widestLineLength = MathHelpers.Max(widestLineLength, lineLength);
                        index += count;
                    }

                    float maxContentSize = widestLineLength + mainContentBoxInset;
                    return MathHelpers.Max(size, MathHelpers.Min(maxContentSize, mainAxisAvailableSpace));
                }

                return lines.Count > 1 ? MathHelpers.Max(size, mainAxisAvailableSpace) : size;
            }

            if (mainAvailableSpace.IsMinContent && constants.IsWrap)
            {
                return LongestLineLength() + mainContentBoxInset;
            }

            // Define a base main_size variable. This is mutated once for iteration over the outer
            // loop over the flex lines as:
            //   "The flex container's max-content size is the largest sum of the afore-calculated sizes of all items within a single line."
            float mainSize = 0f;
            foreach (FlexLine line in lines)
            {
                foreach (FlexItem item in line.Items)
                {
                    float? styleMin = item.MinSize.Main(constants.Dir);
                    float marginSum = item.Margin.MainAxisSum(constants.Dir);
                    float? stylePreferred = item.Size.Main(constants.Dir);
                    float? styleMax = item.MaxSize.Main(constants.Dir);

                    // The spec seems a bit unclear on this point (my initial reading was that the `.maybe_max(style_preferred)` should
                    // not be included here), however for row containers this matches both Chrome and Firefox as of 9th March 2023.
                    // For column containers the flex base size alone is the clamping basis (as the spec says): a column item's
                    // `height` must not inflate the container's max-content size beyond the item's flex-basis.
                    //
                    // Spec: https://www.w3.org/TR/css-flexbox-1/#intrinsic-item-contributions
                    // Spec modification: https://www.w3.org/TR/css-flexbox-1/#change-2016-max-contribution
                    // Issue: https://github.com/w3c/csswg-drafts/issues/1435
                    // Gentest: padding_border_overrides_size_flex_basis_0.html
                    // Gentest: blockflex_min_content_ignores_own_height_as_percentage_basis.html
                    float clampingBasis = constants.IsRow ? item.FlexBasis.MaybeMax(stylePreferred) : item.FlexBasis;
                    float? flexBasisMin = item.FlexShrink == 0f ? clampingBasis : null;
                    float? flexBasisMax = item.FlexGrow == 0f ? clampingBasis : null;
                    float minMainSize = MathHelpers.Max(
                        styleMin.MaybeMax(flexBasisMin) ?? flexBasisMin ?? item.ResolvedMinimumMainSize,
                        item.ResolvedMinimumMainSize);
                    float maxMainSize = styleMax.MaybeMin(flexBasisMax) ?? flexBasisMax ?? float.PositiveInfinity;

                    float contentContribution;
                    if (stylePreferred is float pref && (maxMainSize <= minMainSize || maxMainSize <= pref))
                    {
                        // If the clamping values are such that max <= min, then we can avoid the expensive step of computing the content size
                        // as we know that the clamping values will override it anyway
                        contentContribution = MathHelpers.Max(MathHelpers.Min(pref, maxMainSize), minMainSize) + marginSum;
                    }
                    else if (maxMainSize <= minMainSize)
                    {
                        contentContribution = minMainSize + marginSum;
                    }
                    else if (item.IsScrollContainer())
                    {
                        // Else compute the min- or -max content size and apply the full formula for computing the
                        // min- or max- content contribution
                        contentContribution = item.FlexBasis + marginSum;
                    }
                    else if (stylePreferred is float preferred)
                    {
                        // If the item has a definite preferred main size then that is its content
                        // contribution (an inherent-size measure of the item would return it), floored
                        // by the item's main-axis padding+border, so measuring the item can be skipped.
                        // Min/max clamping is applied in the same way as for measured contributions.
                        float itemPbMain = item.Padding.MainAxisSum(constants.Dir) + item.Border.MainAxisSum(constants.Dir);
                        float innerMainSize = MathHelpers.Max(preferred, itemPbMain);
                        contentContribution = constants.IsRow
                            ? (innerMainSize + marginSum).MaybeClamp(styleMin, styleMax)
                            : (MathHelpers.Max(innerMainSize, item.FlexBasis) + marginSum).MaybeClamp(styleMin, styleMax);
                    }
                    else
                    {
                        // Parent size for child sizing
                        float? crossAxisParentSize = constants.NodeInnerSize.Cross(dir);

                        // Available space for child sizing (excluding the child's margins)
                        float? childMinCross = item.MinSize.Cross(dir);
                        float? childMaxCross = item.MaxSize.Cross(dir);
                        AvailableSpace crossAxisAvailableSpace = availableSpace
                            .Cross(dir)
                            .MapDefiniteValue(val => MathHelpers.Max(
                                constants.DividedCrossSpace(crossAxisParentSize ?? val) - item.Margin.CrossAxisSum(dir),
                                0f))
                            .MaybeClamp(childMinCross, childMaxCross);

                        Size<AvailableSpace> childAvailableSpace = availableSpace.WithCross(dir, crossAxisAvailableSpace);

                        // Known dimensions for child sizing
                        Size<float?> childKnownDimensions = item.Size.WithMain(dir, null);

                        // Clamp the definite cross size by the cross min/max sizes, as
                        // DetermineFlexBaseSize does: an item measured wider than its
                        // max-width wraps onto fewer lines and under-reports its height.
                        Size<float?> transferredMinSize = item.MinSize.MaybeApplyAspectRatio(item.AspectRatio);
                        Size<float?> transferredMaxSize = item.MaxSize.MaybeApplyAspectRatio(item.AspectRatio);
                        childKnownDimensions.SetCross(
                            dir,
                            childKnownDimensions.Cross(dir).MaybeClamp(transferredMinSize.Cross(dir), transferredMaxSize.Cross(dir)));
                        if (item.AlignSelf == AlignItems.Stretch && childKnownDimensions.Cross(dir) is null)
                        {
                            childKnownDimensions.SetCross(dir, crossAxisAvailableSpace.IntoOption());
                        }

                        // Either the min- or max- content size depending on which constraint we are sizing under.
                        // TODO: Optimise by using already computed values where available
                        float measuredMainSize = tree.MeasureChildSize(
                            item.Node,
                            childKnownDimensions,
                            constants.NodeInnerSize,
                            childAvailableSpace,
                            SizingMode.ContentSize,
                            dir.MainAxis(),
                            Line.False);

                        // A known cross size is transferred through the item's aspect-ratio
                        // and floors the measured content size
                        float? transferredMainSize = item.AspectRatio is float ratio && childKnownDimensions.Cross(dir) is float cross
                            ? (constants.IsRow ? cross * ratio : cross / ratio)
                            : null;
                        float innerMainSize = measuredMainSize.MaybeMax(transferredMainSize);

                        // This is somewhat bizarre in that it's asymmetrical depending whether the flex container is a column or a row.
                        //
                        // I *think* this might relate to https://drafts.csswg.org/css-flexbox-1/#algo-main-container:
                        //
                        //    "The automatic block size of a block-level flex container is its max-content size."
                        //
                        // Which could suggest that flex-basis defining a vertical size does not shrink because it is in the block axis, and the automatic size
                        // in the block axis is a MAX content size. Whereas a flex-basis defining a horizontal size does shrink because the automatic size in
                        // inline axis is MIN content size (although I don't have a reference for that).
                        //
                        // Ultimately, this was not found by reading the spec, but by trial and error fixing tests to align with Webkit/Firefox output.
                        // (see the `flex_basis_unconstraint_row` and `flex_basis_uncontraint_column` generated tests which demonstrate this)
                        contentContribution = constants.IsRow
                            ? (innerMainSize + marginSum).MaybeClamp(styleMin, styleMax)
                            : (MathHelpers.Max(innerMainSize, item.FlexBasis) + marginSum).MaybeClamp(styleMin, styleMax);
                    }

                    float diff = contentContribution - marginSum - item.FlexBasis;
                    if (diff > 0f)
                    {
                        item.ContentFlexFraction = diff / MathHelpers.Max(1f, item.FlexGrow);
                    }
                    else if (diff < 0f)
                    {
                        float scaledShrinkFactor = MathHelpers.Max(1f, item.FlexShrink) * item.InnerFlexBasis;
                        item.ContentFlexFraction = diff / scaledShrinkFactor;
                    }
                    else
                    {
                        // We are assuming that diff is 0.0 here and that we haven't accidentally introduced a NaN
                        item.ContentFlexFraction = 0f;
                    }
                }

                // TODO Spec says to scale everything by the line's max flex fraction. But neither Chrome nor firefox implement this
                // so we don't either.
                //
                // Add each item's flex base size to the product of:
                //   - its flex grow factor (or scaled flex shrink factor,if the chosen max-content flex fraction was negative)
                //   - the chosen max-content flex fraction
                // then clamp that result by the max main size floored by the min main size.
                //
                // The flex container's max-content size is the largest sum of the afore-calculated sizes of all items within a single line.

                // A wrapping row container is at least as large as its largest item (which matters when another item
                // has a negative margin). This matches Chrome. Firefox does not apply this floor.
                bool floorByLargestItem = constants.IsWrap && constants.IsRow;
                float largestItemSize = 0f;
                float itemMainSizeSum = 0f;
                foreach (FlexItem item in line.Items)
                {
                    float flexFraction = item.ContentFlexFraction;
                    float flexContribution;
                    if (item.ContentFlexFraction > 0f)
                    {
                        flexContribution = MathHelpers.Max(1f, item.FlexGrow) * flexFraction;
                    }
                    else if (item.ContentFlexFraction < 0f)
                    {
                        float scaledShrinkFactor = MathHelpers.Max(1f, item.FlexShrink) * item.InnerFlexBasis;
                        flexContribution = scaledShrinkFactor == 0f ? 0f : scaledShrinkFactor * flexFraction;
                    }
                    else
                    {
                        flexContribution = 0f;
                    }

                    float size = item.FlexBasis + flexContribution + item.Margin.MainAxisSum(constants.Dir);
                    if (floorByLargestItem)
                    {
                        largestItemSize = MathHelpers.Max(largestItemSize, size);
                    }

                    itemMainSizeSum += size;
                }

                float gapSum = SumAxisGaps(constants.Gap.Main(constants.Dir), line.Items.Count);
                mainSize = MathHelpers.Max(mainSize, MathHelpers.Max(largestItemSize, itemMainSizeSum + gapSum));
            }

            return mainSize + mainContentBoxInset;
        }

        float mainScrollbarGutter = constants.Dir.IsRow() ? constants.ScrollbarGutter.X : constants.ScrollbarGutter.Y;
        outerMainSize = MathHelpers.Max(
            outerMainSize.MaybeClamp(constants.MinSize.Main(constants.Dir), constants.MaxSize.Main(constants.Dir)),
            mainContentBoxInset - mainScrollbarGutter);

        float innerMainSize = MathHelpers.Max(outerMainSize - mainContentBoxInset, 0f);
        constants.ContainerSize.SetMain(constants.Dir, outerMainSize);
        constants.InnerContainerSize.SetMain(constants.Dir, innerMainSize);
        constants.NodeInnerSize.SetMain(constants.Dir, innerMainSize);
    }

    /// <summary>Resolve the flexible lengths of the items within a flex line.</summary>
    private static void ResolveFlexibleLengths(FlexLine line, AlgoConstants constants)
    {
        FlexDirection dir = constants.Dir;
        float totalMainAxisGap = SumAxisGaps(constants.Gap.Main(dir), line.Items.Count);
        float? nodeInnerMain = constants.NodeInnerSize.Main(dir);

        // 1. Determine the used flex factor. Sum the outer hypothetical main sizes of all
        //    items on the line. If the sum is less than the flex container's inner main size,
        //    use the flex grow factor for the rest of this algorithm; otherwise, use the
        //    flex shrink factor.
        float totalHypotheticalOuterMainSize = 0f;
        foreach (FlexItem child in line.Items)
        {
            totalHypotheticalOuterMainSize += child.HypotheticalOuterSize.Main(dir);
        }

        float usedFlexFactor = totalMainAxisGap + totalHypotheticalOuterMainSize;
        bool growing = usedFlexFactor < (nodeInnerMain ?? 0f);
        bool shrinking = usedFlexFactor > (nodeInnerMain ?? 0f);
        bool exactlySized = !growing & !shrinking;

        // 2. Size inflexible items. Freeze, setting its target main size to its hypothetical main size
        //    - Any item that has a flex factor of zero
        //    - If using the flex grow factor: any item that has a flex base size
        //      greater than its hypothetical main size
        //    - If using the flex shrink factor: any item that has a flex base size
        //      smaller than its hypothetical main size
        foreach (FlexItem child in line.Items)
        {
            float innerTargetSize = child.HypotheticalInnerSize.Main(dir);
            child.TargetSize.SetMain(dir, innerTargetSize);

            if (exactlySized
                || (child.FlexGrow == 0f && child.FlexShrink == 0f)
                || (growing && child.FlexBasis > child.HypotheticalInnerSize.Main(dir))
                || (shrinking && child.FlexBasis < child.HypotheticalInnerSize.Main(dir)))
            {
                child.Frozen = true;
                float outerTargetSize = innerTargetSize + child.Margin.MainAxisSum(dir);
                child.OuterTargetSize.SetMain(dir, outerTargetSize);
            }
        }

        if (exactlySized)
        {
            return;
        }

        // 3. Calculate initial free space. Sum the outer sizes of all items on the line,
        //    and subtract this from the flex container's inner main size. For frozen items,
        //    use their outer target main size; for other items, use their outer flex base size.
        float initialUsedSpace = totalMainAxisGap;
        foreach (FlexItem child in line.Items)
        {
            initialUsedSpace += child.Frozen
                ? child.OuterTargetSize.Main(dir)
                : child.FlexBasis + child.Margin.MainAxisSum(dir);
        }

        float initialFreeSpace = nodeInnerMain.MaybeSub(initialUsedSpace) ?? 0f;

        // 4. Loop
        while (true)
        {
            // a. Check for flexible items. If all the flex items on the line are frozen,
            //    free space has been distributed; exit this loop.
            //    The space used by the items and the sums of the unfrozen items' flex factors
            //    (both needed for step b) are computed in the same pass.
            bool allFrozen = true;
            float itemsSpace = 0f;
            float sumFlexGrow = 0f;
            float sumFlexShrink = 0f;
            foreach (FlexItem child in line.Items)
            {
                if (child.Frozen)
                {
                    itemsSpace += child.OuterTargetSize.Main(dir);
                }
                else
                {
                    allFrozen = false;
                    itemsSpace += child.FlexBasis + child.Margin.MainAxisSum(dir);
                    sumFlexGrow += child.FlexGrow;
                    sumFlexShrink += child.FlexShrink;
                }
            }

            if (allFrozen)
            {
                break;
            }

            // b. Calculate the remaining free space as for initial free space, above.
            //    If the sum of the unfrozen flex items' flex factors is less than one,
            //    multiply the initial free space by this sum. If the magnitude of this
            //    value is less than the magnitude of the remaining free space, use this
            //    as the remaining free space.
            float usedSpace = totalMainAxisGap + itemsSpace;
            float freeSpace;
            if (growing && sumFlexGrow < 1f)
            {
                freeSpace = ((initialFreeSpace * sumFlexGrow) - totalMainAxisGap).MaybeMin(nodeInnerMain.MaybeSub(usedSpace));
            }
            else if (shrinking && sumFlexShrink < 1f)
            {
                freeSpace = ((initialFreeSpace * sumFlexShrink) - totalMainAxisGap).MaybeMax(nodeInnerMain.MaybeSub(usedSpace));
            }
            else
            {
                freeSpace = nodeInnerMain.MaybeSub(usedSpace) ?? (usedFlexFactor - usedSpace);
            }

            // c. Distribute free space proportional to the flex factors.
            //    - If the remaining free space is zero
            //        Do Nothing
            //    - If using the flex grow factor
            //        Find the ratio of the item's flex grow factor to the sum of the
            //        flex grow factors of all unfrozen items on the line. Set the item's
            //        target main size to its flex base size plus a fraction of the remaining
            //        free space proportional to the ratio.
            //    - If using the flex shrink factor
            //        For every unfrozen item on the line, multiply its flex shrink factor by
            //        its inner flex base size, and note this as its scaled flex shrink factor.
            //        Find the ratio of the item's scaled flex shrink factor to the sum of the
            //        scaled flex shrink factors of all unfrozen items on the line. Set the item's
            //        target main size to its flex base size minus a fraction of the absolute value
            //        of the remaining free space proportional to the ratio. Note this may result
            //        in a negative inner main size; it will be corrected in the next step.
            //    - Otherwise
            //        Do Nothing
            if (IsNormal(freeSpace))
            {
                if (growing && sumFlexGrow > 0f)
                {
                    foreach (FlexItem child in line.Items)
                    {
                        if (!child.Frozen)
                        {
                            child.TargetSize.SetMain(dir, child.FlexBasis + (freeSpace * (child.FlexGrow / sumFlexGrow)));
                        }
                    }
                }
                else if (shrinking && sumFlexShrink > 0f)
                {
                    float sumScaledShrinkFactor = 0f;
                    foreach (FlexItem child in line.Items)
                    {
                        if (!child.Frozen)
                        {
                            sumScaledShrinkFactor += child.InnerFlexBasis * child.FlexShrink;
                        }
                    }

                    if (sumScaledShrinkFactor > 0f)
                    {
                        foreach (FlexItem child in line.Items)
                        {
                            if (!child.Frozen)
                            {
                                float scaledShrinkFactor = child.InnerFlexBasis * child.FlexShrink;
                                child.TargetSize.SetMain(
                                    dir,
                                    child.FlexBasis + (freeSpace * (scaledShrinkFactor / sumScaledShrinkFactor)));
                            }
                        }
                    }
                }
            }

            // d. Fix min/max violations. Clamp each non-frozen item's target main size by its
            //    used min and max main sizes and floor its content-box size at zero. If the
            //    item's target main size was made smaller by this, it's a max violation.
            //    If the item's target main size was made larger by this, it's a min violation.
            float totalViolation = 0f;
            foreach (FlexItem child in line.Items)
            {
                if (child.Frozen)
                {
                    continue;
                }

                float? resolvedMinMain = child.ResolvedMinimumMainSize;
                float? maxMain = child.MaxSize.Main(dir);
                float clamped = MathHelpers.Max(child.TargetSize.Main(dir).MaybeClamp(resolvedMinMain, maxMain), 0f);
                child.Violation = clamped - child.TargetSize.Main(dir);
                child.TargetSize.SetMain(dir, clamped);
                child.OuterTargetSize.SetMain(dir, child.TargetSize.Main(dir) + child.Margin.MainAxisSum(dir));

                totalViolation += child.Violation;
            }

            // e. Freeze over-flexed items. The total violation is the sum of the adjustments
            //    from the previous step sum(clamped size - unclamped size). If the total violation is:
            //    - Zero
            //        Freeze all items.
            //    - Positive
            //        Freeze all the items with min violations.
            //    - Negative
            //        Freeze all the items with max violations.
            foreach (FlexItem child in line.Items)
            {
                if (child.Frozen)
                {
                    continue;
                }

                if (totalViolation > 0f)
                {
                    child.Frozen = child.Violation > 0f;
                }
                else if (totalViolation < 0f)
                {
                    child.Frozen = child.Violation < 0f;
                }
                else
                {
                    child.Frozen = true;
                }
            }

            // f. Return to the start of this loop.
        }
    }

    /// <summary>Determine the hypothetical cross size of each item.</summary>
    private static void DetermineHypotheticalCrossSize(
        ILayoutFlexboxContainer tree,
        FlexLine line,
        AlgoConstants constants,
        Size<float?> nodeSize,
        Size<AvailableSpace> availableSpace)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;
        FlexDirection dir = constants.Dir;

        // If the flex container is single-line and has a definite cross size then that is the cross size of the
        // line, whatever the hypothetical cross sizes of the items are
        bool lineCrossSizeIsKnown = !constants.IsWrap && nodeSize.Cross(dir).HasValue;

        foreach (FlexItem child in line.Items)
        {
            float paddingBorderSum = child.Padding.Add(child.Border).CrossAxisSum(dir);

            // An item that will be stretched to a line whose cross size is already known is not measured: its
            // hypothetical cross size would be used neither for the line nor for the item
            // (see CalculateCrossSize and DetermineUsedCrossSize)
            if (lineCrossSizeIsKnown && child.IsStretched(dir) && !child.ParticipatesInBaselineAlignment(dir))
            {
                continue;
            }

            // The available space passed to the child excludes the child's margins
            AvailableSpace childAvailableMain = AvailableSpace.Definite(
                MathHelpers.Max(constants.ContainerSize.Main(dir) - child.Margin.MainAxisSum(dir), 0f));

            // Sizes transferred through the aspect ratio clamp the hypothetical cross size
            // https://github.com/w3c/csswg-drafts/issues/10997
            float? transferredMinCross = child.MinSize.MaybeApplyAspectRatio(child.AspectRatio).Cross(dir);
            float? transferredMaxCross = child.MaxSize.MaybeApplyAspectRatio(child.AspectRatio).Cross(dir);

            float? childCross = child.Size
                .Cross(dir)
                .MaybeClamp(transferredMinCross, transferredMaxCross)
                .MaybeMax(paddingBorderSum);
            AvailableSpace childAvailableCross = availableSpace
                .Cross(dir)
                .MapDefiniteValue(val => MathHelpers.Max(constants.DividedCrossSpace(val) - child.Margin.CrossAxisSum(dir), 0f))
                .MaybeClamp(transferredMinCross, transferredMaxCross)
                .MaybeMax(paddingBorderSum);

            // A cross size that is a sizing keyword (min-content, max-content, fit-content,
            // fit-content(...)) determines the available space constraint the item is measured under.
            // The `stretch` keyword is not resolved here: it stretches to the flex line, which is
            // handled in DetermineUsedCrossSize
            float? crossStretchSize = (constants.NodeInnerSize.Cross(dir) is float innerCross
                    ? constants.DividedCrossSpace(innerCross)
                    : (float?)null)
                .MaybeSub(child.Margin.CrossAxisSum(dir))
                .MaybeMax(0f);
            SizingKeywordResolution? resolution = SizingKeyword.ResolveSizingKeyword(
                child.SizeStyle.Cross(dir),
                crossStretchSize,
                constants.NodeInnerSize.Cross(dir),
                calc);
            if (resolution is { IsExact: false } measure)
            {
                childAvailableCross = measure.MeasureSpace;
            }

            float childInnerCross;
            if (childCross is float knownCross)
            {
                childInnerCross = knownCross;
            }
            else
            {
                float measured = tree.ComputeChildLayout(
                        child.Node,
                        new LayoutInput
                        {
                            RunMode = RunMode.ComputeSize,
                            SizingMode = SizingMode.ContentSize,
                            Axis = dir.CrossAxis().ToRequestedAxis(),
                            KnownDimensions = new Size<float?>(
                                constants.IsRow ? child.TargetSize.Width : childCross,
                                constants.IsRow ? childCross : child.TargetSize.Height),
                            KnownDimensionsAreDefinite = ItemKnownDimensionDefiniteness(constants, child),
                            ParentSize = constants.NodeInnerSize,
                            AvailableSpace = new Size<AvailableSpace>(
                                constants.IsRow ? childAvailableMain : childAvailableCross,
                                constants.IsRow ? childAvailableCross : childAvailableMain),
                            VerticalMarginsAreCollapsible = Line.False,
                        })
                    .Size
                    .GetAbs(dir.CrossAxis());
                childInnerCross = MathHelpers.Max(measured.MaybeClamp(transferredMinCross, transferredMaxCross), paddingBorderSum);
            }

            float childOuterCross = childInnerCross + child.Margin.CrossAxisSum(dir);

            child.HypotheticalInnerSize.SetCross(dir, childInnerCross);
            child.HypotheticalOuterSize.SetCross(dir, childOuterCross);
        }
    }

    /// <summary>Calculate the base lines of the children.</summary>
    private static void CalculateChildrenBaseLines(
        ILayoutFlexboxContainer tree,
        Size<float?> nodeSize,
        Size<AvailableSpace> availableSpace,
        List<FlexLine> flexLines,
        AlgoConstants constants)
    {
        // Only compute baselines for flex rows because we only support baseline alignment in the cross axis
        // where that axis is also the inline axis
        // TODO: this may need revisiting if/when we support vertical writing modes
        if (!constants.IsRow)
        {
            return;
        }

        foreach (FlexLine line in flexLines)
        {
            // If a flex line has one or zero items participating in baseline alignment then baseline alignment is a no-op so we skip
            int lineBaselineChildCount = 0;
            foreach (FlexItem child in line.Items)
            {
                if (child.ParticipatesInBaselineAlignment(constants.Dir))
                {
                    lineBaselineChildCount++;
                }
            }

            if (lineBaselineChildCount <= 1)
            {
                continue;
            }

            foreach (FlexItem child in line.Items)
            {
                // Only calculate baselines for children participating in baseline alignment
                if (!child.ParticipatesInBaselineAlignment(constants.Dir))
                {
                    continue;
                }

                LayoutOutput measuredSizeAndBaselines = tree.ComputeChildLayout(
                    child.Node,
                    new LayoutInput
                    {
                        RunMode = RunMode.PerformLayout,
                        SizingMode = SizingMode.ContentSize,
                        Axis = RequestedAxis.Both,
                        KnownDimensions = new Size<float?>(
                            constants.IsRow ? child.TargetSize.Width : child.HypotheticalInnerSize.Width,
                            constants.IsRow ? child.HypotheticalInnerSize.Height : child.TargetSize.Height),
                        KnownDimensionsAreDefinite = ItemKnownDimensionDefiniteness(constants, child),
                        ParentSize = constants.NodeInnerSize,
                        // The available space passed to the child excludes the child's margins
                        AvailableSpace = new Size<AvailableSpace>(
                                constants.IsRow
                                    ? AvailableSpace.Definite(constants.ContainerSize.Width)
                                    : availableSpace.Width.MaybeSet(nodeSize.Width),
                                constants.IsRow
                                    ? availableSpace.Height.MaybeSet(nodeSize.Height)
                                    : AvailableSpace.Definite(constants.ContainerSize.Height))
                            .MaybeSub(child.Margin.SumAxes())
                            .MaybeMax(Size.Zero),
                        VerticalMarginsAreCollapsible = Line.False,
                    });

                float height = measuredSizeAndBaselines.Size.Height;
                float baseline = CommonAlignment.ChildFirstBaseline(measuredSizeAndBaselines.Baselines.First, height, child.Overflow.Y)
                    ?? height;

                child.Baseline = baseline + child.Margin.Top;
            }
        }
    }

    /// <summary>Calculate the cross size of each flex line.</summary>
    private static void CalculateCrossSize(List<FlexLine> flexLines, Size<float?> nodeSize, AlgoConstants constants)
    {
        FlexDirection dir = constants.Dir;

        // If the flex container is single-line and has a definite cross size,
        // the cross size of the flex line is the flex container's inner cross size.
        if (!constants.IsWrap && nodeSize.Cross(dir) is float nodeCross)
        {
            float crossAxisPaddingBorder = constants.ContentBoxInset.CrossAxisSum(dir);
            float? crossMinSize = constants.MinSize.Cross(dir);
            float? crossMaxSize = constants.MaxSize.Cross(dir);
            flexLines[0].CrossSize = MathHelpers.Max(nodeCross.MaybeClamp(crossMinSize, crossMaxSize) - crossAxisPaddingBorder, 0f);
        }
        else
        {
            // Otherwise, for each flex line:
            //
            //    1. Collect all the flex items whose inline-axis is parallel to the main-axis, whose
            //       align-self is baseline, and whose cross-axis margins are both non-auto. Find the
            //       largest of the distances between each item's baseline and its hypothetical outer
            //       cross-start edge, and the largest of the distances between each item's baseline
            //       and its hypothetical outer cross-end edge, and sum these two values.

            //    2. Among all the items not collected by the previous step, find the largest
            //       outer hypothetical cross size.

            //    3. The used cross-size of the flex line is the largest of the numbers found in the
            //       previous two steps and zero.
            foreach (FlexLine line in flexLines)
            {
                float maxBaseline = 0f;
                foreach (FlexItem child in line.Items)
                {
                    maxBaseline = MathHelpers.Max(maxBaseline, child.Baseline);
                }

                float crossSize = 0f;
                foreach (FlexItem child in line.Items)
                {
                    float childCross = child.ParticipatesInBaselineAlignment(dir)
                        ? maxBaseline - child.Baseline + child.HypotheticalOuterSize.Cross(dir)
                        : child.HypotheticalOuterSize.Cross(dir);
                    crossSize = MathHelpers.Max(crossSize, childCross);
                }

                line.CrossSize = crossSize;
            }

            // If the flex container is single-line, then clamp the line's cross-size to be within the container's computed min and max cross sizes.
            // Note that if CSS 2.1's definition of min/max-width/height applied more generally, this behavior would fall out automatically.
            if (!constants.IsWrap)
            {
                float crossAxisPaddingBorder = constants.ContentBoxInset.CrossAxisSum(dir);
                float? crossMinSize = constants.MinSize.Cross(dir);
                float? crossMaxSize = constants.MaxSize.Cross(dir);
                flexLines[0].CrossSize = flexLines[0].CrossSize.MaybeClamp(
                    crossMinSize.MaybeSub(crossAxisPaddingBorder),
                    crossMaxSize.MaybeSub(crossAxisPaddingBorder));
            }
        }
    }

    /// <summary>Handle 'align-content: stretch'.</summary>
    private static void HandleAlignContentStretch(List<FlexLine> flexLines, Size<float?> nodeSize, AlgoConstants constants)
    {
        if (constants.AlignContent != AlignContent.Stretch)
        {
            return;
        }

        FlexDirection dir = constants.Dir;
        float crossAxisPaddingBorder = constants.ContentBoxInset.CrossAxisSum(dir);
        float? crossMinSize = constants.MinSize.Cross(dir);
        float? crossMaxSize = constants.MaxSize.Cross(dir);
        float containerMinInnerCross = (nodeSize.Cross(dir) ?? crossMinSize)
            .MaybeClamp(crossMinSize, crossMaxSize)
            .MaybeSub(crossAxisPaddingBorder)
            .MaybeMax(0f) ?? 0f;

        float totalCrossAxisGap = SumAxisGaps(constants.Gap.Cross(dir), flexLines.Count);
        float linesTotalCross = totalCrossAxisGap;
        foreach (FlexLine line in flexLines)
        {
            linesTotalCross += line.CrossSize;
        }

        if (linesTotalCross < containerMinInnerCross)
        {
            float remaining = containerMinInnerCross - linesTotalCross;
            float addition = remaining / flexLines.Count;
            foreach (FlexLine line in flexLines)
            {
                line.CrossSize += addition;
            }
        }
    }

    /// <summary>Determine the used cross size of each flex item.</summary>
    private static void DetermineUsedCrossSize(ILayoutFlexboxContainer tree, List<FlexLine> flexLines, AlgoConstants constants)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;
        FlexDirection dir = constants.Dir;

        foreach (FlexLine line in flexLines)
        {
            float lineCrossSize = line.CrossSize;

            foreach (FlexItem child in line.Items)
            {
                IFlexboxItemStyle childStyle = tree.GetFlexboxChildStyle(child.Node);

                // A cross size of `stretch` stretches to the flex line like align-self: stretch
                // (but regardless of the alignment style)
                float targetCross;
                if (child.IsStretched(dir))
                {
                    // For some reason this particular usage of max_width is an exception to the rule that max_width's transfer
                    // using the aspect_ratio (if set). Both Chrome and Firefox agree on this. And reading the spec, it seems like
                    // a reasonable interpretation. Although it seems to me that the spec *should* apply aspect_ratio here.
                    // Percentage padding and border resolve against the container's inline size
                    // (its width) on all four sides, not against its height for the top/bottom sides
                    Size<float> boxSizingAdjustment = child.BoxSizingAdjustment(childStyle);
                    Size<float?> maxSizeIgnoringAspectRatio = childStyle.MaxSize
                        .MaybeResolve(constants.NodeInnerSize, calc)
                        .MaybeAdd(boxSizingAdjustment);

                    targetCross = MathHelpers.Max(lineCrossSize - child.Margin.CrossAxisSum(dir), 0f)
                        .MaybeClamp(child.MinSize.Cross(dir), maxSizeIgnoringAspectRatio.Cross(dir));
                }
                else
                {
                    targetCross = child.HypotheticalInnerSize.Cross(dir);
                }

                child.TargetSize.SetCross(dir, targetCross);
                child.OuterTargetSize.SetCross(dir, child.TargetSize.Cross(dir) + child.Margin.CrossAxisSum(dir));
            }
        }
    }

    /// <summary>Distribute any remaining free space.</summary>
    private static void DistributeRemainingFreeSpace(List<FlexLine> flexLines, AlgoConstants constants)
    {
        FlexDirection dir = constants.Dir;

        foreach (FlexLine line in flexLines)
        {
            float totalMainAxisGap = SumAxisGaps(constants.Gap.Main(dir), line.Items.Count);
            float usedSpace = totalMainAxisGap;
            foreach (FlexItem child in line.Items)
            {
                usedSpace += child.OuterTargetSize.Main(dir);
            }

            float freeSpace = constants.InnerContainerSize.Main(dir) - usedSpace;
            int numAutoMargins = 0;

            foreach (FlexItem child in line.Items)
            {
                if (child.MarginIsAuto.MainStart(dir))
                {
                    numAutoMargins += 1;
                }

                if (child.MarginIsAuto.MainEnd(dir))
                {
                    numAutoMargins += 1;
                }
            }

            if (freeSpace > 0f && numAutoMargins > 0)
            {
                float margin = freeSpace / numAutoMargins;

                foreach (FlexItem child in line.Items)
                {
                    if (child.MarginIsAuto.MainStart(dir))
                    {
                        if (constants.IsRow)
                        {
                            child.Margin.Left = margin;
                        }
                        else
                        {
                            child.Margin.Top = margin;
                        }
                    }

                    if (child.MarginIsAuto.MainEnd(dir))
                    {
                        if (constants.IsRow)
                        {
                            child.Margin.Right = margin;
                        }
                        else
                        {
                            child.Margin.Bottom = margin;
                        }
                    }
                }

                // The auto margins have absorbed all of the free space, leaving none for `justify-content`
                freeSpace = 0f;
            }

            int numItems = line.Items.Count;
            bool layoutReverse = dir.IsReverse();
            float gap = constants.Gap.Main(dir);
            AlignContentKeyword justifyContentMode = CommonAlignment.ApplyAlignmentFallback(freeSpace, numItems, constants.JustifyContent);

            for (int i = 0; i < numItems; i++)
            {
                FlexItem child = layoutReverse ? line.Items[numItems - 1 - i] : line.Items[i];
                child.OffsetMain = CommonAlignment.ComputeAlignmentOffset(
                    freeSpace, numItems, gap, justifyContentMode, layoutReverse, i == 0);
            }
        }
    }

    /// <summary>Resolve cross-axis auto margins (and align items along the cross axis).</summary>
    private static void ResolveCrossAxisAutoMargins(List<FlexLine> flexLines, AlgoConstants constants)
    {
        FlexDirection dir = constants.Dir;

        foreach (FlexLine line in flexLines)
        {
            float lineCrossSize = line.CrossSize;
            float maxBaseline = 0f;
            float maxBaselineToBottomDistance = 0f;
            foreach (FlexItem child in line.Items)
            {
                maxBaseline = MathHelpers.Max(maxBaseline, child.Baseline);
                if (child.ParticipatesInBaselineAlignment(dir))
                {
                    maxBaselineToBottomDistance = MathHelpers.Max(
                        maxBaselineToBottomDistance,
                        child.OuterTargetSize.Cross(dir) - child.Baseline);
                }
            }

            foreach (FlexItem child in line.Items)
            {
                float freeSpace = lineCrossSize - child.OuterTargetSize.Cross(dir);

                if (child.MarginIsAuto.CrossStart(dir) && child.MarginIsAuto.CrossEnd(dir))
                {
                    if (constants.IsRow)
                    {
                        child.Margin.Top = freeSpace / 2f;
                        child.Margin.Bottom = freeSpace / 2f;
                    }
                    else
                    {
                        child.Margin.Left = freeSpace / 2f;
                        child.Margin.Right = freeSpace / 2f;
                    }
                }
                else if (child.MarginIsAuto.CrossStart(dir))
                {
                    if (constants.IsRow)
                    {
                        child.Margin.Top = freeSpace;
                    }
                    else
                    {
                        child.Margin.Left = freeSpace;
                    }
                }
                else if (child.MarginIsAuto.CrossEnd(dir))
                {
                    if (constants.IsRow)
                    {
                        child.Margin.Bottom = freeSpace;
                    }
                    else
                    {
                        child.Margin.Right = freeSpace;
                    }
                }
                else
                {
                    // 14. Align all flex items along the cross-axis.
                    child.OffsetCross = AlignFlexItemsAlongCrossAxis(child, freeSpace, maxBaseline, maxBaselineToBottomDistance, constants);
                }
            }
        }
    }

    /// <summary>Align all flex items along the cross-axis.</summary>
    private static float AlignFlexItemsAlongCrossAxis(
        FlexItem child,
        float freeSpace,
        float maxBaseline,
        float maxBaselineToBottomDistance,
        AlgoConstants constants)
    {
        bool crossAxisShouldReverse = constants.IsColumn && constants.LayoutDirection == Direction.Rtl;

        // If align-self uses a "safe" overflow-position keyword and the item would overflow its
        // line cross size, fall back to logical Start to avoid data loss. See CSS Box Alignment 3
        // section 4.3 <https://www.w3.org/TR/css-align-3/#overflow-values>.
        AlignItemsKeyword alignKeyword = child.AlignSelf.IsSafe() && freeSpace < 0f
            ? AlignItemsKeyword.Start
            : child.AlignSelf.Keyword;

        switch (alignKeyword)
        {
            case AlignItemsKeyword.Start:
                return crossAxisShouldReverse ? freeSpace : 0f;
            case AlignItemsKeyword.FlexStart:
                return constants.IsWrapReverse ^ crossAxisShouldReverse ? freeSpace : 0f;
            case AlignItemsKeyword.End:
                return crossAxisShouldReverse ? 0f : freeSpace;
            case AlignItemsKeyword.FlexEnd:
                return constants.IsWrapReverse ^ crossAxisShouldReverse ? 0f : freeSpace;
            case AlignItemsKeyword.Center:
                return freeSpace / 2f;
            case AlignItemsKeyword.Baseline:
                if (constants.IsRow)
                {
                    if (constants.IsWrapReverse)
                    {
                        // In a wrap-reverse container the cross axis is flipped, so the baseline-aligned
                        // group of items is aligned to the cross-start edge, which is the bottom of the line.
                        float lineCrossSize = freeSpace + child.OuterTargetSize.Cross(constants.Dir);
                        return lineCrossSize - maxBaselineToBottomDistance - child.Baseline;
                    }

                    return maxBaseline - child.Baseline;
                }
                else
                {
                    // Until we support vertical writing modes, baseline alignment only makes sense if
                    // the constants.direction is row, so we treat it as flex-start alignment in columns.
                    bool baselineColumnShouldReverse = crossAxisShouldReverse && !constants.IsWrap;
                    return constants.IsWrapReverse ^ baselineColumnShouldReverse ? freeSpace : 0f;
                }

            case AlignItemsKeyword.Stretch:
                return constants.IsWrapReverse ^ crossAxisShouldReverse ? freeSpace : 0f;
            default:
                // Normal is resolved to Stretch, and SelfStart/SelfEnd are resolved to Start/End
                // against the item's own direction when flex items are generated.
                throw new InvalidOperationException("align-self must be resolved before cross axis alignment");
        }
    }

    /// <summary>Determine the flex container's used cross size. Returns the total cross size of the lines.</summary>
    private static float DetermineContainerCrossSize(List<FlexLine> flexLines, Size<float?> nodeSize, AlgoConstants constants)
    {
        FlexDirection dir = constants.Dir;
        float totalCrossAxisGap = SumAxisGaps(constants.Gap.Cross(dir), flexLines.Count);
        float totalLineCrossSize = 0f;
        foreach (FlexLine line in flexLines)
        {
            totalLineCrossSize += line.CrossSize;
        }

        float paddingBorderSum = constants.ContentBoxInset.CrossAxisSum(dir);
        float crossScrollbarGutter = dir.IsRow() ? constants.ScrollbarGutter.Y : constants.ScrollbarGutter.X;
        float? minCrossSize = constants.MinSize.Cross(dir);
        float? maxCrossSize = constants.MaxSize.Cross(dir);
        float outerContainerSize = MathHelpers.Max(
            (nodeSize.Cross(dir) ?? (totalLineCrossSize + totalCrossAxisGap + paddingBorderSum)).MaybeClamp(minCrossSize, maxCrossSize),
            paddingBorderSum - crossScrollbarGutter);
        float innerContainerSize = MathHelpers.Max(outerContainerSize - paddingBorderSum, 0f);

        constants.ContainerSize.SetCross(dir, outerContainerSize);
        constants.InnerContainerSize.SetCross(dir, innerContainerSize);

        return totalLineCrossSize;
    }
}
