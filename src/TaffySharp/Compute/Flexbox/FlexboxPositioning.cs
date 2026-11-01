namespace TaffySharp;

internal static partial class FlexboxAlgorithm
{
    /// <summary>Align all flex lines per align-content.</summary>
    private static void AlignFlexLinesPerAlignContent(List<FlexLine> flexLines, AlgoConstants constants, float totalCrossSize)
    {
        int numLines = flexLines.Count;
        float gap = constants.Gap.Cross(constants.Dir);
        float totalCrossAxisGap = SumAxisGaps(gap, numLines);
        float freeSpace = constants.InnerContainerSize.Cross(constants.Dir) - totalCrossSize - totalCrossAxisGap;
        AlignContentKeyword alignContentMode = CommonAlignment.ApplyAlignmentFallback(freeSpace, numLines, constants.AlignContent);

        for (int i = 0; i < numLines; i++)
        {
            FlexLine line = constants.IsWrapReverse ? flexLines[numLines - 1 - i] : flexLines[i];
            line.OffsetCross = CommonAlignment.ComputeAlignmentOffset(
                freeSpace, numLines, gap, alignContentMode, constants.IsWrapReverse, i == 0);
        }
    }

    /// <summary>Calculates the layout for a flex-item.</summary>
    private static void CalculateFlexItem(
        ILayoutFlexboxContainer tree,
        FlexItem item,
        ref float totalOffsetMain,
        float totalOffsetCross,
        float lineOffsetCross,
        ref Rect<float> totalOverflowRect,
        Rect<float> border,
        AlgoConstants constants)
    {
        Size<float> containerSize = constants.ContainerSize;
        Size<float?> nodeInnerSize = constants.NodeInnerSize;
        FlexDirection direction = constants.Dir;
        Direction layoutDirection = constants.LayoutDirection;
        Size<bool> itemKnownDimensionDefiniteness = ItemKnownDimensionDefiniteness(constants, item);

        LayoutOutput layoutOutput = tree.ComputeChildLayout(
            item.Node,
            new LayoutInput
            {
                RunMode = RunMode.PerformLayout,
                SizingMode = SizingMode.ContentSize,
                Axis = RequestedAxis.Both,
                KnownDimensions = item.TargetSize.AsOptions(),
                KnownDimensionsAreDefinite = itemKnownDimensionDefiniteness,
                ParentSize = nodeInnerSize,
                // The available space passed to the child excludes the child's margins
                AvailableSpace = containerSize.Sub(item.Margin.SumAxes()).Map(s => AvailableSpace.Definite(MathHelpers.Max(s, 0f))),
                VerticalMarginsAreCollapsible = Line.False,
            });
        Size<float> size = layoutOutput.Size;
        Rect<float> scrollableOverflowRect = layoutOutput.ScrollableOverflowRect;

        bool isRtlRow = direction.IsRow() && layoutDirection.IsRtl();
        bool isRtlColumn = direction.IsColumn() && layoutDirection.IsRtl();
        float mainRelativeInset = item.RelativeInset.Main(direction);
        float crossRelativeInset = item.RelativeInset.Cross(direction);
        float effectiveLineOffsetCross = isRtlColumn ? 0f : lineOffsetCross;

        float offsetMain = isRtlRow
            ? totalOffsetMain - item.OffsetMain - item.Margin.MainEnd(direction) - mainRelativeInset - size.Width
            : totalOffsetMain + item.OffsetMain + item.Margin.MainStart(direction) + mainRelativeInset;
        float offsetCross = totalOffsetCross
            + item.OffsetCross
            + effectiveLineOffsetCross
            + item.Margin.CrossStart(direction)
            + crossRelativeInset;

        float innerBaseline = CommonAlignment.ChildFirstBaseline(layoutOutput.Baselines.First, size.Height, item.Overflow.Y)
            ?? size.Height;
        if (direction.IsRow())
        {
            float baselineOffsetCross = totalOffsetCross + item.OffsetCross + effectiveLineOffsetCross + item.Margin.CrossStart(direction);
            item.Baseline = baselineOffsetCross + innerBaseline;
        }
        else
        {
            float baselineOffsetMain = totalOffsetMain + item.OffsetMain + item.Margin.MainStart(direction);
            item.Baseline = baselineOffsetMain + innerBaseline;
        }

        Point<float> location = direction.IsRow()
            ? new Point<float>(offsetMain, offsetCross)
            : new Point<float>(offsetCross, offsetMain);
        Size<float> scrollbarSize = new(
            item.Overflow.Y == Overflow.Scroll ? item.ScrollbarWidth : 0f,
            item.Overflow.X == Overflow.Scroll ? item.ScrollbarWidth : 0f);

        tree.SetUnroundedLayout(
            item.Node,
            new Layout
            {
                Order = item.Order,
                Size = size,
                ScrollableOverflowRect = scrollableOverflowRect,
                ScrollbarSize = scrollbarSize,
                Location = location,
                Padding = item.Padding,
                Border = item.Border,
                Margin = item.Margin,
            });

        // Keep out-of-flow candidates from the item's subtree, translating anchors from
        // item-relative to container-relative coordinates
        item.OofCandidates = layoutOutput.OofCandidates.Take();
        if (!item.OofCandidates.IsEmpty)
        {
            item.OofCandidates.Translate(location);
        }

        if (isRtlRow)
        {
            totalOffsetMain -= item.OffsetMain + item.Margin.MainAxisSum(direction) + size.Main(direction);
        }
        else
        {
            totalOffsetMain += item.OffsetMain + item.Margin.MainAxisSum(direction) + size.Main(direction);
        }

        Point<float> contributionLocation = layoutDirection.IsRtl()
            ? new Point<float>(containerSize.Width - (location.X + size.Width) - border.Right, location.Y - border.Top)
            : new Point<float>(location.X - border.Left, location.Y - border.Top);
        totalOverflowRect = totalOverflowRect.Union(ScrollableOverflow.ComputeScrollableOverflowContribution(
            contributionLocation,
            size,
            scrollableOverflowRect,
            item.Overflow,
            item.Contain,
            constants.IsScrollContainer));
    }

