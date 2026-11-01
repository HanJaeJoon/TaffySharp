namespace TaffySharp;

/// <summary>Line breaking for <c>flex-wrap: balance</c> (Rust <c>flexbox::balance</c>).</summary>
internal static class FlexboxBalance
{
    private const double Infeasible = double.PositiveInfinity;

    private static double ToSize(float value)
    {
        double v = value;
        if (v < 0.0)
        {
            return 0.0;
        }

        if (v > float.MaxValue)
        {
            return float.MaxValue;
        }

        return v;
    }

    private sealed class LineSizes
    {
        public double[] Sums = Array.Empty<double>();
        public bool[] IsZero = Array.Empty<bool>();
        public double GapBetweenItems;
        public double Limit;

        public int ItemCount => Sums.Length;

        public bool IsZeroItem(int index) => IsZero[index];

        public double LineSize(int start, int end)
        {
            double startSum = start == 0 ? 0.0 : Sums[start - 1];
            return Sums[end] - startSum - GapBetweenItems;
        }

        public double LineCost(int start, int end)
        {
            double size = LineSize(start, end);
            if (end > start && size > Limit)
            {
                return Infeasible;
            }

            return size * size;
        }

        public uint[] SuffixGreedyLineCounts()
        {
            int itemCount = ItemCount;
            uint[] counts = new uint[itemCount + 1];

            // The (exclusive) end of the greedy first line of the suffix, which only moves
            // down as the suffix grows leftwards since lines starting earlier are larger
            int lineEnd = itemCount;
            for (int start = itemCount - 1; start >= 0; start--)
            {
                while (lineEnd > start + 1 && LineSize(start, lineEnd - 1) > Limit)
                {
                    lineEnd -= 1;
                }

                counts[start] = 1 + counts[lineEnd];
            }

            return counts;
        }

        public uint[] FitEnds()
        {
            int itemCount = ItemCount;
            uint[] fitEnds = new uint[itemCount];

            // Lines starting later are smaller, so the fit end only moves up
            int fitEnd = 0;
            for (int start = 0; start < itemCount; start++)
            {
                if (fitEnd < start)
                {
                    fitEnd = start;
                }

                while (fitEnd + 1 < itemCount && LineSize(start, fitEnd + 1) <= Limit)
                {
                    fitEnd += 1;
                }

                fitEnds[start] = (uint)fitEnd;
            }

            return fitEnds;
        }
    }

    private sealed class Row
    {
        public LineSizes Sizes = null!;
        public uint[] FitEnds = null!;
        public double[] Prev = null!;
        public List<uint> NonzeroEnds = null!;
        public int MaxEnd;
    }

