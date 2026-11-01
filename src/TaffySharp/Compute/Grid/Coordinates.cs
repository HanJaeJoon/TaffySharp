namespace TaffySharp;

internal static class GridConstants
{
    /// <summary>The maximum number of tracks in an axis (https://www.w3.org/TR/css-grid-1/#overlarge-grids).</summary>
    public const ushort MaxGridTracks = 10_000;
    public const short MinOzLine = -(short)MaxGridTracks;
    public const short MaxOzLine = (short)MaxGridTracks;
}

/// <summary>
/// Represents a grid line position in "CSS Grid Line" coordinates: lines are 1-indexed, negative
/// indexes count back from the end of the explicit grid, and line 0 is invalid.
/// </summary>
public readonly struct GridLine : IEquatable<GridLine>
{
    public GridLine(short value) => Value = value;

    public short Value { get; }

    public static implicit operator GridLine(short value) => new(value);

    public short AsI16() => Value;

    /// <summary>Convert into OriginZero coordinates using the specified explicit track count.</summary>
    internal OriginZeroLine IntoOriginZeroLine(ushort explicitTrackCount)
    {
        int explicitLineCount = explicitTrackCount + 1;
        int ozLine = Value switch
        {
            > 0 => Value - 1,
            < 0 => Value + explicitLineCount,
            _ => throw new InvalidOperationException("Grid line of zero is invalid"),
        };
        return OriginZeroLine.Clamped(ozLine);
    }

    public bool Equals(GridLine other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is GridLine other && Equals(other);

    public override int GetHashCode() => Value;

    public static bool operator ==(GridLine a, GridLine b) => a.Value == b.Value;

    public static bool operator !=(GridLine a, GridLine b) => a.Value != b.Value;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Represents a grid line position in "OriginZero" coordinates: line 0 is the start of the explicit
/// grid and lines before it are negative.
/// </summary>
public readonly struct OriginZeroLine : IEquatable<OriginZeroLine>, IComparable<OriginZeroLine>
{
    public OriginZeroLine(short value) => Value = value;

    public short Value { get; }

    public static OriginZeroLine operator +(OriginZeroLine a, OriginZeroLine b) => new((short)(a.Value + b.Value));

    public static OriginZeroLine operator -(OriginZeroLine a, OriginZeroLine b) => new((short)(a.Value - b.Value));

    public static OriginZeroLine operator +(OriginZeroLine a, ushort b) => new((short)(a.Value + (short)b));

    public static OriginZeroLine operator -(OriginZeroLine a, ushort b) => new((short)(a.Value - (short)b));

    public static bool operator <(OriginZeroLine a, OriginZeroLine b) => a.Value < b.Value;

    public static bool operator >(OriginZeroLine a, OriginZeroLine b) => a.Value > b.Value;

    public static bool operator <=(OriginZeroLine a, OriginZeroLine b) => a.Value <= b.Value;

    public static bool operator >=(OriginZeroLine a, OriginZeroLine b) => a.Value >= b.Value;

    public static OriginZeroLine Min(OriginZeroLine a, OriginZeroLine b) => a.Value <= b.Value ? a : b;

    public static OriginZeroLine Max(OriginZeroLine a, OriginZeroLine b) => a.Value >= b.Value ? a : b;

    /// <summary>Clamp a line index to the range supported by the grid (+/- the maximum track count).</summary>
    internal static OriginZeroLine Clamped(int line) =>
        new((short)MathHelpers.Clamp(line, GridConstants.MinOzLine, GridConstants.MaxOzLine));

    /// <summary>Converts a grid line in OriginZero coordinates into CSS grid line coordinates.</summary>
    internal GridLine IntoGridLine(ushort explicitTrackCount)
    {
        int explicitLineCount = explicitTrackCount + 1;
        return Value >= 0 ? new GridLine((short)(Value + 1)) : new GridLine((short)(Value - explicitLineCount));
    }

    /// <summary>Converts a grid line in OriginZero coordinates into the index of that same grid line in the GridTrackVec.</summary>
    internal int IntoTrackVecIndex(TrackCounts trackCounts) =>
        TryIntoTrackVecIndex(trackCounts) ?? throw new InvalidOperationException(Value > 0
            ? "OriginZero grid line cannot be more than the number of positive grid lines"
            : "OriginZero grid line cannot be less than the number of negative grid lines");

    /// <summary>Like <see cref="IntoTrackVecIndex"/>, but returns <c>null</c> for lines outside the grid.</summary>
    internal int? TryIntoTrackVecIndex(TrackCounts trackCounts)
    {
        if (Value < -trackCounts.NegativeImplicit)
        {
            return null;
        }

        if (Value > trackCounts.Explicit + trackCounts.PositiveImplicit)
        {
            return null;
        }

        return 2 * (Value + trackCounts.NegativeImplicit);
    }

    /// <summary>The number of negative implicit tracks implied by this line.</summary>
    internal ushort ImpliedNegativeImplicitTracks() => Value < 0 ? (ushort)(-Value) : (ushort)0;

    /// <summary>The number of positive implicit tracks implied by this line.</summary>
    internal ushort ImpliedPositiveImplicitTracks(ushort explicitTrackCount) =>
        Value > explicitTrackCount ? (ushort)(Value - explicitTrackCount) : (ushort)0;

    public bool Equals(OriginZeroLine other) => Value == other.Value;

    public int CompareTo(OriginZeroLine other) => Value.CompareTo(other.Value);

    public override bool Equals(object? obj) => obj is OriginZeroLine other && Equals(other);

    public override int GetHashCode() => Value;

    public static bool operator ==(OriginZeroLine a, OriginZeroLine b) => a.Value == b.Value;

    public static bool operator !=(OriginZeroLine a, OriginZeroLine b) => a.Value != b.Value;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal static class OriginZeroLineExtensions
{
    /// <summary>The number of tracks between the start and end lines.</summary>
    public static ushort Span(this Line<OriginZeroLine> line) => (ushort)Math.Max(line.End.Value - line.Start.Value, 0);
}
