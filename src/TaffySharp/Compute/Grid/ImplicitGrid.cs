namespace TaffySharp;

/// <summary>This module is not required for spec compliance, but is used as a performance optimisation to reduce the number of allocations required when creating a grid.</summary>
internal static class ImplicitGrid
{
    /// <summary>
    /// Estimate the number of rows and columns in the grid.
    /// This is used as a performance optimisation to pre-size vectors and reduce allocations. It also forms a necessary step
    /// in the auto-placement:
    ///   - The estimates for the explicit and negative implicit track counts are exact.
    ///   - However, the estimates for the positive explicit track count is a lower bound as auto-placement can affect this
    ///     in ways which are impossible to predict until the auto-placement algorithm is run.
    /// Note that this function internally mixes use of grid track numbers and grid line numbers.
    /// </summary>
    public static (TrackCounts Columns, TrackCounts Rows) ComputeGridSizeEstimate(
        ushort explicitColCount,
        ushort explicitRowCount,
        IReadOnlyList<InBothAbsAxis<Line<GenericGridPlacement<OriginZeroLine>>>> placements)
    {
        // Iterate over children, producing an estimate of the min and max grid lines (in origin-zero coordinates where)
        // along with the span of each item
        (OriginZeroLine colMin, OriginZeroLine colMax, ushort colMaxSpan, OriginZeroLine rowMin, OriginZeroLine rowMax, ushort rowMaxSpan) =
            GetKnownChildPositions(placements);

        // Compute *track* count estimates for each axis from:
        //   - The explicit track counts
        //   - The origin-zero coordinate min and max grid line variables
        ushort negativeImplicitInlineTracks = colMin.ImpliedNegativeImplicitTracks();
        ushort explicitInlineTracks = explicitColCount;
        ushort positiveImplicitInlineTracks = colMax.ImpliedPositiveImplicitTracks(explicitColCount);
        ushort negativeImplicitBlockTracks = rowMin.ImpliedNegativeImplicitTracks();
        ushort explicitBlockTracks = explicitRowCount;
        ushort positiveImplicitBlockTracks = rowMax.ImpliedPositiveImplicitTracks(explicitRowCount);

        // In each axis, adjust positive track estimate if any items have a span that does not fit within
        // the total number of tracks in the estimate
        int totInlineTracks = negativeImplicitInlineTracks + explicitInlineTracks + positiveImplicitInlineTracks;
        if (totInlineTracks < colMaxSpan)
        {
            positiveImplicitInlineTracks = (ushort)(colMaxSpan - explicitInlineTracks - negativeImplicitInlineTracks);
        }

        int totBlockTracks = negativeImplicitBlockTracks + explicitBlockTracks + positiveImplicitBlockTracks;
        if (totBlockTracks < rowMaxSpan)
        {
            positiveImplicitBlockTracks = (ushort)(rowMaxSpan - explicitBlockTracks - negativeImplicitBlockTracks);
        }

        TrackCounts columnCounts = TrackCounts.FromRaw(negativeImplicitInlineTracks, explicitInlineTracks, positiveImplicitInlineTracks);
        TrackCounts rowCounts = TrackCounts.FromRaw(negativeImplicitBlockTracks, explicitBlockTracks, positiveImplicitBlockTracks);

        return (columnCounts, rowCounts);
    }

