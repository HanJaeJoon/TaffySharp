namespace TaffySharp;

/// <summary>The intermediate results of a flexbox calculation for a single item.</summary>
internal sealed class FlexItem
{
    /// <summary>The identifier for the associated node.</summary>
    public NodeId Node;

    /// <summary>The order of the node relative to its siblings.</summary>
    public uint Order;

    /// <summary>The base size of this item.</summary>
    public Size<float?> Size;

    /// <summary>The size style of this item (used for sizing keywords).</summary>
    public Size<Dimension> SizeStyle;

    /// <summary>The minimum allowable size of this item.</summary>
    public Size<float?> MinSize;

    /// <summary>The maximum allowable size of this item.</summary>
    public Size<float?> MaxSize;

    /// <summary>The preferred aspect ratio.</summary>
    public float? AspectRatio;

    /// <summary>The cross-alignment of this item.</summary>
    public AlignItems AlignSelf;

    /// <summary>The overflow style of the item.</summary>
    public Point<Overflow> Overflow;

    /// <summary>The containment of the item.</summary>
    public Contain Contain;

    /// <summary>The width of the scrollbars (if it has any).</summary>
    public float ScrollbarWidth;

    /// <summary>The flex shrink style of the item.</summary>
    public float FlexShrink;

    /// <summary>The flex grow style of the item.</summary>
    public float FlexGrow;

    /// <summary>Whether the flex base size is definite.</summary>
    public bool FlexBasisIsDefinite;

    /// <summary>The minimum size of the item. This differs from MinSize above because it also takes into account content based automatic minimum sizes.</summary>
    public float ResolvedMinimumMainSize;

    /// <summary>The final offset of this item (relative position).</summary>
    public Size<float> RelativeInset;

    /// <summary>The margin of this item.</summary>
    public Rect<float> Margin;

    /// <summary>Whether each margin is an auto margin or not.</summary>
    public Rect<bool> MarginIsAuto;

    /// <summary>The padding of this item.</summary>
    public Rect<float> Padding;

    /// <summary>The border of this item.</summary>
    public Rect<float> Border;

    /// <summary>The default size of this item.</summary>
    public float FlexBasis;

    /// <summary>The default size of this item, minus padding and border.</summary>
    public float InnerFlexBasis;

    /// <summary>The amount by which this item has deviated from its target size.</summary>
    public float Violation;

    /// <summary>Is the size of this item locked.</summary>
    public bool Frozen;

    /// <summary>Either the max- or min- content flex fraction.</summary>
    public float ContentFlexFraction;

    /// <summary>The proposed inner size of this item.</summary>
    public Size<float> HypotheticalInnerSize;

    /// <summary>The proposed outer size of this item.</summary>
    public Size<float> HypotheticalOuterSize;

    /// <summary>The size that this item wants to be.</summary>
    public Size<float> TargetSize;

    /// <summary>The size that this item wants to be, plus any padding and border.</summary>
    public Size<float> OuterTargetSize;

    /// <summary>The position of the bottom edge of this item.</summary>
    public float Baseline;

    /// <summary>A temporary value for the main offset.</summary>
    public float OffsetMain;

    /// <summary>A temporary value for the cross offset.</summary>
    public float OffsetCross;

    /// <summary>Out-of-flow candidates bubbled up from the item's subtree.</summary>
    public OofCandidates OofCandidates;

    /// <summary>Returns the box sizing adjustment (padding + border for content-box sizing).</summary>
    public Size<float> BoxSizingAdjustment(ICoreStyle style) =>
        style.BoxSizing == BoxSizing.ContentBox ? Padding.Add(Border).SumAxes() : TaffySharp.Size.Zero;

    /// <summary>Returns true if the item is a scroll container.</summary>
    public bool IsScrollContainer() => Overflow.X.IsScrollContainer() | Overflow.Y.IsScrollContainer();

    /// <summary>Whether the item stretches to fill its flex line in the cross axis.</summary>
    public bool IsStretched(FlexDirection dir)
    {
        Dimension crossSize = SizeStyle.Cross(dir);
        return !MarginIsAuto.CrossStart(dir)
            && !MarginIsAuto.CrossEnd(dir)
            && (crossSize.IsStretch() || (AlignSelf == AlignItems.Stretch && crossSize.IsAuto()));
    }

    /// <summary>Whether the item participates in baseline alignment.</summary>
    public bool ParticipatesInBaselineAlignment(FlexDirection dir) =>
        AlignSelf == AlignItems.Baseline && !MarginIsAuto.CrossStart(dir) && !MarginIsAuto.CrossEnd(dir);
}

/// <summary>A line of flex items (a contiguous run of the container's items in document order).</summary>
internal sealed class FlexLine
{
    public FlexLine(List<FlexItem> items) => Items = items;

    /// <summary>The items in this line.</summary>
    public List<FlexItem> Items;

    /// <summary>The dimensions of the cross-axis.</summary>
    public float CrossSize;

    /// <summary>The relative offset of the cross-axis.</summary>
    public float OffsetCross;
}

/// <summary>Values that are constant for the duration of the flex algorithm (some are filled in as it runs).</summary>
internal sealed class AlgoConstants
{
    public FlexDirection Dir;
    public Direction LayoutDirection;
    public bool IsRow;
    public bool IsColumn;
    public bool IsWrap;
    public bool IsWrapReverse;
    public bool IsBalance;
    public ushort? LineCount;
    public Size<float?> MinSize;
    public Size<float?> MaxSize;
    public Rect<float> Border;
    public Rect<float> ContentBoxInset;
    public Point<float> ScrollbarGutter;
    public bool IsScrollContainer;
    public Size<float> Gap;
    public AlignItems AlignItems;
    public AlignContent AlignContent;
    public AlignContent JustifyContent;
    public Size<float?> NodeOuterSize;
    public Size<float?> NodeInnerSize;
    public bool KnownMainSizeIsDefinite;
    public bool HasDefiniteMainSize;
    public bool HasDefiniteCrossSize;
    public bool CrossAxisAvailableSpaceIsDefinite;
    public Size<float> ContainerSize;
    public Size<float> InnerContainerSize;

    /// <summary>
    /// The cross space available to one line: with <c>flex-line-count</c> above one, the space is
    /// divided between that many lines (minus the gaps between them).
    /// </summary>
    public float DividedCrossSpace(float crossAvailableSpace)
    {
        if (LineCount is ushort lineCount && lineCount > 1)
        {
            float count = lineCount;
            return (crossAvailableSpace - ((count - 1f) * Gap.Cross(Dir))) / count;
        }

        return crossAvailableSpace;
    }
}
