namespace TaffySharp;

/// <summary>The result of <see cref="Compute.ComputeOofLayoutForArea"/>.</summary>
public sealed class OofLayoutResult
{
    /// <summary>The out-of-flow boxes that were laid out (for which the node is the containing block).</summary>
    public List<NodeId> Hoisted { get; } = new();

    /// <summary>The candidates that were not claimed and must bubble up to an ancestor.</summary>
    public OofCandidates Unclaimed;

    /// <summary>The scrollable overflow contributed by the laid out boxes.</summary>
    public Rect<float> ScrollableOverflowRect;
}

public static partial class Compute
{
    /// <summary>
    /// Updates the stored layout of the provided root node and its children: lays the root out in the
    /// initial containing block (the available space) and positions the out-of-flow boxes that have no
    /// nearer containing block.
    /// </summary>
    public static void ComputeRootLayout<TTree>(TTree tree, NodeId root, Size<AvailableSpace> availableSpace)
        where TTree : ILayoutContainingBlock, ICacheTree
    {
        ICoreStyle style = tree.GetCoreContainerStyle(root);
        Position position = style.Position;
        Direction direction = style.Direction;

        Size<float?> icbSize = availableSpace.IntoOptions();
        Size<float>? oofRootArea = position.IsOutOfFlow() && icbSize.Width is float w && icbSize.Height is float h
            ? new Size<float>(w, h)
            : null;

        // When the root's layout is served from the cache its layout algorithm does not run, so the
        // hoisted children it recorded on a previous run (including those added by the root
        // positioning pass below) are still in place and must not be re-added.
        bool rootIsCached = false;
        Layout layout;
        OofCandidates candidates;
        if (oofRootArea is Size<float> areaSize)
        {
            // The static position of the root places its margin box at the origin of the ICB
            float anchor = direction.IsRtl() ? areaSize.Width : 0f;
            OofCandidate candidate = new()
            {
                Node = root,
                Order = 0,
                Position = position,
                StaticPosition = new Point<AxisStaticPosition>(
                    new AxisStaticPosition(
                        new Line<float>(anchor, anchor),
                        AxisStaticAlign.FromKeyword(direction.IsRtl() ? AxisStaticEdge.End : AxisStaticEdge.Start)),
                    AxisStaticPosition.FromEdge(0f, AxisStaticEdge.Start)),
            };
            OofBoxLayout boxLayout = LayoutOofBox(
                tree,
                candidate,
                areaSize,
                Point.Zero,
                direction,
                // The initial containing block is not a scroll container
                new Point<Overflow>(Overflow.Visible, Overflow.Visible),
                null,
                inputs => rootIsCached = tree.CacheGet(root, inputs).HasValue);
            layout = boxLayout.Layout;
            candidates = boxLayout.Surfaced;
        }
        else
        {
            (layout, candidates) = ComputeInFlowRootLayout(tree, root, availableSpace, ref rootIsCached);
        }

        // Final positioning pass for out-of-flow boxes with no nearer containing block: the initial
        // containing block is the containing block for position: fixed boxes and for
        // position: absolute boxes with no positioned ancestor.
        if (!candidates.IsEmpty)
        {
            Point<Overflow> overflow = tree.GetCoreContainerStyle(root).Overflow;

            // The initial containing block has the dimensions of the viewport (the available space) and is
            // anchored at the canvas origin. In an axis where the available space is indefinite, fall back
            // to the root's padding box.
            Rect<float> areaInset = layout.Border.Add(new Rect<float>(0f, layout.ScrollbarSize.Width, 0f, layout.ScrollbarSize.Height));
            Size<float> rootPaddingBoxSize = layout.Size.Sub(new Size<float>(areaInset.HorizontalAxisSum(), areaInset.VerticalAxisSum()));
            (float areaWidth, float areaX) = icbSize.Width is float width
                ? (MathHelpers.Max(width - layout.ScrollbarSize.Width, 0f), -layout.Location.X)
                : (rootPaddingBoxSize.Width, areaInset.Left);
            (float areaHeight, float areaY) = icbSize.Height is float height
                ? (MathHelpers.Max(height - layout.ScrollbarSize.Height, 0f), -layout.Location.Y)
                : (rootPaddingBoxSize.Height, areaInset.Top);

            List<NodeId> hoisted = new();
            OofCandidates unclaimed = OofCandidates.None;
            PerformOofLayout(
                tree,
                root,
                candidates.AsSlice(),
                new Size<float>(areaWidth, areaHeight),
                new Point<float>(areaX, areaY),
                direction,
                // The root is the initial containing block and claims all remaining candidates
                ContainingBlockClaims.All,
                overflow,
                hoisted,
                ref unclaimed);
            System.Diagnostics.Debug.Assert(unclaimed.IsEmpty, "the root positioning pass must claim all remaining candidates");
            if (!rootIsCached)
            {
                tree.AddHoistedChildren(root, hoisted);
            }
        }
    }

