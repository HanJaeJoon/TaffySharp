namespace TaffySharp;

/// <summary>The amount of space available to a node in a given axis.</summary>
public readonly struct AvailableSpace : IEquatable<AvailableSpace>
{
    private enum Kind : byte
    {
        Definite,
        MinContent,
        MaxContent,
    }

    private readonly Kind _kind;
    private readonly float _value;

    private AvailableSpace(Kind kind, float value)
    {
        _kind = kind;
        _value = value;
    }

    /// <summary>The amount of space available is the specified number of pixels.</summary>
    public static AvailableSpace Definite(float value) => new(Kind.Definite, value);

    /// <summary>The amount of space available is indefinite and the node should be laid out under a min-content constraint.</summary>
    public static AvailableSpace MinContent => new(Kind.MinContent, 0f);

    /// <summary>The amount of space available is indefinite and the node should be laid out under a max-content constraint.</summary>
    public static AvailableSpace MaxContent => new(Kind.MaxContent, 0f);

    public static AvailableSpace Zero => Definite(0f);

    public static implicit operator AvailableSpace(float value) => Definite(value);

    /// <summary>Rust <c>From&lt;Option&lt;f32&gt;&gt;</c>: <c>null</c> becomes max-content.</summary>
    public static AvailableSpace From(float? option) => option is float value ? Definite(value) : MaxContent;

    public bool IsDefinite => _kind == Kind.Definite;

    public bool IsMinContent => _kind == Kind.MinContent;

    public bool IsMaxContent => _kind == Kind.MaxContent;

    /// <summary>The definite value (only meaningful when <see cref="IsDefinite"/>).</summary>
    public float Value => _value;

    public float? IntoOption() => IsDefinite ? _value : null;

    public float UnwrapOr(float @default) => IsDefinite ? _value : @default;

    public float Unwrap() => IsDefinite ? _value : throw new InvalidOperationException("AvailableSpace is not definite");

    public AvailableSpace Or(AvailableSpace @default) => IsDefinite ? this : @default;

    public AvailableSpace OrElse(Func<AvailableSpace> defaultCb) => IsDefinite ? this : defaultCb();

    public float UnwrapOrElse(Func<float> defaultCb) => IsDefinite ? _value : defaultCb();

    public AvailableSpace MaybeSet(float? value) => value is float v ? Definite(v) : this;

    public AvailableSpace MapDefiniteValue(Func<float, float> mapFunction) => IsDefinite ? Definite(mapFunction(_value)) : this;

    /// <summary>Compute free space given the passed used space.</summary>
    public float ComputeFreeSpace(float usedSpace) => _kind switch
    {
        Kind.MaxContent => float.PositiveInfinity,
        Kind.MinContent => 0f,
        _ => _value - usedSpace,
    };

    /// <summary>Compare equality with another AvailableSpace, treating definite values within f32::EPSILON as equal.</summary>
    public bool IsRoughlyEqual(AvailableSpace other)
    {
        if (_kind != other._kind)
        {
            return false;
        }

        return _kind != Kind.Definite || Math.Abs(_value - other._value) < MathHelpers.F32Epsilon;
    }

    public bool Equals(AvailableSpace other) => _kind == other._kind && (_kind != Kind.Definite || _value.Equals(other._value));

    public override bool Equals(object? obj) => obj is AvailableSpace other && Equals(other);

    public override int GetHashCode() => (_kind, _kind == Kind.Definite ? _value : 0f).GetHashCode();

    public static bool operator ==(AvailableSpace a, AvailableSpace b) => a.Equals(b);

    public static bool operator !=(AvailableSpace a, AvailableSpace b) => !a.Equals(b);

    public override string ToString() => _kind switch
    {
        Kind.Definite => $"Definite({_value})",
        Kind.MinContent => "MinContent",
        _ => "MaxContent",
    };
}

public static class AvailableSpaceExtensions
{
    public static Size<float?> IntoOptions(this Size<AvailableSpace> self) => new(self.Width.IntoOption(), self.Height.IntoOption());

    public static Size<AvailableSpace> MaybeSet(this Size<AvailableSpace> self, Size<float?> value) =>
        new(self.Width.MaybeSet(value.Width), self.Height.MaybeSet(value.Height));
}
