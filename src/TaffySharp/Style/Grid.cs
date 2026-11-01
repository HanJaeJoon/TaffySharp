namespace TaffySharp;

/// <summary>Defines a grid area (<c>grid-template-areas</c>).</summary>
public sealed class GridTemplateAreas
{
    public List<GridTemplateArea> Areas { get; set; } = new();

    public ushort RowCount { get; set; }

    public ushort ColumnCount { get; set; }
}

/// <summary>A named grid area. Line indexes are 1-based and the end lines are exclusive.</summary>
public readonly struct GridTemplateArea : IEquatable<GridTemplateArea>
{
    public GridTemplateArea(string name, ushort rowStart, ushort rowEnd, ushort columnStart, ushort columnEnd)
    {
        Name = name;
        RowStart = rowStart;
        RowEnd = rowEnd;
        ColumnStart = columnStart;
        ColumnEnd = columnEnd;
    }

    public string Name { get; }

    public ushort RowStart { get; }

    public ushort RowEnd { get; }

    public ushort ColumnStart { get; }

    public ushort ColumnEnd { get; }

    public bool Equals(GridTemplateArea other) =>
        Name == other.Name && RowStart == other.RowStart && RowEnd == other.RowEnd && ColumnStart == other.ColumnStart && ColumnEnd == other.ColumnEnd;

    public override bool Equals(object? obj) => obj is GridTemplateArea other && Equals(other);

    public override int GetHashCode() => (Name, RowStart, RowEnd, ColumnStart, ColumnEnd).GetHashCode();
}

/// <summary>A named grid line.</summary>
public readonly struct NamedGridLine
{
    public NamedGridLine(string name, ushort index)
    {
        Name = name;
        Index = index;
    }

    public string Name { get; }

    /// <summary>The 1-based index of the line.</summary>
    public ushort Index { get; }
}

internal enum GridAreaAxis
{
    Row,
    Column,
}

internal enum GridAreaEnd
{
    Start,
    End,
}

/// <summary>The set of styles required for a CSS Grid container.</summary>
public interface IGridContainerStyle : ICoreStyle
{
    /// <summary>The track sizing functions of the explicit rows (<c>null</c> means none).</summary>
    IReadOnlyList<GridTemplateComponent>? GridTemplateRows { get; }

    IReadOnlyList<GridTemplateComponent>? GridTemplateColumns { get; }

    IReadOnlyList<TrackSizingFunction> GridAutoRows { get; }

    IReadOnlyList<TrackSizingFunction> GridAutoColumns { get; }

    IReadOnlyList<GridTemplateArea>? GridTemplateAreas { get; }

    ushort GridTemplateAreaRowCount { get; }

    ushort GridTemplateAreaColumnCount { get; }

    /// <summary>The line names of each explicit column line (one list per line).</summary>
    IReadOnlyList<IReadOnlyList<string>>? GridTemplateColumnNames { get; }

    IReadOnlyList<IReadOnlyList<string>>? GridTemplateRowNames { get; }

    GridAutoFlow GridAutoFlow { get; }

    Size<LengthPercentage> Gap { get; }

    AlignContent AlignContent { get; }

    AlignContent JustifyContent { get; }

    AlignItems AlignItems { get; }

    AlignItems JustifyItems { get; }
}

/// <summary>The set of styles required for a CSS Grid item (child of a CSS Grid container).</summary>
public interface IGridItemStyle : ICoreStyle
{
    Line<GridPlacement> GridRow { get; }

    Line<GridPlacement> GridColumn { get; }

    AlignItems? AlignSelf { get; }

    AlignItems? JustifySelf { get; }
}

public static class GridStyleExtensions
{
    /// <summary>Get a grid container's explicit track sizing functions for the specified axis.</summary>
    public static IReadOnlyList<GridTemplateComponent>? GridTemplateTracks(this IGridContainerStyle style, AbsoluteAxis axis) =>
        axis == AbsoluteAxis.Horizontal ? style.GridTemplateColumns : style.GridTemplateRows;

    /// <summary>Get a grid container's alignment for the specified axis (<c>normal</c> behaves as <c>stretch</c>).</summary>
    public static AlignContent GridAlignContent(this IGridContainerStyle style, AbstractAxis axis)
    {
        AlignContent alignment = axis == AbstractAxis.Inline ? style.JustifyContent : style.AlignContent;
        return alignment.Keyword == AlignContentKeyword.Normal ? AlignContent.Stretch : alignment;
    }

