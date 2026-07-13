using System.Text;
using Machina.Core.Measurement;
using Machina.Core.Styling;
using Machina.Layout.Geometry;

namespace Machina.Presenter.Sample;

public sealed record PresenterCardLayout(
    Rect OuterRect,
    Rect ContentRectInOuter,
    Rect HeaderRectInContent,
    Rect BodyRectInContent,
    Rect? FooterRectInContent)
{
    public double InnerWidth => ContentRectInOuter.Width;

    public double InnerHeight => ContentRectInOuter.Height;

    public double BodyTop => BodyRectInContent.Y;

    public double BodyWidth => BodyRectInContent.Width;

    public double BodyHeight => BodyRectInContent.Height;
}

public sealed record PresenterCardTextLayout(
    double LineHeight,
    double LineGap,
    string? Prefix = null);

public static class PresenterCardLayoutHelper
{
    public const string Ellipsis = "...";

    public static PresenterCardLayout ComputeLayout(
        double width,
        double height,
        double contentInset,
        double bodyTopInContent,
        double footerHeightInContent = 0)
    {
        double clampedWidth = Math.Max(0, width);
        double clampedHeight = Math.Max(0, height);
        double clampedInset = Math.Max(0, contentInset);
        double innerWidth = Math.Max(0, clampedWidth - (clampedInset * 2));
        double innerHeight = Math.Max(0, clampedHeight - (clampedInset * 2));
        double bodyTop = Math.Clamp(bodyTopInContent, 0, innerHeight);
        double footerHeight = Math.Clamp(footerHeightInContent, 0, Math.Max(0, innerHeight - bodyTop));
        double bodyHeight = Math.Max(0, innerHeight - bodyTop - footerHeight);

        Rect outerRect = new(0, 0, clampedWidth, clampedHeight);
        Rect contentRectInOuter = new(clampedInset, clampedInset, innerWidth, innerHeight);
        Rect headerRectInContent = new(0, 0, innerWidth, bodyTop);
        Rect bodyRectInContent = new(0, bodyTop, innerWidth, bodyHeight);
        Rect? footerRectInContent = footerHeight > 0
            ? new Rect(0, bodyTop + bodyHeight, innerWidth, footerHeight)
            : null;

        return new PresenterCardLayout(
            outerRect,
            contentRectInOuter,
            headerRectInContent,
            bodyRectInContent,
            footerRectInContent);
    }

    public static int ComputeLineCapacity(PresenterCardLayout layout, PresenterCardTextLayout textLayout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(textLayout);

        return ComputeLineCapacity(layout.BodyHeight, textLayout);
    }