    private static (Layout Layout, OofCandidates Candidates) ComputeInFlowRootLayout<TTree>(
        TTree tree,
        NodeId root,
        Size<AvailableSpace> availableSpace,
        ref bool rootIsCached)
        where TTree : ILayoutContainingBlock, ICacheTree
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;
        Size<float?> knownDimensions = Size.None;

        // The available space passed to the root excludes the root's margins
        Size<AvailableSpace> rootAvailableSpace;
        {
            ICoreStyle style = tree.GetCoreContainerStyle(root);
            Rect<float> margin = style.Margin.ResolveOrZero(availableSpace.Width.IntoOption(), calc);
            rootAvailableSpace = availableSpace.MaybeSub(margin.SumAxes()).MaybeMax(Size.Zero);
        }

        {
            Size<float?> parentSize = availableSpace.IntoOptions();
            ICoreStyle style = tree.GetCoreContainerStyle(root);
            if (style.IsBlock)
            {
                float? aspectRatio = style.AspectRatio;
                Rect<float> padding = style.Padding.ResolveOrZero(parentSize.Width, calc);
                Rect<float> border = style.Border.ResolveOrZero(parentSize.Width, calc);
                Size<float> paddingBorderSize = padding.Add(border).SumAxes();
                Size<float> boxSizingAdjustment = style.BoxSizing == BoxSizing.ContentBox ? paddingBorderSize : Size.Zero;

                Size<float?> minSize = style.MinSize
                    .MaybeResolve(parentSize, calc)
                    .MaybeApplyAspectRatio(aspectRatio)
                    .MaybeAdd(boxSizingAdjustment);
                Size<float?> maxSize = style.MaxSize
                    .MaybeResolve(parentSize, calc)
                    .MaybeApplyAspectRatio(aspectRatio)
                    .MaybeAdd(boxSizingAdjustment);
                Size<float?> clampedStyleSize = style.Size
                    .MaybeResolve(parentSize, calc)
                    .MaybeApplyAspectRatio(aspectRatio)
                    .MaybeAdd(boxSizingAdjustment)
                    .MaybeClamp(minSize, maxSize);

                // If both min and max in a given axis are set and max <= min then this determines the size in that axis
                Size<float?> minMaxDefiniteSize = minSize.ZipMap(
                    maxSize,
                    static (min, max) => min is float mn && max is float mx && mx <= mn ? mn : (float?)null);

                // Block nodes automatically stretch fit their width to fit available space if available space is definite
                Size<float?> availableSpaceBasedSize = new(rootAvailableSpace.Width.IntoOption(), null);

                knownDimensions = knownDimensions
                    .Or(minMaxDefiniteSize)
                    .Or(clampedStyleSize)
                    .Or(availableSpaceBasedSize)
                    .MaybeMax(paddingBorderSize);
            }
        }

        LayoutInput inputs = new()
        {
            KnownDimensions = knownDimensions,
            KnownDimensionsAreDefinite = new(true, true),
            ParentSize = availableSpace.IntoOptions(),
            AvailableSpace = rootAvailableSpace,
            SizingMode = SizingMode.InherentSize,
            Axis = RequestedAxis.Both,
            RunMode = RunMode.PerformLayout,
            VerticalMarginsAreCollapsible = Line.False,
        };
        rootIsCached = tree.CacheGet(root, inputs).HasValue;

        // Recursively compute node layout
        LayoutOutput output = tree.ComputeChildLayout(root, inputs);

        ICoreStyle rootStyle = tree.GetCoreContainerStyle(root);
        float? icbWidth = availableSpace.Width.IntoOption();
        Rect<float> rootPadding = rootStyle.Padding.ResolveOrZero(icbWidth, calc);
        Rect<float> rootBorder = rootStyle.Border.ResolveOrZero(icbWidth, calc);
        Rect<float> rootMargin = rootStyle.Margin.ResolveOrZero(icbWidth, calc);
        Size<float> scrollbarSize = new(
            rootStyle.Overflow.Y == Overflow.Scroll ? rootStyle.ScrollbarWidth : 0f,
            rootStyle.Overflow.X == Overflow.Scroll ? rootStyle.ScrollbarWidth : 0f);
        bool isRtl = rootStyle.Direction.IsRtl();

        // The root's margin box is placed at the origin of the initial containing block
        Point<float> location = new(
            isRtl && icbWidth is float availableWidth ? availableWidth - output.Size.Width - rootMargin.Right : rootMargin.Left,
            rootMargin.Top);

        // A relatively positioned root is offset by its insets (resolved against the initial containing block)
        if (rootStyle.Position == Position.Relative)
        {
            Size<float?> icbSize = availableSpace.IntoOptions();
            Rect<LengthPercentageAuto> inset = rootStyle.Inset;
            float? left = inset.Left.MaybeResolve(icbSize.Width, calc);
            float? right = inset.Right.MaybeResolve(icbSize.Width, calc);
            float? top = inset.Top.MaybeResolve(icbSize.Height, calc);
            float? bottom = inset.Bottom.MaybeResolve(icbSize.Height, calc);
            location.X += isRtl ? (-right ?? left ?? 0f) : (left ?? -right ?? 0f);
            location.Y += top ?? -bottom ?? 0f;
        }