    /// <summary>Get a grid item's row or column placement depending on the axis passed.</summary>
    public static Line<GridPlacement> GridPlacement(this IGridItemStyle style, AbsoluteAxis axis) =>
        axis == AbsoluteAxis.Horizontal ? style.GridColumn : style.GridRow;
}

/// <summary>Controls whether grid items are placed row-wise or column-wise, and whether the sparse or dense packing algorithm is used.</summary>
public enum GridAutoFlow
{
    Row,
    Column,
    RowDense,
    ColumnDense,
}

public static class GridAutoFlowExtensions
{
    public static bool IsDense(this GridAutoFlow self) => self is GridAutoFlow.RowDense or GridAutoFlow.ColumnDense;

    public static AbsoluteAxis PrimaryAxis(this GridAutoFlow self) =>
        self is GridAutoFlow.Row or GridAutoFlow.RowDense ? AbsoluteAxis.Horizontal : AbsoluteAxis.Vertical;
}

internal enum GenericGridPlacementKind : byte
{
    Auto,
    Line,
    Span,
}

/// <summary>A grid line placement specification, generic over the coordinate system (no named lines).</summary>
internal readonly struct GenericGridPlacement<TLine> : IEquatable<GenericGridPlacement<TLine>>
    where TLine : struct, IEquatable<TLine>
{
    private GenericGridPlacement(GenericGridPlacementKind kind, TLine line, ushort span)
    {
        Kind = kind;
        LineValue = line;
        SpanValue = span;
    }

    public GenericGridPlacementKind Kind { get; }

    public TLine LineValue { get; }

    public ushort SpanValue { get; }

    public static GenericGridPlacement<TLine> Auto => default;

    public static GenericGridPlacement<TLine> Line(TLine line) => new(GenericGridPlacementKind.Line, line, 0);

    public static GenericGridPlacement<TLine> Span(ushort span) => new(GenericGridPlacementKind.Span, default, span);

    public bool IsAuto => Kind == GenericGridPlacementKind.Auto;

    public bool IsLine => Kind == GenericGridPlacementKind.Line;

    public bool IsSpan => Kind == GenericGridPlacementKind.Span;

    public bool Equals(GenericGridPlacement<TLine> other) =>
        Kind == other.Kind && LineValue.Equals(other.LineValue) && SpanValue == other.SpanValue;

    public override bool Equals(object? obj) => obj is GenericGridPlacement<TLine> other && Equals(other);

    public override int GetHashCode() => (Kind, LineValue, SpanValue).GetHashCode();

    public override string ToString() => Kind switch
    {
        GenericGridPlacementKind.Line => $"Line({LineValue})",
        GenericGridPlacementKind.Span => $"Span({SpanValue})",
        _ => "Auto",
    };
}

public enum GridPlacementKind : byte
{
    Auto,
    Line,
    NamedLine,
    Span,
    NamedSpan,
}

