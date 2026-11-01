namespace TaffySharp;

/// <summary>The keyword part of an <see cref="AlignItems"/> value.</summary>
public enum AlignItemsKeyword : byte
{
    Normal,
    Start,
    End,
    FlexStart,
    FlexEnd,
    SelfStart,
    SelfEnd,
    Center,
    Baseline,
    Stretch,
}

/// <summary>The keyword part of an <see cref="AlignContent"/> value.</summary>
public enum AlignContentKeyword : byte
{
    Normal,
    Start,
    End,
    FlexStart,
    FlexEnd,
    Center,
    Stretch,
    SpaceBetween,
    SpaceEvenly,
    SpaceAround,
}

/// <summary>The overflow-position keyword (<c>safe</c> / <c>unsafe</c>) of an alignment value.</summary>
public enum AlignmentSafety : byte
{
    Default,
    Unsafe,
    Safe,
}

internal static class AlignContentKeywordExtensions
{
    public static AlignContentKeyword Reversed(this AlignContentKeyword self) => self switch
    {
        AlignContentKeyword.Start => AlignContentKeyword.End,
        AlignContentKeyword.End => AlignContentKeyword.Start,
        AlignContentKeyword.FlexStart => AlignContentKeyword.FlexEnd,
        AlignContentKeyword.FlexEnd => AlignContentKeyword.FlexStart,
        AlignContentKeyword.Stretch => AlignContentKeyword.End,
        _ => self,
    };
}

/// <summary>
/// Used to control how child nodes are aligned (<c>align-items</c>, <c>justify-items</c>, and as
/// <c>align-self</c> / <c>justify-self</c>). Rust's <c>JustifyItems</c>, <c>AlignSelf</c> and
/// <c>JustifySelf</c> are aliases of this type.
/// </summary>
public readonly struct AlignItems : IEquatable<AlignItems>
{
    public AlignItems(AlignItemsKeyword keyword, AlignmentSafety safety = AlignmentSafety.Default)
    {
        Keyword = keyword;
        Safety = safety;
    }

    public AlignItemsKeyword Keyword { get; }

    public AlignmentSafety Safety { get; }

    public static AlignItems Normal => new(AlignItemsKeyword.Normal);
    public static AlignItems Start => new(AlignItemsKeyword.Start);
    public static AlignItems End => new(AlignItemsKeyword.End);
    public static AlignItems FlexStart => new(AlignItemsKeyword.FlexStart);
    public static AlignItems FlexEnd => new(AlignItemsKeyword.FlexEnd);
    public static AlignItems SelfStart => new(AlignItemsKeyword.SelfStart);
    public static AlignItems SelfEnd => new(AlignItemsKeyword.SelfEnd);
    public static AlignItems Center => new(AlignItemsKeyword.Center);
    public static AlignItems Baseline => new(AlignItemsKeyword.Baseline);
    public static AlignItems Stretch => new(AlignItemsKeyword.Stretch);
    public static AlignItems SafeStart => new(AlignItemsKeyword.Start, AlignmentSafety.Safe);
    public static AlignItems SafeEnd => new(AlignItemsKeyword.End, AlignmentSafety.Safe);
    public static AlignItems SafeFlexStart => new(AlignItemsKeyword.FlexStart, AlignmentSafety.Safe);
    public static AlignItems SafeFlexEnd => new(AlignItemsKeyword.FlexEnd, AlignmentSafety.Safe);
    public static AlignItems SafeCenter => new(AlignItemsKeyword.Center, AlignmentSafety.Safe);
    public static AlignItems SafeSelfStart => new(AlignItemsKeyword.SelfStart, AlignmentSafety.Safe);
    public static AlignItems SafeSelfEnd => new(AlignItemsKeyword.SelfEnd, AlignmentSafety.Safe);

    public bool IsSafe() => Safety == AlignmentSafety.Safe;

    public AlignItems WithSafety(AlignmentSafety safety) => new(Keyword, safety);

    public bool IsStretchOrNormal() => Keyword is AlignItemsKeyword.Stretch or AlignItemsKeyword.Normal;

    /// <summary>Resolves <c>self-start</c>/<c>self-end</c> against the item's and the container's direction.</summary>
    public AlignItems ResolveSelfRelative(Direction itemDirection, Direction containerDirection, bool axisIsInline)
    {
        bool flip = axisIsInline && itemDirection != containerDirection;
        AlignItemsKeyword keyword = Keyword switch
        {
            AlignItemsKeyword.SelfStart => flip ? AlignItemsKeyword.End : AlignItemsKeyword.Start,
            AlignItemsKeyword.SelfEnd => flip ? AlignItemsKeyword.Start : AlignItemsKeyword.End,
            AlignItemsKeyword other => other,
        };
        return new(keyword, Safety);
    }

    public bool Equals(AlignItems other) => Keyword == other.Keyword && Safety == other.Safety;

    public override bool Equals(object? obj) => obj is AlignItems other && Equals(other);

    public override int GetHashCode() => ((int)Keyword << 8) | (int)Safety;

    public static bool operator ==(AlignItems a, AlignItems b) => a.Equals(b);

    public static bool operator !=(AlignItems a, AlignItems b) => !a.Equals(b);

    public override string ToString() => Safety == AlignmentSafety.Default ? Keyword.ToString() : $"{Safety}{Keyword}";
}

