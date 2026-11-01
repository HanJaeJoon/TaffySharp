namespace TaffySharp;

/// <summary>The simple absolute horizontal and vertical axis.</summary>
public enum AbsoluteAxis
{
    Horizontal,
    Vertical,
}

/// <summary>The CSS abstract axis (https://www.w3.org/TR/css-writing-modes-3/#abstract-axes).</summary>
public enum AbstractAxis
{
    Inline,
    Block,
}

public static class AxisExtensions
{
    public static AbsoluteAxis OtherAxis(this AbsoluteAxis axis) =>
        axis == AbsoluteAxis.Horizontal ? AbsoluteAxis.Vertical : AbsoluteAxis.Horizontal;

    public static AbstractAxis Other(this AbstractAxis axis) =>
        axis == AbstractAxis.Inline ? AbstractAxis.Block : AbstractAxis.Inline;

    public static AbsoluteAxis AsAbsNaive(this AbstractAxis axis) =>
        axis == AbstractAxis.Inline ? AbsoluteAxis.Horizontal : AbsoluteAxis.Vertical;
}

/// <summary>Container that holds an item in each absolute axis without specifying what kind of item it is.</summary>
internal struct InBothAbsAxis<T>
{
    public T Horizontal;
    public T Vertical;

    public InBothAbsAxis(T horizontal, T vertical)
    {
        Horizontal = horizontal;
        Vertical = vertical;
    }

    public readonly T Get(AbsoluteAxis axis) => axis == AbsoluteAxis.Horizontal ? Horizontal : Vertical;
}

/// <summary>An axis-aligned UI rectangle.</summary>
public struct Rect<T> : IEquatable<Rect<T>>
{
    public T Left;
    public T Right;
    public T Top;
    public T Bottom;

    public Rect(T left, T right, T top, T bottom)
    {
        Left = left;
        Right = right;
        Top = top;
        Bottom = bottom;
    }

    public readonly Rect<T> Transpose() => new(Top, Bottom, Left, Right);

    public readonly Rect<R> Map<R>(Func<T, R> f) => new(f(Left), f(Right), f(Top), f(Bottom));

    internal readonly Rect<R> ZipSize<R, U>(Size<U> size, Func<T, U, R> f) =>
        new(f(Left, size.Width), f(Right, size.Width), f(Top, size.Height), f(Bottom, size.Height));

    public readonly Line<T> HorizontalComponents() => new(Left, Right);

    public readonly Line<T> VerticalComponents() => new(Top, Bottom);

    internal readonly T MainStart(FlexDirection direction) => direction.IsRow() ? Left : Top;

    internal readonly T MainEnd(FlexDirection direction) => direction.IsRow() ? Right : Bottom;

    internal readonly T CrossStart(FlexDirection direction) => direction.IsRow() ? Top : Left;

    internal readonly T CrossEnd(FlexDirection direction) => direction.IsRow() ? Bottom : Right;

    public readonly bool Equals(Rect<T> other)
    {
        EqualityComparer<T> c = EqualityComparer<T>.Default;
        return c.Equals(Left, other.Left) && c.Equals(Right, other.Right) && c.Equals(Top, other.Top) && c.Equals(Bottom, other.Bottom);
    }

    public override readonly bool Equals(object? obj) => obj is Rect<T> other && Equals(other);

    public override readonly int GetHashCode() => (Left, Right, Top, Bottom).GetHashCode();

    public static bool operator ==(Rect<T> a, Rect<T> b) => a.Equals(b);

    public static bool operator !=(Rect<T> a, Rect<T> b) => !a.Equals(b);

    public override readonly string ToString() => $"Rect {{ left: {Left}, right: {Right}, top: {Top}, bottom: {Bottom} }}";
}

/// <summary>An abstract "line". Represents any type that has a start and an end.</summary>
public struct Line<T> : IEquatable<Line<T>>
{
    public T Start;
    public T End;

    public Line(T start, T end)
    {
        Start = start;
        End = end;
    }

    public readonly Line<R> Map<R>(Func<T, R> f) => new(f(Start), f(End));

    public readonly bool Equals(Line<T> other)
    {
        EqualityComparer<T> c = EqualityComparer<T>.Default;
        return c.Equals(Start, other.Start) && c.Equals(End, other.End);
    }

    public override readonly bool Equals(object? obj) => obj is Line<T> other && Equals(other);

    public override readonly int GetHashCode() => (Start, End).GetHashCode();

    public static bool operator ==(Line<T> a, Line<T> b) => a.Equals(b);

    public static bool operator !=(Line<T> a, Line<T> b) => !a.Equals(b);

    public override readonly string ToString() => $"Line {{ start: {Start}, end: {End} }}";
}

/// <summary>The width and height of a rectangle.</summary>
public struct Size<T> : IEquatable<Size<T>>
{
    public T Width;
    public T Height;

    public Size(T width, T height)
    {
        Width = width;
        Height = height;
    }

    public readonly T GetAbs(AbsoluteAxis axis) => axis == AbsoluteAxis.Horizontal ? Width : Height;

    public readonly Size<T> Transpose() => new(Height, Width);