/// <summary>
/// A grid line placement specification (<c>grid-row-start</c>, <c>grid-column-end</c>, ...).
/// Defaults to <c>auto</c>.
/// </summary>
public readonly struct GridPlacement : IEquatable<GridPlacement>
{
    private GridPlacement(GridPlacementKind kind, short line, ushort span, string? name)
    {
        Kind = kind;
        LineIndex = line;
        SpanCount = span;
        Name = name;
    }

    public GridPlacementKind Kind { get; }

    /// <summary>The line index for <see cref="GridPlacementKind.Line"/> and <see cref="GridPlacementKind.NamedLine"/>.</summary>
    public short LineIndex { get; }

    /// <summary>The span for <see cref="GridPlacementKind.Span"/> and <see cref="GridPlacementKind.NamedSpan"/>.</summary>
    public ushort SpanCount { get; }

    /// <summary>The line name for the named variants.</summary>
    public string? Name { get; }

    public static GridPlacement Auto => default;

    /// <summary>Place the item at the specified line (1-based; negative counts from the end).</summary>
    public static GridPlacement Line(short index) => new(GridPlacementKind.Line, index, 0, null);

    /// <summary>Place the item at the nth line with the given name.</summary>
    public static GridPlacement NamedLine(string name, short index) => new(GridPlacementKind.NamedLine, index, 0, name);

    /// <summary>The item spans the specified number of tracks.</summary>
    public static GridPlacement Span(ushort span) => new(GridPlacementKind.Span, 0, span, null);

    /// <summary>The item spans until the nth line with the given name.</summary>
    public static GridPlacement NamedSpan(string name, ushort span) => new(GridPlacementKind.NamedSpan, 0, span, name);

    public GridLine GridLine => new(LineIndex);

    /// <summary>Apply a mapping function if the placement is a line, ignoring named lines and spans (they become <c>auto</c>).</summary>
    internal GenericGridPlacement<OriginZeroLine> IntoOriginZeroPlacementIgnoringNamed(ushort explicitTrackCount) => Kind switch
    {
        GridPlacementKind.Auto => GenericGridPlacement<OriginZeroLine>.Auto,
        // Spans are clamped between 1 (a zero span is an invalid value which is treated as 1)
        // and the maximum track limit (https://www.w3.org/TR/css-grid-1/#overlarge-grids)
        GridPlacementKind.Span => GenericGridPlacement<OriginZeroLine>.Span(ClampSpan(SpanCount)),
        // Grid line zero is an invalid index, so it gets treated as Auto
        GridPlacementKind.Line => LineIndex == 0
            ? GenericGridPlacement<OriginZeroLine>.Auto
            : GenericGridPlacement<OriginZeroLine>.Line(GridLine.IntoOriginZeroLine(explicitTrackCount)),
        _ => GenericGridPlacement<OriginZeroLine>.Auto,
    };

    internal static ushort ClampSpan(ushort span) => (ushort)MathHelpers.Clamp(span, 1, GridConstants.MaxGridTracks);

    public bool Equals(GridPlacement other) =>
        Kind == other.Kind && LineIndex == other.LineIndex && SpanCount == other.SpanCount && Name == other.Name;

    public override bool Equals(object? obj) => obj is GridPlacement other && Equals(other);

    public override int GetHashCode() => (Kind, LineIndex, SpanCount, Name).GetHashCode();

    public static bool operator ==(GridPlacement a, GridPlacement b) => a.Equals(b);

    public static bool operator !=(GridPlacement a, GridPlacement b) => !a.Equals(b);

    public override string ToString() => Kind switch
    {
        GridPlacementKind.Line => $"Line({LineIndex})",
        GridPlacementKind.NamedLine => $"NamedLine({Name}, {LineIndex})",
        GridPlacementKind.Span => $"Span({SpanCount})",
        GridPlacementKind.NamedSpan => $"NamedSpan({Name}, {SpanCount})",
        _ => "Auto",
    };
}

internal static class GridPlacementExtensions
{
    public static Line<GenericGridPlacement<OriginZeroLine>> IntoOriginZeroIgnoringNamed(this Line<GridPlacement> self, ushort explicitTrackCount) =>
        new(self.Start.IntoOriginZeroPlacementIgnoringNamed(explicitTrackCount), self.End.IntoOriginZeroPlacementIgnoringNamed(explicitTrackCount));

    /// <summary>Whether the placement is definite (at least one side is a non-zero line or a named line).</summary>
    public static bool IsDefinite(this Line<GridPlacement> self)
    {
        if (self.Start.Kind == GridPlacementKind.Line && self.Start.LineIndex != 0)
        {
            return true;
        }

        if (self.End.Kind == GridPlacementKind.Line && self.End.LineIndex != 0)
        {
            return true;
        }

        return self.Start.Kind == GridPlacementKind.NamedLine || self.End.Kind == GridPlacementKind.NamedLine;
    }

    public static GenericGridPlacement<OriginZeroLine> IntoOriginZeroPlacement(this GenericGridPlacement<GridLine> self, ushort explicitTrackCount) => self.Kind switch
    {
        GenericGridPlacementKind.Auto => GenericGridPlacement<OriginZeroLine>.Auto,
        GenericGridPlacementKind.Span => GenericGridPlacement<OriginZeroLine>.Span(GridPlacement.ClampSpan(self.SpanValue)),
        _ => self.LineValue.Value == 0
            ? GenericGridPlacement<OriginZeroLine>.Auto
            : GenericGridPlacement<OriginZeroLine>.Line(self.LineValue.IntoOriginZeroLine(explicitTrackCount)),
    };

    public static bool IsDefinite(this Line<GenericGridPlacement<GridLine>> self) =>
        (self.Start.IsLine && self.Start.LineValue.Value != 0) || (self.End.IsLine && self.End.LineValue.Value != 0);

