using System.Globalization;
using System.Text;

namespace TaffySharp;

/// <summary>Values derived from the grid container's style and the layout inputs that are fixed for the whole layout.</summary>
internal struct GridContainerConstants
{
    public Direction Direction;
    public Contain Contain;
    public ContainingBlockClaims ContainingBlockClaims;
    public Rect<float> Padding;
    public Rect<float> Border;
    public Size<float> PaddingBorderSize;
    public Size<float?> MinSize;
    public Size<float?> MaxSize;
    public Size<float?> PreferredSize;
    public Point<float> ScrollbarGutter;
    public bool IsScrollContainer;
    public Rect<float> ContentBoxInset;
    public AlignContent AlignContent;
    public AlignContent JustifyContent;
    public AlignItems AlignItems;
    public AlignItems JustifyItems;
    public Size<AvailableSpace> AvailableGridSpace;
    public Size<float?> OuterNodeSize;
    public Size<float?> InnerMinSize;
    public Size<float?> InnerMaxSize;
    public Size<float?> InnerNodeSize;
}

public static partial class Compute
{
    /// <summary>Compute the grid container's constants from its style and the layout inputs.</summary>
    private static GridContainerConstants ComputeContainerConstants(ILayoutGridContainer tree, IGridContainerStyle style, LayoutInput inputs)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;
        Size<float?> knownDimensions = inputs.KnownDimensions;
        Size<float?> parentSize = inputs.ParentSize;
        Size<AvailableSpace> availableSpace = inputs.AvailableSpace;

        Direction direction = style.Direction;
        Contain contain = style.Contain;
        ContainingBlockClaims containingBlockClaims = style.IsContainingBlock;

        // 1. Compute "available grid space"
        // https://www.w3.org/TR/css-grid-1/#available-grid-space
        float? aspectRatio = style.AspectRatio;
        Rect<float> padding = style.Padding.ResolveOrZero(parentSize.Width, calc);
        Rect<float> border = style.Border.ResolveOrZero(parentSize.Width, calc);
        Rect<float> paddingBorder = padding.Add(border);
        Size<float> paddingBorderSize = paddingBorder.SumAxes();
        Size<float> boxSizingAdjustment = style.BoxSizing == BoxSizing.ContentBox ? paddingBorderSize : Size.Zero;

        Size<float?> minSize = style.MinSize
            .MaybeResolve(parentSize, calc)
            .MaybeApplyAspectRatio(aspectRatio)
            .MaybeAdd(boxSizingAdjustment);
        Size<float?> maxSize = style.MaxSize
            .MaybeResolve(parentSize, calc)
            .MaybeApplyAspectRatio(aspectRatio)
            .MaybeAdd(boxSizingAdjustment);
        Size<float?> preferredSize = inputs.SizingMode == SizingMode.InherentSize
            ? style.Size
                .MaybeResolve(parentSize, calc)
                .MaybeApplyAspectRatio(style.AspectRatio)
                .MaybeAdd(boxSizingAdjustment)
            : Size.None;

        // Scrollbar gutters are reserved when the `overflow` property is set to `Overflow::Scroll`.
        // However, the axis are switched (transposed) because a node that scrolls vertically needs
        // *horizontal* space to be reserved for a scrollbar
        float scrollbarWidth = style.ScrollbarWidth;
        Point<float> scrollbarGutter = style.Overflow.Transpose().Map(overflow => overflow == Overflow.Scroll ? scrollbarWidth : 0f);

        Point<Overflow> styleOverflow = style.Overflow;
        bool isScrollContainer = styleOverflow.X.IsScrollContainer() || styleOverflow.Y.IsScrollContainer();

        Rect<float> contentBoxInset = paddingBorder;
        contentBoxInset.Bottom += scrollbarGutter.Y;
        if (direction == Direction.Ltr)
        {
            contentBoxInset.Right += scrollbarGutter.X;
        }
        else
        {
            contentBoxInset.Left += scrollbarGutter.X;
        }

        // `normal` behaves as `stretch` for grid containers
        AlignContent alignContent = style.GridAlignContent(AbstractAxis.Block);
        AlignContent justifyContent = style.GridAlignContent(AbstractAxis.Inline);
        AlignItems alignItems = style.AlignItems;
        AlignItems justifyItems = style.JustifyItems;

        Size<float?> styleSize = knownDimensions.Or(preferredSize);
        Size<AvailableSpace> constrainedAvailableSpace = new Size<AvailableSpace>(
                styleSize.Width is float w ? AvailableSpace.Definite(w) : availableSpace.Width,
                styleSize.Height is float h ? AvailableSpace.Definite(h) : availableSpace.Height)
            .MaybeClamp(minSize, maxSize)
            .MaybeMax(paddingBorderSize);

        Size<AvailableSpace> availableGridSpace = new(
            constrainedAvailableSpace.Width.MapDefiniteValue(space => space - contentBoxInset.HorizontalAxisSum()),
            constrainedAvailableSpace.Height.MapDefiniteValue(space => space - contentBoxInset.VerticalAxisSum()));

        Size<float?> outerNodeSize = knownDimensions.Or(preferredSize).MaybeClamp(minSize, maxSize).MaybeMax(paddingBorderSize);

        // The track sizing algorithm operates on the grid container's content box, so the min/max sizes
        // (which are border-box sizes) need converting to content-box sizes before being passed to it
        Size<float?> innerMinSize = minSize.MaybeSub(contentBoxInset.SumAxes());
        Size<float?> innerMaxSize = maxSize.MaybeSub(contentBoxInset.SumAxes());
        Size<float?> innerNodeSize = new(
            outerNodeSize.Width - contentBoxInset.HorizontalAxisSum(),
            outerNodeSize.Height - contentBoxInset.VerticalAxisSum());

