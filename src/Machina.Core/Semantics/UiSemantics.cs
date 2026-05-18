namespace Machina.Core.Semantics;

public sealed record UiSemantics(
    UiRole Role,
    string? Label = null,
    bool Disabled = false,
    bool Focusable = false);