    public static Line<GenericGridPlacement<OriginZeroLine>> IntoOriginZero(this Line<GenericGridPlacement<GridLine>> self, ushort explicitTrackCount) =>
        new(self.Start.IntoOriginZeroPlacement(explicitTrackCount), self.End.IntoOriginZeroPlacement(explicitTrackCount));

    /// <summary>For an indefinite placement, the number of tracks it spans.</summary>
    public static ushort IndefiniteSpan<TLine>(this Line<GenericGridPlacement<TLine>> self)
        where TLine : struct, IEquatable<TLine>
    {
        GenericGridPlacement<TLine> s = self.Start;
        GenericGridPlacement<TLine> e = self.End;
        if (s.IsLine && e.IsLine)
        {
            throw new InvalidOperationException("indefinite_span should only be called on indefinite grid tracks");
        }

        if (s.IsSpan)
        {
            return s.SpanValue;
        }

        if (e.IsSpan)
        {
            return e.SpanValue;
        }

        return 1;
    }

    public static bool IsDefinite(this Line<GenericGridPlacement<OriginZeroLine>> self) => self.Start.IsLine || self.End.IsLine;

    /// <summary>Resolves a definite placement into a pair of lines.</summary>
    public static Line<OriginZeroLine> ResolveDefiniteGridLines(this Line<GenericGridPlacement<OriginZeroLine>> self)
    {
        GenericGridPlacement<OriginZeroLine> s = self.Start;
        GenericGridPlacement<OriginZeroLine> e = self.End;
        if (s.IsLine && e.IsLine)
        {
            OriginZeroLine line1 = s.LineValue;
            OriginZeroLine line2 = e.LineValue;
            return line1 == line2
                ? new(line1, line1 + 1)
                : new(OriginZeroLine.Min(line1, line2), OriginZeroLine.Max(line1, line2));
        }

        if (s.IsLine && e.IsSpan)
        {
            return new(s.LineValue, s.LineValue + e.SpanValue);
        }

        if (s.IsLine && e.IsAuto)
        {
            return new(s.LineValue, s.LineValue + 1);
        }

        if (s.IsSpan && e.IsLine)
        {
            return new(e.LineValue - s.SpanValue, e.LineValue);
        }

        if (s.IsAuto && e.IsLine)
        {
            return new(e.LineValue - 1, e.LineValue);
        }

        throw new InvalidOperationException("resolve_definite_grid_tracks should only be called on definite grid tracks");
    }

    /// <summary>Resolves the lines of an absolutely positioned item (either side may stay undefined).</summary>
    public static Line<OriginZeroLine?> ResolveAbsolutelyPositionedGridTracks(this Line<GenericGridPlacement<OriginZeroLine>> self)
    {
        GenericGridPlacement<OriginZeroLine> s = self.Start;
        GenericGridPlacement<OriginZeroLine> e = self.End;
        if (s.IsLine && e.IsLine)
        {
            OriginZeroLine track1 = s.LineValue;
            OriginZeroLine track2 = e.LineValue;
            return track1 == track2
                ? new(track1, track1 + 1)
                : new(OriginZeroLine.Min(track1, track2), OriginZeroLine.Max(track1, track2));
        }

        if (s.IsLine && e.IsSpan)
        {
            return new(s.LineValue, s.LineValue + e.SpanValue);
        }

        if (s.IsLine && e.IsAuto)
        {
            return new(s.LineValue, null);
        }

        if (s.IsSpan && e.IsLine)
        {
            return new(e.LineValue - s.SpanValue, e.LineValue);
        }

        if (s.IsAuto && e.IsLine)
        {
            return new(null, e.LineValue);
        }

        return new(null, null);
    }

    /// <summary>Resolves an indefinite placement starting at the given line.</summary>
    public static Line<OriginZeroLine> ResolveIndefiniteGridTracks(this Line<GenericGridPlacement<OriginZeroLine>> self, OriginZeroLine start)
    {
        GenericGridPlacement<OriginZeroLine> s = self.Start;
        GenericGridPlacement<OriginZeroLine> e = self.End;
        if (s.IsAuto && e.IsAuto)
        {
            return new(start, start + 1);
        }

        if (s.IsSpan && (e.IsAuto || e.IsSpan))
        {
            return new(start, start + s.SpanValue);
        }

        if (s.IsAuto && e.IsSpan)
        {
            return new(start, start + e.SpanValue);
        }

        throw new InvalidOperationException("resolve_indefinite_grid_tracks should only be called on indefinite grid tracks");
    }
}

