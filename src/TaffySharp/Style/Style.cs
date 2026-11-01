namespace TaffySharp;

/// <summary>The core set of styles that are shared between all CSS layout nodes.</summary>
public interface ICoreStyle
{
    /// <summary>Which box generation mode should be used.</summary>
    BoxGenerationMode BoxGenerationMode { get; }

    /// <summary>Is block layout?</summary>
    bool IsBlock { get; }

    /// <summary>Is it a compressible replaced element? (https://drafts.csswg.org/css-sizing-3/#min-content-zero)</summary>
    bool IsCompressibleReplaced { get; }

    /// <summary>Is it a replaced element (such as an image or video)?</summary>
    bool IsReplaced { get; }

    BoxSizing BoxSizing { get; }

    Direction Direction { get; }

    Point<Overflow> Overflow { get; }

    float ScrollbarWidth { get; }

    Position Position { get; }

    Rect<LengthPercentageAuto> Inset { get; }

    /// <summary>Which kinds of out-of-flow descendants this node is the containing block for.</summary>
    ContainingBlockClaims IsContainingBlock { get; }

    Size<Dimension> Size { get; }

    Size<LengthPercentageAuto> MinSize { get; }

    Size<LengthPercentageAuto> MaxSize { get; }

    float? AspectRatio { get; }

    Rect<LengthPercentageAuto> Margin { get; }

    Rect<LengthPercentage> Padding { get; }

    Rect<LengthPercentage> Border { get; }

    Contain Contain { get; }
}

/// <summary>The styles an out-of-flow (absolutely positioned) box needs from its own style.</summary>
public interface IOofItemStyle : ICoreStyle
{
    AlignItems? AlignSelf { get; }

    AlignItems? JustifySelf { get; }

    bool IsTable { get; }

    Line<GridPlacement> GridRow { get; }

    Line<GridPlacement> GridColumn { get; }
}

/// <summary>Sets the layout used for the children of this node.</summary>
public enum Display
{
    /// <summary>The children will follow the block layout algorithm.</summary>
    Block,
    /// <summary>Block layout that always establishes a new block formatting context.</summary>
    FlowRoot,
    /// <summary>The children will follow the flexbox layout algorithm.</summary>
    Flex,
    /// <summary>The children will follow the CSS Grid layout algorithm.</summary>
    Grid,
    /// <summary>The node is hidden, and it's children will also be hidden.</summary>
    None,
}

/// <summary>An abstracted version of the CSS <c>display</c> property where any value other than <c>none</c> is represented by <c>Normal</c>.</summary>
public enum BoxGenerationMode
{
    Normal,
    None,
}

/// <summary>The positioning strategy for this item.</summary>
public enum Position
{
    Static,
    Relative,
    Absolute,
    Fixed,
    Sticky,
}

public static class PositionExtensions
{
    /// <summary>Whether boxes with this position are taken out of the normal flow (absolute and fixed).</summary>
    public static bool IsOutOfFlow(this Position self) => self is Position.Absolute or Position.Fixed;

    /// <summary>Whether this is a positioned value (anything but static).</summary>
    public static bool IsPositioned(this Position self) => self != Position.Static;
}

/// <summary>Which kinds of out-of-flow boxes a node is the containing block for.</summary>
public readonly struct ContainingBlockClaims : IEquatable<ContainingBlockClaims>
{
    public ContainingBlockClaims(bool absolute, bool @fixed)
    {
        Absolute = absolute;
        Fixed = @fixed;
    }

    public bool Absolute { get; }

    public bool Fixed { get; }

    public static ContainingBlockClaims None => new(false, false);

    public static ContainingBlockClaims All => new(true, true);

    public bool ForPosition(Position position) => position switch
    {
        Position.Absolute => Absolute,
        Position.Fixed => Fixed,
        _ => false,
    };

    public bool Equals(ContainingBlockClaims other) => Absolute == other.Absolute && Fixed == other.Fixed;

    public override bool Equals(object? obj) => obj is ContainingBlockClaims other && Equals(other);

    public override int GetHashCode() => (Absolute ? 1 : 0) | (Fixed ? 2 : 0);
}

/// <summary>Specifies whether size styles for this node are assigned to the node's "content box" or "border box".</summary>
public enum BoxSizing
{
    BorderBox,
    ContentBox,
}

/// <summary>How children overflowing their container should affect layout.</summary>
public enum Overflow
{
    Visible,
    Clip,
    Hidden,
    Scroll,
}

public static class OverflowExtensions
{
    /// <summary>Whether this overflow value makes the node a scroll container.</summary>
    public static bool IsScrollContainer(this Overflow self) => self is Overflow.Hidden or Overflow.Scroll;

    /// <summary>Returns <c>0</c> for scroll containers, <c>null</c> otherwise (the automatic minimum size).</summary>
    internal static float? MaybeIntoAutomaticMinSize(this Overflow self) => self.IsScrollContainer() ? 0f : null;
}

