namespace TaffySharp;

/// <summary>Alignment of tracks and final positioning of items.</summary>
internal static class GridAlignment
{
    /// <summary>Align the grid tracks within the grid according to the align-content (rows) or justify-content (columns) property.</summary>
    public static void AlignTracks(
        float gridContainerContentBoxSize,
        Line<float> padding,
        Line<float> border,
        List<GridTrack> tracks,
        AlignContent trackAlignmentStyle,
        bool axisIsReversed)
    {
        float usedSize = 0f;
        foreach (GridTrack track in tracks)
        {
            usedSize += track.BaseSize;
        }

        float freeSpace = gridContainerContentBoxSize - usedSize;
        float origin = padding.Start + border.Start;

        // Count the number of non-collapsed tracks (not counting gutters)
        int numTracks = 0;
        for (int i = 1; i < tracks.Count; i += 2)
        {
            if (!tracks[i].IsCollapsed)
            {
                numTracks++;
            }
        }

        // Grid layout treats gaps as full tracks rather than applying them at alignment so we
        // simply pass zero here. Grid layout is never reversed.
        const float Gap = 0f;
        const bool LayoutIsReversed = false;
        AlignContentKeyword trackAlignment = CommonAlignment.ApplyAlignmentFallback(freeSpace, numTracks, trackAlignmentStyle);
        if (axisIsReversed)
        {
            trackAlignment = trackAlignment.Reversed();
        }

        // If every track is collapsed then no track receives the alignment offset below, but the
        // grid's lines should still be aligned within the container (e.g. at the inline-start edge
        // for RTL), so apply the offset to the origin instead.
        float emptyGridOffset = numTracks == 0
            ? CommonAlignment.ComputeAlignmentOffset(freeSpace, numTracks, Gap, trackAlignment, LayoutIsReversed, true)
            : 0f;

        // Compute offsets. Tracks are stored in logical order; when the axis is reversed (RTL)
        // physical offsets are assigned right-to-left by iterating the tracks in reverse.
        float totalOffset = origin + emptyGridOffset;
        bool seenNonCollapsedTrack = false;
        void PositionTrack(int i, GridTrack track)
        {
            // Odd tracks are gutters (but slices are zero-indexed, so odd tracks have even indices)
            bool isGutter = i % 2 == 0;
            bool isNonCollapsedTrack = !isGutter && !track.IsCollapsed;

            // Alignment offsets should be applied only to non-collapsed tracks.
            bool isFirst = isNonCollapsedTrack && !seenNonCollapsedTrack;
            float offset = isNonCollapsedTrack
                ? CommonAlignment.ComputeAlignmentOffset(freeSpace, numTracks, Gap, trackAlignment, LayoutIsReversed, isFirst)
                : 0f;

            track.Offset = totalOffset + offset;
            totalOffset = totalOffset + offset + track.BaseSize;
            if (isNonCollapsedTrack)
            {
                seenNonCollapsedTrack = true;
            }
        }

        if (axisIsReversed)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                PositionTrack(i, tracks[tracks.Count - 1 - i]);
            }
        }
        else
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                PositionTrack(i, tracks[i]);
            }
        }
    }

    /// <summary>
    /// Align and size a grid item into it's final position.
    /// Returns the item's scrollable overflow contribution, its y position (without any relative offset), its height and its baseline.
    /// </summary>
    public static (Rect<float> OverflowContribution, float YPosition, float Height, float? Baseline) AlignAndPositionItem(
        ILayoutGridContainer tree,
        NodeId node,
        uint order,
        Rect<float> gridArea,
        // Percentage basis for the item's styles. Grid passes the grid area size; grid lanes (css-grid-3) items
        // have the container's content box as their containing block in the stacking axis.
        Size<float> containingBlockSize,
        InBothAbsAxis<AlignItems> containerAlignmentStyles,
        float baselineShim,
        Direction direction,
        float containerBorderBoxWidth,
        Rect<float> containerBorder,
        bool containerIsScrollContainer,
        ref OofCandidates bubbledCandidates)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;
        IGridItemStyle style = tree.GetGridChildStyle(node);

        Point<Overflow> overflow = style.Overflow;
        Contain contain = style.Contain;
        float scrollbarWidth = style.ScrollbarWidth;
        float? aspectRatio = style.AspectRatio;
        bool isReplaced = style.IsReplaced;

        // Fall back to the container's justify-items/align-items if unset (`None`), and then resolve
        // writing-mode-relative self-start/self-end keywords against the item's own direction.
        // The horizontal axis is the inline axis (Taffy only supports horizontal-tb); the vertical
        // (block) axis resolves them to plain start/end.
        Direction itemDirection = style.Direction;
        AlignItems justifySelf = (style.JustifySelf ?? containerAlignmentStyles.Horizontal).ResolveSelfRelative(itemDirection, direction, true);
        AlignItems alignSelf = (style.AlignSelf ?? containerAlignmentStyles.Vertical).ResolveSelfRelative(itemDirection, direction, false);

        Position position = style.Position;
        Rect<LengthPercentageAuto> inset = style.Inset;
        Line<float?> insetHorizontal = inset.HorizontalComponents().Map(size => size.ResolveToOption(containingBlockSize.Width, calc));
        Line<float?> insetVertical = inset.VerticalComponents().Map(size => size.ResolveToOption(containingBlockSize.Height, calc));
        Rect<float> padding = style.Padding.Map(p => p.ResolveOrZero(containingBlockSize.Width, calc));
        Rect<float> border = style.Border.Map(p => p.ResolveOrZero(containingBlockSize.Width, calc));
        Size<float> paddingBorderSize = padding.Add(border).SumAxes();
        Size<float> boxSizingAdjustment = style.BoxSizing == BoxSizing.ContentBox ? paddingBorderSize : Size.Zero;

        Size<Dimension> sizeStyle = style.Size;
        Size<float?> inherentSize = sizeStyle
            .MaybeResolve(containingBlockSize, calc)
            .MaybeApplyAspectRatio(aspectRatio)
            .MaybeAdd(boxSizingAdjustment);
        Size<float?> minSize = style.MinSize
            .MaybeResolve(containingBlockSize, calc)
            .MaybeAdd(boxSizingAdjustment)
            .Or(paddingBorderSize.AsOptions())
            .MaybeMax(paddingBorderSize)
            .MaybeApplyAspectRatio(aspectRatio);
        Size<float?> maxSize = style.MaxSize
            .MaybeResolve(containingBlockSize, calc)
            .MaybeApplyAspectRatio(aspectRatio)
            .MaybeAdd(boxSizingAdjustment);

        // Resolve `normal` alignment (the default if alignment is set on neither the parent or the node itself)
        // Note: if the child has a preferred aspect ratio but neither width or height are set, then the width is stretched
        // and the then height is calculated from the width according the aspect ratio
        // Replaced elements are not stretched: they are sized as for `start`.
        // See: https://www.w3.org/TR/css-grid-1/#grid-item-sizing
        InBothAbsAxis<AlignItems> alignmentStyles = new(
            justifySelf.Keyword == AlignItemsKeyword.Normal
                ? (inherentSize.Width.HasValue || sizeStyle.Width.IsSizingKeyword() || isReplaced ? AlignItems.Start : AlignItems.Stretch)
                : justifySelf,
            alignSelf.Keyword == AlignItemsKeyword.Normal
                ? (inherentSize.Height.HasValue || sizeStyle.Height.IsSizingKeyword() || aspectRatio.HasValue || isReplaced
                    ? AlignItems.Start
                    : AlignItems.Stretch)
                : alignSelf);

        // Note: This is not a bug. It is part of the CSS spec that both horizontal and vertical margins
        // resolve against the WIDTH of the grid area.
        Rect<float?> margin = style.Margin.Map(m => m.ResolveToOption(containingBlockSize.Width, calc));

        Size<float> gridAreaSize = new(gridArea.Right - gridArea.Left, gridArea.Bottom - gridArea.Top);
        Size<float> gridAreaMinusItemMarginsSize = new(
            MathHelpers.Max(gridAreaSize.Width.MaybeSub(margin.Left).MaybeSub(margin.Right), 0f),
            MathHelpers.Max(gridAreaSize.Height.MaybeSub(margin.Top).MaybeSub(margin.Bottom) - baselineShim, 0f));

        // A size that is a sizing keyword (min-content, max-content, fit-content,
        // fit-content(...), stretch) either resolves to an exact size or is resolved
        // by measuring the item under the corresponding available space constraint
        SizingKeywordResolution? keywordWidth = !inherentSize.Width.HasValue
            ? SizingKeyword.ResolveSizingKeyword(sizeStyle.Width, gridAreaMinusItemMarginsSize.Width, containingBlockSize.Width, calc)
            : null;
        SizingKeywordResolution? keywordHeight = !inherentSize.Height.HasValue
            ? SizingKeyword.ResolveSizingKeyword(sizeStyle.Height, gridAreaMinusItemMarginsSize.Height, containingBlockSize.Height, calc)
            : null;

        // If both axes need to be measured then resolve them with a single measure call
        Size<float?> keywordMeasuredSize =
            keywordWidth is { IsExact: false } kw && keywordHeight is { IsExact: false } kh && !position.IsOutOfFlow()
                ? tree.MeasureChildSizeBoth(
                    node,
                    Size.None,
                    containingBlockSize.AsOptions(),
                    new Size<AvailableSpace>(kw.MeasureSpace, kh.MeasureSpace),
                    SizingMode.InherentSize,
                    Line.False).AsOptions()
                : Size.None;

        // If node is absolutely positioned and width is not set explicitly, then deduce it
        // from left, right and container_content_box if both are set.
        float? width = inherentSize.Width ?? ResolveWidth();
        float? ResolveWidth()
        {
            // Apply width derived from both the left and right properties of an absolutely
            // positioned element being set
            if (position.IsOutOfFlow() && insetHorizontal.Start is float left && insetHorizontal.End is float right)
            {
                return MathHelpers.Max(gridAreaMinusItemMarginsSize.Width - left - right, 0f);
            }

            if (keywordWidth is SizingKeywordResolution resolution)
            {
                if (resolution.IsExact)
                {
                    return resolution.ExactSize;
                }

                return keywordMeasuredSize.Width ?? tree.MeasureChildSize(
                    node,
                    Size.None,
                    containingBlockSize.AsOptions(),
                    new Size<AvailableSpace>(resolution.MeasureSpace, AvailableSpace.Definite(gridAreaMinusItemMarginsSize.Height)),
                    SizingMode.InherentSize,
                    AbsoluteAxis.Horizontal,
                    Line.False);
            }

            // Apply width based on stretch alignment if:
            //  - Alignment style is "stretch"
            //  - The node is not absolutely positioned
            //  - The node does not have auto margins in this axis.
            if (margin.Left.HasValue
                && margin.Right.HasValue
                && alignmentStyles.Horizontal == AlignItems.Stretch
                && !position.IsOutOfFlow())
            {
                return gridAreaMinusItemMarginsSize.Width;
            }

            return null;
        }

        // Reapply aspect ratio after stretch and absolute position width adjustments
        Size<float?> sized = new Size<float?>(width, inherentSize.Height).MaybeApplyAspectRatio(aspectRatio);
        width = sized.Width;

        float? height = sized.Height ?? ResolveHeight();
        float? ResolveHeight()
        {
            if (position.IsOutOfFlow() && insetVertical.Start is float top && insetVertical.End is float bottom)
            {
                return MathHelpers.Max(gridAreaMinusItemMarginsSize.Height - top - bottom, 0f);
            }

            if (keywordHeight is SizingKeywordResolution resolution)
            {
                if (resolution.IsExact)
                {
                    return resolution.ExactSize;
                }

                return keywordMeasuredSize.Height ?? tree.MeasureChildSize(
                    node,
                    new Size<float?>(width, null),
                    containingBlockSize.AsOptions(),
                    new Size<AvailableSpace>(
                        width is float w ? AvailableSpace.Definite(w) : AvailableSpace.Definite(gridAreaMinusItemMarginsSize.Width),
                        resolution.MeasureSpace),
                    SizingMode.InherentSize,
                    AbsoluteAxis.Vertical,
                    Line.False);
            }

            // Apply height based on stretch alignment if:
            //  - Alignment style is "stretch"
            //  - The node is not absolutely positioned
            //  - The node does not have auto margins in this axis.
            if (margin.Top.HasValue
                && margin.Bottom.HasValue
                && alignmentStyles.Vertical == AlignItems.Stretch
                && !position.IsOutOfFlow())
            {
                return gridAreaMinusItemMarginsSize.Height;
            }

            return null;
        }

        // Reapply aspect ratio after stretch and absolute position height adjustments, then
        // clamp size by min and max width/height
        Size<float?> clamped = new Size<float?>(width, height).MaybeApplyAspectRatio(aspectRatio).MaybeClamp(minSize, maxSize);

        // Layout node
        Size<float?> size = position.IsOutOfFlow() && (!clamped.Width.HasValue || !clamped.Height.HasValue)
            ? tree.MeasureChildSizeBoth(
                node,
                clamped,
                containingBlockSize.AsOptions(),
                gridAreaMinusItemMarginsSize.Map(AvailableSpace.Definite),
                SizingMode.InherentSize,
                Line.False).AsOptions()
            : clamped;

        LayoutOutput layoutOutput = tree.PerformChildLayout(
            node,
            size,
            containingBlockSize.AsOptions(),
            gridAreaMinusItemMarginsSize.Map(AvailableSpace.Definite),
            SizingMode.InherentSize,
            Line.False);

        // Resolve final size
        Size<float> finalSize = size.UnwrapOr(layoutOutput.Size).MaybeClamp(minSize, maxSize);
        float finalWidth = finalSize.Width;
        float finalHeight = finalSize.Height;

        (float x, Line<float> xMargin) = AlignItemWithinArea(
            new Line<float>(gridArea.Left, gridArea.Right),
            alignmentStyles.Horizontal,
            finalWidth,
            position,
            insetHorizontal,
            margin.HorizontalComponents(),
            0f,
            direction);
        (float yWithoutRelativeOffset, Line<float> yMargin) = AlignItemWithinArea(
            new Line<float>(gridArea.Top, gridArea.Bottom),
            alignmentStyles.Vertical,
            finalHeight,
            position,
            insetVertical,
            margin.VerticalComponents(),
            baselineShim,
            Direction.Ltr);

        // A relative offset moves the item, but does not affect the container's baseline (which is computed from the
        // returned y position, without the offset)
        x += RelativeOffset(position, insetHorizontal, direction);
        float y = yWithoutRelativeOffset + RelativeOffset(position, insetVertical, Direction.Ltr);

        Size<float> scrollbarSize = new(
            overflow.Y == Overflow.Scroll ? scrollbarWidth : 0f,
            overflow.X == Overflow.Scroll ? scrollbarWidth : 0f);

        Rect<float> resolvedMargin = new(xMargin.Start, xMargin.End, yMargin.Start, yMargin.End);

        // Bubble out-of-flow candidates from the item's subtree, translating anchors from
        // item-relative to container-relative coordinates
        OofCandidates itemCandidates = layoutOutput.OofCandidates.Take();
        if (!itemCandidates.IsEmpty)
        {
            itemCandidates.Translate(new Point<float>(x, y));
            bubbledCandidates.Append(ref itemCandidates);
        }

        tree.SetUnroundedLayout(node, new Layout
        {
            Order = order,
            Location = new Point<float>(x, y),
            Size = new Size<float>(finalWidth, finalHeight),
            ScrollableOverflowRect = layoutOutput.ScrollableOverflowRect,
            ScrollbarSize = scrollbarSize,
            Padding = padding,
            Border = border,
            Margin = resolvedMargin,
        });

        // Contributions to the container's scrollable overflow rect are measured from the
        // container's padding-box origin (mirrored for RTL), matching the scrollable overflow region.
        Point<float> contributionLocation = direction.IsRtl()
            ? new Point<float>(containerBorderBoxWidth - (x + finalWidth) - containerBorder.Right, y - containerBorder.Top)
            : new Point<float>(x - containerBorder.Left, y - containerBorder.Top);
        Rect<float> contribution = ScrollableOverflow.ComputeScrollableOverflowContribution(
            contributionLocation,
            new Size<float>(finalWidth, finalHeight),
            layoutOutput.ScrollableOverflowRect,
            overflow,
            contain,
            containerIsScrollContainer);

        float? baseline = CommonAlignment.ChildFirstBaseline(layoutOutput.Baselines.First, finalHeight, overflow.Y);
        return (contribution, yWithoutRelativeOffset, finalHeight, baseline);
    }

    /// <summary>Align and size a grid item along a single axis.</summary>
    public static (float Offset, Line<float> Margin) AlignItemWithinArea(
        Line<float> gridArea,
        AlignItems alignmentStyle,
        float resolvedSize,
        Position position,
        Line<float?> inset,
        Line<float?> margin,
        float baselineShim,
        Direction direction)
    {
        // Calculate grid area dimension in the axis
        Line<float> nonAutoMargin = new((margin.Start ?? 0f) + baselineShim, margin.End ?? 0f);
        float gridAreaSize = MathHelpers.Max(gridArea.End - gridArea.Start, 0f);
        float freeSpace = MathHelpers.Max(gridAreaSize - resolvedSize - nonAutoMargin.Sum(), 0f);

        // Expand auto margins to fill available space
        int autoMarginCount = (margin.Start.HasValue ? 0 : 1) + (margin.End.HasValue ? 0 : 1);
        float autoMarginSize = autoMarginCount > 0 ? freeSpace / autoMarginCount : 0f;
        Line<float> resolvedMargin = new((margin.Start ?? autoMarginSize) + baselineShim, margin.End ?? autoMarginSize);

        bool overflows = resolvedSize + nonAutoMargin.Sum() > gridAreaSize;
        AlignItemsKeyword alignmentKeyword = CommonAlignment.ResolveSelfAlignmentSafety(alignmentStyle, overflows);

        // Compute offset in the axis
        float alignmentBasedOffset = alignmentKeyword switch
        {
            // TODO: Add support for baseline alignment. For now we treat it as "start".
            AlignItemsKeyword.Start or AlignItemsKeyword.FlexStart or AlignItemsKeyword.Baseline or AlignItemsKeyword.Stretch =>
                direction.IsRtl() ? gridAreaSize - resolvedSize - resolvedMargin.End : resolvedMargin.Start,
            AlignItemsKeyword.End or AlignItemsKeyword.FlexEnd =>
                direction.IsRtl() ? resolvedMargin.Start : gridAreaSize - resolvedSize - resolvedMargin.End,
            AlignItemsKeyword.Center => (gridAreaSize - resolvedSize + resolvedMargin.Start - resolvedMargin.End) / 2f,
            // Normal is resolved, and SelfStart/SelfEnd are resolved to Start/End against
            // the item's own direction, in `align_and_position_item`.
            _ => throw new InvalidOperationException("unreachable: unresolved self alignment keyword"),
        };

        float offsetWithinArea;
        if (position.IsOutOfFlow())
        {
            if (inset.Start is float start && inset.End is float end)
            {
                offsetWithinArea = direction.IsRtl()
                    ? gridAreaSize - end - resolvedSize - nonAutoMargin.End
                    : start + nonAutoMargin.Start;
            }
            else if (inset.Start is float startOnly)
            {
                offsetWithinArea = startOnly + nonAutoMargin.Start;
            }
            else if (inset.End is float endOnly)
            {
                offsetWithinArea = gridAreaSize - endOnly - resolvedSize - nonAutoMargin.End;
            }
            else
            {
                offsetWithinArea = alignmentBasedOffset;
            }
        }
        else
        {
            offsetWithinArea = alignmentBasedOffset;
        }

        return (gridArea.Start + offsetWithinArea, resolvedMargin);
    }

    /// <summary>The offset applied to a relatively positioned item in one axis.</summary>
    private static float RelativeOffset(Position position, Line<float?> inset, Direction direction)
    {
        if (position != Position.Relative)
        {
            return 0f;
        }

        float? relativeInset = direction.IsRtl()
            ? (inset.End.HasValue ? -inset.End.Value : inset.Start)
            : (inset.Start ?? (inset.End.HasValue ? -inset.End.Value : null));
        return relativeInset ?? 0f;
    }
}