    public readonly Size<R> Map<R>(Func<T, R> f) => new(f(Width), f(Height));

    public readonly Size<T> MapWidth(Func<T, T> f) => new(f(Width), Height);

    public readonly Size<T> MapHeight(Func<T, T> f) => new(Width, f(Height));

    public readonly Size<R> ZipMap<TOther, R>(Size<TOther> other, Func<T, TOther, R> f) =>
        new(f(Width, other.Width), f(Height, other.Height));

    internal void SetMain(FlexDirection direction, T value)
    {
        if (direction.IsRow())
        {
            Width = value;
        }
        else
        {
            Height = value;
        }
    }

    internal void SetCross(FlexDirection direction, T value)
    {
        if (direction.IsRow())
        {
            Height = value;
        }
        else
        {
            Width = value;
        }
    }

    internal readonly Size<T> WithMain(FlexDirection direction, T value) =>
        direction.IsRow() ? new(value, Height) : new(Width, value);

    internal readonly Size<T> WithCross(FlexDirection direction, T value) =>
        direction.IsRow() ? new(Width, value) : new(value, Height);

    internal readonly Size<T> MapMain(FlexDirection direction, Func<T, T> mapper) =>
        direction.IsRow() ? new(mapper(Width), Height) : new(Width, mapper(Height));

    internal readonly Size<T> MapCross(FlexDirection direction, Func<T, T> mapper) =>
        direction.IsRow() ? new(Width, mapper(Height)) : new(mapper(Width), Height);

    internal readonly T Main(FlexDirection direction) => direction.IsRow() ? Width : Height;

    internal readonly T Cross(FlexDirection direction) => direction.IsRow() ? Height : Width;

    internal readonly T Get(AbstractAxis axis) => axis == AbstractAxis.Inline ? Width : Height;

    internal void Set(AbstractAxis axis, T value)
    {
        if (axis == AbstractAxis.Inline)
        {
            Width = value;
        }
        else
        {
            Height = value;
        }
    }

    internal readonly Size<T> With(AbstractAxis axis, T value) =>
        axis == AbstractAxis.Inline ? new(value, Height) : new(Width, value);

    public readonly bool Equals(Size<T> other)
    {
        EqualityComparer<T> c = EqualityComparer<T>.Default;
        return c.Equals(Width, other.Width) && c.Equals(Height, other.Height);
    }

    public override readonly bool Equals(object? obj) => obj is Size<T> other && Equals(other);

    public override readonly int GetHashCode() => (Width, Height).GetHashCode();

    public static bool operator ==(Size<T> a, Size<T> b) => a.Equals(b);

    public static bool operator !=(Size<T> a, Size<T> b) => !a.Equals(b);

    public override readonly string ToString() => $"Size {{ width: {Width}, height: {Height} }}";
}

/// <summary>A 2-dimensional coordinate.</summary>
public struct Point<T> : IEquatable<Point<T>>
{
    public T X;
    public T Y;

    public Point(T x, T y)
    {
        X = x;
        Y = y;
    }

    public readonly Point<R> Map<R>(Func<T, R> f) => new(f(X), f(Y));

    public readonly T Get(AbstractAxis axis) => axis == AbstractAxis.Inline ? X : Y;

    public readonly Point<T> Transpose() => new(Y, X);

    public void Set(AbstractAxis axis, T value)
    {
        if (axis == AbstractAxis.Inline)
        {
            X = value;
        }
        else
        {
            Y = value;
        }
    }

    internal readonly T Main(FlexDirection direction) => direction.IsRow() ? X : Y;

    internal readonly T Cross(FlexDirection direction) => direction.IsRow() ? Y : X;

    public readonly Size<T> ToSize() => new(X, Y);

    public readonly bool Equals(Point<T> other)
    {
        EqualityComparer<T> c = EqualityComparer<T>.Default;
        return c.Equals(X, other.X) && c.Equals(Y, other.Y);
    }

    public override readonly bool Equals(object? obj) => obj is Point<T> other && Equals(other);

    public override readonly int GetHashCode() => (X, Y).GetHashCode();

    public static bool operator ==(Point<T> a, Point<T> b) => a.Equals(b);

    public static bool operator !=(Point<T> a, Point<T> b) => !a.Equals(b);

    public override readonly string ToString() => $"Point {{ x: {X}, y: {Y} }}";
}

/// <summary>Generic struct which holds a "min" value and a "max" value.</summary>
public struct MinMax<TMin, TMax>
{
    public TMin Min;
    public TMax Max;

    public MinMax(TMin min, TMax max)
    {
        Min = min;
        Max = max;
    }
}

/// <summary>Constants and constructors for <see cref="Size{T}"/> (Rust <c>Size::ZERO</c>, <c>Size::NONE</c>, ...).</summary>
public static class Size
{
    public static Size<float> Zero => new(0f, 0f);

    public static Size<float?> None => new(null, null);

    public static Size<AvailableSpace> MaxContent => new(AvailableSpace.MaxContent, AvailableSpace.MaxContent);

    public static Size<AvailableSpace> MinContent => new(AvailableSpace.MinContent, AvailableSpace.MinContent);