        return new GridContainerConstants
        {
            Direction = direction,
            Contain = contain,
            ContainingBlockClaims = containingBlockClaims,
            Padding = padding,
            Border = border,
            PaddingBorderSize = paddingBorderSize,
            MinSize = minSize,
            MaxSize = maxSize,
            PreferredSize = preferredSize,
            ScrollbarGutter = scrollbarGutter,
            IsScrollContainer = isScrollContainer,
            ContentBoxInset = contentBoxInset,
            AlignContent = alignContent,
            JustifyContent = justifyContent,
            AlignItems = alignItems,
            JustifyItems = justifyItems,
            AvailableGridSpace = availableGridSpace,
            OuterNodeSize = outerNodeSize,
            InnerMinSize = innerMinSize,
            InnerMaxSize = innerMaxSize,
            InnerNodeSize = innerNodeSize,
        };
    }

    /// <summary>
    /// Grid layout algorithm.
    /// This consists of a few phases:
    ///   - Resolving the explicit grid
    ///   - Placing items (which also resolves the implicit grid)
    ///   - Track (row/column) sizing
    ///   - Alignment and Final item placement
    /// </summary>
    public static LayoutOutput ComputeGridLayout(ILayoutGridContainer tree, NodeId node, LayoutInput inputs)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;
        Size<float?> knownDimensions = inputs.KnownDimensions;
        RunMode runMode = inputs.RunMode;

        IGridContainerStyle style = tree.GetGridContainerStyle(node);
        GridContainerConstants constants = ComputeContainerConstants(tree, style, inputs);
        Direction direction = constants.Direction;
        Contain contain = constants.Contain;
        ContainingBlockClaims containingBlockClaims = constants.ContainingBlockClaims;
        Rect<float> padding = constants.Padding;
        Rect<float> border = constants.Border;
        Size<float> paddingBorderSize = constants.PaddingBorderSize;
        Size<float?> minSize = constants.MinSize;
        Size<float?> maxSize = constants.MaxSize;
        Size<float?> preferredSize = constants.PreferredSize;
        Point<float> scrollbarGutter = constants.ScrollbarGutter;
        bool isScrollContainer = constants.IsScrollContainer;
        Rect<float> contentBoxInset = constants.ContentBoxInset;
        AlignContent alignContent = constants.AlignContent;
        AlignContent justifyContent = constants.JustifyContent;
        AlignItems alignItems = constants.AlignItems;
        AlignItems justifyItems = constants.JustifyItems;
        Size<AvailableSpace> availableGridSpace = constants.AvailableGridSpace;
        Size<float?> outerNodeSize = constants.OuterNodeSize;
        Size<float?> innerMinSize = constants.InnerMinSize;
        Size<float?> innerMaxSize = constants.InnerMaxSize;
        Size<float?> innerNodeSize = constants.InnerNodeSize;

        // Short-circuit layout if the container's size is fully determined by the container's size and the run mode
        // is ComputeSize (and thus the container's size is all that we're interested in)
        if (runMode == RunMode.ComputeSize)
        {
            if (outerNodeSize.Width is float width && outerNodeSize.Height is float height)
            {
                return LayoutOutput.FromOuterSize(new Size<float>(width, height));
            }

            // We can also short-circuit if the width is known and only the width has been requested.
            if (inputs.Axis == RequestedAxis.Horizontal && outerNodeSize.Width is float knownWidth)
            {
                return LayoutOutput.FromOuterSize(new Size<float>(knownWidth, 0f));
            }
        }

        // 2. Resolve the explicit grid

        // This is very similar to the inner_node_size except if the inner_node_size is not definite but the node
        // has a min- or max- size style then that will be used in it's place.
        Size<float?> autoFitContainerSize = outerNodeSize
            .Or(maxSize)
            .Or(minSize)
            .MaybeClamp(minSize, maxSize)
            .MaybeMax(paddingBorderSize)
            .MaybeSub(contentBoxInset.SumAxes());

        // If the grid container has a definite size or max size in the relevant axis:
        //   - then the number of repetitions is the largest possible positive integer that does not cause the grid to overflow the content
        //     box of its grid container.
        // Otherwise, if the grid container has a definite min size in the relevant axis:
        //   - then the number of repetitions is the smallest possible positive integer that fulfills that minimum requirement
        // Otherwise, the specified track list repeats only once.
        Size<AutoRepeatStrategy> autoRepeatFitStrategy = outerNodeSize.Or(maxSize).Map(val => val.HasValue
            ? AutoRepeatStrategy.MaxRepetitionsThatDoNotOverflow
            : AutoRepeatStrategy.MinRepetitionsThatDoOverflow);

        // Compute the number of rows and columns in the explicit grid *template*
        // (explicit tracks from grid_areas are computed separately below)
        (ushort colAutoRepetitionCount, ushort gridTemplateColCount) = ExplicitGrid.ComputeExplicitGridSizeInAxis(
            style,
            autoFitContainerSize.Width,
            autoRepeatFitStrategy.Width,
            calc,
            AbsoluteAxis.Horizontal);
        (ushort rowAutoRepetitionCount, ushort gridTemplateRowCount) = ExplicitGrid.ComputeExplicitGridSizeInAxis(
            style,
            autoFitContainerSize.Height,
            autoRepeatFitStrategy.Height,
            calc,
            AbsoluteAxis.Vertical);

        NamedLineResolver nameResolver = NamedLineResolver.New(style, colAutoRepetitionCount, rowAutoRepetitionCount);

        // Clamp the explicit grid to MAX_GRID_TRACKS tracks in each axis
        // https://www.w3.org/TR/css-grid-1/#overlarge-grids
        ushort explicitColCount = (ushort)Math.Min(Math.Max(gridTemplateColCount, nameResolver.AreaColumnCount()), GridConstants.MaxGridTracks);
        ushort explicitRowCount = (ushort)Math.Min(Math.Max(gridTemplateRowCount, nameResolver.AreaRowCount()), GridConstants.MaxGridTracks);

        nameResolver.SetExplicitColumnCount(explicitColCount);
        nameResolver.SetExplicitRowCount(explicitRowCount);

        // Build the per-line names of the explicit grid from the name resolver's collected pairs
        GridLineNames detailedColumnLineNames = nameResolver.DetailedLineNames(AbsoluteAxis.Horizontal);
        GridLineNames detailedRowLineNames = nameResolver.DetailedLineNames(AbsoluteAxis.Vertical);

        // 3. Create the grid items (in document order) and resolve the named lines in their `grid-row`/`grid-column` styles
        // Absolutely positioned children do not take part in grid placement and do not create implicit tracks,
        // so they are excluded here. Each child's style is read and its named lines resolved exactly once; the
        // results drive both the grid size estimate and item placement below.
        int childCount = tree.ChildCount(node);
        List<GridItem> items = new(childCount);
        List<InBothAbsAxis<Line<GenericGridPlacement<OriginZeroLine>>>> placements = new(childCount);
        int childIndex = 0;
        foreach (NodeId childNode in tree.ChildIds(node))
        {
            int index = childIndex++;
            IGridItemStyle childStyle = tree.GetGridChildStyle(childNode);
            if (childStyle.BoxGenerationMode == BoxGenerationMode.None || childStyle.Position.IsOutOfFlow())
            {
                continue;
            }

            placements.Add(new InBothAbsAxis<Line<GenericGridPlacement<OriginZeroLine>>>(
                nameResolver.ResolveColumnNames(childStyle.GridColumn).IntoOriginZero(explicitColCount),
                nameResolver.ResolveRowNames(childStyle.GridRow).IntoOriginZero(explicitRowCount)));
            items.Add(GridItem.NewWithStyleAndOrder(childNode, childStyle, alignItems, justifyItems, (ushort)index));
        }

        // 4. Implicit Grid: Estimate Track Counts
        // Estimate the number of rows and columns in the implicit grid (= the entire grid)
        // This is necessary as part of placement. Doing it early here is a perf optimisation to reduce allocations.
        (TrackCounts estColCounts, TrackCounts estRowCounts) = ImplicitGrid.ComputeGridSizeEstimate(explicitColCount, explicitRowCount, placements);

        // 5. Grid Item Placement
        // Match items (children) to a definite grid position (row start/end and column start/end position)
        CellOccupancyMatrix cellOccupancyMatrix = CellOccupancyMatrix.WithTrackCounts(estColCounts, estRowCounts);

        // `items` is in document order from here on (placement only fills in each item's grid area). The track
        // sizing and baseline passes sort references to the items rather than the items themselves.
        Placement.PlaceGridItems(cellOccupancyMatrix, items, placements, style.GridAutoFlow);

        // Extract track counts from previous step (auto-placement can expand the number of tracks)
        TrackCounts finalColCounts = cellOccupancyMatrix.TrackCountsFor(AbsoluteAxis.Horizontal);
        TrackCounts finalRowCounts = cellOccupancyMatrix.TrackCountsFor(AbsoluteAxis.Vertical);

        // 5. Initialize Tracks
        // Initialize (explicit and implicit) grid tracks (and gutters)
        // This resolves the min and max track sizing functions for all tracks and gutters
        List<GridTrack> columns = new();
        List<GridTrack> rows = new();
        ExplicitGrid.InitializeGridTracks(
            columns,
            finalColCounts,
            style,
            AbsoluteAxis.Horizontal,
            colAutoRepetitionCount,
            columnIndex => cellOccupancyMatrix.ColumnIsOccupied(columnIndex));
        ExplicitGrid.InitializeGridTracks(
            rows,
            finalRowCounts,
            style,
            AbsoluteAxis.Vertical,
            rowAutoRepetitionCount,
            rowIndex => cellOccupancyMatrix.RowIsOccupied(rowIndex));

        // 6. Track Sizing

        // Convert grid placements in origin-zero coordinates to indexes into the GridTrack (rows and columns) vectors
        // This computation is relatively trivial, but it requires the final number of negative (implicit) tracks in
        // each axis, and doing it up-front here means we don't have to keep repeating that calculation
        TrackSizing.ResolveItemTrackIndexes(items, finalColCounts, finalRowCounts);

        // For each item, and in each axis, determine whether the item crosses any flexible (fr) tracks
        // Record this as a boolean (per-axis) on each item for later use in the track-sizing algorithm
        TrackSizing.DetermineIfItemCrossesFlexibleOrIntrinsicTracks(items, columns, rows);

        // Determine if the grid has any baseline aligned items
        bool hasBaselineAlignedItem = items.Any(item => item.ParticipatesInBaselineAlignment());

        // Run track sizing algorithm for Inline axis
        TrackSizing.TrackSizingAlgorithm(
            tree,
            AbstractAxis.Inline,
            innerMinSize.Get(AbstractAxis.Inline),
            innerMaxSize.Get(AbstractAxis.Inline),
            justifyContent,
            alignContent,
            availableGridSpace,
            innerNodeSize,
            columns,
            rows,
            items,
            (track, parentSize, t) => track.MaxTrackSizingFunction.DefiniteValue(parentSize, t.ResolveCalcValue),
            hasBaselineAlignedItem);
        float initialColumnSum = SumBaseSizes(columns);
        innerNodeSize.Width ??= initialColumnSum;

        // If only the container's width has been requested then we can skip sizing the rows entirely (which avoids
        // measuring the height of every item), as column sizing is not re-run when only computing the container's size.
        // Note: the short-circuit above has already handled the case where the container's width is known.
        if (runMode == RunMode.ComputeSize && inputs.Axis == RequestedAxis.Horizontal)
        {
            float width = MathHelpers.Max(
                (initialColumnSum + contentBoxInset.HorizontalAxisSum()).MaybeClamp(minSize.Width, maxSize.Width),
                paddingBorderSize.Width);
            return LayoutOutput.FromOuterSize(new Size<float>(width, 0f));
        }

        foreach (GridItem item in items)
        {
            item.GridAreaSizeCache = null;
        }

        // Run track sizing algorithm for Block axis
        TrackSizing.TrackSizingAlgorithm(
            tree,
            AbstractAxis.Block,
            innerMinSize.Get(AbstractAxis.Block),
            innerMaxSize.Get(AbstractAxis.Block),
            alignContent,
            justifyContent,
            availableGridSpace,
            innerNodeSize,
            rows,
            columns,
            items,
            (track, _, _) => track.BaseSize,
            false); // TODO: Support baseline alignment in the vertical axis
        float initialRowSum = SumBaseSizes(rows);

        // 6. Compute container size
        Size<float?> resolvedStyleSize = knownDimensions.Or(preferredSize);
        Size<float> containerBorderBox = new(
            MathHelpers.Max(
                (resolvedStyleSize.Get(AbstractAxis.Inline) ?? (initialColumnSum + contentBoxInset.HorizontalAxisSum()))
                    .MaybeClamp(minSize.Width, maxSize.Width),
                paddingBorderSize.Width),
            MathHelpers.Max(
                (resolvedStyleSize.Get(AbstractAxis.Block) ?? (initialRowSum + contentBoxInset.VerticalAxisSum()))
                    .MaybeClamp(minSize.Height, maxSize.Height),
                paddingBorderSize.Height));
        Size<float> containerContentBox = new(
            MathHelpers.Max(0f, containerBorderBox.Width - contentBoxInset.HorizontalAxisSum()),
            MathHelpers.Max(0f, containerBorderBox.Height - contentBoxInset.VerticalAxisSum()));

        // If only the container's size has been requested
        if (runMode == RunMode.ComputeSize)
        {
            return LayoutOutput.FromOuterSize(containerBorderBox);
        }

        // The container's size is now determined, so percentages resolve against its content box when re-running track sizing.
        // This may be a no-op if the container's size was known upfront (in which case it already equals `inner_node_size`).
        innerNodeSize = containerContentBox.AsOptions();

        // Column sizing must be re-run (once) if:
        //   - The grid container's width was initially indefinite and there are any columns with percentage track sizing functions
        //   - Any grid item crossing an intrinsically sized track's min content contribution width has changed
        // TODO: Only rerun sizing for tracks that actually require it rather than for all tracks if any need it.
        bool rerunColumnSizing;
        bool intrinsicColumnContributionChanged = false;

        bool hasPercentageColumn = columns.Any(track => track.UsesPercentage());
        bool hasPercentageRow = rows.Any(track => track.UsesPercentage());
        bool parentWidthIndefinite = !outerNodeSize.Width.HasValue;
        rerunColumnSizing = parentWidthIndefinite && hasPercentageColumn;

        if (!rerunColumnSizing)
        {
            // Note: every item must be visited (no short-circuiting) as the loop updates each item's caches
            foreach (GridItem item in items)
            {
                if (!item.CrossesIntrinsicColumn)
                {
                    continue;
                }

                Size<float?> gridAreaSize = item.GridAreaSize(
                    AbstractAxis.Inline,
                    columns,
                    rows,
                    innerNodeSize,
                    (track, _) => track.BaseSize,
                    calc);
                Size<float?> availableSpace = gridAreaSize.With(AbstractAxis.Inline, null);
                float newMinContentContribution = item.MinContentContribution(AbstractAxis.Inline, tree, gridAreaSize, availableSpace);
                bool hasChanged = item.MinContentContributionCache.Width != newMinContentContribution;

                item.GridAreaSizeCache = gridAreaSize;
                item.MinContentContributionCache.Width = newMinContentContribution;
                item.MaxContentContributionCache.Width = null;
                item.MinimumContributionCache.Width = null;

                intrinsicColumnContributionChanged |= hasChanged;
            }

            rerunColumnSizing = intrinsicColumnContributionChanged;
        }
        else
        {
            // Clear intrinsic width caches
            foreach (GridItem item in items)
            {
                item.GridAreaSizeCache = null;
                item.MinContentContributionCache.Width = null;
                item.MaxContentContributionCache.Width = null;
                item.MinimumContributionCache.Width = null;
            }
        }

        // Row sizing must be re-run (once) if:
        //   - The grid container's height was initially indefinite and there are any rows with percentage track sizing functions
        //   - Column sizing was re-run and any grid item crossing an intrinsically sized track's min content contribution height has changed
        // TODO: Only rerun sizing for tracks that actually require it rather than for all tracks if any need it.
        bool parentHeightIndefinite = !outerNodeSize.Height.HasValue;
        bool rerunRowSizing = parentHeightIndefinite && hasPercentageRow;
        bool intrinsicRowContributionChanged = false;
        if (rerunRowSizing)
        {
            // Clear intrinsic height caches
            foreach (GridItem item in items)
            {
                item.GridAreaSizeCache = null;
                item.MinContentContributionCache.Height = null;
                item.MaxContentContributionCache.Height = null;
                item.MinimumContributionCache.Height = null;
            }
        }

        if (rerunColumnSizing)
        {
            // Re-run track sizing algorithm for Inline axis
            TrackSizing.TrackSizingAlgorithm(
                tree,
                AbstractAxis.Inline,
                innerMinSize.Get(AbstractAxis.Inline),
                innerMaxSize.Get(AbstractAxis.Inline),
                justifyContent,
                alignContent,
                availableGridSpace,
                innerNodeSize,
                columns,
                rows,
                items,
                (track, _, _) => track.BaseSize,
                hasBaselineAlignedItem);

            // The column widths may have changed, so the items' intrinsic height contributions need to be recomputed
            // (unless row sizing is being re-run anyway, in which case the caches have already been cleared).
            if (!rerunRowSizing)
            {
                // Note: every item must be visited (no short-circuiting) as the loop updates each item's caches
                foreach (GridItem item in items)
                {
                    if (!item.CrossesIntrinsicRow)
                    {
                        continue;
                    }

                    Size<float?> gridAreaSize = item.GridAreaSize(
                        AbstractAxis.Block,
                        rows,
                        columns,
                        innerNodeSize,
                        (track, _) => track.BaseSize,
                        calc);
                    Size<float?> availableSpace = gridAreaSize.With(AbstractAxis.Block, null);
                    float newMinContentContribution = item.MinContentContribution(AbstractAxis.Block, tree, gridAreaSize, availableSpace);
                    bool hasChanged = item.MinContentContributionCache.Height != newMinContentContribution;

                    item.GridAreaSizeCache = gridAreaSize;
                    item.MinContentContributionCache.Height = newMinContentContribution;
                    item.MaxContentContributionCache.Height = null;
                    item.MinimumContributionCache.Height = null;

                    intrinsicRowContributionChanged |= hasChanged;
                }

                rerunRowSizing = intrinsicRowContributionChanged;
            }
        }

        if (rerunRowSizing)
        {
            // Re-run track sizing algorithm for Block axis
            TrackSizing.TrackSizingAlgorithm(
                tree,
                AbstractAxis.Block,
                innerMinSize.Get(AbstractAxis.Block),
                innerMaxSize.Get(AbstractAxis.Block),
                alignContent,
                justifyContent,
                availableGridSpace,
                innerNodeSize,
                rows,
                columns,
                items,
                (track, _, _) => track.BaseSize,
                false); // TODO: Support baseline alignment in the vertical axis
        }

        if ((intrinsicColumnContributionChanged && !hasPercentageColumn) || (intrinsicRowContributionChanged && !hasPercentageRow))
        {
            float finalColumnSum = SumBaseSizes(columns);
            float finalRowSum = SumBaseSizes(rows);
            if (intrinsicColumnContributionChanged && !hasPercentageColumn)
            {
                containerBorderBox.Width = MathHelpers.Max(
                    (resolvedStyleSize.Get(AbstractAxis.Inline) ?? (finalColumnSum + contentBoxInset.HorizontalAxisSum()))
                        .MaybeClamp(minSize.Width, maxSize.Width),
                    paddingBorderSize.Width);
                containerContentBox.Width = MathHelpers.Max(0f, containerBorderBox.Width - contentBoxInset.HorizontalAxisSum());
            }

            if (intrinsicRowContributionChanged && !hasPercentageRow)
            {
                containerBorderBox.Height = MathHelpers.Max(
                    (resolvedStyleSize.Get(AbstractAxis.Block) ?? (finalRowSum + contentBoxInset.VerticalAxisSum()))
                        .MaybeClamp(minSize.Height, maxSize.Height),
                    paddingBorderSize.Height);
                containerContentBox.Height = MathHelpers.Max(0f, containerBorderBox.Height - contentBoxInset.VerticalAxisSum());
            }
        }

        // If only the container's size has been requested
        if (runMode == RunMode.ComputeSize)
        {
            return LayoutOutput.FromOuterSize(containerBorderBox);
        }

        // 8. Track Alignment

        // Align columns
        float inlineSizeWithoutScrollbar = MathHelpers.Max(containerBorderBox.Width - paddingBorderSize.Width, 0f);
        float inlineScrollbarGutterForAlignment = MathHelpers.Min(scrollbarGutter.X, inlineSizeWithoutScrollbar);
        GridAlignment.AlignTracks(
            containerContentBox.Get(AbstractAxis.Inline),
            new Line<float>(
                padding.Left + (direction.IsRtl() ? inlineScrollbarGutterForAlignment : 0f),
                padding.Right + (direction.IsRtl() ? 0f : inlineScrollbarGutterForAlignment)),
            new Line<float>(border.Left, border.Right),
            columns,
            justifyContent,
            direction.IsRtl());

        // Align rows
        GridAlignment.AlignTracks(
            containerContentBox.Get(AbstractAxis.Block),
            new Line<float>(padding.Top, padding.Bottom),
            new Line<float>(border.Top, border.Bottom),
            rows,
            alignContent,
            false);

        // 9. Size, Align, and Position Grid Items
        Rect<float> itemOverflowRect = Rect.Zero;

        // Out-of-flow candidates in document order: direct out-of-flow children of the grid
        // interleaved with candidates bubbled from in-flow children's subtrees. These are laid out
        // by the out-of-flow positioning pass (`compute_oof_layout`), which runs after this algorithm.
        OofCandidates oofCandidates = OofCandidates.None;

        InBothAbsAxis<AlignItems> containerAlignmentStyles = new(justifyItems, alignItems);

        // Position in-flow children (stored in items vector)
        for (int index = 0; index < items.Count; index++)
        {
            GridItem item = items[index];

            // Tracks are stored in logical order. In RTL the physical offsets are assigned
            // right-to-left, so an item's physical left edge is derived from its logical end
            // line and its physical right edge from its logical start line.
            Rect<float> gridArea = new(
                direction.IsRtl() ? columns[item.ColumnIndexes.End - 1].Offset : columns[item.ColumnIndexes.Start + 1].Offset,
                direction.IsRtl() ? columns[item.ColumnIndexes.Start].Offset : columns[item.ColumnIndexes.End].Offset,
                rows[item.RowIndexes.Start + 1].Offset,
                rows[item.RowIndexes.End].Offset);

            (Rect<float> overflowContribution, float yPosition, float height, float? baseline) = GridAlignment.AlignAndPositionItem(
                tree,
                item.Node,
                (uint)index,
                gridArea,
                new Size<float>(gridArea.Right - gridArea.Left, gridArea.Bottom - gridArea.Top),
                containerAlignmentStyles,
                item.BaselineShim,
                direction,
                containerBorderBox.Width,
                border,
                isScrollContainer,
                ref item.OofCandidates);
            item.YPosition = yPosition;
            item.Height = height;
            item.Baseline = baseline;

            itemOverflowRect = itemOverflowRect.Union(overflowContribution);
        }

        // Position hidden and absolutely positioned children, merging in the candidates bubbled out
        // of in-flow children's subtrees (`items` is sorted by `source_order`, i.e. child index)
        uint order = (uint)items.Count;
        int nextInFlowItem = 0;
        int totalChildCount = tree.ChildCount(node);
        for (int index = 0; index < totalChildCount; index++)
        {
            if (nextInFlowItem < items.Count && items[nextInFlowItem].SourceOrder == index)
            {
                oofCandidates.Append(ref items[nextInFlowItem].OofCandidates);
                nextInFlowItem++;
                continue;
            }

            NodeId child = tree.GetChildId(node, index);
            IGridItemStyle childStyle = tree.GetGridChildStyle(child);

            // Position hidden child
            if (childStyle.BoxGenerationMode == BoxGenerationMode.None)
            {
                tree.SetUnroundedLayout(child, Layout.WithOrder(order));
                tree.PerformChildLayout(child, Size.None, Size.None, Size.MaxContent, SizingMode.InherentSize, Line.False);
                order++;
                continue;
            }

            // Emit out-of-flow children as candidates for the out-of-flow positioning pass
            // (`compute_oof_layout`), which lays out those for which this grid is the containing
            // block after this algorithm has run (resolving their grid areas from the detailed grid
            // info recorded below).
            if (childStyle.Position.IsOutOfFlow())
            {
                Position position = childStyle.Position;
                Direction itemDirection = childStyle.Direction;
                AlignItems justifySelf = (childStyle.JustifySelf ?? justifyItems).ResolveSelfRelative(itemDirection, direction, true);
                AlignItems alignSelf = (childStyle.AlignSelf ?? alignItems).ResolveSelfRelative(itemDirection, direction, false);

                // The static-position rectangle: the grid area determined by the grid-placement
                // properties when this grid is the child's containing block, and otherwise the
                // grid's content box, as if the child were the sole grid item in a grid area
                // coinciding with the grid's content edges (grid placement does not apply when the
                // grid is not the containing block).
                // https://www.w3.org/TR/css-grid-1/#static-position
                Rect<float> area = containingBlockClaims.ForPosition(position)
                    ? ResolveStaticPositionGridArea(
                        childStyle,
                        nameResolver,
                        finalRowCounts,
                        finalColCounts,
                        rows,
                        columns,
                        direction,
                        border,
                        scrollbarGutter,
                        containerBorderBox)
                    : new Rect<float>(
                        border.Left + padding.Left + (direction.IsRtl() ? scrollbarGutter.X : 0f),
                        containerBorderBox.Width - border.Right - padding.Right - (direction.IsRtl() ? 0f : scrollbarGutter.X),
                        border.Top + padding.Top,
                        containerBorderBox.Height - border.Bottom - padding.Bottom - scrollbarGutter.Y);

                oofCandidates.Push(new OofCandidate
                {
                    Node = child,
                    Order = order,
                    Position = position,
                    StaticPosition = new Point<AxisStaticPosition>(
                        AxisStaticPosition.FromAlignment(justifySelf, new Line<float>(area.Left, area.Right), direction.IsRtl()),
                        AxisStaticPosition.FromAlignment(alignSelf, new Line<float>(area.Top, area.Bottom), false)),
                });
                order++;
            }
        }

        Rect<float> absolutePositionInset = border.Add(new Rect<float>(
            direction.IsRtl() ? scrollbarGutter.X : 0f,
            direction.IsRtl() ? 0f : scrollbarGutter.X,
            0f,
            scrollbarGutter.Y));
        Size<float> absolutePositionArea = containerBorderBox.Sub(absolutePositionInset.SumAxes());
        Point<float> absolutePositionOffset = new(absolutePositionInset.Left, absolutePositionInset.Top);

        // Store the detailed grid info before the out-of-flow positioning pass so that the pass can
        // resolve the grid areas of out-of-flow boxes whose containing block is this grid
        GridAreasMap areas = nameResolver.PopulateDetailedLineResolvers(detailedRowLineNames, detailedColumnLineNames);
        tree.SetDetailedGridInfo(
            node,
            new DetailedGridInfo(
                DetailedGridTracksInfo.FromGridTracksAndTrackCount(finalRowCounts, rows, detailedRowLineNames),
                DetailedGridTracksInfo.FromGridTracksAndTrackCount(finalColCounts, columns, detailedColumnLineNames),
                items.Select(DetailedGridItemsInfo.FromGridItem).ToList(),
                areas));

        OofPositioningArea? oofPositioningArea = new OofPositioningArea(absolutePositionArea, absolutePositionOffset);

        // A scroll container's own padding at the end of the content is part of its scrollable
        // overflow region, so it is included in the in-flow overflow rect. Boxes that are not
        // scroll containers do not extend their overflow region by their own padding.
        Rect<float> scrollableOverflowRect = itemOverflowRect;
        if (isScrollContainer)
        {
            scrollableOverflowRect.Right += direction.IsRtl() ? padding.Left : padding.Right;
            scrollableOverflowRect.Bottom += padding.Bottom;
        }

        // If there are no in-flow items then return the container size and the overflow (no baseline)
        if (items.Count == 0)
        {
            LayoutOutput emptyOutput = LayoutOutput.FromSizes(containerBorderBox, scrollableOverflowRect);
            emptyOutput.OofCandidates = oofCandidates;
            emptyOutput.OofPositioningArea = oofPositioningArea;
            return emptyOutput;
        }

        // Determine the grid container baseline(s) (currently we only compute the first baseline)
        // Layout containment suppresses the box's baseline for baseline-alignment purposes
        float? gridContainerBaseline;
        if (contain.SuppressesBaseline())
        {
            gridContainerBaseline = null;
        }
        else
        {
            // Get the row index of the first row containing items
            ushort firstRow = items.Min(item => item.RowIndexes.Start);

            // Check if any items in *this row* participate in baseline alignment
            // (items with an auto block-axis margin do not participate: https://www.w3.org/TR/css-align-3/#baseline-align-self)
            List<GridItem> firstRowItems = items.Where(item => item.RowIndexes.Start == firstRow).ToList();
            GridItem firstItem = firstRowItems[0];
            GridItem baselineItem = firstItem.ParticipatesInBaselineAlignment()
                ? firstItem
                : firstRowItems.Skip(1).FirstOrDefault(item => item.ParticipatesInBaselineAlignment()) ?? firstItem;

            gridContainerBaseline = baselineItem.YPosition + (baselineItem.Baseline ?? baselineItem.Height);
        }

        LayoutOutput output = LayoutOutput.FromSizesAndBaselines(
            containerBorderBox,
            scrollableOverflowRect,
            Baselines.FromFirst(gridContainerBaseline));
        output.OofCandidates = oofCandidates;
        output.OofPositioningArea = oofPositioningArea;
        return output;
    }

    private static float SumBaseSizes(List<GridTrack> tracks)
    {
        float sum = 0f;
        foreach (GridTrack track in tracks)
        {
            sum += track.BaseSize;
        }

        return sum;
    }

    /// <summary>Resolve the static-position rectangle of an out-of-flow child whose containing block is this grid.</summary>
    private static Rect<float> ResolveStaticPositionGridArea(
        IGridItemStyle childStyle,
        NamedLineResolver nameResolver,
        TrackCounts finalRowCounts,
        TrackCounts finalColCounts,
        List<GridTrack> rows,
        List<GridTrack> columns,
        Direction direction,
        Rect<float> border,
        Point<float> scrollbarGutter,
        Size<float> containerBorderBox)
    {
        // Convert grid-col-{start/end} into Option's of indexes into the columns vector
        // The Option is None if the style property is Auto and an unresolvable Span
        Line<int?> maybeColIndexes = nameResolver
            .ResolveColumnNames(childStyle.GridColumn)
            .IntoOriginZero(finalColCounts.Explicit)
            .ResolveAbsolutelyPositionedGridTracks()
            .Map(maybeGridLine => maybeGridLine is OriginZeroLine line ? line.TryIntoTrackVecIndex(finalColCounts) : null);

        // Convert grid-row-{start/end} into Option's of indexes into the row vector
        // The Option is None if the style property is Auto and an unresolvable Span
        Line<int?> maybeRowIndexes = nameResolver
            .ResolveRowNames(childStyle.GridRow)
            .IntoOriginZero(finalRowCounts.Explicit)
            .ResolveAbsolutelyPositionedGridTracks()
            .Map(maybeGridLine => maybeGridLine is OriginZeroLine line ? line.TryIntoTrackVecIndex(finalRowCounts) : null);

        static float LineAsStartEdge(List<GridTrack> tracks, int index) =>
            (index + 1 < tracks.Count ? tracks[index + 1] : tracks[index]).Offset;

        static float LineAsEndEdge(List<GridTrack> tracks, int index) =>
            index == 0 ? (tracks.Count > 1 ? tracks[1] : tracks[0]).Offset : tracks[index].Offset;

        // In RTL, tracks remain in logical order but physical offsets are assigned
        // right-to-left: a line used as an inline-start edge resolves to the physical
        // *right* edge of the track that follows it (its gutter's offset), and a line
        // used as an inline-end edge resolves to the physical *left* edge (offset) of
        // the track that precedes it.
        static float RtlLineAsStartEdge(List<GridTrack> tracks, int index)
        {
            if (tracks.Count > index + 1)
            {
                // The gutter's offset is the physical right edge of the track that follows the line
                return tracks[index].Offset;
            }

            if (index == 0)
            {
                return tracks[0].Offset;
            }

            // No track follows the line: resolve to the line itself, which is the physical
            // left edge of the track that precedes it (the trailing gutter is assigned its
            // offset before any alignment offset is applied, so it cannot be used here)
            return tracks[index - 1].Offset;
        }

        static float RtlLineAsEndEdge(List<GridTrack> tracks, int index) =>
            index == 0 ? tracks[0].Offset : tracks[index - 1].Offset;

        // In RTL the item's physical left edge derives from its logical end line and its
        // physical right edge from its logical start line.
        float gridAreaLeft;
        float gridAreaRight;
        if (direction.IsRtl())
        {
            gridAreaLeft = maybeColIndexes.End is int endIndex ? RtlLineAsEndEdge(columns, endIndex) : border.Left + scrollbarGutter.X;
            gridAreaRight = maybeColIndexes.Start is int startIndex ? RtlLineAsStartEdge(columns, startIndex) : containerBorderBox.Width - border.Right;
        }
        else
        {
            gridAreaLeft = maybeColIndexes.Start is int startIndex ? LineAsStartEdge(columns, startIndex) : border.Left;
            gridAreaRight = maybeColIndexes.End is int endIndex
                ? LineAsEndEdge(columns, endIndex)
                : containerBorderBox.Width - border.Right - scrollbarGutter.X;
        }

        return new Rect<float>(
            gridAreaLeft,
            gridAreaRight,
            maybeRowIndexes.Start is int rowStart ? LineAsStartEdge(rows, rowStart) : border.Top,
            maybeRowIndexes.End is int rowEnd ? LineAsEndEdge(rows, rowEnd) : containerBorderBox.Height - border.Bottom - scrollbarGutter.Y);
    }
}

