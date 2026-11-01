namespace TaffySharp;

public static partial class Compute
{
    /// <summary>
    /// Compute the size of a leaf node (node with no children).
    /// <paramref name="measureFunction"/> receives the known dimensions and the available space and returns the content size.
    /// </summary>
    public static LayoutOutput ComputeLeafLayout(
        LayoutInput inputs,
        ICoreStyle style,
        Func<object, float, float> resolveCalcValue,
        Func<Size<float?>, Size<AvailableSpace>, Size<float>> measureFunction)
    {
        Size<float?> knownDimensions = inputs.KnownDimensions;
        Size<float?> parentSize = inputs.ParentSize;
        Size<AvailableSpace> availableSpace = inputs.AvailableSpace;
        SizingMode sizingMode = inputs.SizingMode;
        RunMode runMode = inputs.RunMode;

        // Note: both horizontal and vertical percentage padding/borders are resolved against the container's inline size (i.e. width).
        // This is not a bug, but is how CSS is specified (see: https://developer.mozilla.org/en-US/docs/Web/CSS/padding#values)
        Rect<float> padding = style.Padding.ResolveOrZero(parentSize.Width, resolveCalcValue);
        Rect<float> border = style.Border.ResolveOrZero(parentSize.Width, resolveCalcValue);
        Rect<float> paddingBorder = padding.Add(border);
        Size<float> pbSum = paddingBorder.SumAxes();
        Size<float> boxSizingAdjustment = style.BoxSizing == BoxSizing.ContentBox ? pbSum : Size.Zero;

        // Resolve node's preferred/min/max sizes (width/heights) against the available space (percentages resolve to pixel values)
        // For ContentSize mode, we pretend that the node has no size styles as these should be ignored.
        Size<float?> nodeSize;
        Size<float?> nodeMinSize;
        Size<float?> nodeMaxSize;
        float? aspectRatio;
        if (sizingMode == SizingMode.ContentSize)
        {
            nodeSize = knownDimensions;
            nodeMinSize = Size.None;
            nodeMaxSize = Size.None;
            aspectRatio = null;
        }
        else
        {
            aspectRatio = style.AspectRatio;
            Size<float?> styleSize = style.Size
                .MaybeResolve(parentSize, resolveCalcValue)
                .MaybeApplyAspectRatio(aspectRatio)
                .MaybeAdd(boxSizingAdjustment);
            Size<float?> styleMinSize = style.MinSize
                .MaybeResolve(parentSize, resolveCalcValue)
                .MaybeApplyAspectRatio(aspectRatio)
                .MaybeAdd(boxSizingAdjustment);
            Size<float?> styleMaxSize = style.MaxSize.MaybeResolve(parentSize, resolveCalcValue).MaybeAdd(boxSizingAdjustment);

            nodeSize = knownDimensions.Or(styleSize);
            nodeMinSize = styleMinSize;
            nodeMaxSize = styleMaxSize;
        }

        // Scrollbar gutters are reserved when the overflow property is set to Overflow.Scroll.
        // However, the axis are switched (transposed) because a node that scrolls vertically needs
        // *horizontal* space to be reserved for a scrollbar
        Point<Overflow> overflow = style.Overflow;
        float scrollbarWidth = style.ScrollbarWidth;
        Point<float> scrollbarGutter = overflow.Transpose().Map(o => o == Overflow.Scroll ? scrollbarWidth : 0f);
        // TODO: make side configurable based on the direction property
        Rect<float> contentBoxInset = paddingBorder;
        contentBoxInset.Right += scrollbarGutter.X;
        contentBoxInset.Bottom += scrollbarGutter.Y;

        bool hasStylesPreventingBeingCollapsedThrough = !style.IsBlock
            || overflow.X.IsScrollContainer()
            || overflow.Y.IsScrollContainer()
            || style.Position.IsOutOfFlow()
            || style.Contain.EstablishesIndependentFormattingContext()
            || padding.Top > 0f
            || padding.Bottom > 0f
            || border.Top > 0f
            || border.Bottom > 0f
            || nodeSize.Height is > 0f
            || nodeMinSize.Height is > 0f;

        // Return early if both width and height are known
        if (runMode == RunMode.ComputeSize && hasStylesPreventingBeingCollapsedThrough)
        {
            if (nodeSize.Width is float width && nodeSize.Height is float height)
            {
                Size<float> earlySize = new Size<float>(width, height)
                    .MaybeClamp(nodeMinSize, nodeMaxSize)
                    .MaybeMax(paddingBorder.SumAxes().AsOptions());
                return new LayoutOutput { Size = earlySize };
            }
        }

        // Compute available space
        Size<AvailableSpace> measureAvailableSpace = new(
            (knownDimensions.Width is float kw ? AvailableSpace.Definite(kw) : availableSpace.Width)
                .MaybeSet(knownDimensions.Width)
                .MaybeSet(nodeSize.Width)
                .MapDefiniteValue(size => size.MaybeClamp(nodeMinSize.Width, nodeMaxSize.Width) - contentBoxInset.HorizontalAxisSum()),
            (knownDimensions.Height is float kh ? AvailableSpace.Definite(kh) : availableSpace.Height)
                .MaybeSet(knownDimensions.Height)
                .MaybeSet(nodeSize.Height)
                .MapDefiniteValue(size => size.MaybeClamp(nodeMinSize.Height, nodeMaxSize.Height) - contentBoxInset.VerticalAxisSum()));

        // Measure node
        Size<float> measuredSize = measureFunction(
            runMode switch
            {
                RunMode.ComputeSize => knownDimensions,
                RunMode.PerformLayout => Size.None,
                _ => throw new InvalidOperationException("unreachable"),
            },
            measureAvailableSpace);
        Size<float> clampedSize = knownDimensions
            .Or(nodeSize)
            .UnwrapOr(measuredSize.Add(contentBoxInset.SumAxes()))
            .MaybeClamp(nodeMinSize, nodeMaxSize);
        Size<float> size = new(
            clampedSize.Width,
            MathHelpers.Max(clampedSize.Height, aspectRatio is float ratio ? clampedSize.Width / ratio : 0f));
        size = size.MaybeMax(paddingBorder.SumAxes().AsOptions());

        // A scroll container's own padding at the end of the content is part of its scrollable
        // overflow region, so it is included in the overflow rect. Boxes that are not scroll
        // containers do not extend their overflow region by their own padding.
        bool isScrollContainer = overflow.X.IsScrollContainer() || overflow.Y.IsScrollContainer();
        bool isRtl = style.Direction.IsRtl();
        float startPadding = isRtl ? padding.Right : padding.Left;
        float endPadding = isRtl ? padding.Left : padding.Right;
        Rect<float> scrollableOverflowRect = new(
            0f,
            startPadding + measuredSize.Width + (isScrollContainer ? endPadding : 0f),
            0f,
            padding.Top + measuredSize.Height + (isScrollContainer ? padding.Bottom : 0f));

        return new LayoutOutput
        {
            Size = size,
            ScrollableOverflowRect = scrollableOverflowRect,
            MarginsCanCollapseThrough = !hasStylesPreventingBeingCollapsedThrough && size.Height == 0f && measuredSize.Height == 0f,
        };
    }
}
