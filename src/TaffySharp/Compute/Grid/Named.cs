using System.Collections;

namespace TaffySharp;

/// <summary>Map from a line name to the (1-based, sorted, deduplicated) lines with that name.</summary>
internal sealed class NamedGridLinesMap : Dictionary<string, List<uint>>
{
    public NamedGridLinesMap()
        : base(StringComparer.Ordinal)
    {
    }
}

/// <summary>The 1-based edge lines of a named grid area. A value of 0 means the edge is not defined.</summary>
internal struct AreaLines : IEquatable<AreaLines>
{
    public uint RowStart;
    public uint RowEnd;
    public uint ColumnStart;
    public uint ColumnEnd;

    public readonly uint? Line(GridAreaAxis axis, GridAreaEnd end)
    {
        uint line = (axis, end) switch
        {
            (GridAreaAxis.Row, GridAreaEnd.Start) => RowStart,
            (GridAreaAxis.Row, GridAreaEnd.End) => RowEnd,
            (GridAreaAxis.Column, GridAreaEnd.Start) => ColumnStart,
            _ => ColumnEnd,
        };
        return line != 0 ? line : null;
    }

    public readonly uint RawLine(GridAreaAxis axis, GridAreaEnd end) => Line(axis, end) ?? 0;

    public void SetLine(GridAreaAxis axis, GridAreaEnd end, uint value)
    {
        switch (axis, end)
        {
            case (GridAreaAxis.Row, GridAreaEnd.Start):
                RowStart = value;
                break;
            case (GridAreaAxis.Row, GridAreaEnd.End):
                RowEnd = value;
                break;
            case (GridAreaAxis.Column, GridAreaEnd.Start):
                ColumnStart = value;
                break;
            default:
                ColumnEnd = value;
                break;
        }
    }

    public readonly bool Equals(AreaLines other) =>
        RowStart == other.RowStart && RowEnd == other.RowEnd && ColumnStart == other.ColumnStart && ColumnEnd == other.ColumnEnd;

    public override readonly bool Equals(object? obj) => obj is AreaLines other && Equals(other);

    public override readonly int GetHashCode() => (RowStart, RowEnd, ColumnStart, ColumnEnd).GetHashCode();
}

/// <summary>Map from an area name to the lines of that area.</summary>
internal sealed class GridAreasMap : Dictionary<string, AreaLines>
{
    public GridAreasMap()
        : base(StringComparer.Ordinal)
    {
    }
}

