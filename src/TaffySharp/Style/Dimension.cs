namespace TaffySharp;

/// <summary>A unit of linear measurement: a length or a percentage (or <c>calc()</c>).</summary>
public readonly struct LengthPercentage : IEquatable<LengthPercentage>
{
    private LengthPercentage(CompactLength raw) => Raw = raw;

    /// <summary>The underlying tagged value (Rust <c>into_raw</c>).</summary>
    public CompactLength Raw { get; }

    public static LengthPercentage Zero => new(CompactLength.Zero);

    /// <summary>An absolute length in some abstract units.</summary>
    public static LengthPercentage Length(float val) => new(CompactLength.Length(val));

    /// <summary>A percentage length relative to the size of the containing block (0.5 is 50%).</summary>
    public static LengthPercentage Percent(float val) => new(CompactLength.Percent(val));

    /// <summary>A <c>calc()</c> value resolved by <see cref="ILayoutPartialTree.ResolveCalcValue"/>.</summary>
    public static LengthPercentage Calc(object calc) => new(CompactLength.Calc(calc));

    /// <summary>Wraps a raw value without validating its tag (Rust <c>from_raw</c>).</summary>
    public static LengthPercentage FromRaw(CompactLength val) => new(val);

    public bool Equals(LengthPercentage other) => Raw.Equals(other.Raw);

    public override bool Equals(object? obj) => obj is LengthPercentage other && Equals(other);

    public override int GetHashCode() => Raw.GetHashCode();

    public static bool operator ==(LengthPercentage a, LengthPercentage b) => a.Equals(b);

    public static bool operator !=(LengthPercentage a, LengthPercentage b) => !a.Equals(b);

    public override string ToString() => Raw.ToString();
}

/// <summary>A unit of linear measurement: a length, a percentage, <c>calc()</c> or <c>auto</c>.</summary>
public readonly struct LengthPercentageAuto : IEquatable<LengthPercentageAuto>
{
    private LengthPercentageAuto(CompactLength raw) => Raw = raw;

    public CompactLength Raw { get; }

    public static LengthPercentageAuto Zero => new(CompactLength.Zero);

    public static LengthPercentageAuto Auto => new(CompactLength.Auto);

    public static LengthPercentageAuto Length(float val) => new(CompactLength.Length(val));

    public static LengthPercentageAuto Percent(float val) => new(CompactLength.Percent(val));

    public static LengthPercentageAuto Calc(object calc) => new(CompactLength.Calc(calc));

    public static LengthPercentageAuto FromRaw(CompactLength val) => new(val);

    public static implicit operator LengthPercentageAuto(LengthPercentage input) => new(input.Raw);

    /// <summary>Returns the resolved value, or <c>null</c> for <c>auto</c>.</summary>
    public float? ResolveToOption(float context, Func<object, float, float> calcResolver) => Raw.Tag switch
    {
        CompactLength.LengthTag => Raw.Value,
        CompactLength.PercentTag => context * Raw.Value,
        CompactLength.AutoTag => null,
        _ when Raw.IsPlainCalc() => calcResolver(Raw.CalcValue!, context),
        _ => throw new InvalidOperationException("LengthPercentageAuto values cannot be constructed with other tags"),
    };

    public bool IsAuto() => Raw.IsAuto();

    public bool Equals(LengthPercentageAuto other) => Raw.Equals(other.Raw);

    public override bool Equals(object? obj) => obj is LengthPercentageAuto other && Equals(other);

    public override int GetHashCode() => Raw.GetHashCode();

    public static bool operator ==(LengthPercentageAuto a, LengthPercentageAuto b) => a.Equals(b);

    public static bool operator !=(LengthPercentageAuto a, LengthPercentageAuto b) => !a.Equals(b);

    public override string ToString() => Raw.ToString();
}

/// <summary>
/// A unit of linear measurement for sizes: a length, a percentage, <c>calc()</c>, <c>auto</c> or an
/// intrinsic sizing keyword.
/// </summary>
public readonly struct Dimension : IEquatable<Dimension>
{
    private Dimension(CompactLength raw) => Raw = raw;

    public CompactLength Raw { get; }

    public static Dimension Zero => new(CompactLength.Zero);

    public static Dimension Auto => new(CompactLength.Auto);

    public static Dimension MinContent => new(CompactLength.MinContent);

    public static Dimension MaxContent => new(CompactLength.MaxContent);

    /// <summary>The <c>fit-content</c> keyword (no argument).</summary>
    public static Dimension FitContent => new(CompactLength.FitContentKeyword);

    public static Dimension Stretch => new(CompactLength.Stretch);

    /// <summary>The <c>content</c> keyword (only meaningful for <c>flex-basis</c>).</summary>
    public static Dimension Content => new(CompactLength.Content);

    public static Dimension Length(float val) => new(CompactLength.Length(val));

    public static Dimension Percent(float val) => new(CompactLength.Percent(val));

    public static Dimension FitContentPx(float limit) => new(CompactLength.FitContentPx(limit));

    public static Dimension FitContentPercent(float limit) => new(CompactLength.FitContentPercent(limit));

    public static Dimension Calc(object calc) => new(CompactLength.Calc(calc));

    public static Dimension FitContentCalc(object calc) => new(CompactLength.FitContentCalc(calc));

    public static Dimension FromRaw(CompactLength val) => new(val);

    public static implicit operator Dimension(LengthPercentage input) => new(input.Raw);

    public static implicit operator Dimension(LengthPercentageAuto input) => new(input.Raw);

    public bool IsFitContentCalc() => Raw.IsFitContentCalc();

    public object? CalcValue => Raw.CalcValue;

    /// <summary>The length value if this is an absolute length, otherwise <c>null</c>.</summary>
    public float? IntoOption() => Raw.Tag == CompactLength.LengthTag ? Raw.Value : null;

    public bool IsAuto() => Raw.IsAuto();

    public bool IsSizingKeyword() => Raw.IsSizingKeyword();

    public bool IsStretch() => Raw.Tag == CompactLength.StretchTag;

    public bool IsContent() => Raw.IsContent();

    public int Tag => Raw.Tag;

    public float Value => Raw.Value;

    public bool Equals(Dimension other) => Raw.Equals(other.Raw);

    public override bool Equals(object? obj) => obj is Dimension other && Equals(other);

    public override int GetHashCode() => Raw.GetHashCode();

    public static bool operator ==(Dimension a, Dimension b) => a.Equals(b);

    public static bool operator !=(Dimension a, Dimension b) => !a.Equals(b);

    public override string ToString() => Raw.ToString();
}