/// <summary>
/// Sets the distribution of space between and around content items (<c>align-content</c>,
/// <c>justify-content</c>). Rust's <c>JustifyContent</c> is an alias of this type.
/// </summary>
public readonly struct AlignContent : IEquatable<AlignContent>
{
    public AlignContent(AlignContentKeyword keyword, AlignmentSafety safety = AlignmentSafety.Default)
    {
        Keyword = keyword;
        Safety = safety;
    }

    public AlignContentKeyword Keyword { get; }

    public AlignmentSafety Safety { get; }

    public static AlignContent Normal => new(AlignContentKeyword.Normal);
    public static AlignContent Start => new(AlignContentKeyword.Start);
    public static AlignContent End => new(AlignContentKeyword.End);
    public static AlignContent FlexStart => new(AlignContentKeyword.FlexStart);
    public static AlignContent FlexEnd => new(AlignContentKeyword.FlexEnd);
    public static AlignContent Center => new(AlignContentKeyword.Center);
    public static AlignContent Stretch => new(AlignContentKeyword.Stretch);
    public static AlignContent SpaceBetween => new(AlignContentKeyword.SpaceBetween);
    public static AlignContent SpaceEvenly => new(AlignContentKeyword.SpaceEvenly);
    public static AlignContent SpaceAround => new(AlignContentKeyword.SpaceAround);
    public static AlignContent SafeStart => new(AlignContentKeyword.Start, AlignmentSafety.Safe);
    public static AlignContent SafeEnd => new(AlignContentKeyword.End, AlignmentSafety.Safe);
    public static AlignContent SafeFlexStart => new(AlignContentKeyword.FlexStart, AlignmentSafety.Safe);
    public static AlignContent SafeFlexEnd => new(AlignContentKeyword.FlexEnd, AlignmentSafety.Safe);
    public static AlignContent SafeCenter => new(AlignContentKeyword.Center, AlignmentSafety.Safe);

    public bool IsSafe() => Safety == AlignmentSafety.Safe;

    public AlignContent WithSafety(AlignmentSafety safety) => new(Keyword, safety);

    public bool Equals(AlignContent other) => Keyword == other.Keyword && Safety == other.Safety;

    public override bool Equals(object? obj) => obj is AlignContent other && Equals(other);

    public override int GetHashCode() => ((int)Keyword << 8) | (int)Safety;

    public static bool operator ==(AlignContent a, AlignContent b) => a.Equals(b);

    public static bool operator !=(AlignContent a, AlignContent b) => !a.Equals(b);

    public override string ToString() => Safety == AlignmentSafety.Default ? Keyword.ToString() : $"{Safety}{Keyword}";
}
