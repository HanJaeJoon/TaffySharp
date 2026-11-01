namespace TaffySharp;

/// <summary>A slot for non-BFC-establishing (in-flow) content within a block formatting context that contains floats.</summary>
public struct ContentSlot
{
    /// <summary>The id of the float segment the slot is in (if any).</summary>
    public int? SegmentId;
    /// <summary>The x position of the start of the slot.</summary>
    public float X;
    /// <summary>The y position of the start of the slot.</summary>
    public float Y;
    /// <summary>The width of the slot.</summary>
    public float Width;
    /// <summary>The height of the slot.</summary>
    public float Height;
}

/// <summary>A slot for a box that establishes a new block formatting context (and thus must avoid floats).</summary>
public struct BfcSlot
{
    /// <summary>The id of the float segment the slot is in (if any).</summary>
    public int? SegmentId;
    /// <summary>The x position of the box's border box.</summary>
    public float X;
    /// <summary>The y position of the box's border box.</summary>
    public float Y;
    /// <summary>The width available to the box's border box (which must not overlap any float).</summary>
    public float BorderWidth;
    /// <summary>The width an auto-width box stretches to (its margins may overlap floats).</summary>
    public float StretchWidth;
}

/// <summary>A floated box that has been placed within a <see cref="FloatContext"/>.</summary>
public struct PlacedFloatedBox
{
    public float Width;
    public float Height;
    public float XInset;
    public float Y;
}

internal static class FloatConstants
{
    public const float FitTolerance = 0.001f;
}

/// <summary>A horizontal band of the block formatting context with constant float insets.</summary>
internal sealed class Segment
{
    public float YStart;
    public float YEnd;
    public float[] Insets = new float[2];
    public bool[] HasFloat = new bool[2];

    public bool YContains(float y) => YStart <= y && y < YEnd;

    public bool FitsFloatWidth(Size<float> floatedBox, FloatDirection direction, float bfcWidth, float[] cbInsets) =>
        FloatContext.FloatFitsHorizontally(floatedBox.Width, direction, bfcWidth, Insets, cbInsets);
}

internal struct FloatFitter
{
    private readonly float _bfcWidth;
    private double _slotHeight;
    private readonly float[] _floatInsets;
    private readonly float[] _cbInsets;

    public FloatFitter(float bfcWidth, float slotHeight, float[] cbInsets)
    {
        _bfcWidth = bfcWidth;
        _slotHeight = slotHeight;
        _floatInsets = new float[2];
        _cbInsets = cbInsets;
    }

    // Horizontal fitting
    public void UnionInsets(float[] insets)
    {
        _floatInsets[0] = MathHelpers.Max(_floatInsets[0], insets[0]);
        _floatInsets[1] = MathHelpers.Max(_floatInsets[1], insets[1]);
    }

    public readonly float PlacedInset(FloatDirection direction)
    {
        int lead = (int)direction;
        return MathHelpers.Max(_floatInsets[lead], _cbInsets[lead]);
    }

    public readonly bool FitsHorizontally(float width, FloatDirection direction) =>
        FloatContext.FloatFitsHorizontally(width, direction, _bfcWidth, _floatInsets, _cbInsets);

    // Vertical fitting
    public void AddHeight(float height) => _slotHeight += height;

    public readonly bool FitsVertically(float height) => _slotHeight >= height;
}

/// <summary>The float state of a block formatting context: placed floats and the segments they divide the context into.</summary>
public sealed class FloatContext
{
    private float _availableWidth;
    private bool _hasFloats;
    private readonly List<PlacedFloatedBox> _leftFloats = new();
    private readonly List<PlacedFloatedBox> _rightFloats = new();
    private readonly List<Segment> _segments = new();
    // Rust `last_placed_floats: [Range<usize>; 2]`
    private readonly int[] _lastPlacedStart = new int[2];
    private readonly int[] _lastPlacedEnd = new int[2];
    private readonly float?[] _clearBottoms = new float?[2];
    private float? _floatCeiling;