/// <summary>The maximum size of a grid track (the second argument of <c>minmax()</c>).</summary>
public readonly struct MaxTrackSizingFunction : IEquatable<MaxTrackSizingFunction>
{
    private MaxTrackSizingFunction(CompactLength raw) => Raw = raw;

    public CompactLength Raw { get; }

    public static MaxTrackSizingFunction Zero => new(CompactLength.Zero);

    public static MaxTrackSizingFunction Auto => new(CompactLength.Auto);

    public static MaxTrackSizingFunction MinContent => new(CompactLength.MinContent);

    public static MaxTrackSizingFunction MaxContent => new(CompactLength.MaxContent);

    public static MaxTrackSizingFunction Length(float val) => new(CompactLength.Length(val));

    public static MaxTrackSizingFunction Percent(float val) => new(CompactLength.Percent(val));

    public static MaxTrackSizingFunction FitContentPx(float limit) => new(CompactLength.FitContentPx(limit));

    public static MaxTrackSizingFunction FitContentPercent(float limit) => new(CompactLength.FitContentPercent(limit));

    public static MaxTrackSizingFunction FitContent(LengthPercentage argument) => new(CompactLength.FitContent(argument));

    public static MaxTrackSizingFunction Fr(float val) => new(CompactLength.Fr(val));

    public static MaxTrackSizingFunction Calc(object calc) => new(CompactLength.Calc(calc));

    public static MaxTrackSizingFunction FitContentCalc(object calc) => new(CompactLength.FitContentCalc(calc));

    public static MaxTrackSizingFunction FromRaw(CompactLength val) => new(val);

    public static implicit operator MaxTrackSizingFunction(LengthPercentage input) => new(input.Raw);

    public static implicit operator MaxTrackSizingFunction(LengthPercentageAuto input) => new(input.Raw);

    /// <summary>Dimension values that are not valid max track sizing functions map to <c>auto</c>.</summary>
    public static implicit operator MaxTrackSizingFunction(Dimension input) => input.Raw.Tag switch
    {
        CompactLength.FitContentKeywordTag or CompactLength.StretchTag or CompactLength.ContentTag => Auto,
        _ => new(input.Raw),
    };

    public static MaxTrackSizingFunction From(MinTrackSizingFunction input) => new(input.Raw);

    public bool IsIntrinsic() => Raw.IsIntrinsic();

    public bool IsMaxContentAlike() => Raw.IsMaxContentAlike();

    public bool IsFr() => Raw.IsFr();

    public bool IsAuto() => Raw.IsAuto();

    public bool IsMinContent() => Raw.IsMinContent();

    public bool IsMaxContent() => Raw.IsMaxContent();

    public bool IsFitContent() => Raw.IsFitContent();

    public bool IsMaxOrFitContent() => Raw.IsMaxOrFitContent();

    /// <summary>Whether the value is definite given the parent size (lengths always; percentages and calc only with a parent size).</summary>
    public bool HasDefiniteValue(float? parentSize) => Raw.Tag switch
    {
        CompactLength.LengthTag => true,
        CompactLength.PercentTag => parentSize.HasValue,
        _ when Raw.IsPlainCalc() => parentSize.HasValue,
        _ => false,
    };

    public float? DefiniteValue(float? parentSize, Func<object, float, float> calcResolver) => Raw.Tag switch
    {
        CompactLength.LengthTag => Raw.Value,
        CompactLength.PercentTag => parentSize * Raw.Value,
        _ when Raw.IsPlainCalc() => parentSize is float size ? calcResolver(Raw.CalcValue!, size) : null,
        _ => null,
    };

    public float? FitContentLimit(float? parentSize, Func<object, float, float> calcResolver) => Raw.Tag switch
    {
        CompactLength.FitContentPxTag => Raw.Value,
        CompactLength.FitContentPercentTag => parentSize * Raw.Value,
        _ when Raw.IsFitContentCalc() => parentSize is float size ? calcResolver(Raw.CalcValue!, size) : null,
        _ => null,
    };

    public float? DefiniteLimit(float? parentSize, Func<object, float, float> calcResolver) => Raw.Tag switch
    {
        CompactLength.FitContentPxTag => Raw.Value,
        CompactLength.FitContentPercentTag => parentSize * Raw.Value,
        _ when Raw.IsFitContentCalc() => parentSize is float size ? calcResolver(Raw.CalcValue!, size) : null,
        _ => DefiniteValue(parentSize, calcResolver),
    };

    public float? ResolvedPercentageSize(float parentSize, Func<object, float, float> calcResolver) => Raw.ResolvedPercentageSize(parentSize, calcResolver);

    public bool UsesPercentage() => Raw.UsesPercentage();

    public bool Equals(MaxTrackSizingFunction other) => Raw.Equals(other.Raw);

    public override bool Equals(object? obj) => obj is MaxTrackSizingFunction other && Equals(other);

    public override int GetHashCode() => Raw.GetHashCode();

    public static bool operator ==(MaxTrackSizingFunction a, MaxTrackSizingFunction b) => a.Equals(b);

    public static bool operator !=(MaxTrackSizingFunction a, MaxTrackSizingFunction b) => !a.Equals(b);

    public override string ToString() => Raw.ToString();
}

