namespace TaffySharp;

/// <summary>Stores the number of tracks in a given dimension. Stores separately the number of tracks in the implicit and explicit grids.</summary>
internal struct TrackCounts : IEquatable<TrackCounts>
{
    /// <summary>The number of tracks in the implicit grid before the explicit grid.</summary>
    public ushort NegativeImplicit;

    /// <summary>The number of tracks in the explicit grid.</summary>
    public ushort Explicit;

    /// <summary>The number of tracks in the implicit grid after the explicit grid.</summary>
    public ushort PositiveImplicit;

    public TrackCounts(ushort negativeImplicit, ushort @explicit, ushort positiveImplicit)
    {
        NegativeImplicit = negativeImplicit;
        Explicit = @explicit;
        PositiveImplicit = positiveImplicit;
    }

    public static TrackCounts FromRaw(ushort negativeImplicit, ushort @explicit, ushort positiveImplicit) => new(negativeImplicit, @explicit, positiveImplicit);

    /// <summary>Count of all tracks.</summary>
    public readonly int Len() => NegativeImplicit + Explicit + PositiveImplicit;

    /// <summary>The OriginZeroLine representing the start of the implicit grid.</summary>
    public readonly OriginZeroLine ImplicitStartLine() => new((short)-NegativeImplicit);

    /// <summary>The OriginZeroLine representing the end of the implicit grid.</summary>
    public readonly OriginZeroLine ImplicitEndLine() => new((short)(Explicit + PositiveImplicit));

    /// <summary>Converts an OriginZero line into the index of the track immediately following it (in the CellOccupancyMatrix).</summary>
    public readonly short OzLineToNextTrack(OriginZeroLine index) => (short)(index.Value + NegativeImplicit);

    /// <summary>Converts a range of OriginZero lines into a (start-inclusive, end-exclusive) range of track indexes.</summary>
    public readonly (short Start, short End) OzLineRangeToTrackRange(Line<OriginZeroLine> input) =>
        (OzLineToNextTrack(input.Start), OzLineToNextTrack(input.End));

    /// <summary>Converts a track index into the OriginZero line immediately preceding it.</summary>
    public readonly OriginZeroLine TrackToPrevOzLine(ushort index) => new((short)(index - NegativeImplicit));

    public readonly bool Equals(TrackCounts other) =>
        NegativeImplicit == other.NegativeImplicit && Explicit == other.Explicit && PositiveImplicit == other.PositiveImplicit;

    public override readonly bool Equals(object? obj) => obj is TrackCounts other && Equals(other);

    public override readonly int GetHashCode() => (NegativeImplicit, Explicit, PositiveImplicit).GetHashCode();
}

/// <summary>Whether a GridTrack represents an actual track or a gutter.</summary>
internal enum GridTrackKind
{
    Track,
    Gutter,
}

/// <summary>
/// Internal sizing information for a single grid track (row/column).
/// Gutters between tracks are sized similarly to actual tracks, so they are also represented by this type.
/// </summary>
internal sealed class GridTrack
{
    public GridTrackKind Kind;

    /// <summary>Whether the track is a collapsed track/gutter. Collapsed tracks are effectively treated as having fixed size of 0.</summary>
    public bool IsCollapsed;

    public MinTrackSizingFunction MinTrackSizingFunction;
    public MaxTrackSizingFunction MaxTrackSizingFunction;

    /// <summary>The distance of the start of the track from the start of the grid container.</summary>
    public float Offset;

    public float BaseSize;
    public float GrowthLimit;

    /// <summary>A temporary scratch value when sizing tracks. Used as an additional amount to add to the estimate for the available space in the opposite axis when content sizing items.</summary>
    public float ContentAlignmentAdjustment;

    /// <summary>A temporary scratch value when "distributing space" to avoid clobbering planned increase variable.</summary>
    public float ItemIncurredIncrease;

    public float BaseSizePlannedIncrease;
    public float GrowthLimitPlannedIncrease;

    /// <summary>A temporary scratch value when "distributing space".</summary>
    public bool InfinitelyGrowable;