    public FloatContext()
    {
    }

    /// <summary>Whether any float has been placed in this context.</summary>
    public bool HasFloats() => _hasFloats;

    /// <summary>Whether any float extends below <paramref name="minY"/>.</summary>
    public bool HasActiveFloats(float minY) => _hasFloats && _segments.Count > 0 && _segments[_segments.Count - 1].YEnd > minY;

    public void SetWidth(float availableWidth) => _availableWidth = availableWidth;

    public IReadOnlyList<PlacedFloatedBox> LeftFloats() => _leftFloats;

    public IReadOnlyList<PlacedFloatedBox> RightFloats() => _rightFloats;

    internal static bool FloatFitsHorizontally(float width, FloatDirection direction, float bfcWidth, float[] floatInsets, float[] cbInsets)
    {
        int lead = (int)direction;
        int trail = 1 - lead;
        float xInset = MathHelpers.Max(floatInsets[lead], cbInsets[lead]);
        bool fitsOppositeFloats =
            floatInsets[trail] == 0f || xInset + width <= bfcWidth - floatInsets[trail] + FloatConstants.FitTolerance;
        bool fitsContainingBlock =
            floatInsets[lead] == 0f || xInset + width <= bfcWidth - cbInsets[trail] + FloatConstants.FitTolerance;
        return fitsOppositeFloats && fitsContainingBlock;
    }

    private void SubdivideSegment(int idx, float divideAtY)
    {
        Segment oldSegment = _segments[idx];
        Segment newSegment = new()
        {
            Insets = (float[])oldSegment.Insets.Clone(),
            HasFloat = (bool[])oldSegment.HasFloat.Clone(),
            YStart = divideAtY,
            YEnd = oldSegment.YEnd,
        };
        if (!oldSegment.YContains(divideAtY) || oldSegment.YStart == divideAtY)
        {
            throw new InvalidOperationException("assertion failed: old_segment.y.contains(&divide_at_y) && old_segment.y.start != divide_at_y");
        }

        oldSegment.YEnd = divideAtY;
        _segments.Insert(idx + 1, newSegment);
    }

    private void UpdateLastPlacedFloat(FloatDirection direction, int placementStart, int placementEnd)
    {
        int slot = (int)direction;
        _lastPlacedStart[slot] = Math.Max(_lastPlacedStart[slot], placementStart);
        _lastPlacedEnd[slot] = Math.Max(_lastPlacedEnd[slot], placementEnd);
    }

    /// <summary>Place a floated box (margin box size) at or below <paramref name="minY"/>, returning its position.</summary>
    public Point<float> PlaceFloatedBox(Size<float> floatedBox, float minY, float[] containingBlockInsets, FloatDirection direction, Clear clear)
    {
        _hasFloats = true;
        PlacedFloatedBox placedFloatedBox = PlaceFloatedBoxInner(floatedBox, minY, containingBlockInsets, direction, clear);

        int slot = (int)direction;
        float bottom = placedFloatedBox.Y + placedFloatedBox.Height;
        _clearBottoms[slot] = _clearBottoms[slot] is float b ? MathHelpers.Max(b, bottom) : bottom;
        float y = placedFloatedBox.Y;
        _floatCeiling = _floatCeiling is float ceiling ? MathHelpers.Max(ceiling, y) : y;

        float xInset = placedFloatedBox.XInset;
        switch (direction)
        {
            case FloatDirection.Left:
                _leftFloats.Add(placedFloatedBox);
                return new Point<float>(xInset, y);
            default:
                _rightFloats.Add(placedFloatedBox);
                return new Point<float>(_availableWidth - xInset - floatedBox.Width, y);
        }
    }

    /// <summary>The index of the first segment in <c>_segments[hwm..]</c> that ends below <paramref name="minY"/>, or the segment count.</summary>
    private int FindStartIndex(int hwm, float minY)
    {
        for (int i = hwm; i < _segments.Count; i++)
        {
            if (_segments[i].YEnd > minY)
            {
                return i;
            }
        }

        return _segments.Count;
    }