/// <summary>The minimum size of a grid track (the first argument of <c>minmax()</c>).</summary>
public readonly struct MinTrackSizingFunction : IEquatable<MinTrackSizingFunction>
{
    private MinTrackSizingFunction(CompactLength raw) => Raw = raw;

    public CompactLength Raw { get; }

    public static MinTrackSizingFunction Zero => new(CompactLength.Zero);

    public static MinTrackSizingFunction Auto => new(CompactLength.Auto);

    public static MinTrackSizingFunction MinContent => new(CompactLength.MinContent);

    public static MinTrackSizingFunction MaxContent => new(CompactLength.MaxContent);

    public static MinTrackSizingFunction Length(float val) => new(CompactLength.Length(val));

    public static MinTrackSizingFunction Percent(float val) => new(CompactLength.Percent(val));

    public static MinTrackSizingFunction Calc(object calc) => new(CompactLength.Calc(calc));

    public static MinTrackSizingFunction FromRaw(CompactLength val) => new(val);

    public static implicit operator MinTrackSizingFunction(LengthPercentage input) => new(input.Raw);

    public static implicit operator MinTrackSizingFunction(LengthPercentageAuto input) => new(input.Raw);

    /// <summary>Dimension values that are not valid min track sizing functions map to <c>auto</c>.</summary>
    public static implicit operator MinTrackSizingFunction(Dimension input) => input.Raw.Tag switch
    {
        CompactLength.FitContentPxTag or CompactLength.FitContentPercentTag or CompactLength.FitContentKeywordTag
            or CompactLength.StretchTag or CompactLength.ContentTag => Auto,
        _ when input.Raw.IsFitContentCalc() => Auto,
        _ => new(input.Raw),
    };

    /// <summary>Max values that are not valid min values (<c>fr</c>, <c>fit-content()</c>) map to <c>auto</c>.</summary>
    public static MinTrackSizingFunction From(MaxTrackSizingFunction input) =>
        input.IsFr() || input.IsFitContent() ? Auto : new(input.Raw);

    public bool IsIntrinsic() => Raw.IsIntrinsic();

    public bool IsMinOrMaxContent() => Raw.IsMinOrMaxContent();

    public bool IsFr() => Raw.IsFr();

    public bool IsAuto() => Raw.IsAuto();

    /// <summary>Whether the value behaves as <c>auto</c>: it is <c>auto</c>, or a percentage with no parent size to resolve against.</summary>
    public bool BehavesAsAuto(float? parentSize) => Raw.IsAuto() || (!parentSize.HasValue && Raw.UsesPercentage());

    public bool IsMinContent() => Raw.IsMinContent();

    public bool IsMaxContent() => Raw.IsMaxContent();

    public float? DefiniteValue(float? parentSize, Func<object, float, float> calcResolver) => Raw.Tag switch
    {
        CompactLength.LengthTag => Raw.Value,
        CompactLength.PercentTag => parentSize * Raw.Value,
        _ when Raw.IsPlainCalc() => parentSize is float size ? calcResolver(Raw.CalcValue!, size) : null,
        _ => null,
    };

    public float? ResolvedPercentageSize(float parentSize, Func<object, float, float> calcResolver) => Raw.ResolvedPercentageSize(parentSize, calcResolver);

    public bool UsesPercentage() => Raw.UsesPercentage();

    public bool Equals(MinTrackSizingFunction other) => Raw.Equals(other.Raw);

    public override bool Equals(object? obj) => obj is MinTrackSizingFunction other && Equals(other);

    public override int GetHashCode() => Raw.GetHashCode();

    public static bool operator ==(MinTrackSizingFunction a, MinTrackSizingFunction b) => a.Equals(b);

    public static bool operator !=(MinTrackSizingFunction a, MinTrackSizingFunction b) => !a.Equals(b);

    public override string ToString() => Raw.ToString();
}

