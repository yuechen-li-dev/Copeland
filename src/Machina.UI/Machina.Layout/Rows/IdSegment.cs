namespace Machina.Layout.Rows;

/// <summary>Canonical, collision-free segments for composed UI identifiers.</summary>
public static class IdSegment
{
    public static string Encode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        // EscapeDataString preserves dots, which are structural in NodeId paths.
        return Uri.EscapeDataString(value).Replace(".", "%2E", StringComparison.Ordinal);
    }
}
