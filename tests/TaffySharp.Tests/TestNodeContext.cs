using System.Text;

namespace TaffySharp.Tests;

/// <summary>Whether text is horizontal or vertical.</summary>
public enum WritingMode
{
    Horizontal,
    Vertical,
}

/// <summary>Node context for tests: the measurement data and how many times the node was measured.</summary>
public sealed class TestNodeContext
{
    private readonly Func<Size<float?>, Size<AvailableSpace>, Size<float>> _measure;

    private TestNodeContext(Func<Size<float?>, Size<AvailableSpace>, Size<float>> measure) => _measure = measure;

    /// <summary>How many times the measure function has been called.</summary>
    public int Count { get; set; }

    public static TestNodeContext Zero() => new((_, _) => Size.Zero);

    public static TestNodeContext Fixed(float width, float height) => new((_, _) => new Size<float>(width, height));

    public static TestNodeContext AspectRatio(float width, float heightRatio) => new((knownDimensions, _) =>
    {
        float w = knownDimensions.Width ?? width;
        float h = knownDimensions.Height ?? w * heightRatio;
        return new Size<float>(w, h);
    });

    public static TestNodeContext AhemText(string textContent, WritingMode writingMode) =>
        new((knownDimensions, availableSpace) => MeasureAhemText(textContent, writingMode, knownDimensions, availableSpace));

    internal Size<float> Measure(Size<float?> knownDimensions, Size<AvailableSpace> availableSpace) => _measure(knownDimensions, availableSpace);

    private static Size<float> MeasureAhemText(string textContent, WritingMode writingMode, Size<float?> knownDimensions, Size<AvailableSpace> availableSpace)
    {
        const char Zws = '\u200B';
        const float HWidth = 10f;
        const float HHeight = 10f;

        AbsoluteAxis inlineAxis = writingMode == WritingMode.Horizontal ? AbsoluteAxis.Horizontal : AbsoluteAxis.Vertical;
        AbsoluteAxis blockAxis = inlineAxis.OtherAxis();
        // Rust str::len is the UTF-8 byte length
        int[] lines = textContent.Split(Zws).Select(line => Encoding.UTF8.GetByteCount(line)).ToArray();

        int minLineLength = lines.Max();
        int maxLineLength = lines.Sum();
        float? knownInline = knownDimensions.GetAbs(inlineAxis);
        AvailableSpace availableInline = availableSpace.GetAbs(inlineAxis);
        float inlineSize = MathHelpers.Max(
            knownInline ?? (availableInline.IsMinContent ? minLineLength * HWidth
                : availableInline.IsMaxContent ? maxLineLength * HWidth
                : MathHelpers.Min(availableInline.Value, maxLineLength * HWidth)),
            minLineLength * HWidth);

        float blockSize;
        if (knownDimensions.GetAbs(blockAxis) is float knownBlock)
        {
            blockSize = knownBlock;
        }
        else
        {
            int inlineLineLength = (int)Math.Floor(inlineSize / HWidth);
            int lineCount = 1;
            int currentLineLength = 0;
            foreach (int line in lines)
            {
                if (currentLineLength + line > inlineLineLength)
                {
                    if (currentLineLength > 0)
                    {
                        lineCount += 1;
                    }

                    currentLineLength = line;
                }
                else
                {
                    currentLineLength += line;
                }
            }

            blockSize = lineCount * HHeight;
        }

        return writingMode == WritingMode.Horizontal ? new Size<float>(inlineSize, blockSize) : new Size<float>(blockSize, inlineSize);
    }
}

public static class TestHelpers
{
    /// <summary>A measure function for tests that works with <see cref="TestNodeContext"/>.</summary>
    public static LayoutOutput MeasureFunction(LayoutInput inputs, NodeId nodeId, TestNodeContext? context, Style style) =>
        Compute.ComputeLeafLayout(inputs, style, (_, _) => 0f, (knownDimensions, availableSpace) =>
        {
            if (knownDimensions.Width is float width && knownDimensions.Height is float height)
            {
                return new Size<float>(width, height);
            }

            if (context is null)
            {
                return new Size<float>(knownDimensions.Width ?? 0f, knownDimensions.Height ?? 0f);
            }

            context.Count += 1;
            Size<float> computeSize = context.Measure(knownDimensions, availableSpace);
            return new Size<float>(knownDimensions.Width ?? computeSize.Width, knownDimensions.Height ?? computeSize.Height);
        });

    /// <summary>A single track whose min sizing function is derived from the max one, as the CSS track parser does.</summary>
    public static TrackSizingFunction Track(MaxTrackSizingFunction max) => new(MinTrackSizingFunction.From(max), max);
}
