namespace TaffySharp;

/// <summary>Generic CSS alignment code that is shared between both the Flexbox and CSS Grid algorithms.</summary>
internal static class CommonAlignment
{
    /// <summary>
    /// The first baseline a child contributes to its parent: a scroll container's baseline is clamped
    /// to its border box (and synthesized from its bottom edge when it has none).
    /// </summary>
    public static float? ChildFirstBaseline(float? firstBaseline, float height, Overflow overflowY)
    {
        if (overflowY.IsScrollContainer())
        {
            return MathHelpers.Max(MathHelpers.Min(firstBaseline ?? height, height), 0f);
        }

        return firstBaseline;
    }

    /// <summary>Safe self-alignment falls back to <c>start</c> when the item overflows its alignment container.</summary>
    public static AlignItemsKeyword ResolveSelfAlignmentSafety(AlignItems alignment, bool overflows) =>
        alignment.Safety == AlignmentSafety.Safe && overflows ? AlignItemsKeyword.Start : alignment.Keyword;

    /// <summary>
    /// Implement fallback alignment.
    /// In addition to the spec at https://www.w3.org/TR/css-align-3/ this implementation follows
    /// the resolution of https://github.com/w3c/csswg-drafts/issues/10154
    /// </summary>
    public static AlignContentKeyword ApplyAlignmentFallback(float freeSpace, int numItems, AlignContent alignmentMode)
    {
        System.Diagnostics.Debug.Assert(
            alignmentMode.Keyword != AlignContentKeyword.Normal,
            "normal must be resolved before computing content alignment");

        AlignContentKeyword keyword = alignmentMode.Keyword == AlignContentKeyword.Normal ? AlignContentKeyword.Stretch : alignmentMode.Keyword;
        bool isSafe = alignmentMode.Safety == AlignmentSafety.Safe;

        // 1. If there is only a single item being aligned or the items overflow the container, the
        //    distributed alignment keywords (stretch, space-*) fall back to a positional keyword
        //    and gain implicit safe semantics so step 2 can flip them to Start on overflow.
        //    https://www.w3.org/TR/css-align-3/#distribution-values
        if (numItems <= 1 || freeSpace <= 0f)
        {
            switch (keyword)
            {
                case AlignContentKeyword.Stretch:
                case AlignContentKeyword.SpaceBetween:
                    keyword = AlignContentKeyword.FlexStart;
                    isSafe = true;
                    break;
                case AlignContentKeyword.SpaceAround:
                case AlignContentKeyword.SpaceEvenly:
                    keyword = AlignContentKeyword.Center;
                    isSafe = true;
                    break;
            }
        }

        // 2. Safe alignment falls back to Start whenever the alignment subject would overflow the
        //    alignment container.
        if (freeSpace <= 0f && isSafe)
        {
            keyword = AlignContentKeyword.Start;
        }

        return keyword;
    }

    /// <summary>
    /// Generic alignment function that is used:
    ///   - For both align-content and justify-content alignment
    ///   - For both the Flexbox and CSS Grid algorithms
    /// CSS Grid does not apply gaps as part of alignment, so the gap parameter should
    /// always be set to zero for CSS Grid.
    /// </summary>
    public static float ComputeAlignmentOffset(
        float freeSpace,
        int numItems,
        float gap,
        AlignContentKeyword alignmentMode,
        bool layoutIsFlexReversed,
        bool isFirst)
    {
        if (isFirst)
        {
            return alignmentMode switch
            {
                AlignContentKeyword.Start => 0f,
                AlignContentKeyword.FlexStart => layoutIsFlexReversed ? freeSpace : 0f,
                AlignContentKeyword.End => freeSpace,
                AlignContentKeyword.FlexEnd => layoutIsFlexReversed ? 0f : freeSpace,
                AlignContentKeyword.Center => freeSpace / 2f,
                AlignContentKeyword.Normal or AlignContentKeyword.Stretch => 0f,
                AlignContentKeyword.SpaceBetween => 0f,
                AlignContentKeyword.SpaceAround => freeSpace >= 0f ? freeSpace / numItems / 2f : freeSpace / 2f,
                AlignContentKeyword.SpaceEvenly => freeSpace >= 0f ? freeSpace / (numItems + 1) : freeSpace / 2f,
                _ => 0f,
            };
        }

        float positiveFreeSpace = MathHelpers.Max(freeSpace, 0f);
        return gap + alignmentMode switch
        {
            AlignContentKeyword.SpaceBetween => positiveFreeSpace / (numItems - 1),
            AlignContentKeyword.SpaceAround => positiveFreeSpace / numItems,
            AlignContentKeyword.SpaceEvenly => positiveFreeSpace / (numItems + 1),
            _ => 0f,
        };
    }
}
