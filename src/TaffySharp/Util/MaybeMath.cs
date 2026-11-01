namespace TaffySharp;

/// <summary>
/// Rust <c>MaybeMath</c>: arithmetic on values that may be undefined (<c>null</c>), where an undefined
/// operand is ignored and an undefined receiver stays undefined.
/// </summary>
public static class MaybeMath
{
    // float? op float?
    public static float? MaybeMin(this float? self, float? rhs) => self is float l ? (rhs is float r ? MathHelpers.Min(l, r) : l) : null;

    public static float? MaybeMax(this float? self, float? rhs) => self is float l ? (rhs is float r ? MathHelpers.Max(l, r) : l) : null;

    public static float? MaybeClamp(this float? self, float? min, float? max)
    {
        if (self is not float b)
        {
            return null;
        }

        if (max is float mx)
        {
            b = MathHelpers.Min(b, mx);
        }

        if (min is float mn)
        {
            b = MathHelpers.Max(b, mn);
        }

        return b;
    }

    public static float? MaybeAdd(this float? self, float? rhs) => self is float l ? (rhs is float r ? l + r : l) : null;

    public static float? MaybeSub(this float? self, float? rhs) => self is float l ? (rhs is float r ? l - r : l) : null;

    // float? op float
    public static float? MaybeMin(this float? self, float rhs) => self is float l ? MathHelpers.Min(l, rhs) : null;

    public static float? MaybeMax(this float? self, float rhs) => self is float l ? MathHelpers.Max(l, rhs) : null;

    public static float? MaybeClamp(this float? self, float min, float max) => self is float l ? MathHelpers.Max(MathHelpers.Min(l, max), min) : null;

    public static float? MaybeAdd(this float? self, float rhs) => self is float l ? l + rhs : null;

    public static float? MaybeSub(this float? self, float rhs) => self is float l ? l - rhs : null;

    // float op float?
    public static float MaybeMin(this float self, float? rhs) => rhs is float r ? MathHelpers.Min(self, r) : self;

    public static float MaybeMax(this float self, float? rhs) => rhs is float r ? MathHelpers.Max(self, r) : self;

    public static float MaybeClamp(this float self, float? min, float? max)
    {
        float b = self;
        if (max is float mx)
        {
            b = MathHelpers.Min(b, mx);
        }

        if (min is float mn)
        {
            b = MathHelpers.Max(b, mn);
        }

        return b;
    }

    public static float MaybeAdd(this float self, float? rhs) => rhs is float r ? self + r : self;

    public static float MaybeSub(this float self, float? rhs) => rhs is float r ? self - r : self;

    // AvailableSpace op float
    public static AvailableSpace MaybeMin(this AvailableSpace self, float rhs) =>
        self.IsDefinite ? AvailableSpace.Definite(MathHelpers.Min(self.Value, rhs)) : AvailableSpace.Definite(rhs);

    public static AvailableSpace MaybeMax(this AvailableSpace self, float rhs) =>
        self.IsDefinite ? AvailableSpace.Definite(MathHelpers.Max(self.Value, rhs)) : self;

    public static AvailableSpace MaybeClamp(this AvailableSpace self, float min, float max) =>
        self.IsDefinite ? AvailableSpace.Definite(MathHelpers.Max(MathHelpers.Min(self.Value, max), min)) : self;

    public static AvailableSpace MaybeAdd(this AvailableSpace self, float rhs) =>
        self.IsDefinite ? AvailableSpace.Definite(self.Value + rhs) : self;

    public static AvailableSpace MaybeSub(this AvailableSpace self, float rhs) =>
        self.IsDefinite ? AvailableSpace.Definite(self.Value - rhs) : self;

    // AvailableSpace op float?
    public static AvailableSpace MaybeMin(this AvailableSpace self, float? rhs)
    {
        if (rhs is not float r)
        {
            return self;
        }

        return self.IsDefinite ? AvailableSpace.Definite(MathHelpers.Min(self.Value, r)) : AvailableSpace.Definite(r);
    }

    public static AvailableSpace MaybeMax(this AvailableSpace self, float? rhs) =>
        self.IsDefinite && rhs is float r ? AvailableSpace.Definite(MathHelpers.Max(self.Value, r)) : self;

    public static AvailableSpace MaybeClamp(this AvailableSpace self, float? min, float? max) =>
        self.IsDefinite ? AvailableSpace.Definite(self.Value.MaybeClamp(min, max)) : self;

    public static AvailableSpace MaybeAdd(this AvailableSpace self, float? rhs) =>
        self.IsDefinite && rhs is float r ? AvailableSpace.Definite(self.Value + r) : self;

    public static AvailableSpace MaybeSub(this AvailableSpace self, float? rhs) =>
        self.IsDefinite && rhs is float r ? AvailableSpace.Definite(self.Value - r) : self;

    // Size<float?> op Size<float?>
    public static Size<float?> MaybeMin(this Size<float?> self, Size<float?> rhs) => new(self.Width.MaybeMin(rhs.Width), self.Height.MaybeMin(rhs.Height));