/// <summary>Information from the computation of grid.</summary>
public sealed class DetailedGridInfo
{
    private readonly GridAreasMap _areas;

    internal DetailedGridInfo(DetailedGridTracksInfo rows, DetailedGridTracksInfo columns, List<DetailedGridItemsInfo> items, GridAreasMap areas)
    {
        Rows = rows;
        Columns = columns;
        Items = items;
        _areas = areas;
    }

    /// <summary>The detailed info of the rows.</summary>
    public DetailedGridTracksInfo Rows { get; }

    /// <summary>The detailed info of the columns.</summary>
    public DetailedGridTracksInfo Columns { get; }

    /// <summary>The detailed info of the grid items (in-flow children, in document order).</summary>
    public List<DetailedGridItemsInfo> Items { get; }

    /// <summary>Write the resolved value of <c>grid-template-rows</c> (see <see cref="GridTemplateRows"/>).</summary>
    public void WriteGridTemplateRows(StringBuilder output, float zoom) => Rows.WriteTrackList(output, zoom);

    /// <summary>Write the resolved value of <c>grid-template-columns</c> (see <see cref="GridTemplateColumns"/>).</summary>
    public void WriteGridTemplateColumns(StringBuilder output, float zoom) => Columns.WriteTrackList(output, zoom);