/// <summary>Resolves named lines in one axis.</summary>
internal readonly struct NamedLineResolverAxis
{
    private readonly NamedGridLinesMap _lines;
    private readonly GridAreasMap _areas;
    private readonly GridAreaAxis _axis;
    private readonly ushort _explicitTrackCount;

    public NamedLineResolverAxis(NamedGridLinesMap lines, GridAreasMap areas, GridAreaAxis axis, ushort explicitTrackCount)
    {
        _lines = lines;
        _areas = areas;
        _axis = axis;
        _explicitTrackCount = explicitTrackCount;
    }

    private uint? AreaLine(string areaName, GridAreaEnd end) =>
        _areas.TryGetValue(areaName, out AreaLines area) ? area.Line(_axis, end) : null;

    /// <summary>Resolve named lines (and named spans) of a placement into plain line/span placements.</summary>
    public Line<GenericGridPlacement<GridLine>> ResolveLineNames(Line<GridPlacement> line)
    {
        GridPlacement startLineResolved = line.Start.Kind == GridPlacementKind.NamedLine
            ? GridPlacement.Line(FindNamedLine(line.Start.Name!, line.Start.LineIndex, GridAreaEnd.Start).Value)
            : line.Start;
        GridPlacement endLineResolved = line.End.Kind == GridPlacementKind.NamedLine
            ? GridPlacement.Line(FindNamedLine(line.End.Name!, line.End.LineIndex, GridAreaEnd.End).Value)
            : line.End;

        // If both the *-start and *-end values of its grid-placement properties specify a line, its grid span is implicit.
        // If it has an explicit span value, its grid span is explicit.
        // Otherwise, its grid span is automatic:
        //   - if it is subgridded in that axis, its grid span is determined from its <line-name-list>;
        //   - otherwise its grid span is 1.
        //
        // <https://drafts.csswg.org/css-grid-2/#grid-span>
        if (startLineResolved.Kind == GridPlacementKind.Line && endLineResolved.Kind == GridPlacementKind.NamedSpan && startLineResolved.LineIndex != 0)
        {
            GridLine startLine = startLineResolved.GridLine;
            OriginZeroLine afterStartLine = startLine.IntoOriginZeroLine(_explicitTrackCount) + (ushort)1;
            OriginZeroLine endLine = NthLineForwards(LinesNamed(endLineResolved.Name!), afterStartLine, Math.Max((int)endLineResolved.SpanCount, 1));
            return new(GenericGridPlacement<GridLine>.Line(startLine), GenericGridPlacement<GridLine>.Line(endLine.IntoGridLine(_explicitTrackCount)));
        }

        if (startLineResolved.Kind == GridPlacementKind.NamedSpan && endLineResolved.Kind == GridPlacementKind.Line && endLineResolved.LineIndex != 0)
        {
            GridLine endLine = endLineResolved.GridLine;
            OriginZeroLine beforeEndLine = endLine.IntoOriginZeroLine(_explicitTrackCount) - (ushort)1;
            OriginZeroLine startLine = NthLineBackwards(LinesNamed(startLineResolved.Name!), beforeEndLine, Math.Max((int)startLineResolved.SpanCount, 1));
            return new(GenericGridPlacement<GridLine>.Line(startLine.IntoGridLine(_explicitTrackCount)), GenericGridPlacement<GridLine>.Line(endLine));
        }

        return new(IntoNonNamed(startLineResolved), IntoNonNamed(endLineResolved));
    }

    private static GenericGridPlacement<GridLine> IntoNonNamed(GridPlacement placement) => placement.Kind switch
    {
        GridPlacementKind.Auto => GenericGridPlacement<GridLine>.Auto,
        GridPlacementKind.Line => GenericGridPlacement<GridLine>.Line(placement.GridLine),
        GridPlacementKind.Span => GenericGridPlacement<GridLine>.Span(placement.SpanCount),
        GridPlacementKind.NamedSpan => GenericGridPlacement<GridLine>.Span(1),
        _ => throw new InvalidOperationException("named lines are resolved before this point"),
    };

    private GridLine FindNamedLine(string name, short idx, GridAreaEnd end)
    {
        // A `<custom-ident>` on its own first attempts to match the edge of a named grid area:
        // the first line named `<custom-ident>-start` (or `<custom-ident>-end` for an end line).
        // Otherwise it is treated as if the integer 1 had been specified along with it.
        if (idx == 0 && AreaLine(name, end) is uint firstLine)
        {
            return OriginZeroLine.Clamped(OzLine(firstLine)).IntoGridLine(_explicitTrackCount);
        }

        IReadOnlyList<uint> lines = LinesNamed(name);
        OriginZeroLine line = idx >= 0
            ? NthLineForwards(lines, new OriginZeroLine(0), Math.Max((int)idx, 1))
            : NthLineBackwards(lines, LastExplicitLine(), -(int)idx);
        return line.IntoGridLine(_explicitTrackCount);
    }

    private IReadOnlyList<uint> LinesNamed(string name)
    {
        if (_lines.TryGetValue(name, out List<uint>? lines))
        {
            return lines;
        }

        if (NamedLineResolver.SplitAreaEdgeName(name) is (string areaName, GridAreaEnd areaEnd) && AreaLine(areaName, areaEnd) is uint line)
        {
            return new[] { line };
        }

        return Array.Empty<uint>();
    }

    private OriginZeroLine LastExplicitLine() => new((short)_explicitTrackCount);

    private OriginZeroLine NthLineForwards(IReadOnlyList<uint> lines, OriginZeroLine firstLine, int n)
    {
        int first = Math.Max((int)firstLine.Value, 0);
        int skip = PartitionPoint(lines, l => OzLine(l) < first);
        int remaining = lines.Count - skip;
        int line = n - 1 < remaining
            ? OzLine(lines[skip + n - 1])
            : Math.Max(first, LastExplicitLine().Value + 1) + (n - remaining) - 1;
        return OriginZeroLine.Clamped(line);
    }

    private OriginZeroLine NthLineBackwards(IReadOnlyList<uint> lines, OriginZeroLine lastLine, int n)
    {
        int last = Math.Min((int)lastLine.Value, LastExplicitLine().Value);
        int count = PartitionPoint(lines, l => OzLine(l) <= last);
        int line = count >= n
            ? OzLine(lines[count - n])
            : Math.Min(last, -1) - (n - count) + 1;
        return OriginZeroLine.Clamped(line);
    }

    private static int PartitionPoint(IReadOnlyList<uint> sortedLines, Func<uint, bool> predicate)
    {
        int index = 0;
        while (index < sortedLines.Count && predicate(sortedLines[index]))
        {
            index++;
        }

        return index;
    }

    private static int OzLine(uint line) => (int)line - 1;
}

