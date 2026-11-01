namespace TaffySharp;

/// <summary>Implements placing items in the grid and resolving the implicit grid. https://www.w3.org/TR/css-grid-1/#placement</summary>
internal static class Placement
{
    /// <summary>Move a position forward by one line (saturating at the coordinate limit).</summary>
    private static OriginZeroLine AdvancePosition(OriginZeroLine position) =>
        new(position.Value == short.MaxValue ? short.MaxValue : (short)(position.Value + 1));

    /// <summary>Resolve an indefinite span starting at a position, saturating the end line at the coordinate limit.</summary>
    private static Line<OriginZeroLine> ResolveIndefiniteGridSpan(OriginZeroLine position, ushort span)
    {
        int start = position.Value;
        return new(ClampToI16(start), ClampToI16(start + span));
    }

    private static OriginZeroLine ClampToI16(int value) => new((short)MathHelpers.Clamp(value, short.MinValue, short.MaxValue));

    /// <summary>
    /// 8.5. Grid Item Placement Algorithm
    /// Place items into the grid, generating new rows/column into the implicit grid as required.
    /// <paramref name="placements"/> holds the resolved (origin-zero) placement of each item in <paramref name="items"/>.
    /// [Specification](https://www.w3.org/TR/css-grid-2/#auto-placement-algo)
    /// </summary>
    public static void PlaceGridItems(
        CellOccupancyMatrix cellOccupancyMatrix,
        IReadOnlyList<GridItem> items,
        IReadOnlyList<InBothAbsAxis<Line<GenericGridPlacement<OriginZeroLine>>>> placements,
        GridAutoFlow gridAutoFlow)
    {
        System.Diagnostics.Debug.Assert(items.Count == placements.Count, "items and placements must have the same length");

        AbsoluteAxis primaryAxis = gridAutoFlow.PrimaryAxis();
        AbsoluteAxis secondaryAxis = primaryAxis.OtherAxis();

        // 1. Place children with definite positions
        for (int i = 0; i < items.Count; i++)
        {
            InBothAbsAxis<Line<GenericGridPlacement<OriginZeroLine>>> placement = placements[i];
            if (!(placement.Horizontal.IsDefinite() && placement.Vertical.IsDefinite()))
            {
                continue;
            }

            (Line<OriginZeroLine> rowSpan, Line<OriginZeroLine> colSpan) = PlaceDefiniteGridItem(placement, primaryAxis);
            RecordGridPlacement(cellOccupancyMatrix, items[i], primaryAxis, rowSpan, colSpan, CellOccupancyState.DefinitelyPlaced);
        }

        // 2. Place remaining children with definite secondary axis positions
        for (int i = 0; i < items.Count; i++)
        {
            InBothAbsAxis<Line<GenericGridPlacement<OriginZeroLine>>> placement = placements[i];
            if (!(placement.Get(secondaryAxis).IsDefinite() && !placement.Get(primaryAxis).IsDefinite()))
            {
                continue;
            }

            (Line<OriginZeroLine> primarySpan, Line<OriginZeroLine> secondarySpan) =
                PlaceDefiniteSecondaryAxisItem(cellOccupancyMatrix, placement, gridAutoFlow);
            RecordGridPlacement(cellOccupancyMatrix, items[i], primaryAxis, primarySpan, secondarySpan, CellOccupancyState.AutoPlaced);
        }

        // 3. Determine the number of columns in the implicit grid
        // By the time we get to this point in the execution, this is actually already accounted for:
        //
        // 3.1 Start with the columns from the explicit grid
        //        => Handled by grid size estimate which is used to pre-size the GridOccupancyMatrix
        //
        // 3.2 Among all the items with a definite column position (explicitly positioned items, items positioned in the previous step,
        //     and items not yet positioned but with a definite column) add columns to the beginning and end of the implicit grid as necessary
        //     to accommodate those items.
        //        => Handled by expand_to_fit_range which expands the GridOccupancyMatrix as necessary
        //            -> Called by mark_area_as
        //            -> Called by record_grid_placement
        //
        // 3.3 If the largest column span among all the items without a definite column position is larger than the width of
        //     the implicit grid, add columns to the end of the implicit grid to accommodate that column span.
        //        => Handled by grid size estimate which is used to pre-size the GridOccupancyMatrix

        // 4. Position the remaining grid items
        // (which either have definite position only in the secondary axis or indefinite positions in both axis)
        OriginZeroLine primaryAxisGridStartLine = cellOccupancyMatrix.TrackCountsFor(primaryAxis).ImplicitStartLine();
        OriginZeroLine secondaryAxisGridStartLine = cellOccupancyMatrix.TrackCountsFor(secondaryAxis).ImplicitStartLine();
        (OriginZeroLine, OriginZeroLine) gridStartPosition = (primaryAxisGridStartLine, secondaryAxisGridStartLine);
        (OriginZeroLine, OriginZeroLine) gridPosition = gridStartPosition;
        for (int i = 0; i < items.Count; i++)
        {
            InBothAbsAxis<Line<GenericGridPlacement<OriginZeroLine>>> placement = placements[i];
            if (placement.Get(secondaryAxis).IsDefinite())
            {
                continue;
            }

            // Compute placement
            (Line<OriginZeroLine> primarySpan, Line<OriginZeroLine> secondarySpan) =
                PlaceIndefinitelyPositionedItem(cellOccupancyMatrix, placement, gridAutoFlow, gridPosition);

            // Record item
            RecordGridPlacement(cellOccupancyMatrix, items[i], primaryAxis, primarySpan, secondarySpan, CellOccupancyState.AutoPlaced);

            // If using the "dense" placement algorithm then reset the grid position back to grid_start_position ready for the next item
            // Otherwise set it to the position of the current item so that the next item it placed after it.
            gridPosition = gridAutoFlow.IsDense() ? gridStartPosition : (primarySpan.End, secondarySpan.Start);
        }
    }