    /// <summary>The resolved value of <c>grid-template-rows</c> (as returned by <c>getComputedStyle</c>), with sizes divided by <paramref name="zoom"/>.</summary>
    public string GridTemplateRows(float zoom) => Rows.ToTrackListString(zoom);

    /// <summary>The resolved value of <c>grid-template-columns</c> (as returned by <c>getComputedStyle</c>), with sizes divided by <paramref name="zoom"/>.</summary>
    public string GridTemplateColumns(float zoom) => Columns.ToTrackListString(zoom);

    /// <summary>
    /// Resolve the grid area of an out-of-flow box whose containing block is this grid, given its
    /// placement styles and the containing block's padding box (lines that are <c>auto</c> or fall
    /// outside the grid resolve to the padding edges).
    /// </summary>
    public Rect<float> ResolveAbsoluteGridArea(Line<GridPlacement> gridRow, Line<GridPlacement> gridColumn, Direction direction, Rect<float> paddingBox)
    {
        Line<float> columns = Columns.ResolveAbsoluteGridAxis(gridColumn, _areas, GridAreaAxis.Column, paddingBox.Left, paddingBox.Right, direction.IsRtl());
        Line<float> rows = Rows.ResolveAbsoluteGridAxis(gridRow, _areas, GridAreaAxis.Row, paddingBox.Top, paddingBox.Bottom, false);
        return new Rect<float>(columns.Start, columns.End, rows.Start, rows.End);
    }

