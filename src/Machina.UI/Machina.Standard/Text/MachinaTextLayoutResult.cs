namespace Machina.Standard.Text;

public readonly record struct MachinaTextBox(
    double X,
    double Y,
    double Width,
    double Height);

public readonly record struct MachinaTextSize(
    double Width,
    double Height);

public sealed record MachinaTextRunStyle(
    MachinaTextVariant Variant,
    bool Strong,
    bool Emphasis,
    bool Code,
    string? LinkHref);

public sealed record MachinaTextRunBox(
    MachinaInline Source,
    string Text,
    MachinaTextBox Bounds,
    MachinaTextRunStyle Style);

public sealed record MachinaTextLineBox(
    int BlockIndex,
    int LineIndex,
    MachinaTextBox Bounds,
    IReadOnlyList<MachinaTextRunBox> Runs);

public enum MachinaTextLayoutDiagnosticCode
{
    BoxTooSmall,
    ContentOverflow,
    UnsupportedInline,
    UnsupportedOverflow,
}

public sealed record MachinaTextLayoutDiagnostic(
    MachinaTextLayoutDiagnosticCode Code,
    string Message);

public sealed record MachinaTextLayoutResult(
    MachinaTextBox Box,
    MachinaTextBox ContentBounds,
    IReadOnlyList<MachinaTextLineBox> Lines,
    IReadOnlyList<MachinaTextRunBox> Runs,
    bool HasOverflow,
    IReadOnlyList<MachinaTextLayoutDiagnostic> Diagnostics,
    IReadOnlyList<MachinaTextDiagnostic> ParseDiagnostics);