    public static Size<float?> New(float width, float height) => new(width, height);

    public static Size<Dimension> FromLengths(float width, float height) => new(Dimension.Length(width), Dimension.Length(height));

    public static Size<Dimension> FromPercent(float width, float height) => new(Dimension.Percent(width), Dimension.Percent(height));

    internal static Size<float?> FromCross(FlexDirection direction, float? value) =>
        direction.IsRow() ? new(null, value) : new(value, null);
}

/// <summary>Constants for <see cref="Rect{T}"/>.</summary>
public static class Rect
{
    public static Rect<float> Zero => new(0f, 0f, 0f, 0f);

    public static Rect<float?> None => new(null, null, null, null);

    public static Rect<Dimension> FromLength(float start, float end, float top, float bottom) =>
        new(Dimension.Length(start), Dimension.Length(end), Dimension.Length(top), Dimension.Length(bottom));

    public static Rect<Dimension> FromPercent(float start, float end, float top, float bottom) =>
        new(Dimension.Percent(start), Dimension.Percent(end), Dimension.Percent(top), Dimension.Percent(bottom));
}

/// <summary>Constants for <see cref="Point{T}"/>.</summary>
public static class Point
{
    public static Point<float> Zero => new(0f, 0f);

    public static Point<float?> None => new(null, null);
}

/// <summary>Constants for <see cref="Line{T}"/>.</summary>
public static class Line
{
    public static Line<bool> True => new(true, true);

    public static Line<bool> False => new(false, false);
}

/// <summary>Arithmetic helpers for the geometry types with concrete type arguments.</summary>
public static class GeometryExtensions
{
    public static float GridAxisSum(this Rect<float> rect, AbsoluteAxis axis) =>
        axis == AbsoluteAxis.Horizontal ? rect.Left + rect.Right : rect.Top + rect.Bottom;

    public static Rect<float> Add(this Rect<float> a, Rect<float> b) =>
        new(a.Left + b.Left, a.Right + b.Right, a.Top + b.Top, a.Bottom + b.Bottom);

    public static float HorizontalAxisSum(this Rect<float> rect) => rect.Left + rect.Right;

    public static float VerticalAxisSum(this Rect<float> rect) => rect.Top + rect.Bottom;

    public static Size<float> SumAxes(this Rect<float> rect) => new(rect.Left + rect.Right, rect.Top + rect.Bottom);

    internal static float MainAxisSum(this Rect<float> rect, FlexDirection direction) =>
        direction.IsRow() ? rect.HorizontalAxisSum() : rect.VerticalAxisSum();

    internal static float CrossAxisSum(this Rect<float> rect, FlexDirection direction) =>
        direction.IsRow() ? rect.VerticalAxisSum() : rect.HorizontalAxisSum();

    public static Rect<float> Union(this Rect<float> a, Rect<float> b) =>
        new(MathHelpers.Min(a.Left, b.Left), MathHelpers.Max(a.Right, b.Right), MathHelpers.Min(a.Top, b.Top), MathHelpers.Max(a.Bottom, b.Bottom));

    public static float Sum(this Line<float> line) => line.Start + line.End;

    public static Size<float> Add(this Size<float> a, Size<float> b) => new(a.Width + b.Width, a.Height + b.Height);

    public static Size<float> Sub(this Size<float> a, Size<float> b) => new(a.Width - b.Width, a.Height - b.Height);

    public static Size<float> F32Max(this Size<float> a, Size<float> b) =>
        new(MathHelpers.Max(a.Width, b.Width), MathHelpers.Max(a.Height, b.Height));

    public static Size<float> F32Min(this Size<float> a, Size<float> b) =>
        new(MathHelpers.Min(a.Width, b.Width), MathHelpers.Min(a.Height, b.Height));

    public static bool HasNonZeroArea(this Size<float> size) => size.Width > 0f && size.Height > 0f;

    public static Point<float> Add(this Point<float> a, Point<float> b) => new(a.X + b.X, a.Y + b.Y);

    public static Size<float?> MaybeApplyAspectRatio(this Size<float?> size, float? aspectRatio)
    {
        if (aspectRatio is float ratio)
        {
            if (size.Width is float width && size.Height is null)
            {
                return new(width, width / ratio);
            }

            if (size.Width is null && size.Height is float height)
            {
                return new(height * ratio, height);
            }
        }

        return size;
    }

    public static Size<float> UnwrapOr(this Size<float?> size, Size<float> alt) => new(size.Width ?? alt.Width, size.Height ?? alt.Height);

    public static Size<float?> Or(this Size<float?> size, Size<float?> alt) => new(size.Width ?? alt.Width, size.Height ?? alt.Height);

    public static bool BothAxisDefined(this Size<float?> size) => size.Width.HasValue && size.Height.HasValue;

    /// <summary>Rust <c>size.map(Some)</c>.</summary>
    public static Size<float?> AsOptions(this Size<float> size) => new(size.Width, size.Height);

    public static Rect<float?> AsOptions(this Rect<float> rect) => new(rect.Left, rect.Right, rect.Top, rect.Bottom);
}