/// <summary>Resolves named grid lines and areas into line numbers.</summary>
internal sealed class NamedLineResolver
{
    private readonly NamedGridLinesMap _rowLines;
    private readonly NamedGridLinesMap _columnLines;
    private readonly GridAreasMap _areas;
    private readonly ushort _areaColumnCount;
    private readonly ushort _areaRowCount;
    private ushort _explicitColumnCount;
    private ushort _explicitRowCount;
    private readonly List<(uint Line, string Name)> _columnLineNamePairs;
    private readonly List<(uint Line, string Name)> _rowLineNamePairs;

    private NamedLineResolver(
        NamedGridLinesMap rowLines,
        NamedGridLinesMap columnLines,
        GridAreasMap areas,
        ushort areaColumnCount,
        ushort areaRowCount,
        List<(uint Line, string Name)> columnLineNamePairs,
        List<(uint Line, string Name)> rowLineNamePairs)
    {
        _rowLines = rowLines;
        _columnLines = columnLines;
        _areas = areas;
        _areaColumnCount = areaColumnCount;
        _areaRowCount = areaRowCount;
        _columnLineNamePairs = columnLineNamePairs;
        _rowLineNamePairs = rowLineNamePairs;
    }

    internal static (string AreaName, GridAreaEnd End)? SplitAreaEdgeName(string name)
    {
        if (name.EndsWith("-start", StringComparison.Ordinal))
        {
            return (name.Substring(0, name.Length - "-start".Length), GridAreaEnd.Start);
        }

        if (name.EndsWith("-end", StringComparison.Ordinal))
        {
            return (name.Substring(0, name.Length - "-end".Length), GridAreaEnd.End);
        }

        return null;
    }

    private static void UpsertLineNameMap(NamedGridLinesMap map, GridAreasMap areas, GridAreaAxis axis, string key, uint value)
    {
        // A line named `foo-start` or `foo-end` is also an edge of the (implicitly) named area `foo`. Record it as such
        // if it is the first such line. If `foo` is also an area defined by `grid-template-areas`, then the line which
        // that area generated also has this name.
        uint areaLine = 0;
        if (SplitAreaEdgeName(key) is (string areaName, GridAreaEnd end))
        {
            // The key is only created if the area is not already in the map
            areas.TryGetValue(areaName, out AreaLines area);
            areaLine = area.RawLine(axis, end);
            area.SetLine(axis, end, areaLine == 0 ? value : Math.Min(areaLine, value));
            areas[areaName] = area;
        }

        if (map.TryGetValue(key, out List<uint>? lines))
        {
            lines.Add(value);
        }
        else
        {
            // Until this entry is created, the only line that can have been recorded for this edge is the area's own
            map[key] = areaLine == 0 ? new List<uint> { value } : new List<uint> { areaLine, value };
        }
    }

