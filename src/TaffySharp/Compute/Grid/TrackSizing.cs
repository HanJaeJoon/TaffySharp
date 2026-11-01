namespace TaffySharp;

/// <summary>
/// Takes an axis, and a list of grid items sorted firstly by whether they cross a flex track
/// in the specified axis (items that cross a flex track last), and then by the number of tracks they cross in
/// the specified axis (ascending order). Yields batches of items with the same span that do not cross a flex
/// track, followed by a single batch of all items that do cross a flex track.
/// </summary>
internal sealed class ItemBatcher
{
    private readonly AbstractAxis _axis;
    private int _indexOffset;
    private ushort _currentSpan;
    private bool _currentIsFlex;

    public ItemBatcher(AbstractAxis axis)
    {
        _axis = axis;
        _indexOffset = 0;
        _currentSpan = 1;
        _currentIsFlex = false;
    }

    /// <summary>Returns the next batch (and whether it is the flex batch), or <c>null</c> if there are no more batches.</summary>
    public (List<GridItem> Batch, bool IsFlex)? Next(List<GridItem> items)
    {
        if (_currentIsFlex || _indexOffset >= items.Count)
        {
            return null;
        }

        GridItem item = items[_indexOffset];
        _currentSpan = item.Span(_axis);
        _currentIsFlex = item.CrossesFlexibleTrack(_axis);

        int nextIndexOffset;
        if (_currentIsFlex)
        {
            nextIndexOffset = items.Count;
        }
        else
        {
            nextIndexOffset = items.FindIndex(i => i.CrossesFlexibleTrack(_axis) || i.Span(_axis) > _currentSpan);
            if (nextIndexOffset < 0)
            {
                nextIndexOffset = items.Count;
            }
        }

        List<GridItem> batch = items.GetRange(_indexOffset, nextIndexOffset - _indexOffset);
        _indexOffset = nextIndexOffset;
        return (batch, _currentIsFlex);
    }
}

/// <summary>
/// This struct captures a bunch of variables which are used to compute the intrinsic sizes of children so that those variables
/// don't have to be passed around all over the place below. It then has methods that implement the intrinsic sizing computations.
/// </summary>
internal sealed class IntrinsicSizeMeasurer
{
    private readonly ILayoutPartialTree _tree;
    private readonly List<GridTrack> _otherAxisTracks;
    private readonly Func<GridTrack, float?, ILayoutPartialTree, float?> _getTrackSizeEstimate;
    private readonly AbstractAxis _axis;
    private readonly Size<float?> _innerNodeSize;
    private readonly Func<object, float, float> _calc;

    public IntrinsicSizeMeasurer(
        ILayoutPartialTree tree,
        List<GridTrack> otherAxisTracks,
        AbstractAxis axis,
        Size<float?> innerNodeSize,
        Func<GridTrack, float?, ILayoutPartialTree, float?> getTrackSizeEstimate)
    {
        _tree = tree;
        _otherAxisTracks = otherAxisTracks;
        _axis = axis;
        _innerNodeSize = innerNodeSize;
        _getTrackSizeEstimate = getTrackSizeEstimate;
        _calc = tree.ResolveCalcValue;
    }

    /// <summary>Compute the available_space to be passed to the child sizing functions.</summary>
    private Size<float?> GridAreaSize(GridItem item, List<GridTrack> axisTracks) =>
        item.GridAreaSizeCached(
            _axis,
            axisTracks,
            _otherAxisTracks,
            _innerNodeSize,
            (track, basis) => _getTrackSizeEstimate(track, basis, _tree),
            _calc);

    /// <summary>Compute the item's resolved margins for size contributions. Horizontal percentage margins always resolve
    /// to zero if the container size is indefinite as otherwise this would introduce a cyclic dependency.</summary>
    private Size<float> MarginsAxisSumsWithBaselineShims(GridItem item, float? percentageBasis) =>
        item.MarginsAxisSumsWithBaselineShims(percentageBasis, _tree);

    public float Calc(object val, float basis) => _tree.ResolveCalcValue(val, basis);

    /// <summary>Retrieve the item's min content contribution from the cache or compute it using the provided parameters.</summary>
    public float MinContentContribution(GridItem item, List<GridTrack> axisTracks)
    {
        Size<float?> gridAreaSize = GridAreaSize(item, axisTracks);
        Size<float?> availableSpace = gridAreaSize.With(_axis, null);
        Size<float> marginAxisSums = MarginsAxisSumsWithBaselineShims(item, availableSpace.Width);
        float contribution = item.MinContentContributionCached(_axis, _tree, gridAreaSize, availableSpace);
        return contribution + marginAxisSums.Get(_axis);
    }

    /// <summary>Retrieve the item's max content contribution from the cache or compute it using the provided parameters.</summary>
    public float MaxContentContribution(GridItem item, List<GridTrack> axisTracks)
    {
        Size<float?> gridAreaSize = GridAreaSize(item, axisTracks);
        Size<float?> availableSpace = gridAreaSize.With(_axis, null);
        Size<float> marginAxisSums = MarginsAxisSumsWithBaselineShims(item, availableSpace.Width);
        float contribution = item.MaxContentContributionCached(_axis, _tree, gridAreaSize, availableSpace);
        return contribution + marginAxisSums.Get(_axis);
    }

    /// <summary>
    /// The minimum contribution of an item is the smallest outer size it can have.
    /// Specifically:
    ///   - If the item's computed preferred size behaves as auto or depends on the size of its containing block in the relevant axis:
    ///     Its minimum contribution is the outer size that would result from assuming the item's used minimum size as its preferred size;
    ///   - Else the item's minimum contribution is its min-content contribution.
    /// Because the minimum contribution often depends on the size of the item's content, it is considered a type of intrinsic size contribution.
    /// </summary>
    public float MinimumContribution(GridItem item, List<GridTrack> axisTracks)
    {
        Size<float?> gridAreaSize = GridAreaSize(item, axisTracks);
        Size<float?> availableSpace = gridAreaSize.With(_axis, null);
        Size<float> marginAxisSums = MarginsAxisSumsWithBaselineShims(item, availableSpace.Width);
        float contribution = item.MinimumContributionCached(_tree, _axis, axisTracks, gridAreaSize, _innerNodeSize);
        return contribution + marginAxisSums.Get(_axis);
    }
}

/// <summary>Whether it is a minimum or maximum size's space being distributed.</summary>
internal enum IntrinsicContributionType
{
    /// <summary>Minimum size's space being distributed.</summary>
    Minimum,

    /// <summary>Maximum size's space being distributed.</summary>
    Maximum,
}

/// <summary>Implements the track sizing algorithm (https://www.w3.org/TR/css-grid-1/#layout-algorithm).</summary>
internal static class TrackSizing
{
    /// <summary>The tracks (excluding the bounding lines) spanned by an item, as a view sharing the track objects.</summary>
    private static List<GridTrack> SpannedTracks(List<GridTrack> tracks, GridItem item, AbstractAxis axis)
    {
        (int start, int end) = item.TrackRangeExcludingLines(axis);
        return tracks.GetRange(start, end - start);
    }

    /// <summary>Rust <c>f32::total_cmp</c>.</summary>
    private static int TotalCmp(float a, float b)
    {
        int left = (int)MathHelpers.ToBits(a);
        int right = (int)MathHelpers.ToBits(b);
        left ^= (int)((uint)(left >> 31) >> 1);
        right ^= (int)((uint)(right >> 31) >> 1);
        return left.CompareTo(right);
    }

