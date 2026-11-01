using System.Runtime.InteropServices;

namespace TaffySharp;

/// <summary>
/// Float helpers with Rust <c>f32</c> semantics, usable on netstandard2.0.
/// </summary>
internal static class MathHelpers
{
    /// <summary>Rust <c>f32::EPSILON</c> (C# <c>float.Epsilon</c> is the smallest subnormal instead).</summary>
    public const float F32Epsilon = 1.1920929E-07f;

    /// <summary>Rust <c>f32::min</c>: if one argument is NaN the other is returned.</summary>
    public static float Min(float a, float b)
    {
        if (float.IsNaN(a))
        {
            return b;
        }

        if (float.IsNaN(b))
        {
            return a;
        }

        return a < b ? a : b;
    }

    /// <summary>Rust <c>f32::max</c>: if one argument is NaN the other is returned.</summary>
    public static float Max(float a, float b)
    {
        if (float.IsNaN(a))
        {
            return b;
        }

        if (float.IsNaN(b))
        {
            return a;
        }

        return a > b ? a : b;
    }

    /// <summary>Rust <c>f32::clamp</c> (assumes <paramref name="min"/> &lt;= <paramref name="max"/>).</summary>
    public static float Clamp(float value, float min, float max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    public static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);

    /// <summary>Taffy's rounding: <c>(value + 0.5).floor()</c>.</summary>
    public static float Round(float value) => (float)Math.Floor(value + 0.5f);

    public static float Floor(float value) => (float)Math.Floor(value);

    public static float Ceil(float value) => (float)Math.Ceiling(value);

    public static float Abs(float value) => Math.Abs(value);

    public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>Rust <c>f32::to_bits</c>.</summary>
    public static uint ToBits(float value) => new FloatBits { Float = value }.Bits;

    [StructLayout(LayoutKind.Explicit)]
    private struct FloatBits
    {
        [FieldOffset(0)]
        public float Float;

        [FieldOffset(0)]
        public uint Bits;
    }
}

internal static class ListExtensions
{
    /// <summary>Stable sort, like Rust's <c>sort_by</c> (<c>List.Sort</c> is unstable).</summary>
    public static void StableSort<T>(this List<T> list, Comparison<T> comparison)
    {
        // Enumerable.OrderBy is a stable sort
        List<T> sorted = list.OrderBy(x => x, Comparer<T>.Create(comparison)).ToList();
        list.Clear();
        list.AddRange(sorted);
    }
}