    private PlacedFloatedBox PlaceFloatedBoxInner(Size<float> floatedBox, float minY, float[] containingBlockInsets, FloatDirection direction, Clear clear)
    {
        int slot = (int)direction;

        // Floats (including zero-sized floats) that occupy no segment still constrain the
        // position of later boxes: rule 5 (a float may not be higher than the top of any
        // earlier float) and `clear` (which clears past the bottom of floats on the
        // relevant side, regardless of their width)
        minY = MathHelpers.Max(
            MathHelpers.Max(minY, _floatCeiling ?? float.NegativeInfinity),
            CleanedThresholdOrNegInf(clear));

        // Ensure that float:
        //    - Starts at or after the last placed float in either direction (CSS2 float rule 5)
        //    - Respects "clear"
        int floatStart = Math.Max(_lastPlacedStart[0], _lastPlacedStart[1]);
        int hwm = clear switch
        {
            Clear.Left => Math.Max(floatStart, _lastPlacedEnd[0] + 1),
            Clear.Right => Math.Max(floatStart, _lastPlacedEnd[1] + 1),
            Clear.Both => Math.Max(_lastPlacedEnd[0], _lastPlacedEnd[1]) + 1,
            _ => floatStart,
        };

        // Ensure that float is placed in a segment at or below "min_y"
        // (ensuring that it is placed at or below min_y within it's segment happens below)
        int startIdx = FindStartIndex(hwm, minY);
        float startY = minY;
        int endIdx = startIdx;

        // Loop over remaining segments, trying to place the float in a position
        // that has space to accomodate it.
        int? start;
        int? end;
        float placedInset;
        while (true)
        {
            // Start segment does not exist: no existing segment can accomodate the float so we must
            // create a new segment below all existing segments (which always has space for it).
            if (startIdx >= _segments.Count)
            {
                start = null;
                end = null;
                placedInset = containingBlockInsets[slot];
                break;
            }

            Segment startSegment = _segments[startIdx];

            // Candidate start segment doesn't have (horizontal) space for the float:
            // => retry with the next segment
            if (!startSegment.FitsFloatWidth(floatedBox, direction, _availableWidth, containingBlockInsets))
            {
                startIdx += 1;
                endIdx = Math.Max(endIdx, startIdx);
                continue;
            }

            startY = MathHelpers.Max(startY, startSegment.YStart);
            float availableHeight = startSegment.YEnd - startY;
            FloatFitter fitter = new(_availableWidth, availableHeight, containingBlockInsets);
            fitter.UnionInsets(startSegment.Insets);

            // Pinning the start segment, loop over segments starting with the start segment
            // to find the end segment:
            //   - The selected segment range must have enough height to contain the float
            //   - All of the segments in the range must have enough horizontal width to contain the float
            bool placed = false;
            start = null;
            end = null;
            placedInset = 0f;
            while (true)
            {
                // End segment does not exist: we must create a new segment below all existing segments
                if (endIdx >= _segments.Count)
                {
                    placedInset = fitter.PlacedInset(direction);
                    start = startIdx;
                    end = null;
                    placed = true;
                    break;
                }

                Segment endSegment = _segments[endIdx];

                // Check horizontal fit. If it does not fit horizontally then it will never fit in this
                // position, so continue the outer loop to find and check a new position
                fitter.UnionInsets(endSegment.Insets);
                if (!fitter.FitsHorizontally(floatedBox.Width, direction))
                {
                    startIdx += 1;
                    endIdx = Math.Max(endIdx, startIdx);
                    break;
                }

                // Check vertical fit. If it does not (yet) fit vertically then continue the inner loop
                // to add another segment to the range of segments we are placing the float in
                if (endIdx != startIdx)
                {
                    fitter.AddHeight(endSegment.YEnd - endSegment.YStart);
                }

                if (!fitter.FitsVertically(floatedBox.Height))
                {
                    endIdx += 1;
                    continue;
                }

                placedInset = fitter.PlacedInset(direction);
                start = startIdx;
                end = endIdx;
                placed = true;
                break;
            }

            if (placed)
            {
                break;
            }
        }

        // Short-circuit for zero-height boxes. Zero-width boxes are still recorded in segments:
        // their edge acts as an obstacle that boxes establishing an independent formatting
        // context may not be placed to the outside of (e.g. via a negative margin).
        if (floatedBox.Height == 0f)
        {
            return new PlacedFloatedBox { Width = floatedBox.Width, Height = floatedBox.Height, Y = startY, XInset = placedInset };
        }

        // Handle case where floated box is placed after all existing segments
        if (start is not int startIndex)
        {
            // Before the first float, the context is float-free all the way up: the first float
            // gets an empty segment above it like any other float placed below all segments, so
            // that content positioned above it (e.g. by a negative margin) is not moved down to
            // it. The first float itself may be above the top of the block formatting context
            // (`y < 0`): its containing block may be pulled up by a negative margin.
            float lastYEnd = _segments.Count > 0 ? _segments[_segments.Count - 1].YEnd : float.NegativeInfinity;
            if (startY > lastYEnd)
            {
                _segments.Add(new Segment { YStart = lastYEnd, YEnd = startY });
            }

            float newStartY = MathHelpers.Max(lastYEnd, startY);
            float[] insets = (float[])containingBlockInsets.Clone();
            insets[slot] += floatedBox.Width;
            bool[] hasFloat = new bool[2];
            hasFloat[slot] = true;
            _segments.Add(new Segment { YStart = newStartY, YEnd = newStartY + floatedBox.Height, Insets = insets, HasFloat = hasFloat });

            // Update last_placed_float
            int newStartIdx = _segments.Count - 1;
            UpdateLastPlacedFloat(direction, newStartIdx, newStartIdx + 1);

            return new PlacedFloatedBox
            {
                Width = floatedBox.Width,
                Height = floatedBox.Height,
                Y = newStartY,
                XInset = containingBlockInsets[slot],
            };
        }

        // If the floated box doesn't start at the exact same y-offset as the segment it starts in, then
        // subdivide that segment into two segments at the y-offset that the floated box starts at, and increment
        // the start index so that the floating box is placed in the second of the two segments.
        if (startY != _segments[startIndex].YStart)
        {
            SubdivideSegment(startIndex, startY);
            startIndex += 1;
            if (end is int e)
            {
                end = e + 1;
            }
        }

        int finalEndIdx;
        if (end is not int endIndex)
        {
            // The float extends below all existing segments: add a segment for the part of it
            // below the last segment (its insets are set along with the other segments below)
            float lastYEnd = _segments[_segments.Count - 1].YEnd;
            float endY = startY + floatedBox.Height;
            if (endY > lastYEnd)
            {
                _segments.Add(new Segment { YStart = lastYEnd, YEnd = endY, Insets = (float[])containingBlockInsets.Clone() });
            }

            finalEndIdx = _segments.Count - 1;
        }
        else
        {
            float endY = startY + floatedBox.Height;

            // Due to floating point imprecision, `end_y` may coincide with (or fall before)
            // the start of the end segment even though the sum of the candidate segment
            // heights was computed as sufficient. In that case the float effectively ends at
            // the boundary of the previous segment, so walk back to the segment that actually
            // contains `end_y`.
            while (endIndex > startIndex && endY <= _segments[endIndex].YStart)
            {
                endIndex -= 1;
            }

            // Only subdivide if `end_y` falls strictly within the segment. If it lands on
            // (or, due to floating point imprecision, beyond) a segment boundary then no
            // subdivision is needed.
            if (_segments[endIndex].YStart < endY && endY < _segments[endIndex].YEnd)
            {
                SubdivideSegment(endIndex, endY);
            }

            finalEndIdx = endIndex;
        }

        // Update inset for the range of segments that the float is placed in
        float placedInsetPlusWidth = placedInset + floatedBox.Width;
        for (int i = startIndex; i <= finalEndIdx; i++)
        {
            _segments[i].Insets[slot] = placedInsetPlusWidth;
            _segments[i].HasFloat[slot] = true;
        }

        // Update last_placed_float
        UpdateLastPlacedFloat(direction, startIndex, finalEndIdx + 1);

        return new PlacedFloatedBox { Width = floatedBox.Width, Height = floatedBox.Height, Y = startY, XInset = placedInset };
    }