    /// <summary>
    /// 8.5. Grid Item Placement Algorithm
    /// Place a single definitely placed item into the grid.
    /// </summary>
    private static (Line<OriginZeroLine> PrimarySpan, Line<OriginZeroLine> SecondarySpan) PlaceDefiniteGridItem(
        InBothAbsAxis<Line<GenericGridPlacement<OriginZeroLine>>> placement,
        AbsoluteAxis primaryAxis)
    {
        // Resolve spans to tracks
        Line<OriginZeroLine> primarySpan = placement.Get(primaryAxis).ResolveDefiniteGridLines();
        Line<OriginZeroLine> secondarySpan = placement.Get(primaryAxis.OtherAxis()).ResolveDefiniteGridLines();
        return (primarySpan, secondarySpan);
    }

    /// <summary>
    /// 8.5. Grid Item Placement Algorithm
    /// Step 2. Place remaining children with definite secondary axis positions.
    /// </summary>
    private static (Line<OriginZeroLine> PrimarySpan, Line<OriginZeroLine> SecondarySpan) PlaceDefiniteSecondaryAxisItem(
        CellOccupancyMatrix cellOccupancyMatrix,
        InBothAbsAxis<Line<GenericGridPlacement<OriginZeroLine>>> placement,
        GridAutoFlow autoFlow)
    {
        AbsoluteAxis primaryAxis = autoFlow.PrimaryAxis();
        AbsoluteAxis secondaryAxis = primaryAxis.OtherAxis();

        OriginZeroLine primaryAxisGridStartLine = cellOccupancyMatrix.TrackCountsFor(primaryAxis).ImplicitStartLine();
        Line<OriginZeroLine> secondaryAxisPlacement = placement.Get(secondaryAxis).ResolveDefiniteGridLines();
        OriginZeroLine startingPosition = autoFlow.IsDense()
            ? primaryAxisGridStartLine
            : cellOccupancyMatrix.AutoPlacementCursor(primaryAxis, secondaryAxisPlacement.Start) ?? primaryAxisGridStartLine;
        ushort primaryAxisSpan = placement.Get(primaryAxis).IndefiniteSpan();

        OriginZeroLine position = startingPosition;
        while (true)
        {
            Line<OriginZeroLine> primaryAxisPlacement = ResolveIndefiniteGridSpan(position, primaryAxisSpan);
            OriginZeroLine? collision = cellOccupancyMatrix.LineAreaCollisionJump(primaryAxis, primaryAxisPlacement, secondaryAxisPlacement);
            if (collision is not OriginZeroLine nextPosition)
            {
                return (primaryAxisPlacement, secondaryAxisPlacement);
            }

            position = nextPosition;
        }
    }

