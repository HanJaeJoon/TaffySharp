namespace TaffySharp;

/// <summary>A cached known-dimensions computation, keyed by the grid area size it was computed for.</summary>
internal struct KnownDimensionsCacheEntry
{
    public Size<float?> GridAreaSize;
    public Size<float?> KnownDimensions;
}

/// <summary>Represents a single grid item.</summary>
internal sealed class GridItem
{
    /// <summary>The id of the node that this item represents.</summary>
    public NodeId Node;

    /// <summary>The order of the item in the children array. Used for sorting.</summary>
    public ushort SourceOrder;

    /// <summary>The item's definite row-start and row-end, as resolved by the placement algorithm (in origin-zero coordinates).</summary>
    public Line<OriginZeroLine> Row;

    /// <summary>The item's definite column-start and column-end, as resolved by the placement algorithm (in origin-zero coordinates).</summary>
    public Line<OriginZeroLine> Column;

    /// <summary>Is it a compressible replaced element? https://drafts.csswg.org/css-sizing-3/#min-content-zero</summary>
    public bool IsCompressibleReplaced;

    /// <summary>Is it a replaced element?</summary>
    public bool IsReplaced;

    /// <summary>The item's overflow style.</summary>
    public Point<Overflow> Overflow;

    /// <summary>The item's box_sizing style.</summary>
    public BoxSizing BoxSizing;

    /// <summary>The item's size style.</summary>
    public Size<Dimension> Size;

    /// <summary>The item's min_size style.</summary>
    public Size<LengthPercentageAuto> MinSize;

    /// <summary>The item's max_size style.</summary>
    public Size<LengthPercentageAuto> MaxSize;

    /// <summary>The item's aspect_ratio style.</summary>
    public float? AspectRatio;

    /// <summary>The item's padding style.</summary>
    public Rect<LengthPercentage> Padding;

    /// <summary>The item's border style.</summary>
    public Rect<LengthPercentage> Border;

    /// <summary>The item's margin style.</summary>
    public Rect<LengthPercentageAuto> Margin;

    /// <summary>The item's align_self property, or the parent's align_items property is not set.</summary>
    public AlignItems AlignSelf;

    /// <summary>The item's justify_self property, or the parent's justify_items property is not set.</summary>
    public AlignItems JustifySelf;

    /// <summary>The items first baseline (horizontal).</summary>
    public float? Baseline;

    /// <summary>Shim for baseline alignment that acts like an extra top margin.</summary>
    public float BaselineShim;

    /// <summary>The item's definite row-start and row-end (same as Row field, except in a different coordinate system) (as indexes into the row tracks array, including gutters).</summary>
    public Line<ushort> RowIndexes;

    /// <summary>The items definite column-start and column-end (same as Column field, except in a different coordinate system) (as indexes into the column tracks array, including gutters).</summary>
    public Line<ushort> ColumnIndexes;

    /// <summary>Whether the item crosses a flexible row.</summary>
    public bool CrossesFlexibleRow;

    /// <summary>Whether the item crosses a flexible column.</summary>
    public bool CrossesFlexibleColumn;

    /// <summary>Whether the item crosses an intrinsic row.</summary>
    public bool CrossesIntrinsicRow;

    /// <summary>Whether the item crosses an intrinsic column.</summary>
    public bool CrossesIntrinsicColumn;

    // Caches for intrinsic size computation. These caches are only valid for a single run of the track-sizing algorithm.

    /// <summary>Cache for the grid area size (in the axis being sized and the estimate in the other axis).</summary>
    public Size<float?>? GridAreaSizeCache;

    /// <summary>Cache for the known_dimensions input to intrinsic sizing computation.</summary>
    public KnownDimensionsCacheEntry? KnownDimensionsCache;

    /// <summary>Cache for the min-content size.</summary>
    public Size<float?> MinContentContributionCache;

    /// <summary>Cache for the minimum contribution.</summary>
    public Size<float?> MinimumContributionCache;

    /// <summary>Cache for the max-content size.</summary>
    public Size<float?> MaxContentContributionCache;

