using Machina.Core.Styling;
using Machina.Layout.Geometry;

namespace Machina.Dominatus.Rendering.Snapshot;

public static class RenderSnapshotTextWriter
{
    public static string FormatRect(Rect rect)
    {
        return $"x={rect.X:0.###} y={rect.Y:0.###} w={rect.Width:0.###} h={rect.Height:0.###}";
    }

    public static string FormatColor(ColorToken color)
    {
        return $"#{color.Rgba:X8}";
    }

    public static string EscapeText(string text)
    {
        return text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