    /// <summary>The resolved fit-content() limit of the track (infinity if it is not a fit-content() track).</summary>
    public float FitContentLimitValue;

    private GridTrack(GridTrackKind kind, MinTrackSizingFunction min, MaxTrackSizingFunction max)
    {
        Kind = kind;
        IsCollapsed = false;
        MinTrackSizingFunction = min;
        MaxTrackSizingFunction = max;
        Offset = 0f;
        BaseSize = 0f;
        GrowthLimit = 0f;
        ContentAlignmentAdjustment = 0f;
        ItemIncurredIncrease = 0f;
        BaseSizePlannedIncrease = 0f;
        GrowthLimitPlannedIncrease = 0f;
        InfinitelyGrowable = false;
        FitContentLimitValue = float.PositiveInfinity;
    }

    /// <summary>Create new GridTrack representing an actual track (not a gutter).</summary>
    public static GridTrack New(MinTrackSizingFunction min, MaxTrackSizingFunction max) => new(GridTrackKind.Track, min, max);

    /// <summary>Create a new GridTrack representing a gutter.</summary>
    public static GridTrack Gutter(LengthPercentage size) => new(GridTrackKind.Gutter, size, size);

    /// <summary>Mark a GridTrack as collapsed. Also sets both of the track's sizing functions to fixed zero-sized sizing functions.</summary>
    public void Collapse()
    {
        IsCollapsed = true;
        MinTrackSizingFunction = MinTrackSizingFunction.Zero;
        MaxTrackSizingFunction = MaxTrackSizingFunction.Zero;
        FitContentLimitValue = float.PositiveInfinity;
    }

    /// <summary>Returns true if the track is flexible (has a Flex MaxTrackSizingFunction), else false.</summary>
    public bool IsFlexible() => MaxTrackSizingFunction.IsFr();

    /// <summary>Returns true if the track uses percentage sizing functions.</summary>
    public bool UsesPercentage() => MinTrackSizingFunction.UsesPercentage() || MaxTrackSizingFunction.UsesPercentage();

    /// <summary>Returns true if the track has an intrinsic min and or max sizing function.</summary>
    public bool HasIntrinsicSizingFunction() => MinTrackSizingFunction.IsIntrinsic() || MaxTrackSizingFunction.IsIntrinsic();

    /// <summary>Resolve and store the fit-content() limit of the track.</summary>
    public void ResolveFitContentLimit(float? axisInnerNodeSize, Func<object, float, float> calcResolver) =>
        FitContentLimitValue = MaxTrackSizingFunction.FitContentLimit(axisInnerNodeSize, calcResolver) ?? float.PositiveInfinity;

    public float FitContentLimit() => FitContentLimitValue;

    /// <summary>Returns the growth limit clamped by the fit-content() limit (if any).</summary>
    public float FitContentLimitedGrowthLimit() => MathHelpers.Min(GrowthLimit, FitContentLimitValue);

    /// <summary>Returns the track's flex factor if it is a flex track, else 0.</summary>
    public float FlexFactor() => MaxTrackSizingFunction.IsFr() ? MaxTrackSizingFunction.Raw.Value : 0f;
}

/// <summary>The occupancy state of a single grid cell.</summary>
internal enum CellOccupancyState
{
    Unoccupied,
    DefinitelyPlaced,
    AutoPlaced,
}

/// <summary>A painted run of cells along one track, in OriginZero coordinates of the other axis.</summary>
internal struct OccupiedInterval
{
    public short Start;
    public short End;
    public CellOccupancyState State;

    public OccupiedInterval(short start, short end, CellOccupancyState state)
    {
        Start = start;
        End = end;
        State = state;
    }

    public readonly bool Overlaps(short start, short end) => Start < end && End > start;
}

/// <summary>The occupied cells along a single track, as a sorted list of disjoint intervals.</summary>
internal sealed class TrackIntervals
{
    public List<OccupiedInterval> Intervals = new();

    /// <summary>The line after the end of the last auto-placed item in this track (for sparse auto-placement).</summary>
    public OriginZeroLine? AutoPlacementCursor;

    public bool IsEmpty() => Intervals.Count == 0;