    /// <summary>The position and size of the grid area of the item with the passed index, or <c>null</c> if there is no such item.</summary>
    public (Point<float> Location, Size<float> Size)? ItemGridArea(int itemIndex)
    {
        if (itemIndex < 0 || itemIndex >= Items.Count)
        {
            return null;
        }

        DetailedGridItemsInfo item = Items[itemIndex];
        Line<float> startCol = Columns.Positions[item.ColumnStart - 1];
        Line<float> endCol = Columns.Positions[item.ColumnEnd - 2];
        float left = MathHelpers.Min(startCol.Start, endCol.Start);
        float right = MathHelpers.Max(startCol.End, endCol.End);
        float top = Rows.Positions[item.RowStart - 1].Start;
        float bottom = Rows.Positions[item.RowEnd - 2].End;
        return (new Point<float>(left, top), new Size<float>(right - left, bottom - top));
    }
}

/// <summary>Information from the computation of grid tracks in a single axis.</summary>
public sealed class DetailedGridTracksInfo
{
    /// <summary>Number of leading implicit grid tracks.</summary>
    public ushort NegativeImplicitTracks { get; }

    /// <summary>Number of explicit grid tracks.</summary>
    public ushort ExplicitTracks { get; }

    /// <summary>Number of trailing implicit grid tracks.</summary>
    public ushort PositiveImplicitTracks { get; }