    private int? ClearedSegment(Clear clear)
    {
        int leftEnd = _lastPlacedEnd[0];
        int rightEnd = _lastPlacedEnd[1];
        return clear switch
        {
            Clear.Left when leftEnd > 0 => leftEnd,
            Clear.Right when rightEnd > 0 => rightEnd,
            Clear.Both when leftEnd > 0 || rightEnd > 0 => Math.Max(leftEnd, rightEnd),
            _ => null,
        };
    }

    /// <summary>The y position below which content with the given <c>clear</c> value must be placed (if any).</summary>
    public float? ClearedThreshold(Clear clear)
    {
        switch (clear)
        {
            case Clear.Left:
                return _clearBottoms[0];
            case Clear.Right:
                return _clearBottoms[1];
            case Clear.Both:
                if (_clearBottoms[0] is float l && _clearBottoms[1] is float r)
                {
                    return MathHelpers.Max(l, r);
                }

                return _clearBottoms[0] ?? _clearBottoms[1];
            default:
                return null;
        }
    }

    private float CleanedThresholdOrNegInf(Clear clear) => ClearedThreshold(clear) ?? float.NegativeInfinity;

    /// <summary>Find the highest slot (at or below <paramref name="minY"/>) for in-flow content.</summary>
    public ContentSlot FindContentSlot(float minY, float[] containingBlockInsets, Clear clear, int? after)
    {
        if (!HasActiveFloats(minY))
        {
            return new ContentSlot
            {
                SegmentId = null,
                X = containingBlockInsets[0],
                Y = minY,
                Width = _availableWidth - containingBlockInsets[0] - containingBlockInsets[1],
                Height = float.PositiveInfinity,
            };
        }

        // Clearance clears past the bottom of floats on the relevant side, including
        // zero-sized floats which occupy no segment
        minY = MathHelpers.Max(minY, CleanedThresholdOrNegInf(clear));

        // The min starting segment index
        int atLeast = after is int a ? a + 1 : 0;
        int hwm = Math.Max(atLeast, ClearedSegment(clear) is int c ? c + 1 : 0);
        int startIdx = FindStartIndex(hwm, minY);

        if (startIdx < _segments.Count)
        {
            Segment segment = _segments[startIdx];
            float insetLeft = MathHelpers.Max(segment.Insets[0], containingBlockInsets[0]);
            float insetRight = MathHelpers.Max(segment.Insets[1], containingBlockInsets[1]);
            return new ContentSlot
            {
                SegmentId = startIdx,
                X = insetLeft,
                Y = MathHelpers.Max(segment.YStart, minY),
                Width = _availableWidth - insetLeft - insetRight,
                Height = float.PositiveInfinity,
            };
        }

        return new ContentSlot
        {
            SegmentId = null,
            X = containingBlockInsets[0],
            Y = minY,
            Width = _availableWidth - containingBlockInsets[0] - containingBlockInsets[1],
            Height = float.PositiveInfinity,
        };
    }