    public CellOccupancyState StateAt(short coordinate)
    {
        foreach (OccupiedInterval interval in Intervals)
        {
            if (interval.Start <= coordinate && coordinate < interval.End)
            {
                return interval.State;
            }
        }

        return CellOccupancyState.Unoccupied;
    }

    /// <summary>Paint the range with the given state, overwriting anything previously painted there.</summary>
    public void Paint(short start, short end, CellOccupancyState state)
    {
        if (start >= end)
        {
            return;
        }

        // Fast path: the painted range lies entirely after all existing intervals
        // (the common case, as placement queries and inserts mostly advance forwards)
        if (Intervals.Count == 0)
        {
            Intervals.Add(new OccupiedInterval(start, end, state));
            return;
        }

        OccupiedInterval last = Intervals[Intervals.Count - 1];
        if (last.End <= start)
        {
            if (last.State == state && last.End == start)
            {
                last.End = end;
                Intervals[Intervals.Count - 1] = last;
            }
            else
            {
                Intervals.Add(new OccupiedInterval(start, end, state));
            }

            return;
        }

        List<OccupiedInterval> result = new();

        // Intervals (or partial intervals) entirely before the painted range
        foreach (OccupiedInterval interval in Intervals)
        {
            if (interval.End <= start)
            {
                result.Add(interval);
            }
            else if (interval.Start < start)
            {
                result.Add(new OccupiedInterval(interval.Start, start, interval.State));
            }
        }

        // The painted range, merged with the preceding interval if it touches and has the same state
        PushMerged(result, new OccupiedInterval(start, end, state));

        // Intervals (or partial intervals) entirely after the painted range
        foreach (OccupiedInterval interval in Intervals)
        {
            OccupiedInterval trimmed;
            if (interval.Start >= end)
            {
                trimmed = interval;
            }
            else if (interval.End > end)
            {
                trimmed = new OccupiedInterval(end, interval.End, interval.State);
            }
            else
            {
                continue;
            }

            PushMerged(result, trimmed);
        }

        Intervals = result;
    }

    private static void PushMerged(List<OccupiedInterval> result, OccupiedInterval interval)
    {
        if (result.Count > 0)
        {
            OccupiedInterval last = result[result.Count - 1];
            if (last.State == interval.State && last.End == interval.Start)
            {
                last.End = interval.End;
                result[result.Count - 1] = last;
                return;
            }
        }

        result.Add(interval);
    }

    /// <summary>The last occupied coordinate of the last interval overlapping the range, if any.</summary>
    public short? CollisionExtent(short start, short end)
    {
        for (int i = Intervals.Count - 1; i >= 0; i--)
        {
            OccupiedInterval interval = Intervals[i];
            if (!(interval.End > start))
            {
                break;
            }

            if (interval.Overlaps(start, end))
            {
                return (short)(interval.End - 1);
            }
        }

        return null;
    }
}

/// <summary>A sparse representation of which grid cells are occupied, used during grid item placement.</summary>
internal sealed class CellOccupancyMatrix
{
    private TrackCounts _columns;
    private TrackCounts _rows;
    private readonly List<TrackIntervals> _rowIntervals;
    private readonly List<TrackIntervals> _columnIntervals;

    private CellOccupancyMatrix(TrackCounts columns, TrackCounts rows)
    {
        _columns = columns;
        _rows = rows;
        _rowIntervals = new List<TrackIntervals>(rows.Len());
        for (int i = 0; i < rows.Len(); i++)
        {
            _rowIntervals.Add(new TrackIntervals());
        }

        _columnIntervals = new List<TrackIntervals>(columns.Len());
        for (int i = 0; i < columns.Len(); i++)
        {
            _columnIntervals.Add(new TrackIntervals());
        }
    }

    /// <summary>Create a CellOccupancyMatrix given a set of provisional track counts.</summary>
    public static CellOccupancyMatrix WithTrackCounts(TrackCounts columns, TrackCounts rows) => new(columns, rows);

    private List<TrackIntervals> TrackLists(AbsoluteAxis trackAxis) =>
        trackAxis == AbsoluteAxis.Horizontal ? _columnIntervals : _rowIntervals;