    /// <summary>Final y position. Used to compute baseline alignment for the container.</summary>
    public float YPosition;

    /// <summary>Final height. Used to compute baseline alignment for the container.</summary>
    public float Height;

    /// <summary>The out-of-flow candidates reported by the item's layout.</summary>
    public OofCandidates OofCandidates;

    private GridItem()
    {
    }

    /// <summary>Create a new item given a concrete style.</summary>
    public static GridItem NewWithStyleAndOrder(NodeId node, IGridItemStyle style, AlignItems parentAlignItems, AlignItems parentJustifyItems, ushort sourceOrder)
    {
        Line<OriginZeroLine> unplaced = new(new OriginZeroLine(0), new OriginZeroLine(0));
        return new GridItem
        {
            Node = node,
            SourceOrder = sourceOrder,
            Row = unplaced,
            Column = unplaced,
            IsCompressibleReplaced = style.IsCompressibleReplaced,
            IsReplaced = style.IsReplaced,
            Overflow = style.Overflow,
            BoxSizing = style.BoxSizing,
            Size = style.Size,
            MinSize = style.MinSize,
            MaxSize = style.MaxSize,
            AspectRatio = style.AspectRatio,
            Padding = style.Padding,
            Border = style.Border,
            Margin = style.Margin,
            AlignSelf = style.AlignSelf ?? parentAlignItems,
            JustifySelf = style.JustifySelf ?? parentJustifyItems,
            Baseline = null,
            BaselineShim = 0f,
            RowIndexes = new(0, 0), // Properly initialised later
            ColumnIndexes = new(0, 0), // Properly initialised later
            CrossesFlexibleRow = false, // Properly initialised later
            CrossesFlexibleColumn = false, // Properly initialised later
            CrossesIntrinsicRow = false, // Properly initialised later
            CrossesIntrinsicColumn = false, // Properly initialised later
            GridAreaSizeCache = null,
            MinContentContributionCache = TaffySharp.Size.None,
            MaxContentContributionCache = TaffySharp.Size.None,
            KnownDimensionsCache = null,
            MinimumContributionCache = TaffySharp.Size.None,
            YPosition = 0f,
            Height = 0f,
            OofCandidates = OofCandidates.None,
        };
    }

    /// <summary>This item has auto margins in the block axis.</summary>
    public bool HasAutoBlockMargin() => Margin.Top.IsAuto() || Margin.Bottom.IsAuto();

    /// <summary>This item's block size depends on the size of the tracks it spans (percentage height in intrinsic or flexible rows).</summary>
    public bool HasCyclicBlockSizeDependency() =>
        Size.Height.Raw.UsesPercentage() && (CrossesIntrinsicRow || CrossesFlexibleRow);

    /// <summary>This item participates in baseline alignment.</summary>
    public bool ParticipatesInBaselineAlignment() =>
        AlignSelf.Keyword == AlignItemsKeyword.Baseline && !HasAutoBlockMargin() && !HasCyclicBlockSizeDependency();

    /// <summary>Get the item's placement in the specified axis.</summary>
    public Line<OriginZeroLine> Placement(AbstractAxis axis) => axis == AbstractAxis.Block ? Row : Column;

    /// <summary>Get the item's placement in the specified axis (as indexes into the tracks array, including gutters).</summary>
    public Line<ushort> PlacementIndexes(AbstractAxis axis) => axis == AbstractAxis.Block ? RowIndexes : ColumnIndexes;

    /// <summary>Returns the (start-inclusive, end-exclusive) range of indexes into the tracks array of the tracks the item spans, excluding the lines (gutters) at either end.</summary>
    public (int Start, int End) TrackRangeExcludingLines(AbstractAxis axis)
    {
        Line<ushort> indexes = PlacementIndexes(axis);
        return (indexes.Start + 1, indexes.End);
    }