    public static NamedLineResolver New(IGridContainerStyle style, ushort columnAutoRepetitions, ushort rowAutoRepetitions)
    {
        NamedGridLinesMap columnLines = new();
        NamedGridLinesMap rowLines = new();
        GridAreasMap areas = new();
        List<(uint Line, string Name)> columnLineNamePairs = new();
        List<(uint Line, string Name)> rowLineNamePairs = new();

        // The size of the area template may be larger than the extents of the named areas
        // due to unnamed (`.`) cells, so it is taken from the style rather than being derived
        // from the areas themselves.
        ushort areaColumnCount = style.GridTemplateAreaColumnCount;
        ushort areaRowCount = style.GridTemplateAreaRowCount;

        if (style.GridTemplateAreas is IReadOnlyList<GridTemplateArea> areaList)
        {
            foreach (GridTemplateArea area in areaList)
            {
                areas[area.Name] = new AreaLines
                {
                    RowStart = area.RowStart,
                    RowEnd = area.RowEnd,
                    ColumnStart = area.ColumnStart,
                    ColumnEnd = area.ColumnEnd,
                };
            }
        }

        CollectLineNames(style.GridTemplateColumns, style.GridTemplateColumnNames, columnAutoRepetitions, columnLines, areas, GridAreaAxis.Column, columnLineNamePairs);
        CollectLineNames(style.GridTemplateRows, style.GridTemplateRowNames, rowAutoRepetitions, rowLines, areas, GridAreaAxis.Row, rowLineNamePairs);

        // Sort and dedup lines for each name
        SortAndDedup(columnLines);
        SortAndDedup(rowLines);

        return new NamedLineResolver(rowLines, columnLines, areas, areaColumnCount, areaRowCount, columnLineNamePairs, rowLineNamePairs);
    }

    private static void CollectLineNames(
        IReadOnlyList<GridTemplateComponent>? tracks,
        IReadOnlyList<IReadOnlyList<string>>? lineNamesList,
        ushort autoRepetitions,
        NamedGridLinesMap lines,
        GridAreasMap areas,
        GridAreaAxis axis,
        List<(uint Line, string Name)> pairs)
    {
        if (tracks is null || lineNamesList is null)
        {
            return;
        }

        uint currentLine = 0;
        int trackIndex = 0;
        foreach (IReadOnlyList<string> lineNames in lineNamesList)
        {
            currentLine += 1;
            foreach (string lineName in lineNames)
            {
                pairs.Add((currentLine, lineName));
                UpsertLineNameMap(lines, areas, axis, lineName, currentLine);
            }

            if (trackIndex >= tracks.Count)
            {
                continue;
            }

            GridTemplateComponent component = tracks[trackIndex++];
            if (component.Repetition is not GridTemplateRepetition repeat)
            {
                continue;
            }

            ushort repeatCount = repeat.Count.Kind == RepetitionCountKind.Count ? repeat.Count.Value : autoRepetitions;

            // Line name sets are positional: set `i` names the `i`th line of each
            // repetition, and the final line name set of each repetition collapses
            // with the first line name set of the following one. An empty list means
            // the repetition's lines are unnamed; any other length must be exactly
            // `track_count + 1` (one set per line, including both edge lines).
            uint lineNameSetCount = (uint)repeat.LineNames.Count;
            uint linesPerRepetition = repeat.TrackCount;
            if (!(lineNameSetCount == 0 || lineNameSetCount == linesPerRepetition + 1))
            {
                throw new InvalidOperationException(
                    $"grid template repetition must have no line name sets or exactly track count + 1 of them ({linesPerRepetition} tracks but {lineNameSetCount} line name sets)");
            }

            for (int i = 0; i < repeatCount; i++)
            {
                uint line = currentLine;
                foreach (List<string> lineNameSet in repeat.LineNames)
                {
                    foreach (string lineName in lineNameSet)
                    {
                        pairs.Add((line, lineName));
                        UpsertLineNameMap(lines, areas, axis, lineName, line);
                    }

                    line++;
                }

                currentLine += linesPerRepetition;

                // Names for lines beyond the maximum track limit are never resolvable:
                // stop generating them (the explicit grid is clamped to MAX_GRID_TRACKS)
                if (currentLine > GridConstants.MaxGridTracks)
                {
                    break;
                }
            }

            // Last line name set collapses with following line name set
            if (repeatCount > 0 && currentLine > 0)
            {
                currentLine -= 1;
            }
        }
    }