    private static void Prepend(List<TrackIntervals> list, int count)
    {
        TrackIntervals[] fresh = new TrackIntervals[count];
        for (int i = 0; i < count; i++)
        {
            fresh[i] = new TrackIntervals();
        }

        list.InsertRange(0, fresh);
    }

    private static void Extend(List<TrackIntervals> list, int count)
    {
        for (int i = 0; i < count; i++)
        {
            list.Add(new TrackIntervals());
        }
    }

    /// <summary>Expands the grid (potentially in all 4 directions) to accommodate the passed area.</summary>
    private void ExpandToFitRange(Line<OriginZeroLine> rowSpan, Line<OriginZeroLine> colSpan)
    {
        // Calculate number of rows and columns missing to accommodate ranges (if any)
        int reqNegativeRows = Math.Max(-_rows.NegativeImplicit - rowSpan.Start.Value, 0);
        int reqPositiveRows = Math.Max(rowSpan.End.Value - _rows.ImplicitEndLine().Value, 0);
        int reqNegativeCols = Math.Max(-_columns.NegativeImplicit - colSpan.Start.Value, 0);
        int reqPositiveCols = Math.Max(colSpan.End.Value - _columns.ImplicitEndLine().Value, 0);

        // Add empty tracks to the front and/or back of the per-track interval lists.
        // Interval contents are stored in OriginZero coordinates, so they do not need to shift.
        if (reqNegativeRows > 0)
        {
            Prepend(_rowIntervals, reqNegativeRows);
        }

        if (reqPositiveRows > 0)
        {
            Extend(_rowIntervals, reqPositiveRows);
        }

        if (reqNegativeCols > 0)
        {
            Prepend(_columnIntervals, reqNegativeCols);
        }

        if (reqPositiveCols > 0)
        {
            Extend(_columnIntervals, reqPositiveCols);
        }

        _rows.NegativeImplicit += (ushort)reqNegativeRows;
        _rows.PositiveImplicit += (ushort)reqPositiveRows;
        _columns.NegativeImplicit += (ushort)reqNegativeCols;
        _columns.PositiveImplicit += (ushort)reqPositiveCols;
    }

    /// <summary>Mark an area of the matrix as occupied, expanding the allocated space as necessary to accommodate the passed area.</summary>
    public void MarkAreaAs(AbsoluteAxis primaryAxis, Line<OriginZeroLine> primarySpan, Line<OriginZeroLine> secondarySpan, CellOccupancyState value)
    {
        (Line<OriginZeroLine> rowSpan, Line<OriginZeroLine> columnSpan) = primaryAxis == AbsoluteAxis.Horizontal
            ? (secondarySpan, primarySpan)
            : (primarySpan, secondarySpan);

        ExpandToFitRange(rowSpan, columnSpan);

        if (value == CellOccupancyState.AutoPlaced && primarySpan.Start < primarySpan.End && secondarySpan.Start < secondarySpan.End)
        {
            AdvanceAutoPlacementCursor(primaryAxis, secondarySpan.Start, primarySpan.End);
        }

        (short rowStart, short rowEnd) = _rows.OzLineRangeToTrackRange(rowSpan);
        (short colStart, short colEnd) = _columns.OzLineRangeToTrackRange(columnSpan);

        for (int rowIndex = rowStart; rowIndex < rowEnd; rowIndex++)
        {
            _rowIntervals[rowIndex].Paint(columnSpan.Start.Value, columnSpan.End.Value, value);
        }

        for (int columnIndex = colStart; columnIndex < colEnd; columnIndex++)
        {
            _columnIntervals[columnIndex].Paint(rowSpan.Start.Value, rowSpan.End.Value, value);
        }
    }