    /// <summary>The start and end position of each track (gutters excluded).</summary>
    public List<Line<float>> Positions { get; }

    /// <summary>Runs of adjacent collapsed tracks, as (start-inclusive, end-exclusive) indexes into <see cref="Positions"/>.</summary>
    public List<(int Start, int End)> CollapsedTracks { get; }

    /// <summary>For an axis with no tracks, the position of its single grid line.</summary>
    public float? EmptyAxisLine { get; }

    /// <summary>The names of the explicit grid lines.</summary>
    public GridLineNames LineNames { get; }

    private DetailedGridTracksInfo(
        ushort negativeImplicitTracks,
        ushort explicitTracks,
        ushort positiveImplicitTracks,
        List<Line<float>> positions,
        List<(int Start, int End)> collapsedTracks,
        float? emptyAxisLine,
        GridLineNames lineNames)
    {
        NegativeImplicitTracks = negativeImplicitTracks;
        ExplicitTracks = explicitTracks;
        PositiveImplicitTracks = positiveImplicitTracks;
        Positions = positions;
        CollapsedTracks = collapsedTracks;
        EmptyAxisLine = emptyAxisLine;
        LineNames = lineNames;
    }

    internal Line<float> ResolveAbsoluteGridAxis(
        Line<GridPlacement> placement,
        GridAreasMap areas,
        GridAreaAxis axis,
        float paddingStart,
        float paddingEnd,
        bool isReversed)
    {
        TrackCounts trackCounts = new(NegativeImplicitTracks, ExplicitTracks, PositiveImplicitTracks);
        short minLine = (short)-trackCounts.NegativeImplicit;
        short maxLine = (short)(trackCounts.Explicit + trackCounts.PositiveImplicit);

        // Resolve the placement to a pair of (sorted) lines first, and only then treat lines
        // that fall outside the grid as `auto`. A placement whose lines end up swapped (e.g.
        // `grid-column: 3 / 1`, or a non-existent named line which resolves past the end of the
        // explicit grid) is normalized before the out-of-grid check, so e.g. `foo / 3` on a
        // 2-column grid resolves to the area between line 3 and the end padding edge.
        Line<int?> resolved = LineNames
            .ResolveLineNames(placement, areas, axis, ExplicitTracks)
            .IntoOriginZero(ExplicitTracks)
            .ResolveAbsolutelyPositionedGridTracks()
            .Map(line => line is OriginZeroLine l && l.Value >= minLine && l.Value <= maxLine
                ? l.TryIntoTrackVecIndex(trackCounts) / 2
                : null);

        float startPosition;
        if (resolved.Start is int startLine)
        {
            float? position = startLine < Positions.Count
                ? AbsoluteTrackEdge(startLine, true, isReversed)
                : Positions.Count > 0
                    ? (isReversed ? Positions[Positions.Count - 1].Start : Positions[Positions.Count - 1].End)
                    : EmptyAxisLine;
            startPosition = position ?? (isReversed ? paddingEnd : paddingStart);
        }
        else
        {
            startPosition = isReversed ? paddingEnd : paddingStart;
        }

        float endPosition;
        if (resolved.End is int endLine)
        {
            float? position = endLine >= 1 && endLine - 1 < Positions.Count
                ? AbsoluteTrackEdge(endLine - 1, false, isReversed)
                : Positions.Count > 0
                    ? (isReversed ? Positions[0].End : Positions[0].Start)
                    : EmptyAxisLine;
            endPosition = position ?? (isReversed ? paddingStart : paddingEnd);
        }
        else
        {
            endPosition = isReversed ? paddingStart : paddingEnd;
        }

        // An area whose lines all fall within a collapsed run is zero-sized and anchored at its
        // logical start edge (as in Chrome; Gecko anchors it before the shared gutter instead).
        return isReversed
            ? new Line<float>(MathHelpers.Min(startPosition, endPosition), startPosition)
            : new Line<float>(startPosition, MathHelpers.Max(startPosition, endPosition));
    }