    /// <summary>
    /// 8.5. Grid Item Placement Algorithm
    /// Step 4. Position the remaining grid items.
    /// </summary>
    private static (Line<OriginZeroLine> PrimarySpan, Line<OriginZeroLine> SecondarySpan) PlaceIndefinitelyPositionedItem(
        CellOccupancyMatrix cellOccupancyMatrix,
        InBothAbsAxis<Line<GenericGridPlacement<OriginZeroLine>>> placement,
        GridAutoFlow autoFlow,
        (OriginZeroLine Primary, OriginZeroLine Secondary) gridPosition)
    {
        AbsoluteAxis primaryAxis = autoFlow.PrimaryAxis();
        AbsoluteAxis secondaryAxis = primaryAxis.OtherAxis();

        Line<GenericGridPlacement<OriginZeroLine>> primaryPlacementStyle = placement.Get(primaryAxis);
        Line<GenericGridPlacement<OriginZeroLine>> secondaryPlacementStyle = placement.Get(secondaryAxis);

        ushort secondarySpan = secondaryPlacementStyle.IndefiniteSpan();
        bool hasDefinitePrimaryAxisPosition = primaryPlacementStyle.IsDefinite();
        OriginZeroLine primaryAxisGridStartLine = cellOccupancyMatrix.TrackCountsFor(primaryAxis).ImplicitStartLine();
        OriginZeroLine primaryAxisGridEndLine = cellOccupancyMatrix.TrackCountsFor(primaryAxis).ImplicitEndLine();
        OriginZeroLine secondaryAxisGridStartLine = cellOccupancyMatrix.TrackCountsFor(secondaryAxis).ImplicitStartLine();

        (OriginZeroLine primaryIdx, OriginZeroLine secondaryIdx) = gridPosition;

        if (hasDefinitePrimaryAxisPosition)
        {
            Line<OriginZeroLine> primarySpan = primaryPlacementStyle.ResolveDefiniteGridLines();

            // Compute secondary axis starting position for search
            secondaryIdx = autoFlow.IsDense()
                // If auto-flow is dense then we always search from the first track
                ? secondaryAxisGridStartLine
                : primarySpan.Start < primaryIdx ? AdvancePosition(secondaryIdx) : secondaryIdx;

            // Item has fixed primary axis position: so we simply increment the secondary axis position
            // until we find a space that the item fits in
            while (true)
            {
                Line<OriginZeroLine> secondaryLines = ResolveIndefiniteGridSpan(secondaryIdx, secondarySpan);

                // If area is occupied, jump the index past the collision and try again
                OriginZeroLine? collision = cellOccupancyMatrix.LineAreaCollisionJump(secondaryAxis, secondaryLines, primarySpan);
                if (collision is OriginZeroLine nextPosition)
                {
                    secondaryIdx = nextPosition;
                    continue;
                }

                // Once we find a free space, return that position
                return (primarySpan, secondaryLines);
            }
        }
        else
        {
            ushort primarySpanCount = primaryPlacementStyle.IndefiniteSpan();

            // Whether the item spans every track in the primary axis. Such an item can only be
            // placed at the primary axis grid start, in a stripe of entirely unoccupied tracks.
            bool spansAllPrimaryTracks = primarySpanCount >= cellOccupancyMatrix.TrackCountsFor(primaryAxis).Len();

            // Item does not have any fixed axis, so we search along the primary axis until we hit the end of the already
            // existent tracks, and then we reset the primary axis back to zero and increment the secondary axis index.
            // We continue in this vein until we find a space that the item fits in.
            while (true)
            {
                Line<OriginZeroLine> primaryLines = ResolveIndefiniteGridSpan(primaryIdx, primarySpanCount);
                Line<OriginZeroLine> secondaryLines = ResolveIndefiniteGridSpan(secondaryIdx, secondarySpan);

                // If the primary index is out of bounds, then increment the secondary index and reset the primary
                // index back to the start of the grid
                bool primaryOutOfBounds = primaryLines.End > primaryAxisGridEndLine;
                if (primaryOutOfBounds)
                {
                    // If the span is out of bounds even at the search start position then it can never fit,
                    // as searching only ever moves the span further away from the start of the grid. Bail out
                    // and let `record_grid_placement` clamp the placement into the limited grid
                    if (primaryIdx == primaryAxisGridStartLine)
                    {
                        return (primaryLines, secondaryLines);
                    }

                    secondaryIdx = AdvancePosition(secondaryIdx);
                    primaryIdx = primaryAxisGridStartLine;
                    continue;
                }

                // If the item spans every primary axis track, it fits if and only if all of the
                // secondary axis tracks it spans are entirely unoccupied. Jump the secondary index
                // past any non-empty tracks in the spanned stripe.
                if (spansAllPrimaryTracks)
                {
                    if (cellOccupancyMatrix.OccupiedTrackJump(secondaryAxis, secondaryLines) is OriginZeroLine nextSecondary)
                    {
                        secondaryIdx = nextSecondary;
                        primaryIdx = primaryAxisGridStartLine;
                        continue;
                    }

                    return (primaryLines, secondaryLines);
                }

                // If area is occupied, jump the primary index past the collision and try again
                OriginZeroLine? collision = cellOccupancyMatrix.LineAreaCollisionJump(primaryAxis, primaryLines, secondaryLines);
                if (collision is OriginZeroLine nextPosition)
                {
                    primaryIdx = nextPosition;
                    continue;
                }

                // Once we find a free space that's in bounds, return that position
                return (primaryLines, secondaryLines);
            }
        }
    }

