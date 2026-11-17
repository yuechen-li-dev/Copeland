using System.Globalization;
using Machina.Core.Authoring;
using Machina.Core.Measurement;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Rows;

namespace Machina.Standard.Authoring;

/// <summary>How a single line of text behaves when it is wider than its box.</summary>
public enum TextOverflow
{
    /// <summary>Trim at a grapheme boundary and append "..." (the accessible text is unchanged by the caller).</summary>
    Ellipsis,
    /// <summary>Keep the text and clip it to the box.</summary>
    Clip,
    /// <summary>Treat overflow as a programming error and throw, for layouts that must never truncate.</summary>
    Throw,
}

/// <summary>Measured single-line text shared by every host, so fitting rules exist in one place.</summary>
public static class TextFit
{
    /// <summary>Trims <paramref name="text"/> to <paramref name="width"/> with "...", using the same measurer as layout.</summary>
    public static string Ellipsize(string text, double width, TextStyle style, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measurer);
        if (measurer.MeasureText(text, style).Width <= width) return text;
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        int low = 0, high = boundaries.Length - 1;
        // Binary search over grapheme boundaries for the longest prefix that fits with the ellipsis.
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (measurer.MeasureText(text[..boundaries[middle]].TrimEnd() + "...", style).Width <= width) low = middle;
            else high = middle - 1;
        }
        if (low > 0) return text[..boundaries[low]].TrimEnd() + "...";
        return measurer.MeasureText("...", style).Width <= width ? "..." : "";
    }
}

public static partial class StandardUI
{
    /// <summary>
    /// One line of text in an explicit box: never wider than <paramref name="width"/>, never shorter than the measured
    /// line (so glyphs are not clipped vertically), vertically aligned within <paramref name="height"/>.
    /// The returned node's Width/Height are the box it occupies; place it like any other node.
    /// </summary>
    public static RectNode TextLine(string text, double width, double height, ITextMeasurer measurer,
        TextSize size = TextSize.Sm, ColorToken? color = null, TextAlignX alignX = TextAlignX.Left,
        TextAlignY alignY = TextAlignY.Center, TextOverflow overflow = TextOverflow.Ellipsis, NodeId? id = null)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        if (!double.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(height) || height < 0) throw new ArgumentOutOfRangeException(nameof(height));
        var style = new TextStyle(color, size, alignX, alignY);
        var measured = measurer.MeasureText(text, style);
        string shown = text;
        if (measured.Width > width)
        {
            shown = overflow switch
            {
                TextOverflow.Ellipsis => TextFit.Ellipsize(text, width, style, measurer),
                TextOverflow.Clip => text,
                _ => throw new ArgumentException($"Text '{text}' is {measured.Width:0.##} wide; the box is {width:0.##}.", nameof(text)),
            };
        }
        double boxHeight = Math.Max(height, measured.Height);
        var line = UI.Text(shown, id: id?.Child("text"), color: color, size: size, alignX: alignX, alignY: alignY);
        return (RectNode)UI.Rect(line, id, width, boxHeight, style: new UiStyle(ClipToBounds: true));
    }
}
