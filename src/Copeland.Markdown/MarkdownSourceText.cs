namespace Copeland.Markdown;

public sealed class MarkdownSourceText
{
    private readonly int[] lineStarts;

    public MarkdownSourceText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Text = text;
        lineStarts = BuildLineStarts(text);
    }

    public string Text { get; }

    public SourceSpan CreateSpan(int start, int length)
    {
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start), start, "Span start must be non-negative.");
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Span length must be non-negative.");
        }

        if (start > Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(start), start, "Span start must be within the source text.");
        }

        if (start + length > Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Span end must be within the source text.");
        }

        SourceLocation startLocation = GetLocation(start);
        SourceLocation endLocation = GetLocation(start + length);
        return new SourceSpan(start, length, startLocation, endLocation);
    }

    public SourceLocation GetLocation(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be non-negative.");
        }

        if (index > Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be within the source text.");
        }

        int lineIndex = FindLineIndex(index);
        int lineStart = lineStarts[lineIndex];
        return new SourceLocation(index, lineIndex + 1, (index - lineStart) + 1);
    }

    private int FindLineIndex(int index)
    {
        int candidate = Array.BinarySearch(lineStarts, index);
        if (candidate >= 0)
        {
            return candidate;
        }

        int insertionPoint = ~candidate;
        return Math.Max(0, insertionPoint - 1);
    }

    private static int[] BuildLineStarts(string text)
    {
        List<int> starts = [0];

        for (int index = 0; index < text.Length; index += 1)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index += 1;
                }

                starts.Add(index + 1);
                continue;
            }

            if (text[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        return starts.ToArray();
    }
}
