namespace TaffySharp;

internal static class ScrollableOverflow
{
    /// <summary>
    /// The contribution of a child box to its parent's scrollable overflow rect, in the parent's
    /// coordinate space (https://www.w3.org/TR/css-overflow-3/#scrollable).
    /// </summary>
    public static Rect<float> ComputeScrollableOverflowContribution(
        Point<float> location,
        Size<float> size,
        Rect<float> scrollableOverflowRect,
        Point<Overflow> overflow,
        Contain contain,
        bool parentIsScrollContainer)
    {
        bool isScrollContainer = overflow.X.IsScrollContainer() || overflow.Y.IsScrollContainer();
        bool overflowIsContained = contain.ContainsScrollableOverflow();
        Point<bool> propagates = new(
            !isScrollContainer && !overflowIsContained && overflow.X == Overflow.Visible,
            !isScrollContainer && !overflowIsContained && overflow.Y == Overflow.Visible);

        Size<float> endExtent = new(
            propagates.X ? MathHelpers.Max(size.Width, scrollableOverflowRect.Right) : size.Width,
            propagates.Y ? MathHelpers.Max(size.Height, scrollableOverflowRect.Bottom) : size.Height);
        if (endExtent.Width <= 0f || endExtent.Height <= 0f)
        {
            return Rect.Zero;
        }

        Point<float> startExtent = new(
            propagates.X ? MathHelpers.Min(0f, scrollableOverflowRect.Left) : 0f,
            propagates.Y ? MathHelpers.Min(0f, scrollableOverflowRect.Top) : 0f);

        Rect<float> contribution = new(
            location.X + startExtent.X,
            location.X + endExtent.Width,
            location.Y + startExtent.Y,
            location.Y + endExtent.Height);

        bool isWhollyUnreachable = contribution.Right <= 0f || contribution.Bottom <= 0f;
        return parentIsScrollContainer && isWhollyUnreachable ? Rect.Zero : contribution;
    }
}