    /// <summary>Clamp a span into the limited grid, keeping it at least one track long.</summary>
    private static Line<OriginZeroLine> ClampSpanToLimitedGrid(Line<OriginZeroLine> span)
    {
        short start = (short)MathHelpers.Clamp(span.Start.Value, GridConstants.MinOzLine, GridConstants.MaxOzLine - 1);
        short end = (short)MathHelpers.Clamp(span.End.Value, start + 1, GridConstants.MaxOzLine);
        return new(new OriginZeroLine(start), new OriginZeroLine(end));
    }

    /// <summary>
    /// Record the grid item in both CellOccupancyMatrix and the GridItems list
    /// once a definite placement has been determined.
    /// </summary>
    private static void RecordGridPlacement(
        CellOccupancyMatrix cellOccupancyMatrix,
        GridItem item,
        AbsoluteAxis primaryAxis,
        Line<OriginZeroLine> primarySpan,
        Line<OriginZeroLine> secondarySpan,
        CellOccupancyState placementType)
    {
        // Clamp placements into the limited grid to prevent arithmetic overflow when growing the
        // implicit grid (https://www.w3.org/TR/css-grid-1/#overlarge-grids)
        primarySpan = ClampSpanToLimitedGrid(primarySpan);
        secondarySpan = ClampSpanToLimitedGrid(secondarySpan);

        // Mark area of grid as occupied
        cellOccupancyMatrix.MarkAreaAs(primaryAxis, primarySpan, secondarySpan, placementType);

        // Record placement on the item
        (Line<OriginZeroLine> colSpan, Line<OriginZeroLine> rowSpan) = primaryAxis == AbsoluteAxis.Horizontal
            ? (primarySpan, secondarySpan)
            : (secondarySpan, primarySpan);
        item.Column = colSpan;
        item.Row = rowSpan;
    }
}