    /// <summary>Rust <c>slice.partition_point</c> over <c>list[lo..hi]</c>, returning an absolute index.</summary>
    private static int PartitionPoint(List<uint> list, int lo, int hi, Func<uint, bool> pred)
    {
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (pred(list[mid]))
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static void FillRow(Row row, double[] cur, uint[] opts, int optsOffset, int startLo, int startHi, int colLo, int colHi)
    {
        if (startLo > startHi)
        {
            return;
        }

        int start = startLo + ((startHi - startLo) / 2);

        // Minimize over the in-range ends preceding a non-zero item, starting from `start`
        // (the line must hold at least one item)
        int firstCol = PartitionPoint(row.NonzeroEnds, colLo, colHi, end => (int)end < start);
        double minCost = Infeasible;
        int? minCol = null;
        for (int col = firstCol; col < colHi; col++)
        {
            int end = (int)row.NonzeroEnds[col];
            double lineCost = row.Sizes.LineCost(start, end);
            if (lineCost == Infeasible)
            {
                // Every longer line also exceeds the limit
                break;
            }

            double cost = lineCost + row.Prev[end + 1];

            // `<=` keeps the *largest* minimizing end, assigning the most items to the
            // earliest lines as the tie-break requires
            if (cost <= minCost)
            {
                minCost = cost;
                minCol = col;
            }
        }

        // Merge the single end preceding a zero item that the zero-sized-item rule allows
        int? minEnd = minCol is int mc ? (int)row.NonzeroEnds[mc] : null;
        int zeroEnd = Math.Min((int)row.FitEnds[start], row.MaxEnd);
        if (row.Sizes.IsZeroItem(zeroEnd + 1))
        {
            double cost = row.Sizes.LineCost(start, zeroEnd) + row.Prev[zeroEnd + 1];
            if (cost < minCost || (cost == minCost && (minEnd is not int e || e < zeroEnd)))
            {
                minCost = cost;
                minEnd = zeroEnd;
            }
        }

        // Infeasible states are excluded from the range by the caller, and every feasible
        // state has a valid transition (see LineSizes.SuffixGreedyLineCounts)
        System.Diagnostics.Debug.Assert(!double.IsInfinity(minCost) && !double.IsNaN(minCost));
        cur[start] = minCost;
        opts[optsOffset + start] = (uint)(minEnd ?? start);

        // The zero-rule end is excluded from the narrowing: only the non-zero ends' minima
        // are monotone
        if (start > startLo)
        {
            int newColHi = minCol is int c1 ? c1 + 1 : colHi;
            FillRow(row, cur, opts, optsOffset, startLo, start - 1, colLo, newColHi);
        }

        if (start < startHi)
        {
            int newColLo = minCol ?? firstCol;
            FillRow(row, cur, opts, optsOffset, start + 1, startHi, newColLo, colHi);
        }
    }

    /// <summary>The number of items on each line of a balanced division of the items.</summary>
    public static List<int> BalancedLineItemCounts(
        IReadOnlyList<float> itemSizes,
        float lineLimit,
        float gapBetweenItems,
        int minLineCount)
    {
        int itemCount = itemSizes.Count;
        System.Diagnostics.Debug.Assert(itemCount > 0);
        double gap = ToSize(gapBetweenItems);
        double limit = lineLimit;

        double[] sums = new double[itemCount];
        bool[] isZero = new bool[itemCount];
        double sum = 0.0;
        for (int i = 0; i < itemCount; i++)
        {
            double size = ToSize(itemSizes[i]);
            sum += size + gap;
            sums[i] = sum;
            isZero[i] = size == 0.0;
        }

        LineSizes sizes = new() { Sums = sums, IsZero = isZero, GapBetweenItems = gap, Limit = limit };

        // `suffixGreedy[start]` is the number of lines greedy line breaking produces for items
        // `start..`, which is also the *fewest* lines the suffix can validly be divided into
        uint[] suffixGreedy = sizes.SuffixGreedyLineCounts();
        int lineCount = Math.Max((int)suffixGreedy[0], MathHelpers.Clamp(minLineCount, 1, itemCount));

        List<int> itemCounts = new(lineCount);
        if (lineCount == itemCount)
        {
            // One item per line is the only division (this covers `flex-line-count` of at least
            // the item count as well as every item overflowing a line of its own)
            for (int i = 0; i < itemCount; i++)
            {
                itemCounts.Add(1);
            }

            return itemCounts;
        }

        uint[] fitEnds = sizes.FitEnds();
        List<uint> nonzeroEnds = new(itemCount - 1);
        for (int end = 0; end < itemCount - 1; end++)
        {
            if (!sizes.IsZeroItem(end + 1))
            {
                nonzeroEnds.Add((uint)end);
            }
        }

        // `prev[start]` is the minimum total score of any valid division of items `start..`
        // into exactly `lines - 1` lines (Infeasible if there is none), and `cur` is the
        // row being computed for `lines` lines: a division into `lines` lines is a first line
        // `start..=end` plus a division of `end + 1..` into `lines - 1` lines.
        // `opts[(lines - 2) * itemCount + start]` records the largest `end` achieving
        // `cur[start]`, from which the chosen division is read back.
        double[] prev = new double[itemCount];
        for (int start = 0; start < itemCount; start++)
        {
            prev[start] = sizes.LineCost(start, itemCount - 1);
        }

        double[] cur = new double[itemCount];
        for (int i = 0; i < itemCount; i++)
        {
            cur[i] = Infeasible;
        }

        uint[] opts = new uint[(lineCount - 1) * itemCount];
        for (int lines = 2; lines <= lineCount; lines++)
        {
            // The remaining `lines - 1` lines need one item each, bounding this line's end
            int maxEnd = itemCount - lines;

            // Starts whose suffix doesn't fit in `lines` lines even with greedy breaking are
            // infeasible; they form a prefix (a longer suffix never needs fewer lines), and
            // excluding them keeps every state FillRow solves feasible
            int firstStart = 0;
            while (firstStart < maxEnd && (int)suffixGreedy[firstStart] > lines)
            {
                cur[firstStart] = Infeasible;
                firstStart += 1;
            }

            int colHi = PartitionPoint(nonzeroEnds, 0, nonzeroEnds.Count, end => (int)end <= maxEnd);
            Row row = new() { Sizes = sizes, FitEnds = fitEnds, Prev = prev, NonzeroEnds = nonzeroEnds, MaxEnd = maxEnd };
            FillRow(row, cur, opts, (lines - 2) * itemCount, firstStart, maxEnd, 0, colHi);
            (prev, cur) = (cur, prev);
        }

        // Read the division back out front to back
        System.Diagnostics.Debug.Assert(!double.IsInfinity(prev[0]) && !double.IsNaN(prev[0]));
        int readStart = 0;
        for (int lines = lineCount; lines >= 2; lines--)
        {
            int end = (int)opts[((lines - 2) * itemCount) + readStart];
            itemCounts.Add(end - readStart + 1);
            readStart = end + 1;
        }

        itemCounts.Add(itemCount - readStart);
        return itemCounts;
    }
}