    private float AbsoluteTrackEdge(int index, bool isStart, bool isReversed)
    {
        // First collapsed run whose end is after the index (the runs are sorted and disjoint)
        int rangeIndex = 0;
        while (rangeIndex < CollapsedTracks.Count && CollapsedTracks[rangeIndex].End <= index)
        {
            rangeIndex++;
        }

        Line<float> track = Positions[index];
        if (rangeIndex < CollapsedTracks.Count && CollapsedTracks[rangeIndex].Start <= index && index < CollapsedTracks[rangeIndex].End)
        {
            (int start, int end) = CollapsedTracks[rangeIndex];
            int? adjacent = isStart ? end : (start >= 1 ? start - 1 : null);
            if (adjacent is int adjacentIndex && adjacentIndex < Positions.Count)
            {
                track = Positions[adjacentIndex];
            }
        }

        // Collapsed gutters overlap: a start edge follows the shared gutter, while an end
        // edge precedes it.
        return isStart != isReversed ? track.Start : track.End;
    }

    private static (List<Line<float>> Positions, List<(int Start, int End)> CollapsedTracks) PositionsFromGridTrackLayout(List<GridTrack> gridTracks)
    {
        List<Line<float>> positions = new(gridTracks.Count / 2);
        List<(int Start, int End)> collapsedTracks = new();
        foreach (GridTrack track in gridTracks)
        {
            if (track.Kind != GridTrackKind.Track)
            {
                continue;
            }

            if (track.IsCollapsed)
            {
                int index = positions.Count;
                if (collapsedTracks.Count > 0 && collapsedTracks[collapsedTracks.Count - 1].End == index)
                {
                    (int start, int end) = collapsedTracks[collapsedTracks.Count - 1];
                    collapsedTracks[collapsedTracks.Count - 1] = (start, end + 1);
                }
                else
                {
                    collapsedTracks.Add((index, index + 1));
                }
            }

            positions.Add(new Line<float>(track.Offset, track.Offset + track.BaseSize));
        }

        return (positions, collapsedTracks);
    }

