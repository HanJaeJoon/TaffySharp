namespace TaffySharp;

/// <summary>How an intrinsic sizing keyword (<c>min-content</c>, <c>fit-content</c>, <c>stretch</c>, ...) resolves.</summary>
internal readonly struct SizingKeywordResolution
{
    private SizingKeywordResolution(bool isExact, AvailableSpace measure, float exact)
    {
        IsExact = isExact;
        MeasureSpace = measure;
        ExactSize = exact;
    }

    /// <summary>Whether the size is known exactly (otherwise it must be measured).</summary>
    public bool IsExact { get; }

    /// <summary>The available space to measure the box with.</summary>
    public AvailableSpace MeasureSpace { get; }

    /// <summary>The exact size.</summary>
    public float ExactSize { get; }

    public static SizingKeywordResolution Measure(AvailableSpace space) => new(false, space, 0f);

    public static SizingKeywordResolution Exact(float size) => new(true, default, size);
}

internal static class SizingKeyword
{
    /// <summary>Resolve a sizing keyword to how the box should be sized, or <c>null</c> if the style is not a (resolvable) sizing keyword.</summary>
    public static SizingKeywordResolution? ResolveSizingKeyword(
        Dimension style,
        float? stretchSize,
        float? percentResolutionBasis,
        Func<object, float, float> calcResolver)
    {
        switch (style.Tag)
        {
            case CompactLength.MinContentTag:
                return SizingKeywordResolution.Measure(AvailableSpace.MinContent);
            case CompactLength.MaxContentTag:
                return SizingKeywordResolution.Measure(AvailableSpace.MaxContent);
            case CompactLength.FitContentPxTag:
                return SizingKeywordResolution.Measure(AvailableSpace.Definite(style.Value));
            case CompactLength.FitContentPercentTag:
                return percentResolutionBasis is float basis
                    ? SizingKeywordResolution.Measure(AvailableSpace.Definite(basis * style.Value))
                    : null;
            case CompactLength.FitContentKeywordTag:
                return stretchSize is float size ? SizingKeywordResolution.Measure(AvailableSpace.Definite(size)) : null;
            case CompactLength.StretchTag:
                return stretchSize is float exact ? SizingKeywordResolution.Exact(exact) : null;
            default:
                if (style.IsFitContentCalc())
                {
                    return percentResolutionBasis is float calcBasis
                        ? SizingKeywordResolution.Measure(AvailableSpace.Definite(calcResolver(style.CalcValue!, calcBasis)))
                        : null;
                }

                return null;
        }
    }

    /// <summary>
    /// Resolve sizing keywords in the size of an absolutely positioned box, measuring the box where
    /// needed, and write the results into <paramref name="knownDimensions"/>.
    /// </summary>
    public static void ResolveAbsoluteSizingKeywords(
        ILayoutPartialTree tree,
        NodeId node,
        ref Size<float?> knownDimensions,
        Size<Dimension> sizeStyle,
        Size<float> areaSize,
        Rect<float?> inset,
        Rect<float?> margin,
        SizingMode sizingMode)
    {
        Size<float> stretchSize = new(
            MathHelpers.Max(
                areaSize.Width - (inset.Left ?? 0f) - (inset.Right ?? 0f) - (margin.Left ?? 0f) - (margin.Right ?? 0f),
                0f),
            MathHelpers.Max(
                areaSize.Height - (inset.Top ?? 0f) - (inset.Bottom ?? 0f) - (margin.Top ?? 0f) - (margin.Bottom ?? 0f),
                0f));

        Func<object, float, float> calc = tree.ResolveCalcValue;
        SizingKeywordResolution? keywordWidth = knownDimensions.Width is null
            ? ResolveSizingKeyword(sizeStyle.Width, stretchSize.Width, areaSize.Width, calc)
            : null;
        SizingKeywordResolution? keywordHeight = knownDimensions.Height is null
            ? ResolveSizingKeyword(sizeStyle.Height, stretchSize.Height, areaSize.Height, calc)
            : null;

        // If both axes need to be measured then resolve them with a single measure call
        if (keywordWidth is { IsExact: false } measureWidth && keywordHeight is { IsExact: false } measureHeight)
        {
            Size<float> measuredSize = tree.MeasureChildSizeBoth(
                node,
                Size.None,
                areaSize.AsOptions(),
                new Size<AvailableSpace>(measureWidth.MeasureSpace, measureHeight.MeasureSpace),
                sizingMode,
                Line.False);
            knownDimensions = measuredSize.AsOptions();
            return;
        }

        if (keywordWidth is SizingKeywordResolution widthResolution)
        {
            knownDimensions.Width = widthResolution.IsExact
                ? widthResolution.ExactSize
                : tree.MeasureChildSize(
                    node,
                    knownDimensions,
                    areaSize.AsOptions(),
                    new Size<AvailableSpace>(widthResolution.MeasureSpace, AvailableSpace.Definite(stretchSize.Height)),
                    sizingMode,
                    AbsoluteAxis.Horizontal,
                    Line.False);
        }

        if (keywordHeight is SizingKeywordResolution heightResolution)
        {
            knownDimensions.Height = heightResolution.IsExact
                ? heightResolution.ExactSize
                : tree.MeasureChildSize(
                    node,
                    knownDimensions,
                    areaSize.AsOptions(),
                    new Size<AvailableSpace>(
                        AvailableSpace.Definite(knownDimensions.Width ?? stretchSize.Width),
                        heightResolution.MeasureSpace),
                    sizingMode,
                    AbsoluteAxis.Vertical,
                    Line.False);
        }
    }
}
