namespace TaffySharp;

/// <summary>
/// A tagged length value: a tag plus either a float value or a <c>calc()</c> handle.
/// Upstream packs this into a tagged pointer; here the tag, value and handle are separate fields.
/// </summary>
public readonly struct CompactLength : IEquatable<CompactLength>
{
    public const int NonPointerBit = 0b0001;
    public const int KeywordBit = 0b0010;
    public const int PercentageBit = 0b0100;
    public const int SizingKeywordBit = 0b1000;

    public const int CalcTag = 0b0000_0100;
    public const int FitContentCalcTag = 0b0000_0110;
    public const int LengthTag = 0b0000_0001;
    public const int PercentTag = 0b0000_0101;
    public const int AutoTag = 0b0000_0011;
    public const int FrTag = 0b0001_0011;
    public const int ContentTag = 0b0010_0011;
    public const int MinContentTag = 0b0000_1011;
    public const int MaxContentTag = 0b0001_1011;
    public const int FitContentPxTag = 0b0010_1011;
    public const int FitContentKeywordTag = 0b0011_1011;
    public const int StretchTag = 0b0100_1011;
    public const int FitContentPercentTag = 0b0000_1111;

    // Stored relative to LengthTag so that default(CompactLength) is a zero length
    private readonly int _tagOffset;
    private readonly float _value;
    private readonly object? _calc;

    private CompactLength(int tag, float value, object? calc)
    {
        _tagOffset = tag - LengthTag;
        _value = value;
        _calc = calc;
    }

    public static CompactLength Length(float val) => new(LengthTag, val, null);

    public static CompactLength Percent(float val) => new(PercentTag, val, null);

    /// <summary>A <c>calc()</c> value. The handle is passed back to <see cref="ILayoutPartialTree.ResolveCalcValue"/>.</summary>
    public static CompactLength Calc(object calc) => new(CalcTag, 0f, calc ?? throw new ArgumentNullException(nameof(calc)));

    public static CompactLength FitContentCalc(object calc) => new(FitContentCalcTag, 0f, calc ?? throw new ArgumentNullException(nameof(calc)));

    public static CompactLength Auto => new(AutoTag, 0f, null);

    public static CompactLength Fr(float val) => new(FrTag, val, null);

    public static CompactLength MinContent => new(MinContentTag, 0f, null);

    public static CompactLength MaxContent => new(MaxContentTag, 0f, null);

    public static CompactLength FitContentPx(float limit) => new(FitContentPxTag, limit, null);

    public static CompactLength FitContentPercent(float limit) => new(FitContentPercentTag, limit, null);

    public static CompactLength FitContentKeyword => new(FitContentKeywordTag, 0f, null);

    public static CompactLength Stretch => new(StretchTag, 0f, null);

    public static CompactLength Content => new(ContentTag, 0f, null);

    public static CompactLength Zero => Length(0f);

    /// <summary>
    /// Wraps a <c>fit-content()</c> argument (Rust <c>TaffyFitContent::fit_content</c>).
    /// </summary>
    public static CompactLength FitContent(LengthPercentage lp)
    {
        CompactLength v = lp.Raw;
        return v.Tag switch
        {
            LengthTag => FitContentPx(v.Value),
            PercentTag => FitContentPercent(v.Value),
            _ when v.IsPlainCalc() => FitContentCalc(v.CalcValue!),
            _ => throw new InvalidOperationException("unreachable"),
        };
    }

    public int Tag => _tagOffset + LengthTag;

    public float Value => _value;

    public object? CalcValue => _calc;

    public bool IsPlainCalc() => _calc != null && Tag == CalcTag;

    public bool IsFitContentCalc() => (Tag & (NonPointerBit | KeywordBit)) == KeywordBit;

    public bool HasCalc() => (Tag & NonPointerBit) == 0;

    public bool IsZero() => Equals(Zero);

    public bool IsLengthOrPercentage() => (Tag & (NonPointerBit | KeywordBit)) == NonPointerBit;

    public bool IsLengthPercentageOrCalc() => (Tag & KeywordBit) == 0;

    public bool IsAuto() => Tag == AutoTag;

    public bool IsContent() => Tag == ContentTag;

    public bool IsMinContent() => Tag == MinContentTag;

    public bool IsMaxContent() => Tag == MaxContentTag;

    public bool IsFitContent() => Tag is FitContentPxTag or FitContentPercentTag || IsFitContentCalc();

    public bool IsSizingKeyword()
    {
        const int mask = NonPointerBit | KeywordBit | SizingKeywordBit;
        return (Tag & mask) == mask || IsFitContentCalc();
    }

    public bool IsMaxOrFitContent() => Tag is MaxContentTag or FitContentPxTag or FitContentPercentTag || IsFitContentCalc();

    public bool IsMaxContentAlike() => Tag is AutoTag or MaxContentTag or FitContentPxTag or FitContentPercentTag || IsFitContentCalc();

    public bool IsMinOrMaxContent() => Tag is MinContentTag or MaxContentTag;

    public bool IsIntrinsic() => Tag is AutoTag or MinContentTag or MaxContentTag or FitContentPxTag or FitContentPercentTag || IsFitContentCalc();

    public bool IsFr() => Tag == FrTag;

    public bool UsesPercentage() => (Tag & PercentageBit) != 0;

    public float? ResolvedPercentageSize(float parentSize, Func<object, float, float> calcResolver)
    {
        if (Tag == PercentTag)
        {
            return _value * parentSize;
        }

        if (IsPlainCalc())
        {
            return calcResolver(_calc!, parentSize);
        }

        return null;
    }

    public bool Equals(CompactLength other) => _tagOffset == other._tagOffset && _value.Equals(other._value) && ReferenceEquals(_calc, other._calc);

    public override bool Equals(object? obj) => obj is CompactLength other && Equals(other);

    public override int GetHashCode() => (_tagOffset, _value).GetHashCode();

    public static bool operator ==(CompactLength a, CompactLength b) => a.Equals(b);

    public static bool operator !=(CompactLength a, CompactLength b) => !a.Equals(b);

    public override string ToString() => Tag switch
    {
        LengthTag => $"{_value}px",
        PercentTag => $"{_value * 100f}%",
        AutoTag => "auto",
        FrTag => $"{_value}fr",
        ContentTag => "content",
        MinContentTag => "min-content",
        MaxContentTag => "max-content",
        FitContentPxTag => $"fit-content({_value}px)",
        FitContentPercentTag => $"fit-content({_value * 100f}%)",
        FitContentKeywordTag => "fit-content",
        StretchTag => "stretch",
        _ => "calc(...)",
    };
}