/// <summary>The sizing function of a grid track: a min and a max track sizing function (Rust <c>MinMax&lt;Min, Max&gt;</c>).</summary>
public readonly struct TrackSizingFunction : IEquatable<TrackSizingFunction>
{
    public TrackSizingFunction(MinTrackSizingFunction min, MaxTrackSizingFunction max)
    {
        Min = min;
        Max = max;
    }

    public MinTrackSizingFunction Min { get; }

    public MaxTrackSizingFunction Max { get; }

    public static TrackSizingFunction Auto => new(MinTrackSizingFunction.Auto, MaxTrackSizingFunction.Auto);

    public static TrackSizingFunction MinContent => new(MinTrackSizingFunction.MinContent, MaxTrackSizingFunction.MinContent);

    public static TrackSizingFunction MaxContent => new(MinTrackSizingFunction.MaxContent, MaxTrackSizingFunction.MaxContent);

    public static TrackSizingFunction Zero => new(MinTrackSizingFunction.Zero, MaxTrackSizingFunction.Zero);

    public static TrackSizingFunction Length(float value) => new(MinTrackSizingFunction.Length(value), MaxTrackSizingFunction.Length(value));

    public static TrackSizingFunction Percent(float value) => new(MinTrackSizingFunction.Percent(value), MaxTrackSizingFunction.Percent(value));

    /// <summary>A flexible track (<c>1fr</c> is <c>minmax(auto, 1fr)</c>).</summary>
    public static TrackSizingFunction Fr(float flex) => new(MinTrackSizingFunction.Auto, MaxTrackSizingFunction.Fr(flex));

    /// <summary><c>fit-content(argument)</c>, which is <c>minmax(auto, fit-content(argument))</c>.</summary>
    public static TrackSizingFunction FitContent(LengthPercentage argument) => new(MinTrackSizingFunction.Auto, MaxTrackSizingFunction.FitContent(argument));

    /// <summary><c>minmax(min, max)</c>.</summary>
    public static TrackSizingFunction MinMax(MinTrackSizingFunction min, MaxTrackSizingFunction max) => new(min, max);

    public static implicit operator TrackSizingFunction(LengthPercentage input) => new(input, input);

    public static implicit operator TrackSizingFunction(LengthPercentageAuto input) => new(input, input);

    public static implicit operator TrackSizingFunction(Dimension input) => new(input, input);

    public MinTrackSizingFunction MinSizingFunction() => Min;

    public MaxTrackSizingFunction MaxSizingFunction() => Max;

    /// <summary>Whether at least one of the components ("min" and "max") are fixed sizing functions.</summary>
    public bool HasFixedComponent() => Min.Raw.IsLengthPercentageOrCalc() || Max.Raw.IsLengthPercentageOrCalc();

    public bool Equals(TrackSizingFunction other) => Min.Equals(other.Min) && Max.Equals(other.Max);

    public override bool Equals(object? obj) => obj is TrackSizingFunction other && Equals(other);

    public override int GetHashCode() => (Min, Max).GetHashCode();

    public static bool operator ==(TrackSizingFunction a, TrackSizingFunction b) => a.Equals(b);

    public static bool operator !=(TrackSizingFunction a, TrackSizingFunction b) => !a.Equals(b);

    public override string ToString() => $"minmax({Min}, {Max})";
}

public enum RepetitionCountKind : byte
{
    Count,
    AutoFill,
    AutoFit,
}

