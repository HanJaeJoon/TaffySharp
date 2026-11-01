namespace TaffySharp;

/// <summary>The result of clearing a cache.</summary>
public enum ClearState
{
    /// <summary>The cache had entries that were cleared.</summary>
    Cleared,
    /// <summary>The cache was already empty.</summary>
    AlreadyEmpty,
}

/// <summary>A cache for caching the results of a sizing a Grid Item or Flexbox Item.</summary>
public sealed class Cache
{
    private const int CacheSize = 9;

    private const uint InfinityBits = 0b_0_11111111_00000000000000000000000u;
    private const uint NegInfinityBits = 0b_1_11111111_00000000000000000000000u;

    // The CacheKey encodes two floats as a ulong. The floats are always non-negative, so two extra
    // bits encoding the RequestedAxis are packed into their sign bits.
    private const ulong SignBit1 = 1UL << 63;
    private const ulong SignBit2 = 1UL << 31;
    private const ulong BothSignBitsMask = SignBit1 | SignBit2;
    private const ulong NonSignBitsMask = ~BothSignBitsMask;
    private const ulong XAxisValueMask = (ulong)uint.MaxValue << 32;

    private const byte InherentSizeBit = 0b001;
    private const byte CollapsibleStartMarginBit = 0b010;
    private const byte CollapsibleEndMarginBit = 0b100;

    private static uint OptionCacheKey(float? input) => input is float value ? MathHelpers.ToBits(value) : InfinityBits;

    private static ulong SizeOptionCacheKey(Size<float?> input) =>
        ((ulong)OptionCacheKey(input.Width) << 32) | OptionCacheKey(input.Height);

    private static uint AvailableSpaceCacheKey(AvailableSpace input)
    {
        if (input.IsDefinite)
        {
            return MathHelpers.ToBits(-input.Value);
        }

        return input.IsMinContent ? NegInfinityBits : InfinityBits;
    }

    private static uint MixedCacheKey(float? kd, AvailableSpace avs) => kd is float value ? MathHelpers.ToBits(value) : AvailableSpaceCacheKey(avs);

    private static ulong SizeMixedCacheKey(Size<float?> kd, Size<AvailableSpace> avs) =>
        ((ulong)MixedCacheKey(kd.Width, avs.Width) << 32) | MixedCacheKey(kd.Height, avs.Height);

    private readonly struct CacheKey : IEquatable<CacheKey>
    {
        public readonly ulong KdAvailableSpace;
        public readonly ulong ParentSize;
        public readonly Size<bool> KnownDimensionsAreDefinite;

        public CacheKey(in LayoutInput input)
        {
            // Pack axis enum into spare bits in the known_dimensions and available_space values
            ulong extraBits = input.Axis switch
            {
                RequestedAxis.Horizontal => SignBit1,
                RequestedAxis.Vertical => SignBit2,
                _ => SignBit1 | SignBit2,
            };
            KdAvailableSpace = SizeMixedCacheKey(input.KnownDimensions, input.AvailableSpace);
            ParentSize = (SizeOptionCacheKey(input.ParentSize) & NonSignBitsMask) | extraBits;
            KnownDimensionsAreDefinite = new(
                input.KnownDimensionsAreDefinite.Width || !input.KnownDimensions.Width.HasValue,
                input.KnownDimensionsAreDefinite.Height || !input.KnownDimensions.Height.HasValue);
        }

        public ulong XAxisParentSize() => ParentSize & (XAxisValueMask & NonSignBitsMask);

        public ulong RequestedAxisBits() => ParentSize & BothSignBitsMask;

        public bool SizeIsValidFor(in CacheKey other)
        {
            ulong entryAxis = RequestedAxisBits();
            return entryAxis == BothSignBitsMask || entryAxis == other.RequestedAxisBits();
        }

        public bool Equals(CacheKey other) =>
            KdAvailableSpace == other.KdAvailableSpace && ParentSize == other.ParentSize && KnownDimensionsAreDefinite == other.KnownDimensionsAreDefinite;
    }

    private static byte CacheModeFrom(in LayoutInput input)
    {
        int inherentSize = input.SizingMode == SizingMode.InherentSize ? 1 : 0;
        Line<bool> collapsible = input.VerticalMarginsAreCollapsible;
        return (byte)((inherentSize * InherentSizeBit)
            | ((collapsible.Start ? 1 : 0) * CollapsibleStartMarginBit)
            | ((collapsible.End ? 1 : 0) * CollapsibleEndMarginBit));
    }