    /// <summary>Find the highest slot (at or below <paramref name="minY"/>) for a box that establishes a new block formatting context.</summary>
    public BfcSlot FindBfcSlot(float minY, float[] containingBlockInsets, float[] margins, Direction direction, Clear clear, int? after)
    {
        float[] marginInsets = { containingBlockInsets[0] + margins[0], containingBlockInsets[1] + margins[1] };
        float noFloatWidth = _availableWidth - marginInsets[0] - marginInsets[1];
        BfcSlot noFloatSlot = new()
        {
            SegmentId = null,
            X = marginInsets[0],
            Y = minY,
            BorderWidth = noFloatWidth,
            StretchWidth = noFloatWidth,
        };

        if (!HasActiveFloats(minY))
        {
            return noFloatSlot;
        }

        // Clearance clears past the bottom of floats on the relevant side, including
        // zero-sized floats which occupy no segment
        minY = MathHelpers.Max(minY, CleanedThresholdOrNegInf(clear));

        // The min starting segment index
        int atLeast = after is int a ? a + 1 : 0;
        int hwm = Math.Max(atLeast, ClearedSegment(clear) is int c ? c + 1 : 0);
        int startIdx = FindStartIndex(hwm, minY);

        if (startIdx < _segments.Count)
        {
            Segment segment = _segments[startIdx];
            int lead = direction == Direction.Rtl ? 1 : 0;
            int trail = 1 - lead;
            bool hasLeadFloat = segment.HasFloat[lead];
            bool hasTrailFloat = segment.HasFloat[trail];

            float[] fitInsets = new float[2];
            float[] stretchInsets = new float[2];
            fitInsets[lead] = hasLeadFloat ? MathHelpers.Max(segment.Insets[lead], marginInsets[lead]) : marginInsets[lead];
            stretchInsets[lead] = fitInsets[lead];
            fitInsets[trail] = hasTrailFloat
                ? MathHelpers.Max(segment.Insets[trail], containingBlockInsets[trail])
                // A positive trailing margin may overflow the containing block edge (it does
                // not affect fit), but a negative one widens the space for the border box
                : MathHelpers.Min(marginInsets[trail], containingBlockInsets[trail]);
            stretchInsets[trail] = hasTrailFloat
                ? MathHelpers.Max(segment.Insets[trail], marginInsets[trail])
                : marginInsets[trail];

            return new BfcSlot
            {
                SegmentId = startIdx,
                X = fitInsets[0],
                Y = MathHelpers.Max(segment.YStart, minY),
                BorderWidth = _availableWidth - fitInsets[0] - fitInsets[1],
                StretchWidth = _availableWidth - stretchInsets[0] - stretchInsets[1],
            };
        }

        // Below all floats
        BfcSlot below = noFloatSlot;
        below.Y = MathHelpers.Max(_segments.Count > 0 ? _segments[_segments.Count - 1].YEnd : minY, minY);
        return below;
    }
}