    /// <summary>
    /// To make track sizing efficient we want to order tracks.
    /// Here a placement is either a Line representing a row-start/row-end or a column-start/column-end.
    /// </summary>
    public static float ComputeAlignmentGutterAdjustment(
        AlignContent alignment,
        float? axisInnerNodeSize,
        Func<GridTrack, float?, float?> getTrackSizeEstimate,
        List<GridTrack> tracks)
    {
        if (tracks.Count <= 1)
        {
            return 0f;
        }

        // As items never cross the outermost gutters in a grid, we can simplify our calculations by
        // treating Start and End the same. The safety modifier doesn't influence gutter weight;
        // overflow fallback is handled when offsets are computed.
        int outerGutterWeight = alignment.Keyword switch
        {
            AlignContentKeyword.Start or AlignContentKeyword.FlexStart or AlignContentKeyword.End
                or AlignContentKeyword.FlexEnd or AlignContentKeyword.Center => 1,
            // Normal is resolved to Stretch in `compute_grid_layout`
            AlignContentKeyword.Normal or AlignContentKeyword.Stretch => 0,
            AlignContentKeyword.SpaceBetween => 0,
            AlignContentKeyword.SpaceAround => 1,
            AlignContentKeyword.SpaceEvenly => 1,
            _ => 0,
        };

        int innerGutterWeight = alignment.Keyword switch
        {
            AlignContentKeyword.FlexStart or AlignContentKeyword.Start or AlignContentKeyword.FlexEnd
                or AlignContentKeyword.End or AlignContentKeyword.Center or AlignContentKeyword.Normal
                or AlignContentKeyword.Stretch => 0,
            AlignContentKeyword.SpaceBetween => 1,
            AlignContentKeyword.SpaceAround => 2,
            AlignContentKeyword.SpaceEvenly => 1,
            _ => 0,
        };

        if (innerGutterWeight == 0)
        {
            return 0f;
        }

        if (axisInnerNodeSize is float innerSize)
        {
            float? trackSizeSum = 0f;
            foreach (GridTrack track in tracks)
            {
                if (getTrackSizeEstimate(track, innerSize) is float estimate)
                {
                    trackSizeSum += estimate;
                }
                else
                {
                    trackSizeSum = null;
                    break;
                }
            }

            float freeSpace = trackSizeSum is float sum ? MathHelpers.Max(0f, innerSize - sum) : 0f;
            int weightedTrackCount = ((tracks.Count - 3) / 2 * innerGutterWeight) + (2 * outerGutterWeight);
            return freeSpace / weightedTrackCount * innerGutterWeight;
        }

        return 0f;
    }

    /// <summary>Convert origin-zero coordinates track placement in grid track vector indexes.</summary>
    public static void ResolveItemTrackIndexes(List<GridItem> items, TrackCounts columnCounts, TrackCounts rowCounts)
    {
        foreach (GridItem item in items)
        {
            item.ColumnIndexes = item.Column.Map(line => (ushort)line.IntoTrackVecIndex(columnCounts));
            item.RowIndexes = item.Row.Map(line => (ushort)line.IntoTrackVecIndex(rowCounts));
        }
    }

    /// <summary>Determine (in each axis) whether the item crosses any flexible tracks.</summary>
    public static void DetermineIfItemCrossesFlexibleOrIntrinsicTracks(List<GridItem> items, List<GridTrack> columns, List<GridTrack> rows)
    {
        foreach (GridItem item in items)
        {
            (int colStart, int colEnd) = item.TrackRangeExcludingLines(AbstractAxis.Inline);
            (int rowStart, int rowEnd) = item.TrackRangeExcludingLines(AbstractAxis.Block);
            item.CrossesFlexibleColumn = AnyInRange(columns, colStart, colEnd, track => track.IsFlexible());
            item.CrossesIntrinsicColumn = AnyInRange(columns, colStart, colEnd, track => track.HasIntrinsicSizingFunction());
            item.CrossesFlexibleRow = AnyInRange(rows, rowStart, rowEnd, track => track.IsFlexible());
            item.CrossesIntrinsicRow = AnyInRange(rows, rowStart, rowEnd, track => track.HasIntrinsicSizingFunction());
        }
    }