    /// <summary>
    /// Determines whether a grid area is entirely unoccupied. If it is not, returns the first line in the primary
    /// axis after the last occupied cell that collides with the area (the next candidate start position).
    /// </summary>
    public OriginZeroLine? LineAreaCollisionJump(AbsoluteAxis primaryAxis, Line<OriginZeroLine> primarySpan, Line<OriginZeroLine> secondarySpan)
    {
        List<TrackIntervals> trackLists = TrackLists(primaryAxis.OtherAxis());
        TrackCounts secondaryCounts = TrackCountsFor(primaryAxis.OtherAxis());
        (short rangeStart, short rangeEnd) = secondaryCounts.OzLineRangeToTrackRange(secondarySpan);

        // Out of bounds cells are considered unoccupied, so clamp the secondary range to the
        // tracks which actually exist
        int secondaryStart = Math.Max((int)rangeStart, 0);
        int secondaryEnd = Math.Min((int)rangeEnd, (short)trackLists.Count);

        short? extent = null;
        for (int secondaryIndex = secondaryStart; secondaryIndex < secondaryEnd; secondaryIndex++)
        {
            if (trackLists[secondaryIndex].CollisionExtent(primarySpan.Start.Value, primarySpan.End.Value) is not short cell)
            {
                continue;
            }

            extent = extent is short best ? Math.Max(best, cell) : cell;
        }

        return extent is short c ? new OriginZeroLine((short)(c + 1)) : null;
    }

    /// <summary>The line after the last track in the span (in the given axis) that contains any occupied cell.</summary>
    public OriginZeroLine? OccupiedTrackJump(AbsoluteAxis axis, Line<OriginZeroLine> span)
    {
        TrackCounts counts = TrackCountsFor(axis);
        List<TrackIntervals> trackLists = TrackLists(axis);
        (short rangeStart, short rangeEnd) = counts.OzLineRangeToTrackRange(span);
        int start = Math.Max((int)rangeStart, 0);
        int end = Math.Min((int)rangeEnd, (short)trackLists.Count);
        for (int index = end - 1; index >= start; index--)
        {
            if (!trackLists[index].IsEmpty())
            {
                OriginZeroLine line = counts.TrackToPrevOzLine((ushort)index);
                return line + (ushort)1;
            }
        }

        return null;
    }

    /// <summary>Determines whether the specified row contains any items.</summary>
    public bool RowIsOccupied(int rowIndex) => rowIndex < _rowIntervals.Count && !_rowIntervals[rowIndex].IsEmpty();

    /// <summary>Determines whether the specified column contains any items.</summary>
    public bool ColumnIsOccupied(int columnIndex) => columnIndex < _columnIntervals.Count && !_columnIntervals[columnIndex].IsEmpty();

    /// <summary>Returns the track counts of this CellOccupancyMatrix in the relevant axis.</summary>
    public TrackCounts TrackCountsFor(AbsoluteAxis trackType) => trackType == AbsoluteAxis.Horizontal ? _columns : _rows;

    /// <summary>The auto-placement cursor of the track (in the other axis) that starts at the given line, if any.</summary>
    public OriginZeroLine? AutoPlacementCursor(AbsoluteAxis trackType, OriginZeroLine startAt)
    {
        TrackCounts trackCounts = TrackCountsFor(trackType.OtherAxis());
        short trackComputedIndex = trackCounts.OzLineToNextTrack(startAt);
        List<TrackIntervals> trackLists = TrackLists(trackType.OtherAxis());
        if (trackComputedIndex < 0 || trackComputedIndex >= (short)trackLists.Count)
        {
            // Index out of bounds: no tracks to search
            return null;
        }

        return trackLists[trackComputedIndex].AutoPlacementCursor;
    }

    private void AdvanceAutoPlacementCursor(AbsoluteAxis primaryAxis, OriginZeroLine secondaryStart, OriginZeroLine primaryEnd)
    {
        int trackIndex = TrackCountsFor(primaryAxis.OtherAxis()).OzLineToNextTrack(secondaryStart);
        List<TrackIntervals> tracks = primaryAxis == AbsoluteAxis.Horizontal ? _rowIntervals : _columnIntervals;
        OriginZeroLine? cursor = tracks[trackIndex].AutoPlacementCursor;
        // Rust: max(cursor, Some(primary_end)) where None < Some
        tracks[trackIndex].AutoPlacementCursor = cursor is OriginZeroLine c ? OriginZeroLine.Max(c, primaryEnd) : primaryEnd;
    }
}