    internal static DetailedGridTracksInfo FromGridTracksAndTrackCount(TrackCounts trackCount, List<GridTrack> gridTracks, GridLineNames lineNames)
    {
        (List<Line<float>> positions, List<(int Start, int End)> collapsedTracks) = PositionsFromGridTrackLayout(gridTracks);

        // An axis with no tracks consists of a single gutter whose offset is where the axis'
        // single grid line was positioned by content alignment
        float? emptyAxisLine = positions.Count == 0 && gridTracks.Count > 0 ? gridTracks[0].Offset : null;
        return new DetailedGridTracksInfo(
            trackCount.NegativeImplicit,
            trackCount.Explicit,
            trackCount.PositiveImplicit,
            positions,
            collapsedTracks,
            emptyAxisLine,
            lineNames);
    }

    /// <summary>The names of the line with the passed 0-indexed line index (counting from the start of the implicit grid).</summary>
    public IReadOnlyList<string> NamesForLine(int lineIndex) =>
        lineIndex >= NegativeImplicitTracks ? LineNames.Line(lineIndex - NegativeImplicitTracks) : Array.Empty<string>();

    /// <summary>Iterate over the names of every line of the grid (including the implicit grid lines, which are unnamed).</summary>
    public GridLineNamesIter IterLineNames()
    {
        if (LineNames.IsEmpty())
        {
            return LineNames.Iter();
        }

        int totalLineCount = Positions.Count + 1;
        int leadingEmpty = NegativeImplicitTracks;
        int trailingEmpty = Math.Max(totalLineCount - (leadingEmpty + LineNames.LineCount()), 0);
        return LineNames.IterPadded(leadingEmpty, trailingEmpty);
    }

    /// <summary>Write the resolved track list (as returned by <c>getComputedStyle</c>), with sizes divided by <paramref name="zoom"/>.</summary>
    public void WriteTrackList(StringBuilder output, float zoom)
    {
        static void WriteLineNames(StringBuilder output, IReadOnlyList<string> names)
        {
            output.Append('[');
            for (int i = 0; i < names.Count; i++)
            {
                if (i != 0)
                {
                    output.Append(' ');
                }

                output.Append(names[i]);
            }

            output.Append(']');
        }

        if (Positions.Count == 0)
        {
            output.Append("none");
            return;
        }

        bool needsSpace = false;
        for (int trackIndex = 0; trackIndex < Positions.Count; trackIndex++)
        {
            Line<float> position = Positions[trackIndex];
            IReadOnlyList<string> names = NamesForLine(trackIndex);
            if (names.Count > 0)
            {
                if (needsSpace)
                {
                    output.Append(' ');
                }

                WriteLineNames(output, names);
                needsSpace = true;
            }

            if (needsSpace)
            {
                output.Append(' ');
            }

            output.Append(FormatF32((position.End - position.Start) / zoom)).Append("px");
            needsSpace = true;
        }

        IReadOnlyList<string> trailingNames = NamesForLine(Positions.Count);
        if (trailingNames.Count > 0)
        {
            output.Append(' ');
            WriteLineNames(output, trailingNames);
        }
    }

    /// <summary>The resolved track list as a string (see <see cref="WriteTrackList"/>).</summary>
    public string ToTrackListString(float zoom)
    {
        StringBuilder output = new();
        WriteTrackList(output, zoom);
        return output.ToString();
    }

    /// <summary>Format a float like Rust's <c>Display</c> for <c>f32</c>: the shortest round-trip digits, never in exponent notation.</summary>
    private static string FormatF32(float value)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        int exponentIndex = text.IndexOf('E');
        if (exponentIndex < 0)
        {
            return text;
        }

        bool negative = text[0] == '-';
        string mantissa = text.Substring(negative ? 1 : 0, exponentIndex - (negative ? 1 : 0));
        int exponent = int.Parse(text.Substring(exponentIndex + 1), CultureInfo.InvariantCulture);
        int pointIndex = mantissa.IndexOf('.');
        string digits = pointIndex < 0 ? mantissa : mantissa.Remove(pointIndex, 1);
        int integerDigits = (pointIndex < 0 ? mantissa.Length : pointIndex) + exponent;
        string result = integerDigits <= 0
            ? "0." + new string('0', -integerDigits) + digits
            : integerDigits >= digits.Length
                ? digits + new string('0', integerDigits - digits.Length)
                : digits.Substring(0, integerDigits) + "." + digits.Substring(integerDigits);
        return negative ? "-" + result : result;
    }
}

/// <summary>Grid area information from the placement algorithm. The values are 1-indexed grid line numbers bounding the area.</summary>
public sealed class DetailedGridItemsInfo
{
    private DetailedGridItemsInfo(ushort rowStart, ushort rowEnd, ushort columnStart, ushort columnEnd)
    {
        RowStart = rowStart;
        RowEnd = rowEnd;
        ColumnStart = columnStart;
        ColumnEnd = columnEnd;
    }

    /// <summary>Row-start with 1-indexed grid line numbers.</summary>
    public ushort RowStart { get; }

    /// <summary>Row-end with 1-indexed grid line numbers.</summary>
    public ushort RowEnd { get; }

    /// <summary>Column-start with 1-indexed grid line numbers.</summary>
    public ushort ColumnStart { get; }

    /// <summary>Column-end with 1-indexed grid line numbers.</summary>
    public ushort ColumnEnd { get; }

    internal static DetailedGridItemsInfo FromGridItem(GridItem gridItem)
    {
        static ushort ToOneIndexedGridLine(ushort gridTrackIndex) => (ushort)((gridTrackIndex / 2) + 1);

        return new DetailedGridItemsInfo(
            ToOneIndexedGridLine(gridItem.RowIndexes.Start),
            ToOneIndexedGridLine(gridItem.RowIndexes.End),
            ToOneIndexedGridLine(gridItem.ColumnIndexes.Start),
            ToOneIndexedGridLine(gridItem.ColumnIndexes.End));
    }
}
