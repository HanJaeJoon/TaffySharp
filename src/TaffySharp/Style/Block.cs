namespace TaffySharp;

/// <summary>The set of styles required for a Block layout container.</summary>
public interface IBlockContainerStyle : ICoreStyle
{
    TextAlign TextAlign { get; }

    AlignContent AlignContent { get; }

    AlignItems JustifyItems { get; }
}

/// <summary>The set of styles required for a Block layout item (child of a Block container).</summary>
public interface IBlockItemStyle : ICoreStyle
{
    /// <summary>Whether the item is a table. Table children are handled specially in block layout.</summary>
    bool IsTable { get; }

    AlignItems? AlignSelf { get; }

    AlignItems? JustifySelf { get; }

    AlignContent AlignContent { get; }

    Float Float { get; }

    Clear Clear { get; }
}

/// <summary>Used by block layout to implement the legacy behaviour of <c>&lt;center&gt;</c> and <c>&lt;div align="left | right | center"&gt;</c>.</summary>
public enum TextAlign
{
    Auto,
    LegacyLeft,
    LegacyRight,
    LegacyCenter,
}