    /// <summary>Calculates the layout of a line of flex items.</summary>
    private static void CalculateLayoutLine(
        ILayoutFlexboxContainer tree,
        FlexLine line,
        ref float totalOffsetCross,
        ref Rect<float> overflowRect,
        Rect<float> border,
        AlgoConstants constants)
    {
        Size<float> containerSize = constants.ContainerSize;
        Rect<float> paddingBorder = constants.ContentBoxInset;
        FlexDirection direction = constants.Dir;
        Direction layoutDirection = constants.LayoutDirection;

        float totalOffsetMain = layoutDirection.IsRtl() && direction.IsRow()
            ? containerSize.Width - paddingBorder.MainEnd(direction)
            : paddingBorder.MainStart(direction);
        float lineOffsetCross = line.OffsetCross;

        bool isRtlColumn = layoutDirection.IsRtl() && direction.IsColumn();
        if (isRtlColumn)
        {
            totalOffsetCross -= lineOffsetCross + line.CrossSize;
        }

        int count = line.Items.Count;
        bool reverse = direction.IsReverse();
        for (int i = 0; i < count; i++)
        {
            FlexItem item = reverse ? line.Items[count - 1 - i] : line.Items[i];
            CalculateFlexItem(tree, item, ref totalOffsetMain, totalOffsetCross, lineOffsetCross, ref overflowRect, border, constants);
        }

        if (!isRtlColumn)
        {
            totalOffsetCross += lineOffsetCross + line.CrossSize;
        }
    }

