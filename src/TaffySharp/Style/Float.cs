namespace TaffySharp;

/// <summary>The <c>float</c> property. <c>None</c> is first so that it is the C# default.</summary>
public enum Float
{
    None,
    Left,
    Right,
}

/// <summary>The direction of a floated box.</summary>
public enum FloatDirection : byte
{
    Left = 0,
    Right = 1,
}

/// <summary>The <c>clear</c> property. <c>None</c> is first so that it is the C# default.</summary>
public enum Clear
{
    None,
    Left,
    Right,
    Both,
}

public static class FloatExtensions
{
    public static bool IsFloated(this Float self) => self is Float.Left or Float.Right;

    public static FloatDirection? FloatDirection(this Float self) => self switch
    {
        Float.Left => TaffySharp.FloatDirection.Left,
        Float.Right => TaffySharp.FloatDirection.Right,
        _ => null,
    };
}