        Layout layout = new()
        {
            Order = 0,
            Location = location,
            Size = output.Size,
            ScrollableOverflowRect = output.ScrollableOverflowRect,
            ScrollbarSize = scrollbarSize,
            Padding = rootPadding,
            Border = rootBorder,
            // TODO: support auto margins for root node?
            Margin = rootMargin,
        };

        tree.SetUnroundedLayout(root, layout);
        return (layout, output.OofCandidates.Take());
    }

    /// <summary>
    /// Resolve the static offset of an out-of-flow box from its static position, its final size and
    /// its resolved margins.
    /// </summary>
    public static Point<float> ResolveStaticOffset(Point<AxisStaticPosition> staticPosition, Size<float> finalSize, Rect<float> resolvedMargin) =>
        new(
            ResolveStaticOffsetAxis(staticPosition.X, finalSize.Width, new Line<float>(resolvedMargin.Left, resolvedMargin.Right)),
            ResolveStaticOffsetAxis(staticPosition.Y, finalSize.Height, new Line<float>(resolvedMargin.Top, resolvedMargin.Bottom)));

    private static float ResolveStaticOffsetAxis(AxisStaticPosition sp, float size, Line<float> margin)
    {
        float marginBoxSize = size + margin.Start + margin.End;
        bool overflows = sp.Align.Safety == AlignmentSafety.Safe && marginBoxSize > sp.Area.End - sp.Area.Start;
        AxisStaticEdge keyword = overflows ? sp.Align.Fallback : sp.Align.Keyword;
        return AlignInLine(sp.Area, marginBoxSize, keyword) + margin.Start;
    }

    private static float AlignInLine(Line<float> container, float size, AxisStaticEdge edge) => edge switch
    {
        AxisStaticEdge.Start => container.Start,
        AxisStaticEdge.End => container.End - size,
        _ => (container.Start + container.End - size) / 2f,
    };

    private enum OofAlignment
    {
        Normal,
        Stretch,
        Start,
        Center,
        End,
    }

    private static OofAlignment OofAlignmentFromResolved(AlignItems alignment)
    {
        System.Diagnostics.Debug.Assert(
            alignment.Keyword is not (AlignItemsKeyword.SelfStart or AlignItemsKeyword.SelfEnd),
            "self-start/self-end must be resolved before computing out-of-flow alignment");
        return alignment.Keyword switch
        {
            AlignItemsKeyword.Normal => OofAlignment.Normal,
            AlignItemsKeyword.Stretch => OofAlignment.Stretch,
            AlignItemsKeyword.Center => OofAlignment.Center,
            AlignItemsKeyword.End or AlignItemsKeyword.FlexEnd or AlignItemsKeyword.SelfEnd => OofAlignment.End,
            // The fallback alignment of baseline is start
            _ => OofAlignment.Start,
        };
    }

    private static AxisStaticEdge PhysicalEdge(OofAlignment alignment, bool reversed)
    {
        if (alignment == OofAlignment.Center)
        {
            return AxisStaticEdge.Center;
        }

        bool isEnd = alignment == OofAlignment.End;
        return isEnd != reversed ? AxisStaticEdge.End : AxisStaticEdge.Start;
    }

    /// <summary>The geometry of an out-of-flow box in one axis.</summary>
    private struct OofAxis
    {
        public float CbSize;
        public Line<float?> Inset;
        public Line<float?> Margin;
        public AxisStaticPosition StaticPosition;
        public OofAlignment Alignment;
        public AlignmentSafety Safety;
        public bool IsInline;
        public bool Reversed;
        public bool IsScrollAxis;

        public readonly AxisStaticEdge StartEdge() => Reversed ? AxisStaticEdge.End : AxisStaticEdge.Start;

        public readonly AxisStaticEdge EndEdge() => Reversed ? AxisStaticEdge.Start : AxisStaticEdge.End;

        /// <summary>The inset-modified containing block (https://www.w3.org/TR/css-position-3/#resolving-insets).</summary>
        public readonly Line<float> InsetModifiedContainingBlock()
        {
            Line<float> imcb;
            AxisStaticEdge weakerEdge;
            if (Inset.Start is float start && Inset.End is float end)
            {
                imcb = new(start, CbSize - end);
                weakerEdge = EndEdge();
            }
            else if (Inset.Start is float loneStart)
            {
                // A lone auto inset resolves to zero, and is the weaker inset
                imcb = new(loneStart, CbSize);
                weakerEdge = AxisStaticEdge.End;
            }
            else if (Inset.End is float loneEnd)
            {
                imcb = new(0f, CbSize - loneEnd);
                weakerEdge = AxisStaticEdge.Start;
            }
            else
            {
                // Both insets are auto: resolve them from the static position rectangle and the
                // alignment within it
                AxisStaticPosition sp = StaticPosition;
                switch (sp.Align.Keyword)
                {
                    case AxisStaticEdge.Start:
                        imcb = new(sp.Area.Start, CbSize);
                        break;
                    case AxisStaticEdge.End:
                        imcb = new(0f, sp.Area.End);
                        break;
                    default:
                        // Center the IMCB on the static position rectangle's center, bounded by
                        // the nearer edge of the containing block
                        float center = (sp.Area.Start + sp.Area.End) / 2f;
                        float startDistance = center;
                        float endDistance = CbSize - center;
                        imcb = Math.Abs(startDistance) <= Math.Abs(endDistance)
                            ? new(0f, 2f * startDistance)
                            : new(CbSize - (2f * endDistance), CbSize);
                        break;
                }

                weakerEdge = EndEdge();
            }

            // Overconstrained insets: the weaker inset is reduced to bring the IMCB size up to zero
            if (imcb.End < imcb.Start)
            {
                if (weakerEdge == AxisStaticEdge.Start)
                {
                    imcb.Start = imcb.End;
                }
                else
                {
                    imcb.End = imcb.Start;
                }
            }

            return imcb;
        }

        public readonly float InsetModifiedContainingBlockSize()
        {
            Line<float> imcb = InsetModifiedContainingBlock();
            return imcb.End - imcb.Start;
        }

        public readonly float StretchFitSize()
        {
            float nonAutoMarginSum = (Margin.Start ?? 0f) + (Margin.End ?? 0f);
            return MathHelpers.Max(InsetModifiedContainingBlockSize() - nonAutoMarginSum, 0f);
        }

        public readonly bool IsStretchSized(bool isReplaced, bool isTable)
        {
            bool noAutoInset = Inset.Start.HasValue && Inset.End.HasValue;
            return noAutoInset && Alignment switch
            {
                OofAlignment.Stretch => true,
                OofAlignment.Normal => !isReplaced && !isTable,
                _ => false,
            };
        }

        public readonly Line<float> ResolveMargins(float size)
        {
            bool bothInsets = Inset.Start.HasValue && Inset.End.HasValue;
            Line<float> imcb = InsetModifiedContainingBlock();
            float freeSpace = imcb.End - imcb.Start - size;
            if (Margin.Start is float start && Margin.End is float end)
            {
                return new(start, end);
            }

            // If either inset is auto then auto margins resolve to zero
            if (!bothInsets)
            {
                return new(Margin.Start ?? 0f, Margin.End ?? 0f);
            }

            if (Margin.Start is float onlyStart)
            {
                return new(onlyStart, freeSpace - onlyStart);
            }

            if (Margin.End is float onlyEnd)
            {
                return new(freeSpace - onlyEnd, onlyEnd);
            }

            // In the inline axis, negative free space is absorbed entirely by the end margin
            if (freeSpace < 0f && IsInline)
            {
                return Reversed ? new(freeSpace, 0f) : new(0f, freeSpace);
            }

            return new(freeSpace / 2f, freeSpace / 2f);
        }

        public readonly float Position(float size, Line<float> margin)
        {
            Line<float> imcb = InsetModifiedContainingBlock();
            if (!Inset.Start.HasValue && !Inset.End.HasValue)
            {
                // Both insets are auto: static alignment, with safe overflow relative to the IMCB
                AxisStaticPosition sp = StaticPosition;
                float staticMarginBoxSize = size + margin.Start + margin.End;
                float offset = sp.Align.Safety == AlignmentSafety.Safe && staticMarginBoxSize > imcb.End - imcb.Start
                    ? AlignInLine(imcb, staticMarginBoxSize, StartEdge())
                    : AlignInLine(sp.Area, staticMarginBoxSize, sp.Align.Keyword);
                return offset + margin.Start;
            }

            // One auto inset: the margin box is aligned to the edge of the stronger inset
            if (!Inset.End.HasValue)
            {
                return imcb.Start + margin.Start;
            }

            if (!Inset.Start.HasValue)
            {
                return imcb.End - size - margin.End;
            }

            // Auto margins have already absorbed all of the free space
            if (!Margin.Start.HasValue || !Margin.End.HasValue)
            {
                return imcb.Start + margin.Start;
            }

            float marginBoxSize = size + margin.Start + margin.End;
            float imcbSize = imcb.End - imcb.Start;
            AxisStaticEdge edge = PhysicalEdge(Alignment, Reversed);
            float marginBoxStart;
            switch (Safety)
            {
                case AlignmentSafety.Unsafe:
                    marginBoxStart = AlignInLine(imcb, marginBoxSize, edge);
                    break;
                case AlignmentSafety.Safe:
                    marginBoxStart = AlignInLine(imcb, marginBoxSize, marginBoxSize > imcbSize ? StartEdge() : edge);
                    break;
                default:
                    // Default overflow alignment for absolutely positioned boxes:
                    // https://www.w3.org/TR/css-align-3/#auto-safety-position
                    if (marginBoxSize <= imcbSize || Alignment == OofAlignment.Normal)
                    {
                        // 1. Fits within the IMCB: align as specified
                        marginBoxStart = AlignInLine(imcb, marginBoxSize, edge);
                    }
                    else
                    {
                        // The overflow limit rect is the bounding rect of the IMCB and the
                        // containing block, extended to infinity in the scrollable direction
                        // of a scroll container
                        Line<float> limit = new(MathHelpers.Min(imcb.Start, 0f), MathHelpers.Max(imcb.End, CbSize));
                        if (IsScrollAxis)
                        {
                            if (Reversed)
                            {
                                limit.Start = float.NegativeInfinity;
                            }
                            else
                            {
                                limit.End = float.PositiveInfinity;
                            }
                        }

                        if (marginBoxSize <= limit.End - limit.Start)
                        {
                            // 2. Fits within the overflow limit rect: cover the IMCB and align
                            // as specified as far as possible without overflowing the limit
                            float start = AlignInLine(imcb, marginBoxSize, edge);
                            marginBoxStart = MathHelpers.Max(MathHelpers.Min(start, limit.End - marginBoxSize), limit.Start);
                        }
                        else
                        {
                            // 3. Start-align within the overflow limit rect
                            marginBoxStart = AlignInLine(limit, marginBoxSize, StartEdge());
                        }
                    }

                    break;
            }

            return marginBoxStart + margin.Start;
        }
    }

    /// <summary>
    /// Lay out the out-of-flow candidates for which <paramref name="nodeId"/> is the containing block
    /// (called after the node's own layout, with the positioning area the algorithm reported). Unclaimed
    /// candidates are left in <paramref name="output"/> to bubble up.
    /// </summary>
    public static void ComputeOofLayout(ILayoutContainingBlock tree, NodeId nodeId, ref LayoutOutput output)
    {
        if (output.OofPositioningArea is not OofPositioningArea area)
        {
            return;
        }

        if (output.OofCandidates.IsEmpty)
        {
            tree.ClearHoistedChildren(nodeId);
            return;
        }

        ICoreStyle style = tree.GetCoreContainerStyle(nodeId);
        Direction direction = style.Direction;
        ContainingBlockClaims claims = style.IsContainingBlock;

        OofCandidates candidates = output.OofCandidates.Take();
        OofLayoutResult result = ComputeOofLayoutForArea(tree, nodeId, candidates.AsSlice(), area, direction, claims);

        // Always record the hoisted child list (even when empty) so that lists recorded by previous
        // layout runs do not persist
        tree.ClearHoistedChildren(nodeId);
        tree.AddHoistedChildren(nodeId, result.Hoisted);
        output.OofCandidates = result.Unclaimed;
        output.ScrollableOverflowRect = output.ScrollableOverflowRect.Union(result.ScrollableOverflowRect);
    }

    /// <summary>
    /// Lay out the out-of-flow candidates claimed by <paramref name="claims"/> in the given positioning area,
    /// using <paramref name="geometryOwner"/> as the containing block.
    /// </summary>
    public static OofLayoutResult ComputeOofLayoutForArea(
        ILayoutContainingBlock tree,
        NodeId geometryOwner,
        IReadOnlyList<OofCandidate> candidates,
        OofPositioningArea area,
        Direction direction,
        ContainingBlockClaims claims)
    {
        Point<Overflow> overflow = tree.GetCoreContainerStyle(geometryOwner).Overflow;
        OofLayoutResult result = new();
        OofCandidates unclaimed = OofCandidates.None;
        result.ScrollableOverflowRect = PerformOofLayout(
            tree,
            geometryOwner,
            candidates,
            area.Size,
            area.Offset,
            direction,
            claims,
            overflow,
            result.Hoisted,
            ref unclaimed);
        result.Unclaimed = unclaimed;
        return result;
    }

    internal struct OofBoxLayout
    {
        public Layout Layout;
        public OofCandidates Surfaced;
        public Point<Overflow> Overflow;
        public Contain Contain;
    }

    /// <summary>Lay out a single out-of-flow box in the given area (https://www.w3.org/TR/css-position-3/#abspos-layout).</summary>
    internal static OofBoxLayout LayoutOofBox(
        ILayoutContainingBlock tree,
        OofCandidate candidate,
        Size<float> areaSize,
        Point<float> areaOffset,
        Direction direction,
        Point<Overflow> containerOverflow,
        NodeId? gridOwner,
        Action<LayoutInput>? beforeLayout)
    {
        Func<object, float, float> calc = tree.ResolveCalcValue;
        IOofItemStyle childStyle = tree.GetOofItemStyle(candidate.Node);

        // If the current node is a grid container then the box is positioned relative to the
        // grid area determined by its grid-placement properties (which falls back to the passed
        // area for auto placement)
        if (gridOwner is NodeId owner && tree.GetDetailedLayoutInfo(owner).Grid is DetailedGridInfo gridInfo)
        {
            Rect<float> areaRect = new(areaOffset.X, areaOffset.X + areaSize.Width, areaOffset.Y, areaOffset.Y + areaSize.Height);
            Rect<float> gridArea = gridInfo.ResolveAbsoluteGridArea(childStyle.GridRow, childStyle.GridColumn, direction, areaRect);
            areaSize = new Size<float>(gridArea.Right - gridArea.Left, gridArea.Bottom - gridArea.Top);
            areaOffset = new Point<float>(gridArea.Left, gridArea.Top);
        }

        float areaWidth = areaSize.Width;
        float areaHeight = areaSize.Height;

        float? aspectRatio = childStyle.AspectRatio;
        Rect<float?> margin = childStyle.Margin.Map(m => m.ResolveToOption(areaWidth, calc));
        Rect<float> padding = childStyle.Padding.ResolveOrZero(areaWidth, calc);
        Rect<float> border = childStyle.Border.ResolveOrZero(areaWidth, calc);
        Size<float> paddingBorderSum = padding.Add(border).SumAxes();
        Size<float> boxSizingAdjustment = childStyle.BoxSizing == BoxSizing.ContentBox ? paddingBorderSum : Size.Zero;

        // Resolve inset
        Rect<LengthPercentageAuto> insetStyle = childStyle.Inset;
        float? left = insetStyle.Left.MaybeResolve(areaWidth, calc);
        float? right = insetStyle.Right.MaybeResolve(areaWidth, calc);
        float? top = insetStyle.Top.MaybeResolve(areaHeight, calc);
        float? bottom = insetStyle.Bottom.MaybeResolve(areaHeight, calc);

        // Compute known dimensions from min/max/inherent size styles
        Size<Dimension> sizeStyle = childStyle.Size;
        Size<float?> styleSize = sizeStyle
            .MaybeResolve(areaSize, calc)
            .MaybeApplyAspectRatio(aspectRatio)
            .MaybeAdd(boxSizingAdjustment);
        Size<float?> minSize = childStyle.MinSize
            .MaybeResolve(areaSize, calc)
            .MaybeApplyAspectRatio(aspectRatio)
            .MaybeAdd(boxSizingAdjustment)
            .Or(paddingBorderSum.AsOptions())
            .MaybeMax(paddingBorderSum);
        Size<float?> maxSize = childStyle.MaxSize
            .MaybeResolve(areaSize, calc)
            .MaybeApplyAspectRatio(aspectRatio)
            .MaybeAdd(boxSizingAdjustment);
        Size<float?> knownDimensions = styleSize.MaybeClamp(minSize, maxSize);

        bool isReplaced = childStyle.IsReplaced;
        bool isTable = childStyle.IsTable;
        Direction itemDirection = childStyle.Direction;
        // Taffy only supports the horizontal-tb writing mode, so only the inline axis can be reversed.
        // auto (null) behaves as normal for absolutely positioned boxes: it does not defer to the
        // *-items properties of the containing block.
        AlignItems justifySelf = (childStyle.JustifySelf ?? AlignItems.Normal).ResolveSelfRelative(itemDirection, direction, true);
        AlignItems alignSelf = (childStyle.AlignSelf ?? AlignItems.Normal).ResolveSelfRelative(itemDirection, direction, false);
        Point<Overflow> overflow = childStyle.Overflow;
        float scrollbarWidth = childStyle.ScrollbarWidth;
        Contain contain = childStyle.Contain;

        // Static positions are relative to the containing block's border box: make them relative to
        // the inset-resolution area
        float offsetX = areaOffset.X;
        float offsetY = areaOffset.Y;
        Point<AxisStaticPosition> staticPosition = new(
            new AxisStaticPosition(candidate.StaticPosition.X.Area.Map(v => v - offsetX), candidate.StaticPosition.X.Align),
            new AxisStaticPosition(candidate.StaticPosition.Y.Area.Map(v => v - offsetY), candidate.StaticPosition.Y.Align));

        OofAxis horizontal = new()
        {
            CbSize = areaWidth,
            Inset = new(left, right),
            Margin = new(margin.Left, margin.Right),
            StaticPosition = staticPosition.X,
            Alignment = OofAlignmentFromResolved(justifySelf),
            Safety = justifySelf.Safety,
            IsInline = true,
            Reversed = direction.IsRtl(),
            IsScrollAxis = containerOverflow.X.IsScrollContainer(),
        };
        OofAxis vertical = new()
        {
            CbSize = areaHeight,
            Inset = new(top, bottom),
            Margin = new(margin.Top, margin.Bottom),
            StaticPosition = staticPosition.Y,
            Alignment = OofAlignmentFromResolved(alignSelf),
            Safety = alignSelf.Safety,
            IsInline = false,
            Reversed = false,
            IsScrollAxis = containerOverflow.Y.IsScrollContainer(),
        };

        Size<float> stretchFitSize = new(horizontal.StretchFitSize(), vertical.StretchFitSize());

        // The space available to the box's border box is its stretch-fit size (the IMCB minus its
        // non-auto margins). The available space for a table never exceeds that of the containing
        // block (minus the margins) https://www.w3.org/TR/css-tables-3/#abspos
        Size<float> availableBorderBox = stretchFitSize;
        if (isTable)
        {
            Rect<float> nonAutoMargin = margin.Map(m => m ?? 0f);
            availableBorderBox = availableBorderBox.F32Min(new Size<float>(
                MathHelpers.Max(areaWidth - nonAutoMargin.HorizontalAxisSum(), 0f),
                MathHelpers.Max(areaHeight - nonAutoMargin.VerticalAxisSum(), 0f)));
        }

        Size<AvailableSpace> availableSpace = availableBorderBox.MaybeClamp(minSize, maxSize).Map(AvailableSpace.Definite);

        // Resolve any sizing keywords (min-content, max-content, fit-content, fit-content(...),
        // stretch) in the size styles. An explicitly sized axis takes precedence over the
        // automatic size below.
        if (sizeStyle.Width.IsSizingKeyword() || sizeStyle.Height.IsSizingKeyword())
        {
            SizingKeyword.ResolveAbsoluteSizingKeywords(
                tree,
                candidate.Node,
                ref knownDimensions,
                sizeStyle,
                areaSize,
                new Rect<float?>(left, right, top, bottom),
                margin,
                SizingMode.ContentSize);
            knownDimensions = knownDimensions.MaybeApplyAspectRatio(aspectRatio).MaybeClamp(minSize, maxSize);
        }

        // Resolve automatic sizes https://www.w3.org/TR/css-position-3/#abspos-auto-size
        //
        // An auto size is the stretch-fit size (the IMCB minus margins) if the self-alignment in
        // that axis is stretch (or normal for a non-replaced, non-table box) and neither inset is
        // auto. Otherwise it is the fit-content size, which is measured below. Stretch-fit sizes are
        // resolved first so that a size in the other axis can be transferred through the aspect ratio.
        // The block axis is the ratio-dependent axis, so a stretched inline size is transferred
        // through the aspect ratio rather than also stretching the block size. An explicit
        // stretch in both axes stretches both and ignores the aspect ratio.
        bool stretchWidth = !knownDimensions.Width.HasValue && horizontal.IsStretchSized(isReplaced, isTable);
        bool stretchHeight = !knownDimensions.Height.HasValue && vertical.IsStretchSized(isReplaced, isTable);
        bool bothExplicitStretch = horizontal.Alignment == OofAlignment.Stretch && vertical.Alignment == OofAlignment.Stretch;
        float? stretchAspectRatio = stretchWidth && stretchHeight && bothExplicitStretch ? null : aspectRatio;
        if (stretchWidth)
        {
            if (isTable)
            {
                // A table cannot be stretched smaller than its content: stretch-fit is a minimum
                minSize.Width = MathHelpers.Max(minSize.Width ?? 0f, stretchFitSize.Width);
            }
            else
            {
                knownDimensions.Width = stretchFitSize.Width;
                knownDimensions = knownDimensions.MaybeApplyAspectRatio(stretchAspectRatio).MaybeClamp(minSize, maxSize);
            }
        }

        if (!knownDimensions.Height.HasValue && stretchHeight)
        {
            if (isTable)
            {
                minSize.Height = MathHelpers.Max(minSize.Height ?? 0f, stretchFitSize.Height);
            }
            else
            {
                knownDimensions.Height = stretchFitSize.Height;
                knownDimensions = knownDimensions.MaybeApplyAspectRatio(stretchAspectRatio).MaybeClamp(minSize, maxSize);
            }
        }

        Size<float> finalSize;
        if (knownDimensions.Width is float knownWidth && knownDimensions.Height is float knownHeight)
        {
            finalSize = new Size<float>(knownWidth, knownHeight);
        }
        else
        {
            Size<float> measuredSize = tree.MeasureChildSizeBoth(
                candidate.Node,
                knownDimensions,
                areaSize.AsOptions(),
                availableSpace,
                SizingMode.ContentSize,
                Line.False);
            finalSize = knownDimensions.UnwrapOr(measuredSize);
        }

        finalSize = finalSize.MaybeClamp(minSize, maxSize);

        LayoutInput layoutInput = new()
        {
            KnownDimensions = finalSize.AsOptions(),
            KnownDimensionsAreDefinite = new(true, true),
            ParentSize = areaSize.AsOptions(),
            AvailableSpace = availableSpace,
            SizingMode = SizingMode.ContentSize,
            Axis = RequestedAxis.Both,
            RunMode = RunMode.PerformLayout,
            VerticalMarginsAreCollapsible = Line.False,
        };
        beforeLayout?.Invoke(layoutInput);
        LayoutOutput layoutOutput = tree.ComputeChildLayout(candidate.Node, layoutInput);

        // Resolve auto margins https://www.w3.org/TR/css-position-3/#abspos-margins
        Line<float> horizontalMargin = horizontal.ResolveMargins(finalSize.Width);
        Line<float> verticalMargin = vertical.ResolveMargins(finalSize.Height);
        Rect<float> resolvedMargin = new(horizontalMargin.Start, horizontalMargin.End, verticalMargin.Start, verticalMargin.End);

        // Align the margin box within the IMCB https://www.w3.org/TR/css-position-3/#abspos-alignment
        Point<float> location = new(
            horizontal.Position(finalSize.Width, horizontalMargin) + areaOffset.X,
            vertical.Position(finalSize.Height, verticalMargin) + areaOffset.Y);

        // Note: axis intentionally switched here as scrollbars take up space in the opposite axis
        // to the axis in which scrolling is enabled.
        Size<float> scrollbarSize = new(
            overflow.Y == Overflow.Scroll ? scrollbarWidth : 0f,
            overflow.X == Overflow.Scroll ? scrollbarWidth : 0f);

        Layout layout = new()
        {
            Order = candidate.Order,
            Size = finalSize,
            ScrollableOverflowRect = layoutOutput.ScrollableOverflowRect,
            ScrollbarSize = scrollbarSize,
            Location = location,
            Padding = padding,
            Border = border,
            Margin = resolvedMargin,
        };

        tree.SetUnroundedLayout(candidate.Node, layout);
        return new OofBoxLayout
        {
            Layout = layout,
            Surfaced = layoutOutput.OofCandidates.Take(),
            Overflow = overflow,
            Contain = contain,
        };
    }

    /// <summary>
    /// Lay out the claimed candidates in the area and collect the unclaimed ones. Returns the scrollable
    /// overflow contributed by the laid out boxes.
    /// </summary>
    internal static Rect<float> PerformOofLayout(
        ILayoutContainingBlock tree,
        NodeId nodeId,
        IReadOnlyList<OofCandidate> candidates,
        Size<float> areaSize,
        Point<float> areaOffset,
        Direction direction,
        ContainingBlockClaims claims,
        Point<Overflow> containerOverflow,
        List<NodeId> hoisted,
        ref OofCandidates unclaimed)
    {
        Rect<float> absoluteOverflowRect = Rect.Zero;
        if (candidates.Count == 0)
        {
            return absoluteOverflowRect;
        }

        bool isScrollContainer = containerOverflow.X.IsScrollContainer() || containerOverflow.Y.IsScrollContainer();

        // Split the candidate list into an initial work list of claimed candidates and the unclaimed
        // remainder. Further claimed candidates surfaced while laying out a claimed box are appended
        // to the work list (Blink-style re-sweep).
        List<OofCandidate> worklist = new();
        foreach (OofCandidate candidate in candidates)
        {
            if (claims.ForPosition(candidate.Position))
            {
                worklist.Add(candidate);
            }
            else
            {
                unclaimed.Push(candidate);
            }
        }

        int index = 0;
        while (index < worklist.Count)
        {
            OofCandidate candidate = worklist[index];
            index++;
            hoisted.Add(candidate.Node);

            OofBoxLayout boxLayout = LayoutOofBox(tree, candidate, areaSize, areaOffset, direction, containerOverflow, nodeId, null);
            Layout layout = boxLayout.Layout;
            Point<float> location = layout.Location;
            Size<float> finalSize = layout.Size;

            // Re-sweep any candidates surfaced from within the box's subtree, translating their
            // anchors so they are relative to this node's border box
            OofCandidates surfaced = boxLayout.Surfaced;
            if (!surfaced.IsEmpty)
            {
                surfaced.Translate(location);
                foreach (OofCandidate surfacedCandidate in surfaced)
                {
                    if (claims.ForPosition(surfacedCandidate.Position))
                    {
                        worklist.Add(surfacedCandidate);
                    }
                    else
                    {
                        unclaimed.Push(surfacedCandidate);
                    }
                }
            }

            // Location is measured from the scroll origin (the inline-start edge: right side in RTL)
            Point<float> relativeLocation = direction.IsRtl()
                ? new Point<float>(areaSize.Width - (location.X - areaOffset.X) - finalSize.Width, location.Y - areaOffset.Y)
                : new Point<float>(location.X - areaOffset.X, location.Y - areaOffset.Y);
            absoluteOverflowRect = absoluteOverflowRect.Union(ScrollableOverflow.ComputeScrollableOverflowContribution(
                relativeLocation,
                finalSize,
                layout.ScrollableOverflowRect,
                boxLayout.Overflow,
                boxLayout.Contain,
                isScrollContainer));
        }

        return absoluteOverflowRect;
    }
}