    private bool _hasFinalLayoutEntry;
    private CacheKey _finalLayoutKey;
    private LayoutOutput _finalLayoutContent;
    private readonly bool[] _hasMeasureEntry = new bool[CacheSize];
    private readonly CacheKey[] _measureKeys = new CacheKey[CacheSize];
    private readonly Size<float>[] _measureContents = new Size<float>[CacheSize];
    private ushort _recentlyUsedEntries;
    private byte _nextMeasureEntry;
    private bool _isEmpty = true;
    private byte _mode;

    /// <summary>Return the cached result if it exists, otherwise <c>null</c>.</summary>
    public LayoutOutput? Get(in LayoutInput input)
    {
        if (_mode != CacheModeFrom(input))
        {
            return null;
        }

        CacheKey key = new(input);
        switch (input.RunMode)
        {
            case RunMode.PerformLayout:
                return _hasFinalLayoutEntry && _finalLayoutKey.Equals(key) ? _finalLayoutContent.Clone() : null;
            case RunMode.ComputeSize:
                for (int index = 0; index < CacheSize; index++)
                {
                    if (!_hasMeasureEntry[index])
                    {
                        continue;
                    }

                    CacheKey entryKey = _measureKeys[index];
                    if (entryKey.KdAvailableSpace == key.KdAvailableSpace
                        && entryKey.KnownDimensionsAreDefinite == key.KnownDimensionsAreDefinite
                        && entryKey.XAxisParentSize() == key.XAxisParentSize()
                        && entryKey.SizeIsValidFor(key))
                    {
                        _recentlyUsedEntries |= (ushort)(1 << index);
                        return LayoutOutput.FromOuterSize(_measureContents[index]);
                    }
                }

                return null;
            default:
                return null;
        }
    }

    /// <summary>Store a computed size in the cache.</summary>
    public void Store(in LayoutInput input, LayoutOutput layoutOutput)
    {
        if (input.RunMode == RunMode.PerformHiddenLayout)
        {
            return;
        }

        byte mode = CacheModeFrom(input);
        if (_mode != mode)
        {
            Clear();
            _mode = mode;
        }

        CacheKey key = new(input);
        switch (input.RunMode)
        {
            case RunMode.PerformLayout:
                _isEmpty = false;
                _hasFinalLayoutEntry = true;
                _finalLayoutKey = key;
                _finalLayoutContent = layoutOutput.Clone();
                break;
            case RunMode.ComputeSize:
                // Measure entries only store the size, and cache hits are reconstructed with
                // LayoutOutput.FromOuterSize, which resets the margin-collapse metadata. Results that
                // carry such metadata cannot be reconstructed from their size, so don't cache them.
                if (layoutOutput.MarginsCanCollapseThrough
                    || layoutOutput.TopMargin != CollapsibleMarginSet.Zero
                    || layoutOutput.BottomMargin != CollapsibleMarginSet.Zero)
                {
                    return;
                }

                _isEmpty = false;
                for (int index = 0; index < CacheSize; index++)
                {
                    if (_hasMeasureEntry[index] && _measureKeys[index].Equals(key))
                    {
                        _measureContents[index] = layoutOutput.Size;
                        _recentlyUsedEntries |= (ushort)(1 << index);
                        return;
                    }
                }

                while ((_recentlyUsedEntries & (1 << _nextMeasureEntry)) != 0)
                {
                    _recentlyUsedEntries &= (ushort)~(1 << _nextMeasureEntry);
                    _nextMeasureEntry++;
                    if (_nextMeasureEntry == CacheSize)
                    {
                        _nextMeasureEntry = 0;
                    }
                }

                int entryIndex = _nextMeasureEntry;
                _hasMeasureEntry[entryIndex] = true;
                _measureKeys[entryIndex] = key;
                _measureContents[entryIndex] = layoutOutput.Size;
                _recentlyUsedEntries |= (ushort)(1 << entryIndex);
                _nextMeasureEntry++;
                if (_nextMeasureEntry == CacheSize)
                {
                    _nextMeasureEntry = 0;
                }

                break;
        }
    }

    /// <summary>Clear all cache entries and reports clear operation outcome.</summary>
    public ClearState Clear()
    {
        if (_isEmpty)
        {
            return ClearState.AlreadyEmpty;
        }

        _isEmpty = true;
        _hasFinalLayoutEntry = false;
        _finalLayoutContent = default;
        Array.Clear(_hasMeasureEntry, 0, CacheSize);
        _recentlyUsedEntries = 0;
        _nextMeasureEntry = 0;
        return ClearState.Cleared;
    }

    /// <summary>Returns true if all cache entries are None, else false.</summary>
    public bool IsEmpty() => !_hasFinalLayoutEntry && !_hasMeasureEntry.Any(entry => entry);
}