    private static void SortAndDedup(NamedGridLinesMap map)
    {
        foreach (List<uint> lines in map.Values)
        {
            lines.Sort();
            int write = 0;
            for (int read = 0; read < lines.Count; read++)
            {
                if (write == 0 || lines[read] != lines[write - 1])
                {
                    lines[write++] = lines[read];
                }
            }

            lines.RemoveRange(write, lines.Count - write);
        }
    }

    /// <summary>Build the per-line names of the explicit grid in the given axis.</summary>
    public GridLineNames DetailedLineNames(AbsoluteAxis axis)
    {
        (List<(uint Line, string Name)> pairs, ushort explicitTrackCount) = axis == AbsoluteAxis.Horizontal
            ? (_columnLineNamePairs, _explicitColumnCount)
            : (_rowLineNamePairs, _explicitRowCount);

        if (pairs.Count == 0)
        {
            return new GridLineNames();
        }

        // Stable sort by line number preserves the source order of names within each line
        List<(uint Line, string Name)> sortedPairs = new(pairs);
        sortedPairs.StableSort((a, b) => a.Line.CompareTo(b.Line));

        int lineCount = explicitTrackCount + 1;
        GridLineNames lineNames = GridLineNames.WithCapacity(sortedPairs.Count, lineCount + 1);
        int pairIndex = 0;
        for (uint line = 1; line <= (uint)lineCount; line++)
        {
            lineNames.StartLine();
            while (pairIndex < sortedPairs.Count)
            {
                (uint pairLine, string name) = sortedPairs[pairIndex];
                if (pairLine != line)
                {
                    break;
                }

                pairIndex++;
                if (!lineNames.CurrentLineContains(name))
                {
                    lineNames.PushName(name);
                }
            }
        }

        return lineNames;
    }

    public Line<GenericGridPlacement<GridLine>> ResolveRowNames(Line<GridPlacement> line) => ResolveLineNames(line, GridAreaAxis.Row);

    public Line<GenericGridPlacement<GridLine>> ResolveColumnNames(Line<GridPlacement> line) => ResolveLineNames(line, GridAreaAxis.Column);

    public Line<GenericGridPlacement<GridLine>> ResolveLineNames(Line<GridPlacement> line, GridAreaAxis axis)
    {
        (NamedGridLinesMap lines, ushort explicitTrackCount) = axis == GridAreaAxis.Row
            ? (_rowLines, _explicitRowCount)
            : (_columnLines, _explicitColumnCount);
        return new NamedLineResolverAxis(lines, _areas, axis, explicitTrackCount).ResolveLineNames(line);
    }

    /// <summary>Move the named line maps into the detailed line names and return the areas map.</summary>
    public GridAreasMap PopulateDetailedLineResolvers(GridLineNames rows, GridLineNames columns)
    {
        rows.Resolver = _rowLines;
        columns.Resolver = _columnLines;
        return _areas;
    }

    public ushort AreaColumnCount() => _areaColumnCount;

    public ushort AreaRowCount() => _areaRowCount;

    public void SetExplicitColumnCount(ushort count) => _explicitColumnCount = count;

    public void SetExplicitRowCount(ushort count) => _explicitRowCount = count;
}

/// <summary>The names of the lines of the explicit grid in one axis (one list of names per line).</summary>
public sealed class GridLineNames : IEnumerable<IReadOnlyList<string>>, IEquatable<GridLineNames>
{
    private readonly List<string> _names;
    private readonly List<uint> _offsets;

    public GridLineNames()
    {
        _names = new List<string>();
        _offsets = new List<uint>();
        Resolver = new NamedGridLinesMap();
    }

