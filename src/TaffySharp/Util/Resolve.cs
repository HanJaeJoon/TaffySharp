namespace TaffySharp;

/// <summary>
/// Rust <c>MaybeResolve</c> and <c>ResolveOrZero</c>: resolve style values against a (possibly undefined) context size.
/// <c>calc</c> resolves <c>calc()</c> values (see <see cref="ILayoutPartialTree.ResolveCalcValue"/>).
/// </summary>
public static class Resolve
{
    public static float? MaybeResolve(this LengthPercentage self, float? context, Func<object, float, float> calc)
    {
        CompactLength v = self.Raw;
        switch (v.Tag)
        {
            case CompactLength.LengthTag:
                return v.Value;
            case CompactLength.PercentTag:
                return context * v.Value;
            default:
                if (v.IsPlainCalc())
                {
                    return context is float dim ? calc(v.CalcValue!, dim) : null;
                }

                throw new InvalidOperationException("unreachable");
        }
    }

    public static float? MaybeResolve(this LengthPercentageAuto self, float? context, Func<object, float, float> calc)
    {
        CompactLength v = self.Raw;
        switch (v.Tag)
        {
            case CompactLength.AutoTag:
                return null;
            case CompactLength.LengthTag:
                return v.Value;
            case CompactLength.PercentTag:
                return context * v.Value;
            default:
                if (v.IsPlainCalc())
                {
                    return context is float dim ? calc(v.CalcValue!, dim) : null;
                }

                throw new InvalidOperationException("unreachable");
        }
    }

    public static float? MaybeResolve(this Dimension self, float? context, Func<object, float, float> calc)
    {
        CompactLength v = self.Raw;
        switch (v.Tag)
        {
            case CompactLength.AutoTag:
                return null;
            // The content keyword is only valid for flex-basis. In any other context it behaves as auto.
            case CompactLength.ContentTag:
                return null;
            case CompactLength.LengthTag:
                return v.Value;
            case CompactLength.PercentTag:
                return context * v.Value;
            default:
                if (v.IsPlainCalc())
                {
                    return context is float dim ? calc(v.CalcValue!, dim) : null;
                }

                // Intrinsic sizing keywords cannot be resolved to a definite size out of context.
                // Layout algorithms that support them must handle them explicitly.
                if (v.IsSizingKeyword())
                {
                    return null;
                }

                throw new InvalidOperationException("unreachable");
        }
    }

    public static Size<float?> MaybeResolve(this Size<LengthPercentage> self, Size<float?> context, Func<object, float, float> calc) =>
        new(self.Width.MaybeResolve(context.Width, calc), self.Height.MaybeResolve(context.Height, calc));

    public static Size<float?> MaybeResolve(this Size<LengthPercentageAuto> self, Size<float?> context, Func<object, float, float> calc) =>
        new(self.Width.MaybeResolve(context.Width, calc), self.Height.MaybeResolve(context.Height, calc));

    public static Size<float?> MaybeResolve(this Size<Dimension> self, Size<float?> context, Func<object, float, float> calc) =>
        new(self.Width.MaybeResolve(context.Width, calc), self.Height.MaybeResolve(context.Height, calc));

    public static Size<float?> MaybeResolve(this Size<LengthPercentage> self, Size<float> context, Func<object, float, float> calc) =>
        new(self.Width.MaybeResolve(context.Width, calc), self.Height.MaybeResolve(context.Height, calc));

    public static Size<float?> MaybeResolve(this Size<LengthPercentageAuto> self, Size<float> context, Func<object, float, float> calc) =>
        new(self.Width.MaybeResolve(context.Width, calc), self.Height.MaybeResolve(context.Height, calc));

    public static Size<float?> MaybeResolve(this Size<Dimension> self, Size<float> context, Func<object, float, float> calc) =>
        new(self.Width.MaybeResolve(context.Width, calc), self.Height.MaybeResolve(context.Height, calc));

    public static float ResolveOrZero(this LengthPercentage self, float? context, Func<object, float, float> calc) =>
        self.MaybeResolve(context, calc) ?? 0f;

    public static float ResolveOrZero(this LengthPercentageAuto self, float? context, Func<object, float, float> calc) =>
        self.MaybeResolve(context, calc) ?? 0f;

    public static float ResolveOrZero(this Dimension self, float? context, Func<object, float, float> calc) =>
        self.MaybeResolve(context, calc) ?? 0f;

    public static Size<float> ResolveOrZero(this Size<LengthPercentage> self, Size<float?> context, Func<object, float, float> calc) =>
        new(self.Width.ResolveOrZero(context.Width, calc), self.Height.ResolveOrZero(context.Height, calc));

    public static Size<float> ResolveOrZero(this Size<LengthPercentageAuto> self, Size<float?> context, Func<object, float, float> calc) =>
        new(self.Width.ResolveOrZero(context.Width, calc), self.Height.ResolveOrZero(context.Height, calc));

    public static Size<float> ResolveOrZero(this Size<Dimension> self, Size<float?> context, Func<object, float, float> calc) =>
        new(self.Width.ResolveOrZero(context.Width, calc), self.Height.ResolveOrZero(context.Height, calc));

    public static Rect<float> ResolveOrZero(this Rect<LengthPercentage> self, Size<float?> context, Func<object, float, float> calc) =>
        new(self.Left.ResolveOrZero(context.Width, calc), self.Right.ResolveOrZero(context.Width, calc),
            self.Top.ResolveOrZero(context.Height, calc), self.Bottom.ResolveOrZero(context.Height, calc));

    public static Rect<float> ResolveOrZero(this Rect<LengthPercentageAuto> self, Size<float?> context, Func<object, float, float> calc) =>
        new(self.Left.ResolveOrZero(context.Width, calc), self.Right.ResolveOrZero(context.Width, calc),
            self.Top.ResolveOrZero(context.Height, calc), self.Bottom.ResolveOrZero(context.Height, calc));

    public static Rect<float> ResolveOrZero(this Rect<Dimension> self, Size<float?> context, Func<object, float, float> calc) =>
        new(self.Left.ResolveOrZero(context.Width, calc), self.Right.ResolveOrZero(context.Width, calc),
            self.Top.ResolveOrZero(context.Height, calc), self.Bottom.ResolveOrZero(context.Height, calc));

    public static Rect<float> ResolveOrZero(this Rect<LengthPercentage> self, float? context, Func<object, float, float> calc) =>
        new(self.Left.ResolveOrZero(context, calc), self.Right.ResolveOrZero(context, calc),
            self.Top.ResolveOrZero(context, calc), self.Bottom.ResolveOrZero(context, calc));

    public static Rect<float> ResolveOrZero(this Rect<LengthPercentageAuto> self, float? context, Func<object, float, float> calc) =>
        new(self.Left.ResolveOrZero(context, calc), self.Right.ResolveOrZero(context, calc),
            self.Top.ResolveOrZero(context, calc), self.Bottom.ResolveOrZero(context, calc));

    public static Rect<float> ResolveOrZero(this Rect<Dimension> self, float? context, Func<object, float, float> calc) =>
        new(self.Left.ResolveOrZero(context, calc), self.Right.ResolveOrZero(context, calc),
            self.Top.ResolveOrZero(context, calc), self.Bottom.ResolveOrZero(context, calc));
}