    /// <summary>Do a final layout pass and collect the resulting layouts.</summary>
    private static Rect<float> FinalLayoutPass(ILayoutFlexboxContainer tree, List<FlexLine> flexLines, AlgoConstants constants)
    {
        float totalOffsetCross = constants.IsColumn && constants.LayoutDirection.IsRtl()
            ? constants.ContainerSize.Width - constants.ContentBoxInset.CrossEnd(constants.Dir)
            : constants.ContentBoxInset.CrossStart(constants.Dir);

        Rect<float> overflowRect = Rect.Zero;

        int numLines = flexLines.Count;
        for (int i = 0; i < numLines; i++)
        {
            FlexLine line = constants.IsWrapReverse ? flexLines[numLines - 1 - i] : flexLines[i];
            CalculateLayoutLine(tree, line, ref totalOffsetCross, ref overflowRect, constants.Border, constants);
        }

        // A scroll container's own padding at the end of the content is part of its scrollable
        // overflow region, so it is included in the overflow rect. Boxes that are not scroll
        // containers do not extend their overflow region by their own padding.
        if (constants.IsScrollContainer)
        {
            overflowRect.Right += constants.LayoutDirection.IsRtl()
                ? constants.ContentBoxInset.Left - constants.Border.Left - constants.ScrollbarGutter.X
                : constants.ContentBoxInset.Right - constants.Border.Right - constants.ScrollbarGutter.X;
            overflowRect.Bottom += constants.ContentBoxInset.Bottom - constants.Border.Bottom - constants.ScrollbarGutter.Y;
        }

        return overflowRect;
    }

