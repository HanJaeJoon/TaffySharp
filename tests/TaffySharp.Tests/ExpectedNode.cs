using System.Globalization;
using System.Text;

namespace TaffySharp.Tests;

/// <summary>Expected (or computed) layout of a node, compared like upstream's <c>OutputNode</c> in tests/xml.rs.</summary>
public sealed class ExpectedNode
{
    public ExpectedNode(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }

    public Size<float>? ScrollSize { get; set; }

    public string? ResolvedRows { get; set; }

    public string? ResolvedColumns { get; set; }

    public List<ExpectedNode> Children { get; } = new();

    /// <summary>Asserts that the final layout of the tree rooted at <paramref name="root"/> matches <paramref name="expected"/>.</summary>
    public static void AssertLayout(TaffyTree<TestNodeContext> tree, NodeId root, ExpectedNode expected)
    {
        ExpectedNode actual = FromTree(tree, root);
        if (!expected.Matches(actual))
        {
            Assert.Fail("\nEXPECTED\n" + expected.Print() + "\nACTUAL\n" + actual.Print());
        }
    }

    private static ExpectedNode FromTree(TaffyTree<TestNodeContext> tree, NodeId node)
    {
        Layout layout = tree.Layout(node);
        ExpectedNode output = new(layout.Location.X, layout.Location.Y, layout.Size.Width, layout.Size.Height)
        {
            ScrollSize = new Size<float>(layout.ScrollWidth(), layout.ScrollHeight()),
        };
        if (tree.DetailedLayoutInfo(node).Grid is DetailedGridInfo info)
        {
            output.ResolvedRows = info.GridTemplateRows(1f);
            output.ResolvedColumns = info.GridTemplateColumns(1f);
        }

        foreach (NodeId child in tree.Children(node))
        {
            output.Children.Add(FromTree(tree, child));
        }

        return output;
    }

    private static bool Close(float expected, float actual) => Math.Abs(expected - actual) < 0.1f;

    private bool Matches(ExpectedNode other)
    {
        // Only assert scroll sizes and resolved track lists when both the expectation and the computed value are available
        bool scrollSizeMatches = ScrollSize is not Size<float> expected || other.ScrollSize is not Size<float> actual
            || (Close(expected.Width, actual.Width) && Close(expected.Height, actual.Height));
        return Close(X, other.X) && Close(Y, other.Y) && Close(Width, other.Width) && Close(Height, other.Height)
            && scrollSizeMatches
            && TrackListsMatch(ResolvedRows, other.ResolvedRows)
            && TrackListsMatch(ResolvedColumns, other.ResolvedColumns)
            && Children.Count == other.Children.Count
            && Children.Zip(other.Children, (e, a) => e.Matches(a)).All(m => m);
    }

    /// <summary>Compares resolved track lists: line names exactly, track sizes with a tolerance.</summary>
    private static bool TrackListsMatch(string? expected, string? actual)
    {
        if (expected is null || actual is null)
        {
            return true;
        }

        List<string> e = ParseTrackList(expected);
        List<string> a = ParseTrackList(actual);
        return e.Count == a.Count && e.Zip(a, (x, y) =>
            x.EndsWith("px", StringComparison.Ordinal) && y.EndsWith("px", StringComparison.Ordinal)
                ? Close(ParsePx(x), ParsePx(y))
                : x == y).All(m => m);
    }

    /// <summary>Splits e.g. <c>[foo] 10px 20.5px [bar baz]</c> into name groups and sizes; <c>none</c> is empty.</summary>
    private static List<string> ParseTrackList(string input)
    {
        input = input.Trim();
        List<string> tokens = new();
        if (input == "none")
        {
            return tokens;
        }

        string rest = input;
        while ((rest = rest.TrimStart()).Length > 0)
        {
            if (rest[0] == '[')
            {
                int end = rest.IndexOf(']');
                if (end < 0)
                {
                    throw new FormatException($"unterminated line name group in \"{input}\"");
                }

                tokens.Add("[" + string.Join(" ", rest.Substring(1, end - 1).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) + "]");
                rest = rest.Substring(end + 1);
            }
            else
            {
                int end = rest.IndexOf(' ');
                if (end < 0)
                {
                    end = rest.Length;
                }

                string size = rest.Substring(0, end);
                ParsePx(size);
                tokens.Add(size);
                rest = rest.Substring(end);
            }
        }

        return tokens;
    }

    private static float ParsePx(string size) =>
        size.EndsWith("px", StringComparison.Ordinal) && float.TryParse(size.Substring(0, size.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : throw new FormatException($"invalid track size \"{size}\"");

    private string Print()
    {
        StringBuilder sb = new();
        Print(sb, "");
        return sb.ToString();
    }

    private void Print(StringBuilder sb, string indent)
    {
        sb.Append(indent).Append(string.Format(CultureInfo.InvariantCulture, "[x: {0,-4} y: {1,-4} w: {2,-4} h: {3,-4}]", X, Y, Width, Height));
        if (ScrollSize is Size<float> scroll)
        {
            sb.Append(string.Format(CultureInfo.InvariantCulture, " [scroll_w: {0,-4} scroll_h: {1,-4}]", scroll.Width, scroll.Height));
        }

        if (ResolvedRows is not null)
        {
            sb.Append(" [rows: ").Append(ResolvedRows).Append(']');
        }

        if (ResolvedColumns is not null)
        {
            sb.Append(" [columns: ").Append(ResolvedColumns).Append(']');
        }

        sb.Append('\n');
        foreach (ExpectedNode child in Children)
        {
            child.Print(sb, indent + "    ");
        }
    }
}
