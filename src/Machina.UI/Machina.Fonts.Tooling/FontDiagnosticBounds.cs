using Machina.Fonts.ReferenceRendering;

namespace Machina.Fonts.Tooling;

public sealed record FontDiagnosticBounds(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => (Right - Left) + 1;

    public int Height => (Bottom - Top) + 1;
}

public sealed record FontDiagnosticBoundsSet
{
    public FontDiagnosticBounds? BrowserBounds { get; init; }

    public FontDiagnosticBounds? MachinaBounds { get; init; }

    public FontDiagnosticBounds? DirectOutlineBounds { get; init; }

    public FontDiagnosticBounds? MsdfBounds { get; init; }

    public IReadOnlyList<FontDiagnosticBounds> WireframeBounds { get; init; } = Array.Empty<FontDiagnosticBounds>();
}

public sealed record FontDiagnosticBoundsOverlayOptions
{
    public bool ShowBounds { get; init; } = true;

    public bool ShowWireframes { get; init; } = true;

    public Rgba32 BrowserBoundsColor { get; init; } = new(0, 220, 255, 255);

    public Rgba32 MachinaBoundsColor { get; init; } = new(255, 148, 32, 255);

    public Rgba32 DirectOutlineBoundsColor { get; init; } = new(96, 255, 96, 255);

    public Rgba32 MsdfBoundsColor { get; init; } = new(255, 148, 32, 255);

    public Rgba32 WireframeColor { get; init; } = new(255, 204, 96, 255);
}