    private static bool AnyInRange(List<GridTrack> tracks, int start, int end, Func<GridTrack, bool> predicate)
    {
        for (int i = start; i < end; i++)
        {
            if (predicate(tracks[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Track sizing algorithm. Note: Gutters are treated as empty fixed-size tracks for the purpose of the track sizing algorithm.</summary>
    public static void TrackSizingAlgorithm(
        ILayoutPartialTree tree,
        AbstractAxis axis,
        float? axisMinSize,
        float? axisMaxSize,
        AlignContent axisAlignment,
        AlignContent otherAxisAlignment,
        Size<AvailableSpace> availableGridSpace,
        Size<float?> innerNodeSize,
        List<GridTrack> axisTracks,
        List<GridTrack> otherAxisTracks,
        List<GridItem> items,
        Func<GridTrack, float?, ILayoutPartialTree, float?> getTrackSizeEstimate,
        bool hasBaselineAlignedItem)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;

        // 11.4 Initialise Track sizes
        // Initialize each track's base size and growth limit.
        float? percentageBasis = innerNodeSize.Get(axis) ?? axisMinSize;
        InitializeTrackSizes(tree, axisTracks, percentageBasis);

        // Resolve fit-content() limits against the container's inner size (these are fixed for the rest of this pass)
        float? axisInnerNodeSize = innerNodeSize.Get(axis);
        foreach (GridTrack track in axisTracks)
        {
            track.ResolveFitContentLimit(axisInnerNodeSize, calc);
        }

        // 11.5.1 Shim item baselines
        if (hasBaselineAlignedItem)
        {
            ResolveItemBaselines(tree, axis, items, innerNodeSize);
        }

        // If all tracks have a fixed min track sizing function and base_size = growth_limit,
        // then the track sizes are already final and we can skip the rest of this function.
        // Note that tracks with an intrinsic min track sizing function can still grow beyond
        // a fixed growth limit (e.g. minmax(auto, 0px)), so they cannot be skipped.
        if (axisTracks.All(track =>
            track.BaseSize == track.GrowthLimit && track.MinTrackSizingFunction.DefiniteValue(percentageBasis, calc).HasValue))
        {
            return;
        }

        // Pre-computations for 11.5 Resolve Intrinsic Track Sizes

        // Compute an additional amount to add to each spanned gutter when computing item's estimated size in the
        // in the opposite axis based on the alignment, container size, and estimated track sizes in that axis
        float gutterAlignmentAdjustment = ComputeAlignmentGutterAdjustment(
            otherAxisAlignment,
            innerNodeSize.Get(axis.Other()),
            (track, basis) => getTrackSizeEstimate(track, basis, tree),
            otherAxisTracks);
        if (otherAxisTracks.Count > 3)
        {
            for (int i = 2; i < otherAxisTracks.Count; i += 2)
            {
                otherAxisTracks[i].ContentAlignmentAdjustment = gutterAlignmentAdjustment;
            }
        }

        // 11.5 Resolve Intrinsic Track Sizes
        ResolveIntrinsicTrackSizes(
            tree,
            axis,
            axisTracks,
            otherAxisTracks,
            items,
            availableGridSpace.Get(axis),
            innerNodeSize,
            getTrackSizeEstimate);

        // 11.6. Maximise Tracks
        // Distributes free space (if any) to tracks with FINITE growth limits, up to their limits.
        MaximiseTracks(axisTracks, availableGridSpace.Get(axis));

        // For the purpose of the final two expansion steps ("Expand Flexible Tracks" and "Stretch auto Tracks"), we only want to expand
        // into space generated by the grid container's size (as defined by either it's preferred size style or by it's parent node through
        // something like stretch alignment), not just any available space. To do this we map definite available space to AvailableSpace::MaxContent
        // in the case that inner_node_size is None. A min-content constraint in the block axis is also mapped to AvailableSpace::MaxContent,
        // as the min-content block size of a grid container is its max-content block size.
        AvailableSpace axisAvailableSpaceForExpansion;
        if (innerNodeSize.Get(axis) is float availableSpace)
        {
            axisAvailableSpaceForExpansion = AvailableSpace.Definite(availableSpace);
        }
        else
        {
            axisAvailableSpaceForExpansion = availableGridSpace.Get(axis).IsMinContent && axis == AbstractAxis.Inline
                ? AvailableSpace.MinContent
                : AvailableSpace.MaxContent;
        }

        // 11.7. Expand Flexible Tracks
        // This step sizes flexible tracks using the largest value it can assign to an fr without exceeding the available space.
        ExpandFlexibleTracks(
            tree,
            axis,
            axisTracks,
            otherAxisTracks,
            items,
            axisMinSize,
            axisMaxSize,
            axisAvailableSpaceForExpansion,
            innerNodeSize,
            getTrackSizeEstimate);

        // 11.8. Stretch auto Tracks
        // This step expands tracks that have an auto max track sizing function by dividing any remaining positive, definite free space equally amongst them.
        if (axisAlignment == AlignContent.Stretch)
        {
            StretchAutoTracks(axisTracks, axisMinSize, axisAvailableSpaceForExpansion);
        }
    }

    /// <summary>Add any planned base size increases to the base size after a round of distributing space to base sizes. Reset the planned base size increase to zero ready for the next round.</summary>
    private static void FlushPlannedBaseSizeIncreases(List<GridTrack> tracks)
    {
        foreach (GridTrack track in tracks)
        {
            track.BaseSize += track.BaseSizePlannedIncrease;
            track.BaseSizePlannedIncrease = 0f;
        }
    }

    /// <summary>Add any planned growth limit increases to the growth limit after a round of distributing space to growth limits. Reset the planned growth limit increase to zero ready for the next round.</summary>
    private static void FlushPlannedGrowthLimitIncreases(List<GridTrack> tracks, bool setInfinitelyGrowable)
    {
        foreach (GridTrack track in tracks)
        {
            if (track.GrowthLimitPlannedIncrease > 0f)
            {
                track.GrowthLimit = track.GrowthLimit == float.PositiveInfinity
                    ? track.BaseSize + track.GrowthLimitPlannedIncrease
                    : track.GrowthLimit + track.GrowthLimitPlannedIncrease;
                track.InfinitelyGrowable = setInfinitelyGrowable;
            }
            else
            {
                track.InfinitelyGrowable = false;
            }

            track.GrowthLimitPlannedIncrease = 0f;
        }
    }

    /// <summary>11.4 Initialise Track sizes. Initialize each track's base size and growth limit.</summary>
    private static void InitializeTrackSizes(ILayoutPartialTree tree, List<GridTrack> axisTracks, float? axisInnerNodeSize)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;
        foreach (GridTrack track in axisTracks)
        {
            // For each track, if the track's min track sizing function is:
            // - A fixed sizing function
            //     Resolve to an absolute length and use that size as the track's initial base size.
            //     Note: Indefinite lengths cannot occur, as they're treated as auto.
            // - An intrinsic sizing function
            //     Use an initial base size of zero.
            track.BaseSize = track.MinTrackSizingFunction.DefiniteValue(axisInnerNodeSize, calc) ?? 0f;

            // For each track, if the track's max track sizing function is:
            // - A fixed sizing function
            //     Resolve to an absolute length and use that size as the track's initial growth limit.
            // - An intrinsic sizing function
            //     Use an initial growth limit of infinity.
            // - A flexible sizing function
            //     Use an initial growth limit of infinity.
            track.GrowthLimit = track.MaxTrackSizingFunction.DefiniteValue(axisInnerNodeSize, calc) ?? float.PositiveInfinity;

            // In all cases, if the growth limit is less than the base size, increase the growth limit to match the base size.
            if (track.GrowthLimit < track.BaseSize)
            {
                track.GrowthLimit = track.BaseSize;
            }
        }
    }

    /// <summary>11.5.1 Shim baseline-aligned items so their intrinsic size contributions reflect their baseline alignment.</summary>
    private static void ResolveItemBaselines(ILayoutPartialTree tree, AbstractAxis axis, List<GridItem> items, Size<float?> innerNodeSize)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;

        // Sort items by track in the other axis (row) start position so that we can iterate items in groups which
        // are in the same track in the other axis (row)
        AbstractAxis otherAxis = axis.Other();
        List<GridItem> sorted = new(items);
        sorted.StableSort((a, b) => a.Placement(otherAxis).Start.CompareTo(b.Placement(otherAxis).Start));

        // Iterate over grid rows
        int rowStartIndex = 0;
        while (rowStartIndex < sorted.Count)
        {
            // Get the row index of the current row
            OriginZeroLine currentRow = sorted[rowStartIndex].Placement(otherAxis).Start;

            // Find the item index of the first item that is in a different row (or the end of the list)
            int rowEndIndex = rowStartIndex;
            while (rowEndIndex < sorted.Count && sorted[rowEndIndex].Placement(otherAxis).Start == currentRow)
            {
                rowEndIndex++;
            }

            List<GridItem> rowItems = sorted.GetRange(rowStartIndex, rowEndIndex - rowStartIndex);
            rowStartIndex = rowEndIndex;

            // Count how many items in *this row* are baseline aligned
            // If a row has one or zero items participating in baseline alignment then baseline alignment is a no-op
            // for those items and we skip further computations for that row
            int rowBaselineItemCount = rowItems.Count(item => item.ParticipatesInBaselineAlignment());
            if (rowBaselineItemCount <= 1)
            {
                continue;
            }

            // Compute the baselines of all items in the row participating in baseline alignment
            foreach (GridItem item in rowItems)
            {
                if (!item.ParticipatesInBaselineAlignment())
                {
                    continue;
                }

                LayoutOutput measuredSizeAndBaselines = tree.PerformChildLayout(
                    item.Node,
                    Size.None,
                    innerNodeSize,
                    Size.MinContent,
                    SizingMode.InherentSize,
                    Line.False);

                float height = measuredSizeAndBaselines.Size.Height;
                float baseline = CommonAlignment.ChildFirstBaseline(measuredSizeAndBaselines.Baselines.First, height, item.Overflow.Y) ?? height;
                item.Baseline = baseline;
            }

            // Items are aligned by the distance from the top of their margin box to their baseline
            float MarginBoxBaseline(GridItem item) =>
                (item.Baseline ?? 0f) + item.Margin.Top.ResolveOrZero(innerNodeSize.Width, calc);

            // Compute the max baseline of all items in the row participating in baseline alignment
            float? rowMaxBaseline = null;
            foreach (GridItem item in rowItems)
            {
                if (item.ParticipatesInBaselineAlignment())
                {
                    float value = MarginBoxBaseline(item);
                    if (rowMaxBaseline is not float max || TotalCmp(value, max) >= 0)
                    {
                        rowMaxBaseline = value;
                    }
                }
            }

            // Compute the baseline shim for each item in the row participating in baseline alignment
            foreach (GridItem item in rowItems)
            {
                if (item.ParticipatesInBaselineAlignment())
                {
                    item.BaselineShim = rowMaxBaseline!.Value - MarginBoxBaseline(item);
                    item.KnownDimensionsCache = null;
                }
            }
        }
    }

    /// <summary>11.5 Resolve Intrinsic Track Sizes.</summary>
    private static void ResolveIntrinsicTrackSizes(
        ILayoutPartialTree tree,
        AbstractAxis axis,
        List<GridTrack> axisTracks,
        List<GridTrack> otherAxisTracks,
        List<GridItem> allItems,
        AvailableSpace axisAvailableGridSpace,
        Size<float?> innerNodeSize,
        Func<GridTrack, float?, ILayoutPartialTree, float?> getTrackSizeEstimate)
    {
        // Step 1. Shim baseline-aligned items so their intrinsic size contributions reflect their baseline alignment.
        // Already done at this point. See resolve_item_baselines function.

        // Step 2.

        // The track sizing algorithm requires us to iterate through the items in ascending order of the number of
        // tracks they span (first items that span 1 track, then items that span 2 tracks, etc).
        // To avoid having to do multiple iterations of the items, we pre-sort them into this order.
        //
        // Items that cross a flexible track are all processed together in a single batch (regardless of their span), so
        // they only need to be moved to the end of the list. And the order in which items within a batch are processed
        // does not affect the result, so the remaining items only need to be sorted if any of them span more than one track.
        List<GridItem> frontItems = new(allItems.Count);
        List<GridItem> backItems = new();
        bool needsSort = false;
        foreach (GridItem item in allItems)
        {
            if (item.CrossesFlexibleTrack(axis))
            {
                backItems.Add(item);
            }
            else
            {
                needsSort |= item.Span(axis) > 1;
                frontItems.Add(item);
            }
        }

        if (needsSort)
        {
            frontItems.StableSort((a, b) => a.Span(axis).CompareTo(b.Span(axis)));
        }

        // Items pushed to the back of the FrontBackVecBuilder end up in reverse push order
        backItems.Reverse();
        List<GridItem> items = frontItems;
        items.AddRange(backItems);

        // Step 2, Step 3 and Step 4
        // 2 & 3. Iterate over items that don't cross a flex track. Items should have already been sorted in ascending order
        // of the number of tracks they span. Step 2 is the 1 track case and has an optimised implementation
        // 4. Next, repeat the previous step instead considering (together, rather than grouped by span size) all items
        // that do span a track with a flexible sizing function while

        // Compute item's intrinsic (content-based) sizes
        // Note: For items with a specified minimum size of auto (the initial value), the minimum contribution is usually equivalent
        // to the min-content contribution, but can differ in some cases, see 6.6 Automatic Minimum Size of Grid Items.
        // Also, minimum contribution <= min-content contribution <= max-content contribution.

        float? axisInnerNodeSize = innerNodeSize.Get(axis);
        IntrinsicSizeMeasurer itemSizer = new(tree, otherAxisTracks, axis, innerNodeSize, getTrackSizeEstimate);
        Func<object, float, float> calc = itemSizer.Calc;

        ItemBatcher batchedItemIterator = new(axis);
        while (batchedItemIterator.Next(items) is { } next)
        {
            (List<GridItem> batch, bool isFlex) = next;

            // 2. Size tracks to fit non-spanning items: For each track with an intrinsic track sizing function and not a flexible sizing function,
            // consider the items in it with a span of 1:
            ushort batchSpan = batch[0].Placement(axis).Span();
            if (!isFlex && batchSpan == 1)
            {
                foreach (GridItem item in batch)
                {
                    int trackIndex = item.PlacementIndexes(axis).Start + 1;
                    GridTrack track = axisTracks[trackIndex];

                    // Handle base sizes
                    float newBaseSize;
                    int minTag = track.MinTrackSizingFunction.Raw.Tag;
                    if (minTag == CompactLength.MinContentTag)
                    {
                        newBaseSize = MathHelpers.Max(track.BaseSize, itemSizer.MinContentContribution(item, axisTracks));
                    }
                    else if (minTag == CompactLength.MaxContentTag)
                    {
                        newBaseSize = MathHelpers.Max(track.BaseSize, itemSizer.MaxContentContribution(item, axisTracks));
                    }
                    else if (track.MinTrackSizingFunction.BehavesAsAuto(axisInnerNodeSize))
                    {
                        // If the container size is indefinite and has not yet been resolved then percentage sized
                        // tracks are treated as auto
                        float space;

                        // QUIRK: The spec says that:
                        //
                        //   If the grid container is being sized under a min- or max-content constraint, use the items' limited
                        //   min-content contributions in place of their minimum contributions here.
                        //
                        // However, in practice browsers only seem to apply this rule if the item is not a scroll container
                        // (note that overflow:hidden counts as a scroll container), giving the automatic minimum size of scroll
                        // containers (zero) precedence over the min-content contributions.
                        if ((axisAvailableGridSpace.IsMinContent || axisAvailableGridSpace.IsMaxContent)
                            && !item.Overflow.Get(axis).IsScrollContainer())
                        {
                            float axisMinimumSize = itemSizer.MinimumContribution(item, axisTracks);
                            float axisMinContentSize = itemSizer.MinContentContribution(item, axisTracks);
                            float? limit = track.MaxTrackSizingFunction.DefiniteLimit(axisInnerNodeSize, calc);
                            space = MathHelpers.Max(axisMinContentSize.MaybeMin(limit), axisMinimumSize);
                        }
                        else
                        {
                            space = itemSizer.MinimumContribution(item, axisTracks);
                        }

                        newBaseSize = MathHelpers.Max(track.BaseSize, space);
                    }
                    else
                    {
                        // Do nothing as it's not an intrinsic track sizing function
                        newBaseSize = track.BaseSize;
                    }

                    track.BaseSize = newBaseSize;

                    // Handle growth limits
                    //
                    // Computing an item's contributions calls into the child, so they are only computed if they are used.
                    // The min-content contribution is computed for every item in an intrinsic track (even where only the
                    // max-content contribution determines the growth limit), as the check for whether track sizing needs
                    // to be re-run compares each such item's min-content contribution against the one cached here.
                    MaxTrackSizingFunction maxTrackSizingFunction = track.MaxTrackSizingFunction;
                    float fitContentLimit = track.FitContentLimit();
                    float? minContentContribution = track.HasIntrinsicSizingFunction()
                        ? itemSizer.MinContentContribution(item, axisTracks)
                        : null;

                    float? plannedIncrease;
                    if (maxTrackSizingFunction.IsFitContent())
                    {
                        // Always increase the growth limit to at least the size of the *fit-content limited*
                        // max-content contribution
                        float maxContentContribution = MathHelpers.Min(itemSizer.MaxContentContribution(item, axisTracks), fitContentLimit);

                        // If item is not a scroll container, then increase the growth limit to at least the
                        // size of the min-content contribution
                        plannedIncrease = minContentContribution is float minContent && !item.Overflow.Get(axis).IsScrollContainer()
                            ? MathHelpers.Max(minContent, maxContentContribution)
                            : maxContentContribution;
                    }
                    else if (maxTrackSizingFunction.IsMaxContentAlike()
                        || (maxTrackSizingFunction.UsesPercentage() && !axisInnerNodeSize.HasValue))
                    {
                        // If the container size is indefinite and has not yet been resolved then percentage sized
                        // tracks should be treated as auto (this matches Chrome's behaviour and seems sensible)
                        plannedIncrease = itemSizer.MaxContentContribution(item, axisTracks);
                    }
                    else if (maxTrackSizingFunction.IsIntrinsic())
                    {
                        plannedIncrease = minContentContribution;
                    }
                    else
                    {
                        plannedIncrease = null;
                    }

                    if (plannedIncrease is float increase)
                    {
                        track.GrowthLimitPlannedIncrease = MathHelpers.Max(track.GrowthLimitPlannedIncrease, increase);
                    }
                }

                foreach (GridTrack track in axisTracks)
                {
                    if (track.GrowthLimitPlannedIncrease > 0f)
                    {
                        track.GrowthLimit = track.GrowthLimit == float.PositiveInfinity
                            ? track.GrowthLimitPlannedIncrease
                            : MathHelpers.Max(track.GrowthLimit, track.GrowthLimitPlannedIncrease);
                    }

                    track.InfinitelyGrowable = false;
                    track.GrowthLimitPlannedIncrease = 0f;
                    if (track.GrowthLimit < track.BaseSize)
                    {
                        track.GrowthLimit = track.BaseSize;
                    }
                }

                continue;
            }

            // 1. For intrinsic minimums:
            // First increase the base size of tracks with an intrinsic min track sizing function
            foreach (GridItem item in batch)
            {
                if (!item.CrossesIntrinsicTrack(axis))
                {
                    continue;
                }

                // ...by distributing extra space as needed to accommodate these items' minimum contributions.
                //
                // QUIRK: The spec says that:
                //
                //   If the grid container is being sized under a min- or max-content constraint, use the items' limited min-content contributions
                //   in place of their minimum contributions here.
                //
                // However, in practice browsers only seem to apply this rule if the item is not a scroll container (note that overflow:hidden counts as
                // a scroll container), giving the automatic minimum size of scroll containers (zero) precedence over the min-content contributions.
                float space;
                if ((axisAvailableGridSpace.IsMinContent || axisAvailableGridSpace.IsMaxContent)
                    && !item.Overflow.Get(axis).IsScrollContainer())
                {
                    float axisMinimumSize = itemSizer.MinimumContribution(item, axisTracks);
                    float axisMinContentSize = itemSizer.MinContentContribution(item, axisTracks);
                    float? limit = item.SpannedTrackLimit(axis, axisTracks, axisInnerNodeSize, calc);
                    float limitedMinContent = MathHelpers.Max(axisMinContentSize.MaybeMin(limit), axisMinimumSize);

                    // For items crossing flexible tracks, browsers hand the content-derived contribution to
                    // the flexible tracks only in proportion to the crossed flex factor sum, clamped at one
                    // (CSS Grid 2, 12.5: "if the sum is less than one, distribute that proportion of
                    // space"). The proportion applies to the space left after covering the spanned
                    // inflexible tracks, and the item's minimum contribution acts as a floor on the result:
                    // a definite size still spreads over `0fr` tracks in full. This is what lets a `0fr`
                    // track holding a `min-height: 0` item collapse to zero, which the
                    // `grid-template-rows: 0fr` to `1fr` collapse animation pattern relies on.
                    if (isFlex)
                    {
                        List<GridTrack> spannedTracks = SpannedTracks(axisTracks, item, axis);
                        float inflexibleSizes = InflexibleBaseSizeSum(spannedTracks);
                        float scale = MathHelpers.Min(CrossedFlexFactorSum(spannedTracks), 1f);
                        float excess = MathHelpers.Max(limitedMinContent - inflexibleSizes, 0f);
                        space = MathHelpers.Max(axisMinimumSize, inflexibleSizes + (excess * scale));
                    }
                    else
                    {
                        space = limitedMinContent;
                    }
                }
                else
                {
                    space = itemSizer.MinimumContribution(item, axisTracks);
                }

                List<GridTrack> tracks = SpannedTracks(axisTracks, item, axis);
                if (space > 0f)
                {
                    bool HasIntrinsicMinTrackSizingFunction(GridTrack track) =>
                        !track.MinTrackSizingFunction.DefiniteValue(axisInnerNodeSize, calc).HasValue;

                    if (item.Overflow.Get(axis).IsScrollContainer())
                    {
                        DistributeItemSpaceToBaseSize(
                            isFlex,
                            space,
                            tracks,
                            HasIntrinsicMinTrackSizingFunction,
                            track => track.FitContentLimitedGrowthLimit(),
                            IntrinsicContributionType.Minimum);
                    }
                    else
                    {
                        DistributeItemSpaceToBaseSize(
                            isFlex,
                            space,
                            tracks,
                            HasIntrinsicMinTrackSizingFunction,
                            track => track.GrowthLimit,
                            IntrinsicContributionType.Minimum);
                    }
                }
            }

            FlushPlannedBaseSizeIncreases(axisTracks);

            // 2. For content-based minimums:
            // Next continue to increase the base size of tracks with a min track sizing function of min-content or max-content
            // by distributing extra space as needed to account for these items' min-content contributions.
            static bool HasMinOrMaxContentMinTrackSizingFunction(GridTrack track) => track.MinTrackSizingFunction.IsMinOrMaxContent();
            foreach (GridItem item in batch)
            {
                if (!item.SpansTrackMatching(axis, axisTracks, HasMinOrMaxContentMinTrackSizingFunction))
                {
                    continue;
                }

                float space = itemSizer.MinContentContribution(item, axisTracks);
                List<GridTrack> tracks = SpannedTracks(axisTracks, item, axis);
                if (space > 0f)
                {
                    if (item.Overflow.Get(axis).IsScrollContainer())
                    {
                        DistributeItemSpaceToBaseSize(
                            isFlex,
                            space,
                            tracks,
                            HasMinOrMaxContentMinTrackSizingFunction,
                            track => track.FitContentLimitedGrowthLimit(),
                            IntrinsicContributionType.Minimum);
                    }
                    else
                    {
                        DistributeItemSpaceToBaseSize(
                            isFlex,
                            space,
                            tracks,
                            HasMinOrMaxContentMinTrackSizingFunction,
                            track => track.GrowthLimit,
                            IntrinsicContributionType.Minimum);
                    }
                }
            }

            FlushPlannedBaseSizeIncreases(axisTracks);

            // 3. For max-content minimums:
            // If the grid container is being sized under a max-content constraint, continue to increase the base size of tracks with
            // a min track sizing function of auto or max-content by distributing extra space as needed to account for these items'
            // limited max-content contributions.
            static bool HasMaxContentMinTrackSizingFunction(GridTrack track) => track.MinTrackSizingFunction.IsMaxContent();
            if (axisAvailableGridSpace == AvailableSpace.MaxContent)
            {
                static bool HasAutoMinTrackSizingFunction(GridTrack track) =>
                    track.MinTrackSizingFunction.IsAuto() && !track.MaxTrackSizingFunction.IsMinContent();

                foreach (GridItem item in batch)
                {
                    if (!item.SpansTrackMatching(axis, axisTracks, track =>
                        HasAutoMinTrackSizingFunction(track) || HasMaxContentMinTrackSizingFunction(track)))
                    {
                        continue;
                    }

                    float axisMaxContentSize = itemSizer.MaxContentContribution(item, axisTracks);
                    float? limit = item.SpannedTrackLimit(axis, axisTracks, axisInnerNodeSize, calc);
                    float space = axisMaxContentSize.MaybeMin(limit);

                    // As for the intrinsic minimums above: scale the space beyond the spanned inflexible
                    // tracks by the crossed flex factor sum, clamped at one. Anchoring at the inflexible
                    // track sizes rather than the current base sizes keeps this pass from compounding with
                    // the scaling already applied to the min-content contribution.
                    if (isFlex)
                    {
                        List<GridTrack> spannedTracks = SpannedTracks(axisTracks, item, axis);
                        float inflexibleSizes = InflexibleBaseSizeSum(spannedTracks);
                        float scale = MathHelpers.Min(CrossedFlexFactorSum(spannedTracks), 1f);
                        space = inflexibleSizes + (MathHelpers.Max(space - inflexibleSizes, 0f) * scale);
                    }

                    List<GridTrack> tracks = SpannedTracks(axisTracks, item, axis);
                    if (space > 0f)
                    {
                        // If any of the tracks spanned by the item have a MaxContent min track sizing function then
                        // distribute space only to those tracks. Otherwise distribute space to tracks with an Auto min
                        // track sizing function.
                        //
                        // Note: this prioritisation of MaxContent over Auto is not mentioned in the spec (which suggests that
                        // we ought to distribute space evenly between MaxContent and Auto tracks). But it is implemented like
                        // this in both Chrome and Firefox (and it does have a certain logic to it), so we implement it too for
                        // compatibility.
                        //
                        // See: https://www.w3.org/TR/css-grid-1/#track-size-max-content-min
                        if (tracks.Any(HasMaxContentMinTrackSizingFunction))
                        {
                            DistributeItemSpaceToBaseSize(
                                isFlex,
                                space,
                                tracks,
                                HasMaxContentMinTrackSizingFunction,
                                _ => float.PositiveInfinity,
                                IntrinsicContributionType.Maximum);
                        }
                        else
                        {
                            DistributeItemSpaceToBaseSize(
                                isFlex,
                                space,
                                tracks,
                                HasAutoMinTrackSizingFunction,
                                track => track.FitContentLimitedGrowthLimit(),
                                IntrinsicContributionType.Maximum);
                        }
                    }
                }

                FlushPlannedBaseSizeIncreases(axisTracks);
            }

            // In all cases, continue to increase the base size of tracks with a min track sizing function of max-content by distributing
            // extra space as needed to account for these items' max-content contributions.
            foreach (GridItem item in batch)
            {
                if (!item.SpansTrackMatching(axis, axisTracks, HasMaxContentMinTrackSizingFunction))
                {
                    continue;
                }

                float axisMaxContentSize = itemSizer.MaxContentContribution(item, axisTracks);
                float space = axisMaxContentSize;
                List<GridTrack> tracks = SpannedTracks(axisTracks, item, axis);
                if (space > 0f)
                {
                    DistributeItemSpaceToBaseSize(
                        isFlex,
                        space,
                        tracks,
                        HasMaxContentMinTrackSizingFunction,
                        track => track.GrowthLimit,
                        IntrinsicContributionType.Maximum);
                }
            }

            FlushPlannedBaseSizeIncreases(axisTracks);

            // 4. If at this point any track's growth limit is now less than its base size, increase its growth limit to match its base size.
            foreach (GridTrack track in axisTracks)
            {
                if (track.GrowthLimit < track.BaseSize)
                {
                    track.GrowthLimit = track.BaseSize;
                }
            }

            // If a track is a flexible track, then it has flexible max track sizing function
            // It cannot also have an intrinsic max track sizing function, so these steps do not apply.
            if (!isFlex)
            {
                // 5. For intrinsic maximums: Next increase the growth limit of tracks with an intrinsic max track sizing function by
                // distributing extra space as needed to account for these items' min-content contributions.
                bool HasIntrinsicMaxTrackSizingFunction(GridTrack track) =>
                    !track.MaxTrackSizingFunction.HasDefiniteValue(axisInnerNodeSize);
                foreach (GridItem item in batch)
                {
                    if (!item.SpansTrackMatching(axis, axisTracks, HasIntrinsicMaxTrackSizingFunction))
                    {
                        continue;
                    }

                    float axisMinContentSize = itemSizer.MinContentContribution(item, axisTracks);
                    float space = axisMinContentSize;
                    List<GridTrack> tracks = SpannedTracks(axisTracks, item, axis);
                    if (space > 0f)
                    {
                        DistributeItemSpaceToGrowthLimit(space, tracks, HasIntrinsicMaxTrackSizingFunction);
                    }
                }

                // Mark any tracks whose growth limit changed from infinite to finite in this step as infinitely growable for the next step.
                FlushPlannedGrowthLimitIncreases(axisTracks, true);

                // 6. For max-content maximums: Lastly continue to increase the growth limit of tracks with a max track sizing function of max-content
                // by distributing extra space as needed to account for these items' max-content contributions. However, limit the growth of any
                // fit-content() tracks by their fit-content() argument.
                bool HasMaxContentMaxTrackSizingFunction(GridTrack track) =>
                    track.MaxTrackSizingFunction.IsMaxContentAlike()
                    || (track.MaxTrackSizingFunction.UsesPercentage() && !axisInnerNodeSize.HasValue);
                foreach (GridItem item in batch)
                {
                    if (!item.SpansTrackMatching(axis, axisTracks, HasMaxContentMaxTrackSizingFunction))
                    {
                        continue;
                    }

                    float axisMaxContentSize = itemSizer.MaxContentContribution(item, axisTracks);
                    float space = axisMaxContentSize;
                    List<GridTrack> tracks = SpannedTracks(axisTracks, item, axis);
                    if (space > 0f)
                    {
                        DistributeItemSpaceToGrowthLimit(space, tracks, HasMaxContentMaxTrackSizingFunction);
                    }
                }

                // Mark any tracks whose growth limit changed from infinite to finite in this step as infinitely growable for the next step.
                FlushPlannedGrowthLimitIncreases(axisTracks, false);
            }
        }

        // Step 5. If any track still has an infinite growth limit (because, for example, it had no items placed
        // in it or it is a flexible track), set its growth limit to its base size.
        // NOTE: this step is super-important to ensure that the "Maximise Tracks" step doesn't affect flexible tracks
        foreach (GridTrack track in axisTracks)
        {
            if (track.GrowthLimit == float.PositiveInfinity)
            {
                track.GrowthLimit = track.BaseSize;
            }
        }
    }

    private static float InflexibleBaseSizeSum(List<GridTrack> tracks)
    {
        float sum = 0f;
        foreach (GridTrack track in tracks)
        {
            if (!track.IsFlexible())
            {
                sum += track.BaseSize;
            }
        }

        return sum;
    }

    /// <summary>The sum of the flex factors of the flexible tracks in the passed slice.</summary>
    private static float CrossedFlexFactorSum(List<GridTrack> tracks)
    {
        float sum = 0f;
        foreach (GridTrack track in tracks)
        {
            if (track.IsFlexible())
            {
                sum += track.FlexFactor();
            }
        }

        return sum;
    }

    /// <summary>11.5.1. Distributing Extra Space Across Spanned Tracks. https://www.w3.org/TR/css-grid-1/#extra-space</summary>
    private static void DistributeItemSpaceToBaseSize(
        bool isFlex,
        float space,
        List<GridTrack> tracks,
        Func<GridTrack, bool> trackIsAffected,
        Func<GridTrack, float> trackLimit,
        IntrinsicContributionType intrinsicContributionType)
    {
        if (isFlex)
        {
            bool Filter(GridTrack track) => track.IsFlexible() && trackIsAffected(track);

            // If the sum of the flex factors of the affected tracks is greater than zero, distribute
            // space according to the ratios of the tracks' flex factors. Otherwise distribute space equally.
            //
            // Note: the spec says to compute the sum over all flexible tracks spanned by the item, and to
            // blend flex-factor-proportional and equal distribution when the sum is below one. But Chrome
            // computes the sum over only the affected tracks and uses pure flex-factor ratios whenever
            // that sum is non-zero, so we do the same for compatibility.
            float flexFactorSum = 0f;
            foreach (GridTrack track in tracks)
            {
                if (Filter(track))
                {
                    flexFactorSum += track.FlexFactor();
                }
            }

            if (flexFactorSum > 0f)
            {
                DistributeItemSpaceToBaseSizeInner(space, tracks, Filter, track => track.FlexFactor(), trackLimit, intrinsicContributionType);
            }
            else
            {
                DistributeItemSpaceToBaseSizeInner(space, tracks, Filter, _ => 1f, trackLimit, intrinsicContributionType);
            }
        }
        else
        {
            DistributeItemSpaceToBaseSizeInner(space, tracks, trackIsAffected, _ => 1f, trackLimit, intrinsicContributionType);
        }
    }

    /// <summary>Inner function that doesn't account for differences due to distributing to flex items.</summary>
    private static void DistributeItemSpaceToBaseSizeInner(
        float space,
        List<GridTrack> tracks,
        Func<GridTrack, bool> trackIsAffected,
        Func<GridTrack, float> trackDistributionProportion,
        Func<GridTrack, float> trackLimit,
        IntrinsicContributionType intrinsicContributionType)
    {
        // Skip this distribution if there is either
        //   - no space to distribute
        //   - no affected tracks to distribute space to
        if (space == 0f || !tracks.Any(trackIsAffected))
        {
            return;
        }

        // Define get_base_size function. This is passed to the distribute_space_up_to_limits helper function
        // to indicate that it is the base size that is being distributed to.
        static float GetBaseSize(GridTrack track) => track.BaseSize;

        // 1. Find the space to distribute
        float trackSizes = 0f;
        foreach (GridTrack track in tracks)
        {
            trackSizes += track.BaseSize;
        }

        float extraSpace = MathHelpers.Max(0f, space - trackSizes);

        // 2. Distribute space up to limits:
        // Note: there are two exit conditions to this loop:
        //   - We run out of space to distribute (extra_space falls below THRESHOLD)
        //   - We run out of growable tracks to distribute to
        const float Threshold = 0.000001f;
        extraSpace = DistributeSpaceUpToLimits(extraSpace, tracks, trackIsAffected, trackDistributionProportion, GetBaseSize, trackLimit);

        // 3. Distribute remaining span beyond limits (if any)
        if (extraSpace > Threshold)
        {
            // When accommodating minimum contributions or accommodating min-content contributions:
            //   - any affected track that happens to also have an intrinsic max track sizing function;
            // When accommodating max-content contributions:
            //   - any affected track that happens to also have a max-content max track sizing function
            Func<GridTrack, bool> filter = intrinsicContributionType == IntrinsicContributionType.Minimum
                ? track => track.MaxTrackSizingFunction.IsIntrinsic()
                : track => track.MaxTrackSizingFunction.IsMaxOrFitContent();

            // If there are no such tracks (matching filter above), then use all affected tracks.
            int numberOfTracks = tracks.Count(track => trackIsAffected(track) && filter(track));
            if (numberOfTracks == 0)
            {
                filter = _ => true;
            }

            // When distributing beyond limits, growth limits are ignored, but the argument
            // to any fit-content() max track sizing function still caps growth.
            DistributeSpaceUpToLimits(
                extraSpace,
                tracks,
                track => trackIsAffected(track) && filter(track),
                trackDistributionProportion,
                GetBaseSize,
                track => track.FitContentLimit());
        }

        // 4. For each affected track, if the track's item-incurred increase is larger than the track's planned increase
        // set the track's planned increase to that value.
        foreach (GridTrack track in tracks)
        {
            if (track.ItemIncurredIncrease > track.BaseSizePlannedIncrease)
            {
                track.BaseSizePlannedIncrease = track.ItemIncurredIncrease;
            }

            // Reset the item_incurresed increase ready for the next space distribution
            track.ItemIncurredIncrease = 0f;
        }
    }

    /// <summary>11.5.1. Distributing Extra Space Across Spanned Tracks. This is simplified (and faster) version of the algorithm for growth limits.</summary>
    private static void DistributeItemSpaceToGrowthLimit(float space, List<GridTrack> tracks, Func<GridTrack, bool> trackIsAffected)
    {
        // Skip this distribution if there is either
        //   - no space to distribute
        //   - no affected tracks to distribute space to
        if (space == 0f || tracks.Count(trackIsAffected) == 0)
        {
            return;
        }

        // 1. Find the space to distribute
        float trackSizes = 0f;
        foreach (GridTrack track in tracks)
        {
            trackSizes += track.GrowthLimit == float.PositiveInfinity ? track.BaseSize : track.GrowthLimit;
        }

        float extraSpace = MathHelpers.Max(0f, space - trackSizes);

        // 2. Distribute space up to limits:
        // For growth limits the limit is the growth limit itself unless the track is marked infinitely growable (or its
        // growth limit is still infinite), in which case it is the track's fit-content() argument if it has one, or
        // infinity if it does not. As the affected size of a track that isn't growable is its growth limit, those
        // tracks won't receive any space in this step.
        static float GrowthLimitOrBaseSize(GridTrack track) =>
            track.GrowthLimit == float.PositiveInfinity ? track.BaseSize : track.GrowthLimit;

        extraSpace = DistributeSpaceUpToLimits(
            extraSpace,
            tracks,
            track => trackIsAffected(track) && (track.InfinitelyGrowable || track.GrowthLimit == float.PositiveInfinity),
            _ => 1f,
            GrowthLimitOrBaseSize,
            track => track.FitContentLimit());

        // 3. Distribute space beyond limits
        // If space remains after all tracks are frozen, unfreeze and continue to distribute space to the item-incurred increase
        // ...when handling any intrinsic growth limit: all affected tracks.
        if (extraSpace > 0f)
        {
            DistributeSpaceUpToLimits(extraSpace, tracks, trackIsAffected, _ => 1f, GrowthLimitOrBaseSize, track => track.FitContentLimit());
        }

        // 4. For each affected track, if the track's item-incurred increase is larger than the track's planned increase
        // set the track's planned increase to that value.
        foreach (GridTrack track in tracks)
        {
            if (track.ItemIncurredIncrease > track.GrowthLimitPlannedIncrease)
            {
                track.GrowthLimitPlannedIncrease = track.ItemIncurredIncrease;
            }

            // Reset the item_incurresed increase ready for the next space distribution
            track.ItemIncurredIncrease = 0f;
        }
    }

    /// <summary>
    /// 11.6 Maximise Tracks
    /// Distributes free space (if any) to tracks with FINITE growth limits, up to their limits.
    /// </summary>
    private static void MaximiseTracks(List<GridTrack> axisTracks, AvailableSpace axisAvailableGridSpace)
    {
        float usedSpace = 0f;
        foreach (GridTrack track in axisTracks)
        {
            usedSpace += track.BaseSize;
        }

        float freeSpace = axisAvailableGridSpace.ComputeFreeSpace(usedSpace);
        if (freeSpace == float.PositiveInfinity)
        {
            foreach (GridTrack track in axisTracks)
            {
                track.BaseSize = track.GrowthLimit;
            }
        }
        else if (freeSpace > 0f)
        {
            DistributeSpaceUpToLimits(
                freeSpace,
                axisTracks,
                _ => true,
                _ => 1f,
                track => track.BaseSize,
                track => track.FitContentLimitedGrowthLimit());
            foreach (GridTrack track in axisTracks)
            {
                track.BaseSize += track.ItemIncurredIncrease;
                track.ItemIncurredIncrease = 0f;
            }
        }
    }

    /// <summary>
    /// 11.7. Expand Flexible Tracks
    /// This step sizes flexible tracks using the largest value it can assign to an fr without exceeding the available space.
    /// </summary>
    private static void ExpandFlexibleTracks(
        ILayoutPartialTree tree,
        AbstractAxis axis,
        List<GridTrack> axisTracks,
        List<GridTrack> otherAxisTracks,
        List<GridItem> items,
        float? axisMinSize,
        float? axisMaxSize,
        AvailableSpace axisAvailableSpaceForExpansion,
        Size<float?> innerNodeSize,
        Func<GridTrack, float?, ILayoutPartialTree, float?> getTrackSizeEstimate)
    {
        IntrinsicSizeMeasurer itemSizer = new(tree, otherAxisTracks, axis, innerNodeSize, getTrackSizeEstimate);

        // First, find the grid's used flex fraction:
        float flexFraction;
        if (axisAvailableSpaceForExpansion.IsDefinite)
        {
            // If the free space is zero:
            //    The used flex fraction is zero.
            // Otherwise, if the free space is a definite length:
            //   The used flex fraction is the result of finding the size of an fr using all of the grid tracks and
            //   a space to fill of the available grid space.
            float availableSpace = axisAvailableSpaceForExpansion.Value;
            float usedSpace = 0f;
            foreach (GridTrack track in axisTracks)
            {
                usedSpace += track.BaseSize;
            }

            float freeSpace = availableSpace - usedSpace;
            flexFraction = freeSpace <= 0f ? 0f : FindSizeOfFr(axisTracks, availableSpace);
        }
        else if (axisAvailableSpaceForExpansion.IsMinContent)
        {
            // If ... sizing the grid container under a min-content constraint the used flex fraction is zero.
            flexFraction = 0f;
        }
        else
        {
            // Otherwise, if the free space is an indefinite length:
            // The used flex fraction is the maximum of:

            // For each flexible track, if the flexible track's flex factor is greater than one,
            // the result of dividing the track's base size by its flex factor; otherwise, the track's base size.
            float? trackMax = null;
            foreach (GridTrack track in axisTracks)
            {
                if (!track.MaxTrackSizingFunction.IsFr())
                {
                    continue;
                }

                float flexFactor = track.FlexFactor();
                float value = flexFactor > 1f ? track.BaseSize / flexFactor : track.BaseSize;
                if (trackMax is not float max || TotalCmp(value, max) >= 0)
                {
                    trackMax = value;
                }
            }

            // For each grid item that crosses a flexible track, the result of finding the size of an fr using all the grid tracks
            // that the item crosses and a space to fill of the item's max-content contribution.
            float? itemMax = null;
            foreach (GridItem item in items)
            {
                if (!item.CrossesFlexibleTrack(axis))
                {
                    continue;
                }

                float maxContentContribution = itemSizer.MaxContentContribution(item, axisTracks);
                List<GridTrack> tracks = SpannedTracks(axisTracks, item, axis);
                float value = FindSizeOfFr(tracks, maxContentContribution);
                if (itemMax is not float max || TotalCmp(value, max) >= 0)
                {
                    itemMax = value;
                }
            }

            float naiveFlexFraction = MathHelpers.Max(trackMax ?? 0f, itemMax ?? 0f);

            // If using this flex fraction would cause the grid to be smaller than the grid container's min-width/height (or larger than the
            // grid container's max-width/height), then redo this step, treating the free space as definite and the available grid space as equal
            // to the grid container's inner size when it's sized to its min-width/height (max-width/height).
            // (Note: min_size takes precedence over max_size)
            float hypotheticalGridSize = 0f;
            foreach (GridTrack track in axisTracks)
            {
                if (track.MaxTrackSizingFunction.IsFr())
                {
                    float trackFlexFactor = track.MaxTrackSizingFunction.Raw.Value;
                    hypotheticalGridSize += MathHelpers.Max(track.BaseSize, trackFlexFactor * naiveFlexFraction);
                }
                else
                {
                    hypotheticalGridSize += track.BaseSize;
                }
            }

            float minSize = axisMinSize ?? 0f;
            float maxSize = axisMaxSize ?? float.PositiveInfinity;
            if (hypotheticalGridSize < minSize)
            {
                flexFraction = FindSizeOfFr(axisTracks, minSize);
            }
            else if (hypotheticalGridSize > maxSize)
            {
                flexFraction = FindSizeOfFr(axisTracks, maxSize);
            }
            else
            {
                flexFraction = naiveFlexFraction;
            }
        }

        // For each flexible track, if the product of the used flex fraction and the track's flex factor is greater
        // than the track's base size, set its base size to that product.
        foreach (GridTrack track in axisTracks)
        {
            if (track.MaxTrackSizingFunction.IsFr())
            {
                float trackFlexFactor = track.MaxTrackSizingFunction.Raw.Value;
                track.BaseSize = MathHelpers.Max(track.BaseSize, trackFlexFactor * flexFraction);
            }
        }
    }

    /// <summary>
    /// 11.7.1. Find the Size of an fr
    /// This algorithm finds the largest size that an fr unit can be without exceeding the target size.
    /// It must be called with a set of grid tracks and some quantity of space to fill.
    /// </summary>
    private static float FindSizeOfFr(List<GridTrack> tracks, float spaceToFill)
    {
        // Handle the trivial case where there is no space to fill
        // Do not remove as otherwise the loop below will loop infinitely
        if (spaceToFill == 0f)
        {
            return 0f;
        }

        // If the product of the hypothetical fr size (computed below) and any flexible track's flex factor
        // is less than the track's base size, then we must restart this algorithm treating all such tracks as inflexible.
        // We therefore wrap the entire algorithm in a loop, with an hypothetical_fr_size of INFINITY such that the above
        // condition can never be true for the first iteration.
        float hypotheticalFrSize = float.PositiveInfinity;
        float previousIterHypotheticalFrSize;

        // Every restart of the algorithm treats at least one more flexible track as inflexible, so a valid
        // hypothetical fr size is always found within `tracks.len() + 1` iterations. Non-finite values (which
        // can arise from non-finite style inputs) break that invariant as comparisons against `NaN` are always
        // false, so we bound the iteration count to guarantee termination.
        int maxIterations = tracks.Count + 1;
        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            // Let leftover space be the space to fill minus the base sizes of the non-flexible grid tracks.
            // Let flex factor sum be the sum of the flex factors of the flexible tracks. If this value is less than 1, set it to 1 instead.
            // We compute both of these in a single loop to avoid iterating over the data twice
            float usedSpace = 0f;
            float naiveFlexFactorSum = 0f;
            foreach (GridTrack track in tracks)
            {
                // Tracks for which flex_factor * hypothetical_fr_size < track.base_size are treated as inflexible
                if (track.MaxTrackSizingFunction.IsFr()
                    && track.MaxTrackSizingFunction.Raw.Value * hypotheticalFrSize >= track.BaseSize)
                {
                    naiveFlexFactorSum += track.MaxTrackSizingFunction.Raw.Value;
                }
                else
                {
                    usedSpace += track.BaseSize;
                }
            }

            float leftoverSpace = spaceToFill - usedSpace;
            float flexFactor = MathHelpers.Max(naiveFlexFactorSum, 1f);

            // Let the hypothetical fr size be the leftover space divided by the flex factor sum.
            previousIterHypotheticalFrSize = hypotheticalFrSize;
            hypotheticalFrSize = leftoverSpace / flexFactor;

            // If the product of the hypothetical fr size and a flexible track's flex factor is less than the track's base size,
            // restart this algorithm treating all such tracks as inflexible.
            // We keep track of the hypothetical_fr_size
            float currentFrSize = hypotheticalFrSize;
            float previousFrSize = previousIterHypotheticalFrSize;
            bool hypotheticalFrSizeIsValid = tracks.All(track =>
            {
                if (track.MaxTrackSizingFunction.IsFr())
                {
                    float trackFlexFactor = track.MaxTrackSizingFunction.Raw.Value;
                    return trackFlexFactor * currentFrSize >= track.BaseSize
                        || trackFlexFactor * previousFrSize < track.BaseSize;
                }

                return true;
            });
            if (hypotheticalFrSizeIsValid)
            {
                break;
            }
        }

        // Return the hypothetical fr size.
        return hypotheticalFrSize;
    }

    /// <summary>
    /// 11.8. Stretch auto Tracks
    /// This step expands tracks that have an auto max track sizing function by dividing any remaining positive, definite free space equally amongst them.
    /// </summary>
    private static void StretchAutoTracks(List<GridTrack> axisTracks, float? axisMinSize, AvailableSpace axisAvailableSpaceForExpansion)
    {
        int numAutoTracks = axisTracks.Count(track => track.MaxTrackSizingFunction.IsAuto());
        if (numAutoTracks > 0)
        {
            float usedSpace = 0f;
            foreach (GridTrack track in axisTracks)
            {
                usedSpace += track.BaseSize;
            }

            // If the free space is indefinite, but the grid container has a definite min-width/height
            // use that size to calculate the free space for this step instead.
            float freeSpace = axisAvailableSpaceForExpansion.IsDefinite
                ? axisAvailableSpaceForExpansion.ComputeFreeSpace(usedSpace)
                : axisMinSize is float size ? size - usedSpace : 0f;
            if (freeSpace > 0f)
            {
                float extraSpacePerAutoTrack = freeSpace / numAutoTracks;
                foreach (GridTrack track in axisTracks)
                {
                    if (track.MaxTrackSizingFunction.IsAuto())
                    {
                        track.BaseSize += extraSpacePerAutoTrack;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Helper function for distributing space to tracks evenly.
    /// Used by both distribute_item_space_to_base_size and maximise_tracks steps.
    /// </summary>
    private static float DistributeSpaceUpToLimits(
        float spaceToDistribute,
        List<GridTrack> tracks,
        Func<GridTrack, bool> trackIsAffected,
        Func<GridTrack, float> trackDistributionProportion,
        Func<GridTrack, float> trackAffectedProperty,
        Func<GridTrack, float> trackLimit)
    {
        // Define a small constant to avoid infinite loops due to rounding errors. Rather than stopping distributing
        // extra space when it gets to exactly zero, we will stop when it falls below this amount
        const float Threshold = 0.01f;

        // Each iteration freezes at least one track at its limit, so the loop always completes within
        // `tracks.len() + 1` iterations. Non-finite values (which can arise from non-finite style inputs) can
        // prevent any space from being distributed, so we bound the iteration count to guarantee termination.
        int maxIterations = tracks.Count + 1;
        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            if (spaceToDistribute <= Threshold)
            {
                break;
            }

            float trackDistributionProportionSum = 0f;
            float? minIncreaseLimit = null;
            foreach (GridTrack track in tracks)
            {
                if (trackAffectedProperty(track) + track.ItemIncurredIncrease < trackLimit(track) && trackIsAffected(track))
                {
                    trackDistributionProportionSum += trackDistributionProportion(track);

                    // Compute item-incurred increase for this iteration
                    float increaseLimit = (trackLimit(track) - trackAffectedProperty(track) - track.ItemIncurredIncrease)
                        / trackDistributionProportion(track);
                    if (minIncreaseLimit is not float min || TotalCmp(increaseLimit, min) < 0)
                    {
                        minIncreaseLimit = increaseLimit;
                    }
                }
            }

            if (trackDistributionProportionSum == 0f)
            {
                break;
            }

            // We will never pass an empty track list to this function
            float iterationItemIncurredIncrease = MathHelpers.Min(minIncreaseLimit!.Value, spaceToDistribute / trackDistributionProportionSum);

            foreach (GridTrack track in tracks)
            {
                if (!trackIsAffected(track))
                {
                    continue;
                }

                float increase = iterationItemIncurredIncrease * trackDistributionProportion(track);
                if (increase > 0f && trackAffectedProperty(track) + track.ItemIncurredIncrease + increase <= trackLimit(track) + Threshold)
                {
                    track.ItemIncurredIncrease += increase;
                    spaceToDistribute -= increase;
                }
            }
        }

        return spaceToDistribute;
    }
}