    public static int ComputeLineCapacity(double availableHeight, PresenterCardTextLayout textLayout)
    {
        ArgumentNullException.ThrowIfNull(textLayout);

        double lineSpan = textLayout.LineHeight + textLayout.LineGap;
        if (availableHeight <= 0 || lineSpan <= 0)
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Floor((availableHeight + textLayout.LineGap) / lineSpan));
    }

    public static IReadOnlyList<string> ClipLinesToFit(
        IReadOnlyList<string> lines,
        double width,
        double height,
        PresenterCardTextLayout textLayout,
        TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(textLayout);

        int maxLineCount = ComputeLineCapacity(height, textLayout);
        if (maxLineCount <= 0)
        {
            return [];
        }

        List<string> visibleLines = [];
        for (int index = 0; index < lines.Count && visibleLines.Count < maxLineCount; index++)
        {
            bool isLastVisibleLine = visibleLines.Count == maxLineCount - 1;
            bool needsOverflowEllipsis = isLastVisibleLine && index < lines.Count - 1;
            visibleLines.Add(ClipSingleLine(lines[index], width, textLayout, style, needsOverflowEllipsis));
        }

        return visibleLines;
    }

    public static IReadOnlyList<string> WrapOrClipLinesToFit(
        IReadOnlyList<string> lines,
        double width,
        double height,
        PresenterCardTextLayout textLayout,
        TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(textLayout);

        int maxLineCount = ComputeLineCapacity(height, textLayout);
        if (maxLineCount <= 0)
        {
            return [];
        }

        List<string> visibleLines = [];
        for (int sourceIndex = 0; sourceIndex < lines.Count && visibleLines.Count < maxLineCount; sourceIndex++)
        {
            List<string> wrappedLines = WrapSingleLine(lines[sourceIndex], width, style);

            if (wrappedLines.Count == 0)
            {
                wrappedLines.Add(string.Empty);
            }

            for (int wrappedIndex = 0; wrappedIndex < wrappedLines.Count && visibleLines.Count < maxLineCount; wrappedIndex++)
            {
                bool isLastVisibleLine = visibleLines.Count == maxLineCount - 1;
                bool hasMoreContent = wrappedIndex < wrappedLines.Count - 1 || sourceIndex < lines.Count - 1;
                visibleLines.Add(
                    hasMoreContent && isLastVisibleLine
                        ? ClipContentToWidth(wrappedLines[wrappedIndex], width, style)
                        : wrappedLines[wrappedIndex]);
            }
        }

        return visibleLines;
    }

    private static string ClipSingleLine(
        string content,
        double width,
        PresenterCardTextLayout textLayout,
        TextStyle style,
        bool forceEllipsis)
    {
        if (string.IsNullOrEmpty(content) || width <= 0)
        {
            return string.Empty;
        }

        string prefix = textLayout.Prefix ?? string.Empty;
        double prefixWidth = string.IsNullOrEmpty(prefix) ? 0 : Measure(prefix, style);
        double contentWidth = Math.Max(0, width - prefixWidth);

        if (contentWidth <= 0)
        {
            return string.Empty;
        }

        bool contentFits = Measure(content, style) <= contentWidth;
        string visibleContent = !forceEllipsis && contentFits
            ? content
            : ClipContentToWidth(content, contentWidth, style);

        return prefix + visibleContent;
    }

    private static string ClipContentToWidth(string content, double width, TextStyle style)
    {
        if (string.IsNullOrEmpty(content) || width <= 0)
        {
            return string.Empty;
        }

        if (Measure(content, style) <= width)
        {
            return content;
        }

        if (Measure(Ellipsis, style) > width)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (char character in content)
        {
            string candidate = builder.ToString() + character + Ellipsis;
            if (Measure(candidate, style) > width)
            {
                break;
            }

            builder.Append(character);
        }

        return builder.Length == 0
            ? Ellipsis
            : builder.ToString().TrimEnd() + Ellipsis;
    }

    private static List<string> WrapSingleLine(string content, double width, TextStyle style)
    {
        List<string> wrappedLines = [];
        string remaining = content.Trim();

        if (string.IsNullOrEmpty(remaining) || width <= 0)
        {
            return wrappedLines;
        }

        while (remaining.Length > 0)
        {
            if (Measure(remaining, style) <= width)
            {
                wrappedLines.Add(remaining);
                break;
            }

            int lastWhitespaceBeforeOverflow = -1;
            int lastCharacterBeforeOverflow = -1;

            for (int index = 0; index < remaining.Length; index++)
            {
                string candidate = remaining[..(index + 1)];
                if (Measure(candidate, style) > width)
                {
                    break;
                }

                lastCharacterBeforeOverflow = index;
                if (char.IsWhiteSpace(remaining[index]))
                {
                    lastWhitespaceBeforeOverflow = index;
                }
            }

            if (lastCharacterBeforeOverflow < 0)
            {
                wrappedLines.Add(ClipContentToWidth(remaining, width, style));
                break;
            }

            int splitIndex = lastWhitespaceBeforeOverflow >= 0
                ? lastWhitespaceBeforeOverflow
                : lastCharacterBeforeOverflow + 1;
            string line = remaining[..splitIndex].Trim();

            if (line.Length == 0)
            {
                line = remaining[..(lastCharacterBeforeOverflow + 1)].Trim();
                splitIndex = lastCharacterBeforeOverflow + 1;
            }

            wrappedLines.Add(line);
            remaining = remaining[splitIndex..].TrimStart();
        }

        return wrappedLines;
    }

    private static double Measure(string text, TextStyle style)
    {
        return DeterministicTextMeasurer.Instance.MeasureText(text, style).Width;
    }
}