    /// <summary>
    /// Iterate over children, producing an estimate of the min and max grid *lines* along with the span of each item.
    /// Min and max grid lines are returned in origin-zero coordinates.
    /// The span is measured in tracks spanned.
    /// </summary>
    private static (OriginZeroLine ColMin, OriginZeroLine ColMax, ushort ColMaxSpan, OriginZeroLine RowMin, OriginZeroLine RowMax, ushort RowMaxSpan) GetKnownChildPositions(
        IReadOnlyList<InBothAbsAxis<Line<GenericGridPlacement<OriginZeroLine>>>> placements)
    {
        OriginZeroLine colMin = new(0), colMax = new(0);
        ushort colMaxSpan = 0;
        OriginZeroLine rowMin = new(0), rowMax = new(0);
        ushort rowMaxSpan = 0;

        foreach (InBothAbsAxis<Line<GenericGridPlacement<OriginZeroLine>>> placement in placements)
        {
            // Note: that the children reference the lines in between (and around) the tracks not tracks themselves,
            // and thus we must subtract 1 to get an accurate estimate of the number of tracks
            (OriginZeroLine childColMin, OriginZeroLine childColMax, ushort childColSpan) = ChildMinLineMaxLineSpan(placement.Horizontal);
            (OriginZeroLine childRowMin, OriginZeroLine childRowMax, ushort childRowSpan) = ChildMinLineMaxLineSpan(placement.Vertical);
            colMin = OriginZeroLine.Min(colMin, childColMin);
            colMax = OriginZeroLine.Max(colMax, childColMax);
            colMaxSpan = Math.Max(colMaxSpan, childColSpan);
            rowMin = OriginZeroLine.Min(rowMin, childRowMin);
            rowMax = OriginZeroLine.Max(rowMax, childRowMax);
            rowMaxSpan = Math.Max(rowMaxSpan, childRowSpan);
        }

        return (colMin, colMax, colMaxSpan, rowMin, rowMax, rowMaxSpan);
    }

    /// <summary>
    /// Helper function for compute_grid_size_estimate.
    /// Produces a conservative estimate of the greatest and smallest grid lines used by a single grid item.
    /// Values are returned in origin-zero coordinates.
    /// </summary>
    private static (OriginZeroLine Min, OriginZeroLine Max, ushort Span) ChildMinLineMaxLineSpan(Line<GenericGridPlacement<OriginZeroLine>> ozLine)
    {
        // 8.3.1. Grid Placement Conflict Handling
        // A. If the placement for a grid item contains two lines, and the start line is further end-ward than the end line, swap the two lines.
        // B. If the start line is equal to the end line, remove the end line.
        // C. If the placement contains two spans, remove the one contributed by the end grid-placement property.
        // D. If the placement contains only a span for a named line, replace it with a span of 1.
        GenericGridPlacement<OriginZeroLine> start = ozLine.Start;
        GenericGridPlacement<OriginZeroLine> end = ozLine.End;

        OriginZeroLine min;
        OriginZeroLine max;
        if (start.IsLine && end.IsLine)
        {
            // Both tracks specified (see rules A and B above)
            OriginZeroLine track1 = start.LineValue;
            OriginZeroLine track2 = end.LineValue;
            min = track1 == track2 ? track1 : OriginZeroLine.Min(track1, track2);
            max = track1 == track2 ? track1 + (ushort)1 : OriginZeroLine.Max(track1, track2);
        }
        else if (start.IsLine)
        {
            // Start track specified
            OriginZeroLine track = start.LineValue;
            min = track;
            max = end.IsSpan ? track + end.SpanValue : track + (ushort)1;
        }
        else if (end.IsLine)
        {
            // End track specified
            // An auto start with a definite end resolves to a span of 1 ending at that line
            OriginZeroLine track = end.LineValue;
            min = start.IsSpan ? track - start.SpanValue : track - (ushort)1;
            max = track;
        }
        else
        {
            // Only spans or autos
            // We ignore spans here by returning 0 which never effect the estimate as these are accounted for separately
            min = new OriginZeroLine(0);
            max = new OriginZeroLine(0);
        }

        // Calculate span only for indefinitely placed items as we don't need for other items (whose required space will
        // be taken into account by min and max)
        ushort span = !start.IsLine && !end.IsLine ? ozLine.IndefiniteSpan() : (ushort)1;

        // Clamp the min and max lines into the limited grid so that the estimated implicit track counts
        // stay within the maximum track limit (https://www.w3.org/TR/css-grid-1/#overlarge-grids).
        // This matches the clamping of the actual item placements performed during placement.
        OriginZeroLine clampedMin = new(Math.Max(min.Value, GridConstants.MinOzLine));
        OriginZeroLine clampedMax = new(Math.Min(max.Value, GridConstants.MaxOzLine));

        return (clampedMin, clampedMax, span);
    }
}