    /// <summary>
    /// Collect the out-of-flow candidates of the container in document order: its direct out-of-flow
    /// children (with their static positions) interleaved with the candidates bubbled up from the
    /// subtrees of its in-flow items.
    /// </summary>
    private static void CollectOofCandidates(
        ILayoutFlexboxContainer tree,
        NodeId node,
        AlgoConstants constants,
        List<FlexLine> flexLines,
        ref OofCandidates candidates)
    {
        // Lines are contiguous slices of the items in document order (reversal is applied to
        // positions, not storage), so walking the lines yields items sorted by `order`
        List<FlexItem> items = new();
        foreach (FlexLine line in flexLines)
        {
            foreach (FlexItem item in line.Items)
            {
                if (!item.OofCandidates.IsEmpty)
                {
                    items.Add(item);
                }
            }
        }

        int nextItem = 0;

        FlexDirection dir = constants.Dir;
        Size<float> containerSize = constants.ContainerSize;
        Rect<float> contentBoxInset = constants.ContentBoxInset;
        bool mainAxisIsHorizontal = constants.IsRow;
        bool crossAxisIsHorizontal = !constants.IsRow;
        bool mainIsRtl = mainAxisIsHorizontal && constants.LayoutDirection.IsRtl();
        bool crossIsRtl = crossAxisIsHorizontal && constants.LayoutDirection.IsRtl();
        bool mainAxisFlexStartReversed = dir.IsReverse() ^ mainIsRtl;
        bool crossAxisFlexStartReversed = constants.IsWrapReverse ^ crossIsRtl;

        // The static-position area in each axis is the container's content box
        Line<float> mainArea = new(
            contentBoxInset.MainStart(dir),
            containerSize.Main(dir) - contentBoxInset.MainEnd(dir));
        Line<float> crossArea = new(
            contentBoxInset.CrossStart(dir),
            containerSize.Cross(dir) - contentBoxInset.CrossEnd(dir));

        // Cross-axis static position (align-self).
        //
        // `start`/`end` (and `baseline`, whose static-position fallback is `start`) are
        // writing-mode relative: they flip for RTL but not for `wrap-reverse`.
        // `flex-start`/`flex-end` and the `stretch` fallback are flex-relative.
        AxisStaticEdge CrossEdgeFor(AlignItemsKeyword keyword)
        {
            bool startPosition = keyword switch
            {
                AlignItemsKeyword.Start or AlignItemsKeyword.Baseline => !crossIsRtl,
                AlignItemsKeyword.End => crossIsRtl,
                _ => true,
            };
            switch (keyword)
            {
                // Stretch alignment does not apply to absolutely positioned items
                // See "Example 3" at https://www.w3.org/TR/css-flexbox-1/#abspos-items
                // Note: Stretch should be FlexStart not Start when we support both
                case AlignItemsKeyword.Start:
                case AlignItemsKeyword.End:
                case AlignItemsKeyword.Baseline:
                    return startPosition ? AxisStaticEdge.Start : AxisStaticEdge.End;
                case AlignItemsKeyword.Stretch:
                case AlignItemsKeyword.FlexStart:
                    return crossAxisFlexStartReversed ? AxisStaticEdge.End : AxisStaticEdge.Start;
                case AlignItemsKeyword.FlexEnd:
                    return crossAxisFlexStartReversed ? AxisStaticEdge.Start : AxisStaticEdge.End;
                case AlignItemsKeyword.Center:
                    return AxisStaticEdge.Center;
                default:
                    // Normal is resolved to Stretch, and SelfStart/SelfEnd are resolved to Start/End
                    // against the item's own direction where `align_self` is read below.
                    throw new InvalidOperationException("align-self must be resolved before computing the static position");
            }
        }

        int childCount = tree.ChildCount(node);
        for (int order = 0; order < childCount; order++)
        {
            NodeId child = tree.GetChildId(node, order);
            IFlexboxItemStyle childStyle = tree.GetFlexboxChildStyle(child);
            Position position = childStyle.Position;
            if (childStyle.BoxGenerationMode == BoxGenerationMode.None)
            {
                continue;
            }

            // In-flow item: merge the candidates bubbled out of its subtree
            if (!position.IsOutOfFlow())
            {
                if (nextItem < items.Count && items[nextItem].Order == (uint)order)
                {
                    candidates.Append(ref items[nextItem].OofCandidates);
                    nextItem++;
                }

                continue;
            }

            AlignItems alignSelf = ResolveNormalAlignment(childStyle.AlignSelf ?? constants.AlignItems)
                .ResolveSelfRelative(childStyle.Direction, constants.LayoutDirection, constants.IsColumn);

            // Main-axis static position (justify-content).
            //
            // Stretch is an invalid value for justify_content in the flexbox algorithm, so we
            // treat it as if it wasn't set (and thus we default to FlexStart behaviour).
            // Normal has already been resolved to FlexStart.
            //
            // The `safe` overflow-position keyword is intentionally NOT applied here, even when
            // the abs-positioned item would overflow the main axis: Chrome does not apply safe
            // fallback to `justify-content` on absolutely-positioned flex items (only the
            // cross-axis `align-self` does so).
            //
            // `start`/`end` are writing-mode relative (they flip for RTL but not for
            // reversed flex-directions), whereas `flex-start`/`flex-end` and the
            // distributed keywords' fallbacks are flex-relative.
            AlignContentKeyword mainKeyword = constants.JustifyContent.Keyword;
            bool mainStartPosition = mainKeyword switch
            {
                AlignContentKeyword.Start => !mainIsRtl,
                AlignContentKeyword.End => mainIsRtl,
                _ => true,
            };
            AxisStaticEdge mainEdge = mainKeyword switch
            {
                AlignContentKeyword.SpaceBetween or AlignContentKeyword.Normal or AlignContentKeyword.Stretch or AlignContentKeyword.FlexStart =>
                    mainAxisFlexStartReversed ? AxisStaticEdge.End : AxisStaticEdge.Start,
                AlignContentKeyword.FlexEnd => mainAxisFlexStartReversed ? AxisStaticEdge.Start : AxisStaticEdge.End,
                AlignContentKeyword.Start or AlignContentKeyword.End => mainStartPosition ? AxisStaticEdge.Start : AxisStaticEdge.End,
                _ => AxisStaticEdge.Center,
            };

            AxisStaticPosition cross = new(
                crossArea,
                new AxisStaticAlign(
                    CrossEdgeFor(alignSelf.Keyword),
                    alignSelf.Safety,
                    CrossEdgeFor(CommonAlignment.ResolveSelfAlignmentSafety(alignSelf, true))));
            AxisStaticPosition main = new(mainArea, AxisStaticAlign.FromKeyword(mainEdge));
            Point<AxisStaticPosition> staticPosition = constants.IsRow
                ? new Point<AxisStaticPosition>(main, cross)
                : new Point<AxisStaticPosition>(cross, main);

            candidates.Push(new OofCandidate
            {
                Node = child,
                Order = (uint)order,
                Position = position,
                StaticPosition = staticPosition,
            });
        }
    }
}