/// <summary>The CSS <c>contain</c> property (only <c>layout</c> and <c>paint</c> affect layout).</summary>
public readonly struct Contain : IEquatable<Contain>
{
    private readonly byte _bits;

    private Contain(byte bits) => _bits = bits;

    public static Contain None => new(0);

    public static Contain Layout => new(1 << 0);

    public static Contain Paint => new(1 << 1);

    public static Contain Content => new((1 << 0) | (1 << 1));

    public bool Contains(Contain other) => (_bits & other._bits) == other._bits;

    public bool Intersects(Contain other) => (_bits & other._bits) != 0;

    public Contain Union(Contain other) => new((byte)(_bits | other._bits));

    public static Contain operator |(Contain a, Contain b) => a.Union(b);

    /// <summary>Whether this establishes an independent formatting context.</summary>
    public bool EstablishesIndependentFormattingContext() => Intersects(Layout.Union(Paint));

    /// <summary>Whether layout containment suppresses the box's baselines.</summary>
    public bool SuppressesBaseline() => Contains(Layout);

    /// <summary>Whether descendant overflow is contained (it does not contribute to ancestors' scrollable overflow).</summary>
    public bool ContainsScrollableOverflow() => Intersects(Layout.Union(Paint));

    public bool Equals(Contain other) => _bits == other._bits;

    public override bool Equals(object? obj) => obj is Contain other && Equals(other);

    public override int GetHashCode() => _bits;

    public static bool operator ==(Contain a, Contain b) => a.Equals(b);

    public static bool operator !=(Contain a, Contain b) => !a.Equals(b);
}

/// <summary>The inline base direction (<c>direction</c> property).</summary>
public enum Direction
{
    Ltr,
    Rtl,
}

public static class DirectionExtensions
{
    public static bool IsRtl(this Direction self) => self == Direction.Rtl;
}