    /// <summary>Returns whether any of the tracks the item spans match the predicate.</summary>
    public bool SpansTrackMatching(AbstractAxis axis, IReadOnlyList<GridTrack> axisTracks, Func<GridTrack, bool> predicate)
    {
        (int start, int end) = TrackRangeExcludingLines(axis);
        for (int i = start; i < end; i++)
        {
            if (predicate(axisTracks[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the number of tracks that this item spans in the specified axis.</summary>
    public ushort Span(AbstractAxis axis) => axis == AbstractAxis.Block ? Row.Span() : Column.Span();

    /// <summary>Returns the pre-computed value indicating whether the grid item crosses a flexible track in the specified axis.</summary>
    public bool CrossesFlexibleTrack(AbstractAxis axis) => axis == AbstractAxis.Inline ? CrossesFlexibleColumn : CrossesFlexibleRow;

    /// <summary>Returns the pre-computed value indicating whether the grid item crosses an intrinsic track in the specified axis.</summary>
    public bool CrossesIntrinsicTrack(AbstractAxis axis) => axis == AbstractAxis.Inline ? CrossesIntrinsicColumn : CrossesIntrinsicRow;

    /// <summary>
    /// For an item spanning multiple tracks, the upper limit used to calculate its limited min-/max-content contribution is the
    /// sum of the fixed max track sizing functions of any tracks it spans, and is applied if it only spans such tracks.
    /// </summary>
    public float? SpannedTrackLimit(AbstractAxis axis, IReadOnlyList<GridTrack> axisTracks, float? axisParentSize, Func<object, float, float> resolveCalcValue)
    {
        (int start, int end) = TrackRangeExcludingLines(axis);
        float limit = 0f;
        for (int i = start; i < end; i++)
        {
            if (axisTracks[i].MaxTrackSizingFunction.DefiniteLimit(axisParentSize, resolveCalcValue) is not float value)
            {
                return null;
            }

            limit += value;
        }

        return limit;
    }

    /// <summary>
    /// Similar to the spanned_track_limit, but excludes FitContent arguments from the limit.
    /// Used to clamp the automatic minimum contributions of an item.
    /// </summary>
    public float? SpannedFixedTrackLimit(AbstractAxis axis, IReadOnlyList<GridTrack> axisTracks, float? axisParentSize, Func<object, float, float> resolveCalcValue)
    {
        (int start, int end) = TrackRangeExcludingLines(axis);
        float limit = 0f;
        for (int i = start; i < end; i++)
        {
            if (axisTracks[i].MaxTrackSizingFunction.DefiniteValue(axisParentSize, resolveCalcValue) is not float value)
            {
                return null;
            }

            limit += value;
        }

        return limit;
    }

    /// <summary>Compute the known_dimensions to be passed to the child sizing functions, reusing the cached value if the grid area size is unchanged.</summary>
    private Size<float?> KnownDimensionsCached(ILayoutPartialTree tree, Size<float?> gridAreaSize)
    {
        if (KnownDimensionsCache is KnownDimensionsCacheEntry entry && entry.GridAreaSize.Equals(gridAreaSize))
        {
            return entry.KnownDimensions;
        }

        Size<float?> knownDimensions = KnownDimensions(tree, gridAreaSize);
        KnownDimensionsCache = new KnownDimensionsCacheEntry { GridAreaSize = gridAreaSize, KnownDimensions = knownDimensions };
        return knownDimensions;
    }

    private bool IsStretchAligned(AlignItems alignment) => alignment.Keyword switch
    {
        AlignItemsKeyword.Stretch => true,
        AlignItemsKeyword.Normal => !IsReplaced,
        _ => false,
    };

    /// <summary>Compute the known_dimensions to be passed to the child sizing functions.</summary>
    private Size<float?> KnownDimensions(ILayoutPartialTree tree, Size<float?> gridAreaSize)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;
        Size<float> margins = MarginsAxisSumsWithBaselineShims(gridAreaSize.Width, tree);
        float? aspectRatio = AspectRatio;

        // CSS resolves percentage padding and border against the inline size of the containing
        // block. For a grid item under intrinsic measurement, that inline-size basis is the grid
        // area's width when it is definite.
        // Spec:
        // https://www.w3.org/TR/css-grid-1/#item-margins
        // https://www.w3.org/TR/CSS22/box.html#padding-properties
        Rect<float> padding = Padding.ResolveOrZero(gridAreaSize.Width, calc);
        Rect<float> border = Border.ResolveOrZero(gridAreaSize.Width, calc);
        Size<float> paddingBorderSize = padding.Add(border).SumAxes();
        Size<float> boxSizingAdjustment = BoxSizing == BoxSizing.ContentBox ? paddingBorderSize : TaffySharp.Size.Zero;

        Size<float?> inherentSize = Size
            .MaybeResolve(gridAreaSize, calc)
            .MaybeApplyAspectRatio(aspectRatio)
            .MaybeAdd(boxSizingAdjustment);
        Size<float?> minSize = MinSize
            .MaybeResolve(gridAreaSize, calc)
            .MaybeApplyAspectRatio(aspectRatio)
            .MaybeAdd(boxSizingAdjustment)
            // The size of the item is floored by its padding and border
            .Or(paddingBorderSize.AsOptions())
            .MaybeMax(paddingBorderSize);
        Size<float?> maxSize = MaxSize
            .MaybeResolve(gridAreaSize, calc)
            .MaybeApplyAspectRatio(aspectRatio)
            .MaybeAdd(boxSizingAdjustment);

        Size<float?> gridAreaMinusItemMarginsSize = gridAreaSize.MaybeSub(margins).MaybeMax(TaffySharp.Size.Zero);

        // If node is absolutely positioned and width is not set explicitly, then deduce it
        // from left, right and container_content_box if both are set.
        float? width = inherentSize.Width;
        if (width is null)
        {
            // A width that is a sizing keyword is not auto, so it does not stretch. The stretch
            // keyword resolves to an exact width; the others resolve during content measurement.
            if (Size.Width.IsSizingKeyword())
            {
                width = SizingKeyword.ResolveSizingKeyword(Size.Width, gridAreaMinusItemMarginsSize.Width, gridAreaSize.Width, calc) is { IsExact: true } resolution
                    ? resolution.ExactSize
                    : null;
            }
            // Apply width based on stretch alignment if:
            //  - Alignment style is "stretch" or "normal". Note that "normal" is treated as "stretch"
            //    here regardless of whether the item has a preferred size or aspect ratio in this axis,
            //    which differs from the rule used when the item is finally aligned (see `align_and_position_item`).
            //    Replaced elements are not stretched by "normal".
            //  - The node is not absolutely positioned
            //  - The node does not have auto margins in this axis.
            else if (!Margin.Left.IsAuto() && !Margin.Right.IsAuto() && IsStretchAligned(JustifySelf))
            {
                width = gridAreaMinusItemMarginsSize.Width;
            }
        }

        // Reapply aspect ratio after stretch and absolute position width adjustments
        Size<float?> size = new Size<float?>(width, inherentSize.Height).MaybeApplyAspectRatio(aspectRatio);

        float? height = size.Height;
        if (height is null)
        {
            // A height that is a sizing keyword is not auto, so it does not stretch. The stretch
            // keyword resolves to an exact height; the others resolve during content measurement.
            if (Size.Height.IsSizingKeyword())
            {
                height = SizingKeyword.ResolveSizingKeyword(Size.Height, gridAreaMinusItemMarginsSize.Height, gridAreaSize.Height, calc) is { IsExact: true } resolution
                    ? resolution.ExactSize
                    : null;
            }
            // Apply height based on stretch alignment if:
            //  - Alignment style is "stretch" or "normal" (see the note on width above)
            //  - The node is not absolutely positioned
            //  - The node does not have auto margins in this axis.
            else if (!Margin.Top.IsAuto() && !Margin.Bottom.IsAuto() && IsStretchAligned(AlignSelf))
            {
                height = gridAreaMinusItemMarginsSize.Height;
            }
        }

        // Reapply aspect ratio after stretch and absolute position height adjustments
        size = new Size<float?>(size.Width, height).MaybeApplyAspectRatio(aspectRatio);

        // Clamp size by min and max width/height
        return size.MaybeClamp(minSize, maxSize);
    }

    /// <summary>
    /// Compute the available_space to be passed to the child sizing functions.
    /// These are estimates based on either the max track sizing function or the provisional base size in the opposite
    /// axis to the one currently being sized.
    /// https://www.w3.org/TR/css-grid-1/#algo-overview
    /// </summary>
    public Size<float?> GridAreaSize(
        AbstractAxis axis,
        IReadOnlyList<GridTrack> axisTracks,
        IReadOnlyList<GridTrack> otherAxisTracks,
        Size<float?> availableSpace,
        Func<GridTrack, float?, float?> getTrackSizeEstimate,
        Func<object, float, float> resolveCalcValue)
    {
        Size<float?> size = TaffySharp.Size.None;

        float? axisSum = 0f;
        (int start, int end) = TrackRangeExcludingLines(axis);
        for (int i = start; i < end; i++)
        {
            GridTrack track = axisTracks[i];
            float? minSize = track.MinTrackSizingFunction.DefiniteValue(availableSpace.Get(axis), resolveCalcValue);
            float? maxSize = minSize is null ? null : track.MaxTrackSizingFunction.DefiniteValue(availableSpace.Get(axis), resolveCalcValue);
            if (minSize is null || maxSize is null || minSize.Value != maxSize.Value)
            {
                axisSum = null;
                break;
            }

            axisSum += track.BaseSize;
        }

        size.Set(axis, axisSum);

        AbstractAxis otherAxis = axis.Other();
        float? otherSum = 0f;
        (int otherStart, int otherEnd) = TrackRangeExcludingLines(otherAxis);
        for (int i = otherStart; i < otherEnd; i++)
        {
            GridTrack track = otherAxisTracks[i];
            if (getTrackSizeEstimate(track, availableSpace.Get(otherAxis)) is not float estimate)
            {
                otherSum = null;
                break;
            }

            otherSum += estimate + track.ContentAlignmentAdjustment;
        }

        size.Set(otherAxis, otherSum);
        return size;
    }

    /// <summary>Retrieve the available_space from the cache or compute them using the passed parameters.</summary>
    public Size<float?> GridAreaSizeCached(
        AbstractAxis axis,
        IReadOnlyList<GridTrack> axisTracks,
        IReadOnlyList<GridTrack> otherAxisTracks,
        Size<float?> availableSpace,
        Func<GridTrack, float?, float?> getTrackSizeEstimate,
        Func<object, float, float> resolveCalcValue)
    {
        if (GridAreaSizeCache is Size<float?> cached)
        {
            return cached;
        }

        Size<float?> gridAreaSize = GridAreaSize(axis, axisTracks, otherAxisTracks, availableSpace, getTrackSizeEstimate, resolveCalcValue);
        GridAreaSizeCache = gridAreaSize;
        return gridAreaSize;
    }

    /// <summary>Compute the item's resolved margins for size contributions. Horizontal percentage margins always resolve to zero if the container size is indefinite as otherwise this would introduce a cyclic dependency.</summary>
    public Size<float> MarginsAxisSumsWithBaselineShims(float? innerNodeWidth, ILayoutPartialTree tree)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;
        return new Rect<float>(
            Margin.Left.ResolveOrZero(0f, calc),
            Margin.Right.ResolveOrZero(0f, calc),
            Margin.Top.ResolveOrZero(innerNodeWidth, calc) + BaselineShim,
            Margin.Bottom.ResolveOrZero(innerNodeWidth, calc))
            .SumAxes();
    }

    /// <summary>Compute the item's min content contribution from the provided parameters.</summary>
    public float MinContentContribution(AbstractAxis axis, ILayoutPartialTree tree, Size<float?> gridAreaSize, Size<float?> availableSpace)
    {
        Size<float?> knownDimensions = KnownDimensionsCached(tree, gridAreaSize);

        // If the item's size in the axis being measured is already known then that size is its contribution,
        // and we can avoid calling into the child entirely.
        if (knownDimensions.Get(axis) is float size)
        {
            return size;
        }

        // The child sees the grid area as its containing block during intrinsic measurement, so
        // percentage box properties resolve against the grid area when that size is definite.
        // Spec:
        // https://www.w3.org/TR/css-grid-1/#grid-item-sizing
        // https://www.w3.org/TR/css-grid-1/#algo-overview
        return tree.MeasureChildSize(
            Node,
            knownDimensions,
            gridAreaSize,
            KeywordAdjustedAvailableSpace(
                gridAreaSize,
                ToAvailableSpace(AvailableSpaceMinusMargins(gridAreaSize, availableSpace, tree), AvailableSpace.MinContent),
                tree),
            SizingMode.InherentSize,
            axis.AsAbsNaive(),
            Line.False);
    }

    /// <summary>Retrieve the item's min content contribution from the cache or compute it using the provided parameters.</summary>
    public float MinContentContributionCached(AbstractAxis axis, ILayoutPartialTree tree, Size<float?> gridAreaSize, Size<float?> availableSpace)
    {
        if (MinContentContributionCache.Get(axis) is float cached)
        {
            return cached;
        }

        float size = MinContentContribution(axis, tree, gridAreaSize, availableSpace);
        MinContentContributionCache.Set(axis, size);
        return size;
    }

    /// <summary>Compute the item's max content contribution from the provided parameters.</summary>
    public float MaxContentContribution(AbstractAxis axis, ILayoutPartialTree tree, Size<float?> gridAreaSize, Size<float?> availableSpace)
    {
        Size<float?> knownDimensions = KnownDimensionsCached(tree, gridAreaSize);

        // If the item's size in the axis being measured is already known then that size is its contribution,
        // and we can avoid calling into the child entirely.
        if (knownDimensions.Get(axis) is float size)
        {
            return size;
        }

        // See the min-content path above. Max-content measurement uses the same containing-block
        // basis so percentage-dependent item geometry is measured from the grid area rather than
        // from the container.
        return tree.MeasureChildSize(
            Node,
            knownDimensions,
            gridAreaSize,
            KeywordAdjustedAvailableSpace(
                gridAreaSize,
                ToAvailableSpace(AvailableSpaceMinusMargins(gridAreaSize, availableSpace, tree), AvailableSpace.MaxContent),
                tree),
            SizingMode.InherentSize,
            axis.AsAbsNaive(),
            Line.False);
    }

    private static Size<AvailableSpace> ToAvailableSpace(Size<float?> size, AvailableSpace fallback) => new(
        size.Width is float width ? AvailableSpace.Definite(width) : fallback,
        size.Height is float height ? AvailableSpace.Definite(height) : fallback);

    /// <summary>Subtract the item's margins (and baseline shim) from the definite components of the available space.</summary>
    private Size<float?> AvailableSpaceMinusMargins(Size<float?> gridAreaSize, Size<float?> availableSpace, ILayoutPartialTree tree)
    {
        if (availableSpace.Width is null && availableSpace.Height is null)
        {
            return availableSpace;
        }

        Func<object, float, float> calc = tree.ResolveCalcValue;
        Rect<float> margin = Margin.ResolveOrZero(gridAreaSize.Width, calc);
        return new Size<float?>(
            availableSpace.Width is float width ? MathHelpers.Max(width - margin.HorizontalAxisSum(), 0f) : null,
            availableSpace.Height is float height ? MathHelpers.Max(height - margin.VerticalAxisSum() - BaselineShim, 0f) : null);
    }

    /// <summary>Replace the available space in each axis whose preferred size is a sizing keyword with the space that keyword measures in.</summary>
    private Size<AvailableSpace> KeywordAdjustedAvailableSpace(Size<float?> gridAreaSize, Size<AvailableSpace> availableSpace, ILayoutPartialTree tree)
    {
        if (!Size.Width.IsSizingKeyword() && !Size.Height.IsSizingKeyword())
        {
            return availableSpace;
        }

        Func<object, float, float> calc = tree.ResolveCalcValue;
        Size<float> margins = MarginsAxisSumsWithBaselineShims(gridAreaSize.Width, tree);
        Size<AvailableSpace> adjusted = availableSpace;
        foreach (AbstractAxis axis in new[] { AbstractAxis.Inline, AbstractAxis.Block })
        {
            Dimension sizeStyle = Size.Get(axis);
            if (!sizeStyle.IsSizingKeyword())
            {
                continue;
            }

            float? stretchSize = gridAreaSize.Get(axis).MaybeSub(margins.Get(axis)).MaybeMax(0f);
            if (SizingKeyword.ResolveSizingKeyword(sizeStyle, stretchSize, gridAreaSize.Get(axis), calc) is { IsExact: false } resolution)
            {
                adjusted.Set(axis, resolution.MeasureSpace);
            }
        }

        return adjusted;
    }

    /// <summary>Retrieve the item's max content contribution from the cache or compute it using the provided parameters.</summary>
    public float MaxContentContributionCached(AbstractAxis axis, ILayoutPartialTree tree, Size<float?> gridAreaSize, Size<float?> availableSpace)
    {
        if (MaxContentContributionCache.Get(axis) is float cached)
        {
            return cached;
        }

        float size = MaxContentContribution(axis, tree, gridAreaSize, availableSpace);
        MaxContentContributionCache.Set(axis, size);
        return size;
    }

    /// <summary>
    /// The minimum contribution of an item is the smallest outer size it can have.
    /// Specifically:
    ///   - If the item's computed preferred size behaves as auto or depends on the size of its containing block in the relevant axis:
    ///     Its minimum contribution is the outer size that would result from assuming the item's used minimum size as its preferred size;
    ///   - Else the item's minimum contribution is its min-content contribution.
    /// Because the minimum contribution often depends on the size of the item's content, it is considered a type of intrinsic size contribution.
    /// See: https://www.w3.org/TR/css-grid-1/#min-size-auto
    /// </summary>
    public float MinimumContribution(ILayoutPartialTree tree, AbstractAxis axis, IReadOnlyList<GridTrack> axisTracks, Size<float?> gridAreaSize, Size<float?> innerNodeSize)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;
        Rect<float> padding = Padding.ResolveOrZero(gridAreaSize.Width, calc);
        Rect<float> border = Border.ResolveOrZero(gridAreaSize.Width, calc);
        Size<float> paddingBorderSize = padding.Add(border).SumAxes();
        Size<float> boxSizingAdjustment = BoxSizing == BoxSizing.ContentBox ? paddingBorderSize : TaffySharp.Size.Zero;

        float? result = null;
        if (Size.MaybeResolve(gridAreaSize, calc).MaybeApplyAspectRatio(AspectRatio).MaybeAdd(boxSizingAdjustment).Get(axis) is float preferredSize)
        {
            // A definite preferred size is clamped by the minimum and maximum sizes. For a replaced element (but
            // not a form control), an indefinite percentage maximum size is resolved against zero. The spec also
            // applies this to form controls (https://drafts.csswg.org/css-sizing-3/#min-content-zero) but no
            // browser does (https://github.com/w3c/csswg-drafts/issues/6348).
            LengthPercentageAuto minSizeStyle = MinSize.Get(axis);
            LengthPercentageAuto maxSizeStyle = MaxSize.Get(axis);
            if (minSizeStyle.IsAuto() && maxSizeStyle.IsAuto())
            {
                result = preferredSize;
            }
            else
            {
                float? basis = gridAreaSize.Get(axis);
                float? maxSizeBasis = IsReplaced ? basis ?? 0f : basis;
                float adjustment = boxSizingAdjustment.Get(axis);
                float? minSize = minSizeStyle.MaybeResolve(basis, calc);
                float? maxSize = maxSizeStyle.MaybeResolve(maxSizeBasis, calc);
                result = preferredSize.MaybeClamp(minSize.MaybeAdd(adjustment), maxSize.MaybeAdd(adjustment));
            }
        }

        if (result is null)
        {
            // A preferred size that is a sizing keyword other than `stretch` does not behave as auto,
            // so the minimum contribution is the min-content contribution.
            Dimension size = Size.Get(axis);
            if (size.IsSizingKeyword() && !size.IsStretch())
            {
                result = MinContentContributionCached(axis, tree, gridAreaSize, gridAreaSize);
            }
        }

        result ??= MinSize
            .MaybeResolve(gridAreaSize.UnwrapOr(TaffySharp.Size.Zero), calc)
            .MaybeApplyAspectRatio(AspectRatio)
            .MaybeAdd(boxSizingAdjustment)
            .Get(axis);

        result ??= Overflow.Get(axis).MaybeIntoAutomaticMinSize();

        if (result is float resolved)
        {
            return resolved;
        }

        // Automatic minimum size. See https://www.w3.org/TR/css-grid-1/#min-size-auto

        // To provide a more reasonable default minimum size for grid items, the used value of its automatic minimum size
        // in a given axis is the content-based minimum size if all of the following are true:
        (int start, int end) = TrackRangeExcludingLines(axis);

        // it is not a scroll container (handled above)

        // it spans at least one track in that axis whose min track sizing function is auto
        bool spansAutoMinTrack = false;
        bool spansFrTrack = false;
        for (int i = start; i < end; i++)
        {
            spansAutoMinTrack |= axisTracks[i].MinTrackSizingFunction.BehavesAsAuto(innerNodeSize.Get(axis));
            spansFrTrack |= axisTracks[i].MaxTrackSizingFunction.IsFr();
        }

        // if it spans more than one track in that axis, none of those tracks are flexible
        bool onlySpanOneTrack = end - start == 1;
        bool useContentBasedMinimum = spansAutoMinTrack && (onlySpanOneTrack || !spansFrTrack);

        // Otherwise, the automatic minimum size is zero, as usual.
        if (!useContentBasedMinimum)
        {
            return 0f;
        }

        float minimumContribution = MinContentContributionCached(axis, tree, gridAreaSize, gridAreaSize);

        // If the item is a compressible replaced element, and has a definite preferred size or maximum size in the
        // relevant axis, the size suggestion is capped by those sizes; for this purpose, any indefinite percentages
        // in these sizes are resolved against zero (and considered definite).
        if (IsCompressibleReplaced)
        {
            float? size = Size.Get(axis).MaybeResolve(0f, calc);
            float? maxSize = MaxSize.Get(axis).MaybeResolve(0f, calc);
            minimumContribution = minimumContribution.MaybeMin(size).MaybeMin(maxSize);
        }

        // The content-based minimum size is additionally clamped by the sum of any fixed max track sizing
        // functions of the tracks the item spans. Note that this clamp does not apply to explicitly specified
        // preferred or minimum sizes, and that the argument to fit-content() does not clamp the content-based
        // minimum size in the same way as a fixed max track sizing function. The clamp is a stretch fit into
        // the limit, so it does not shrink the item below its padding + border.
        float? limit = SpannedFixedTrackLimit(axis, axisTracks, innerNodeSize.Get(axis), calc);
        return minimumContribution.MaybeMin(limit is float l ? MathHelpers.Max(l, paddingBorderSize.Get(axis)) : null);
    }

    /// <summary>Retrieve the item's minimum contribution from the cache or compute it using the provided parameters.</summary>
    public float MinimumContributionCached(ILayoutPartialTree tree, AbstractAxis axis, IReadOnlyList<GridTrack> axisTracks, Size<float?> gridAreaSize, Size<float?> innerNodeSize)
    {
        if (MinimumContributionCache.Get(axis) is float cached)
        {
            return cached;
        }

        float size = MinimumContribution(tree, axis, axisTracks, gridAreaSize, innerNodeSize);
        MinimumContributionCache.Set(axis, size);
        return size;
    }
}