    public static Size<float?> MaybeMax(this Size<float?> self, Size<float?> rhs) => new(self.Width.MaybeMax(rhs.Width), self.Height.MaybeMax(rhs.Height));

    public static Size<float?> MaybeClamp(this Size<float?> self, Size<float?> min, Size<float?> max) =>
        new(self.Width.MaybeClamp(min.Width, max.Width), self.Height.MaybeClamp(min.Height, max.Height));

    public static Size<float?> MaybeAdd(this Size<float?> self, Size<float?> rhs) => new(self.Width.MaybeAdd(rhs.Width), self.Height.MaybeAdd(rhs.Height));

    public static Size<float?> MaybeSub(this Size<float?> self, Size<float?> rhs) => new(self.Width.MaybeSub(rhs.Width), self.Height.MaybeSub(rhs.Height));

    // Size<float?> op Size<float>
    public static Size<float?> MaybeMin(this Size<float?> self, Size<float> rhs) => new(self.Width.MaybeMin(rhs.Width), self.Height.MaybeMin(rhs.Height));

    public static Size<float?> MaybeMax(this Size<float?> self, Size<float> rhs) => new(self.Width.MaybeMax(rhs.Width), self.Height.MaybeMax(rhs.Height));

    public static Size<float?> MaybeClamp(this Size<float?> self, Size<float> min, Size<float> max) =>
        new(self.Width.MaybeClamp(min.Width, max.Width), self.Height.MaybeClamp(min.Height, max.Height));

    public static Size<float?> MaybeAdd(this Size<float?> self, Size<float> rhs) => new(self.Width.MaybeAdd(rhs.Width), self.Height.MaybeAdd(rhs.Height));

    public static Size<float?> MaybeSub(this Size<float?> self, Size<float> rhs) => new(self.Width.MaybeSub(rhs.Width), self.Height.MaybeSub(rhs.Height));

    // Size<float> op Size<float?>
    public static Size<float> MaybeMin(this Size<float> self, Size<float?> rhs) => new(self.Width.MaybeMin(rhs.Width), self.Height.MaybeMin(rhs.Height));

    public static Size<float> MaybeMax(this Size<float> self, Size<float?> rhs) => new(self.Width.MaybeMax(rhs.Width), self.Height.MaybeMax(rhs.Height));

    public static Size<float> MaybeClamp(this Size<float> self, Size<float?> min, Size<float?> max) =>
        new(self.Width.MaybeClamp(min.Width, max.Width), self.Height.MaybeClamp(min.Height, max.Height));

    public static Size<float> MaybeAdd(this Size<float> self, Size<float?> rhs) => new(self.Width.MaybeAdd(rhs.Width), self.Height.MaybeAdd(rhs.Height));

    public static Size<float> MaybeSub(this Size<float> self, Size<float?> rhs) => new(self.Width.MaybeSub(rhs.Width), self.Height.MaybeSub(rhs.Height));

    // Size<AvailableSpace> op Size<float>
    public static Size<AvailableSpace> MaybeMin(this Size<AvailableSpace> self, Size<float> rhs) => new(self.Width.MaybeMin(rhs.Width), self.Height.MaybeMin(rhs.Height));

    public static Size<AvailableSpace> MaybeMax(this Size<AvailableSpace> self, Size<float> rhs) => new(self.Width.MaybeMax(rhs.Width), self.Height.MaybeMax(rhs.Height));

    public static Size<AvailableSpace> MaybeAdd(this Size<AvailableSpace> self, Size<float> rhs) => new(self.Width.MaybeAdd(rhs.Width), self.Height.MaybeAdd(rhs.Height));

    public static Size<AvailableSpace> MaybeSub(this Size<AvailableSpace> self, Size<float> rhs) => new(self.Width.MaybeSub(rhs.Width), self.Height.MaybeSub(rhs.Height));

    // Size<AvailableSpace> op Size<float?>
    public static Size<AvailableSpace> MaybeMin(this Size<AvailableSpace> self, Size<float?> rhs) => new(self.Width.MaybeMin(rhs.Width), self.Height.MaybeMin(rhs.Height));

    public static Size<AvailableSpace> MaybeMax(this Size<AvailableSpace> self, Size<float?> rhs) => new(self.Width.MaybeMax(rhs.Width), self.Height.MaybeMax(rhs.Height));

    public static Size<AvailableSpace> MaybeClamp(this Size<AvailableSpace> self, Size<float?> min, Size<float?> max) =>
        new(self.Width.MaybeClamp(min.Width, max.Width), self.Height.MaybeClamp(min.Height, max.Height));

    public static Size<AvailableSpace> MaybeAdd(this Size<AvailableSpace> self, Size<float?> rhs) => new(self.Width.MaybeAdd(rhs.Width), self.Height.MaybeAdd(rhs.Height));

    public static Size<AvailableSpace> MaybeSub(this Size<AvailableSpace> self, Size<float?> rhs) => new(self.Width.MaybeSub(rhs.Width), self.Height.MaybeSub(rhs.Height));
}