/// <summary>
/// A typed representation of the CSS style information for a single node.
/// The default values match the defaults of the CSS spec, except <see cref="Display"/> which
/// defaults to <see cref="Display.Flex"/> like upstream.
/// </summary>
public sealed class Style : ICoreStyle, IOofItemStyle, IBlockContainerStyle, IBlockItemStyle,
    IFlexboxContainerStyle, IFlexboxItemStyle, IGridContainerStyle, IGridItemStyle
{
    /// <summary>A new style with all default values (Rust <c>Style::DEFAULT</c>).</summary>
    public static Style Default => new();

    public Display Display { get; set; } = Display.Flex;

    /// <summary>Whether the item is a table (for <see cref="IOofItemStyle.IsTable"/> and <see cref="IBlockItemStyle.IsTable"/>).</summary>
    public bool ItemIsTable { get; set; }

    /// <summary>Whether the item is a replaced element (such as an image or video).</summary>
    public bool ItemIsReplaced { get; set; }

    /// <summary>Whether the item is a compressible replaced element.</summary>
    public bool ItemIsCompressibleReplaced { get; set; }

    public BoxSizing BoxSizing { get; set; } = BoxSizing.BorderBox;

    public Direction Direction { get; set; } = Direction.Ltr;

    public Point<Overflow> Overflow { get; set; } = new(TaffySharp.Overflow.Visible, TaffySharp.Overflow.Visible);

    /// <summary>How much space (in points) should be reserved for the scrollbars of <c>Overflow.Scroll</c> nodes.</summary>
    public float ScrollbarWidth { get; set; }

    public Contain Contain { get; set; } = TaffySharp.Contain.None;

    public Float Float { get; set; } = TaffySharp.Float.None;

    public Clear Clear { get; set; } = TaffySharp.Clear.None;

    /// <summary>
    /// Defaults to <see cref="Position.Static"/> as in CSS (older taffy versions defaulted to relative). An absolute
    /// box is laid out against its containing block, the nearest ancestor that is not <see cref="Position.Static"/>
    /// (or the root; fixed boxes always use the root), and its <see cref="Layout.Location"/> is relative to that
    /// ancestor, not to its parent. <see cref="TaffyTree{TNodeContext}.HoistedChildren"/> lists the out-of-flow
    /// boxes of each containing block.
    /// </summary>
    public Position Position { get; set; } = Position.Static;

    public Rect<LengthPercentageAuto> Inset { get; set; } = new(LengthPercentageAuto.Auto, LengthPercentageAuto.Auto, LengthPercentageAuto.Auto, LengthPercentageAuto.Auto);

    public Size<Dimension> Size { get; set; } = new(Dimension.Auto, Dimension.Auto);

    public Size<LengthPercentageAuto> MinSize { get; set; } = new(LengthPercentageAuto.Auto, LengthPercentageAuto.Auto);

    public Size<LengthPercentageAuto> MaxSize { get; set; } = new(LengthPercentageAuto.Auto, LengthPercentageAuto.Auto);

    /// <summary>Sets a preferred aspect ratio for the item (width / height).</summary>
    public float? AspectRatio { get; set; }

    public Rect<LengthPercentageAuto> Margin { get; set; } = new(LengthPercentageAuto.Zero, LengthPercentageAuto.Zero, LengthPercentageAuto.Zero, LengthPercentageAuto.Zero);

    public Rect<LengthPercentage> Padding { get; set; } = new(LengthPercentage.Zero, LengthPercentage.Zero, LengthPercentage.Zero, LengthPercentage.Zero);

    public Rect<LengthPercentage> Border { get; set; } = new(LengthPercentage.Zero, LengthPercentage.Zero, LengthPercentage.Zero, LengthPercentage.Zero);

    public AlignItems AlignItems { get; set; } = AlignItems.Normal;

    public AlignItems? AlignSelf { get; set; }

    public AlignItems JustifyItems { get; set; } = AlignItems.Normal;

    public AlignItems? JustifySelf { get; set; }

    public AlignContent AlignContent { get; set; } = AlignContent.Normal;

    public AlignContent JustifyContent { get; set; } = AlignContent.Normal;

    /// <summary>How large should the gaps between items in a grid or flex container be?</summary>
    public Size<LengthPercentage> Gap { get; set; } = new(LengthPercentage.Zero, LengthPercentage.Zero);

    public TextAlign TextAlign { get; set; } = TextAlign.Auto;

    public FlexDirection FlexDirection { get; set; } = FlexDirection.Row;

    public FlexWrap FlexWrap { get; set; } = FlexWrap.NoWrap;

    public ushort FlexLineCount { get; set; } = 1;

    public Dimension FlexBasis { get; set; } = Dimension.Auto;

    public float FlexGrow { get; set; }

    public float FlexShrink { get; set; } = 1f;

    public List<GridTemplateComponent> GridTemplateRows { get; set; } = new();

    public List<GridTemplateComponent> GridTemplateColumns { get; set; } = new();

    public List<TrackSizingFunction> GridAutoRows { get; set; } = new();

    public List<TrackSizingFunction> GridAutoColumns { get; set; } = new();

    public GridAutoFlow GridAutoFlow { get; set; } = GridAutoFlow.Row;

    public GridTemplateAreas? GridTemplateAreas { get; set; }

    /// <summary>The named lines between the columns (one list of names per line).</summary>
    public List<List<string>> GridTemplateColumnNames { get; set; } = new();

    /// <summary>The named lines between the rows (one list of names per line).</summary>
    public List<List<string>> GridTemplateRowNames { get; set; } = new();

    public Line<GridPlacement> GridRow { get; set; } = new(GridPlacement.Auto, GridPlacement.Auto);

    public Line<GridPlacement> GridColumn { get; set; } = new(GridPlacement.Auto, GridPlacement.Auto);

    // CoreStyle members that are derived from other properties
    public BoxGenerationMode BoxGenerationMode => Display == Display.None ? BoxGenerationMode.None : BoxGenerationMode.Normal;

    public bool IsBlock => Display == Display.Block;

    public bool IsCompressibleReplaced => ItemIsReplaced || ItemIsCompressibleReplaced;

    public bool IsReplaced => ItemIsReplaced;

    public bool IsTable => ItemIsTable;

    public ContainingBlockClaims IsContainingBlock => new(Position.IsPositioned(), false);

    IReadOnlyList<GridTemplateComponent>? IGridContainerStyle.GridTemplateRows => GridTemplateRows;

    IReadOnlyList<GridTemplateComponent>? IGridContainerStyle.GridTemplateColumns => GridTemplateColumns;

    IReadOnlyList<TrackSizingFunction> IGridContainerStyle.GridAutoRows => GridAutoRows;

    IReadOnlyList<TrackSizingFunction> IGridContainerStyle.GridAutoColumns => GridAutoColumns;

    IReadOnlyList<GridTemplateArea>? IGridContainerStyle.GridTemplateAreas => GridTemplateAreas?.Areas;

    ushort IGridContainerStyle.GridTemplateAreaRowCount => GridTemplateAreas?.RowCount ?? 0;

    ushort IGridContainerStyle.GridTemplateAreaColumnCount => GridTemplateAreas?.ColumnCount ?? 0;

    IReadOnlyList<IReadOnlyList<string>>? IGridContainerStyle.GridTemplateColumnNames => GridTemplateColumnNames;

    IReadOnlyList<IReadOnlyList<string>>? IGridContainerStyle.GridTemplateRowNames => GridTemplateRowNames;

    /// <summary>A copy of this style; the grid lists are copied too.</summary>
    public Style Clone()
    {
        Style clone = (Style)MemberwiseClone();
        clone.GridTemplateRows = new(GridTemplateRows);
        clone.GridTemplateColumns = new(GridTemplateColumns);
        clone.GridAutoRows = new(GridAutoRows);
        clone.GridAutoColumns = new(GridAutoColumns);
        clone.GridTemplateColumnNames = GridTemplateColumnNames.Select(names => new List<string>(names)).ToList();
        clone.GridTemplateRowNames = GridTemplateRowNames.Select(names => new List<string>(names)).ToList();
        return clone;
    }
}