/// <summary>Computes the contribution of floats to the intrinsic width of their block container.</summary>
public sealed class FloatIntrinsicWidthCalculator
{
    private readonly AvailableSpace _availableWidth;
    private readonly float[] _sideSums = new float[2];
    private float _contribution;
    private float _widest;

    public FloatIntrinsicWidthCalculator(AvailableSpace availableWidth) => _availableWidth = availableWidth;

    public void AddFloat(float width, FloatDirection direction, Clear clear)
    {
        if (_availableWidth.IsMinContent)
        {
            _contribution = MathHelpers.Max(_contribution, width);
        }
        else
        {
            // Definite available width means the container is being fit-content sized
            // (e.g. it is itself a float being shrink-to-fit sized). The floats' max-content
            // contribution (widths summed) is clamped by the available width in `Result`.
            //
            // A float that clears a side is placed below the floats on that side, so its
            // width does not accumulate with theirs.
            if (clear is Clear.Left or Clear.Both)
            {
                _sideSums[0] = 0f;
            }

            if (clear is Clear.Right or Clear.Both)
            {
                _sideSums[1] = 0f;
            }

            _sideSums[(int)direction] += width;
            _contribution = MathHelpers.Max(_contribution, _sideSums[0] + _sideSums[1]);
        }

        _widest = MathHelpers.Max(_widest, width);
    }

    public float Result() =>
        // Fit-content sizing: clamp the max-content contribution between the available
        // width and the min-content contribution (floats narrow by wrapping onto new
        // bands, but never below the widest single float).
        _availableWidth.IsDefinite
            ? MathHelpers.Max(MathHelpers.Min(_contribution, _availableWidth.Value), _widest)
            : _contribution;
}