    private GridLineNames(int nameCapacity, int offsetCapacity)
    {
        _names = new List<string>(nameCapacity);
        _offsets = new List<uint>(offsetCapacity) { 0 };
        Resolver = new NamedGridLinesMap();
    }

    internal NamedGridLinesMap Resolver { get; set; }

    internal static GridLineNames WithCapacity(int nameCapacity, int offsetCapacity) => new(nameCapacity, offsetCapacity);

    internal void StartLine() => _offsets.Add((uint)_names.Count);

    internal void PushName(string name)
    {
        _names.Add(name);
        _offsets[_offsets.Count - 1] = (uint)_names.Count;
    }

    internal bool CurrentLineContains(string name)
    {
        foreach (string n in Line(LineCount() - 1))
        {
            if (n == name)
            {
                return true;
            }
        }

        return false;
    }

    internal Line<GenericGridPlacement<GridLine>> ResolveLineNames(Line<GridPlacement> line, GridAreasMap areas, GridAreaAxis axis, ushort explicitTrackCount) =>
        new NamedLineResolverAxis(Resolver, areas, axis, explicitTrackCount).ResolveLineNames(line);

    /// <summary>Whether no line has a name.</summary>
    public bool IsEmpty() => _names.Count == 0;

    /// <summary>The number of lines stored.</summary>
    public int LineCount() => Math.Max(_offsets.Count - 1, 0);

    /// <summary>The names of the line at the given (0-based) index; empty if out of range.</summary>
    public IReadOnlyList<string> Line(int lineIndex)
    {
        if (lineIndex < 0 || lineIndex + 1 >= _offsets.Count)
        {
            return Array.Empty<string>();
        }

        int start = (int)_offsets[lineIndex];
        int end = (int)_offsets[lineIndex + 1];
        return _names.GetRange(start, end - start);
    }

    /// <summary>Iterate over the names of each line.</summary>
    public GridLineNamesIter Iter() => IterPadded(0, 0);

    internal GridLineNamesIter IterPadded(int leadingEmpty, int trailingEmpty) => new(this, leadingEmpty, trailingEmpty);

    public IEnumerator<IReadOnlyList<string>> GetEnumerator() => Iter().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(GridLineNames? other)
    {
        if (other is null)
        {
            return false;
        }

        if (!_names.SequenceEqual(other._names) || !_offsets.SequenceEqual(other._offsets) || Resolver.Count != other.Resolver.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, List<uint>> entry in Resolver)
        {
            if (!other.Resolver.TryGetValue(entry.Key, out List<uint>? lines) || !entry.Value.SequenceEqual(lines))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is GridLineNames other && Equals(other);

    public override int GetHashCode() => (_names.Count, _offsets.Count).GetHashCode();
}

/// <summary>Iterates over the names of each line, optionally padded with empty lines at either end.</summary>
public sealed class GridLineNamesIter : IEnumerable<IReadOnlyList<string>>
{
    private readonly GridLineNames _lineNames;
    private readonly int _leadingEmpty;
    private readonly int _trailingEmpty;

    internal GridLineNamesIter(GridLineNames lineNames, int leadingEmpty, int trailingEmpty)
    {
        _lineNames = lineNames;
        _leadingEmpty = leadingEmpty;
        _trailingEmpty = trailingEmpty;
    }

    /// <summary>The number of lines yielded (Rust <c>ExactSizeIterator::len</c>).</summary>
    public int Len() => _leadingEmpty + _lineNames.LineCount() + _trailingEmpty;

    public IEnumerator<IReadOnlyList<string>> GetEnumerator()
    {
        for (int i = 0; i < _leadingEmpty; i++)
        {
            yield return Array.Empty<string>();
        }

        int lineCount = _lineNames.LineCount();
        for (int i = 0; i < lineCount; i++)
        {
            yield return _lineNames.Line(i);
        }

        for (int i = 0; i < _trailingEmpty; i++)
        {
            yield return Array.Empty<string>();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
