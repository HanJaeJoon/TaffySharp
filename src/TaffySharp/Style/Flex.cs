namespace TaffySharp;

/// <summary>The set of styles required for a Flexbox container.</summary>
public interface IFlexboxContainerStyle : ICoreStyle
{
    FlexDirection FlexDirection { get; }

    FlexWrap FlexWrap { get; }

    /// <summary>The number of flex lines for <c>flex-wrap: balance</c> (CSS Flexbox Level 2 <c>flex-line-count</c>).</summary>
    ushort FlexLineCount { get; }

    Size<LengthPercentage> Gap { get; }

    AlignContent AlignContent { get; }

    AlignItems AlignItems { get; }

    AlignContent JustifyContent { get; }
}

/// <summary>The set of styles required for a Flexbox item (child of a Flexbox container).</summary>
public interface IFlexboxItemStyle : ICoreStyle
{
    Dimension FlexBasis { get; }

    float FlexGrow { get; }

    float FlexShrink { get; }

    AlignItems? AlignSelf { get; }
}

/// <summary>Controls whether flex items are forced onto one line or can wrap onto multiple lines.</summary>
public enum FlexWrap
{
    NoWrap,
    Wrap,
    WrapReverse,
    /// <summary>Wrap into balanced lines (<c>flex-wrap: balance</c>).</summary>
    Balance,
    /// <summary><c>flex-wrap: wrap-reverse balance</c>.</summary>
    BalanceReverse,
}

/// <summary>The direction of the flexbox layout main axis.</summary>
public enum FlexDirection
{
    Row,
    Column,
    RowReverse,
    ColumnReverse,
}

internal static class FlexExtensions
{
    public static bool IsMultiLine(this FlexWrap self) => self != FlexWrap.NoWrap;

    public static bool IsReverse(this FlexWrap self) => self is FlexWrap.WrapReverse or FlexWrap.BalanceReverse;

    public static bool IsBalance(this FlexWrap self) => self is FlexWrap.Balance or FlexWrap.BalanceReverse;

    public static bool IsRow(this FlexDirection self) => self is FlexDirection.Row or FlexDirection.RowReverse;

    public static bool IsColumn(this FlexDirection self) => self is FlexDirection.Column or FlexDirection.ColumnReverse;

    public static bool IsReverse(this FlexDirection self) => self is FlexDirection.RowReverse or FlexDirection.ColumnReverse;

    public static bool MainAxisIsRequestedAxis(this FlexDirection self, RequestedAxis axis) => axis switch
    {
        RequestedAxis.Horizontal => self.IsRow(),
        RequestedAxis.Vertical => self.IsColumn(),
        _ => false,
    };

    public static AbsoluteAxis MainAxis(this FlexDirection self) => self.IsRow() ? AbsoluteAxis.Horizontal : AbsoluteAxis.Vertical;

    public static AbsoluteAxis CrossAxis(this FlexDirection self) => self.IsRow() ? AbsoluteAxis.Vertical : AbsoluteAxis.Horizontal;
}