/// <summary>The first argument to a <c>repeat()</c>: a positive integer, <c>auto-fill</c> or <c>auto-fit</c>.</summary>
public readonly struct RepetitionCount : IEquatable<RepetitionCount>
{
    private RepetitionCount(RepetitionCountKind kind, ushort count)
    {
        Kind = kind;
        Value = count;
    }

    public RepetitionCountKind Kind { get; }

    /// <summary>The repetition count for <see cref="RepetitionCountKind.Count"/>.</summary>
    public ushort Value { get; }

    public static RepetitionCount AutoFill => new(RepetitionCountKind.AutoFill, 0);

    public static RepetitionCount AutoFit => new(RepetitionCountKind.AutoFit, 0);

    public static RepetitionCount Count(ushort count) => new(RepetitionCountKind.Count, count);

    public static implicit operator RepetitionCount(ushort count) => Count(count);

    public bool IsAuto => Kind is RepetitionCountKind.AutoFill or RepetitionCountKind.AutoFit;

    public bool Equals(RepetitionCount other) => Kind == other.Kind && Value == other.Value;

    public override bool Equals(object? obj) => obj is RepetitionCount other && Equals(other);

    public override int GetHashCode() => ((int)Kind << 16) | Value;

    public static bool operator ==(RepetitionCount a, RepetitionCount b) => a.Equals(b);

    public static bool operator !=(RepetitionCount a, RepetitionCount b) => !a.Equals(b);

    public override string ToString() => Kind switch
    {
        RepetitionCountKind.AutoFill => "auto-fill",
        RepetitionCountKind.AutoFit => "auto-fit",
        _ => Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}

/// <summary>A <c>repeat()</c> clause in <c>grid-template-columns</c> / <c>grid-template-rows</c>.</summary>
public sealed class GridTemplateRepetition
{
    public GridTemplateRepetition(RepetitionCount count, List<TrackSizingFunction> tracks, List<List<string>>? lineNames = null)
    {
        Count = count;
        Tracks = tracks;
        LineNames = lineNames ?? new List<List<string>>();
    }

    public RepetitionCount Count { get; set; }

    public List<TrackSizingFunction> Tracks { get; set; }

    /// <summary>
    /// The line names for the repeated tracks: set <c>i</c> names the <c>i</c>th line of each repetition. Must be empty
    /// or contain exactly <c>Tracks.Count + 1</c> sets (sets may be empty); other lengths throw during layout.
    /// </summary>
    public List<List<string>> LineNames { get; set; }

    /// <summary>The number of tracks in the repetition, saturating at <c>ushort.MaxValue</c>.</summary>
    public ushort TrackCount => (ushort)Math.Min(Tracks.Count, ushort.MaxValue);
}

/// <summary>An element in <c>grid-template-columns</c> / <c>grid-template-rows</c>: a single track or a <c>repeat()</c>.</summary>
public readonly struct GridTemplateComponent : IEquatable<GridTemplateComponent>
{
    private GridTemplateComponent(TrackSizingFunction single, GridTemplateRepetition? repetition)
    {
        SingleTrack = single;
        Repetition = repetition;
    }

    /// <summary>The track sizing function when <see cref="IsSingle"/>.</summary>
    public TrackSizingFunction SingleTrack { get; }

    /// <summary>The repetition when this is a <c>repeat()</c>, otherwise <c>null</c>.</summary>
    public GridTemplateRepetition? Repetition { get; }

    public bool IsSingle => Repetition is null;

    public static GridTemplateComponent Single(TrackSizingFunction track) => new(track, null);

    public static GridTemplateComponent Repeat(GridTemplateRepetition repetition) => new(default, repetition ?? throw new ArgumentNullException(nameof(repetition)));

    /// <summary>Rust <c>style_helpers::repeat</c>.</summary>
    public static GridTemplateComponent Repeat(RepetitionCount count, IEnumerable<TrackSizingFunction> tracks) =>
        Repeat(new GridTemplateRepetition(count, tracks.ToList()));

    /// <summary>Rust <c>style_helpers::evenly_sized_tracks</c>: <c>count</c> tracks of <c>1fr</c>.</summary>
    public static List<GridTemplateComponent> EvenlySizedTracks(ushort count) =>
        Enumerable.Repeat(Single(TrackSizingFunction.Fr(1f)), count).ToList();

    public static implicit operator GridTemplateComponent(TrackSizingFunction track) => Single(track);

    public static implicit operator GridTemplateComponent(LengthPercentage track) => Single(track);

    public static implicit operator GridTemplateComponent(Dimension track) => Single(track);

    /// <summary>Whether this is an auto-repeated <c>repeat()</c> (<c>auto-fill</c> or <c>auto-fit</c>).</summary>
    public bool IsAutoRepetition() => Repetition is { Count.IsAuto: true };

    public bool Equals(GridTemplateComponent other) => SingleTrack.Equals(other.SingleTrack) && ReferenceEquals(Repetition, other.Repetition);

    public override bool Equals(object? obj) => obj is GridTemplateComponent other && Equals(other);

    public override int GetHashCode() => (SingleTrack, Repetition).GetHashCode();

    public override string ToString() => Repetition is null ? SingleTrack.ToString() : $"repeat({Repetition.Count}, ...)";
}
