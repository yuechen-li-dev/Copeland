using Machina.Fonts.Generation;

namespace Machina.Fonts.ReferenceRendering;

public enum StaticTextHorizontalAlignment
{
    Left,
    Center,
    Right,
}

public enum StaticTextVerticalAlignment
{
    Top,
    Middle,
    Bottom,
    Baseline,
}

public enum StaticTextClipMode
{
    None,
    ClipToContentRect,
}

public enum StaticTextLineHeightMode
{
    FontMetrics,
    Explicit,
}

public sealed record StaticTextRenderRequest(
    string Text,
    FontFaceId FontFaceId,
    DirectOutlineRect Rect,
    double FontSize,
    DirectOutlineTextPadding Padding,
    StaticTextHorizontalAlignment HorizontalAlignment,
    StaticTextVerticalAlignment VerticalAlignment,
    StaticTextLineHeightMode LineHeightMode,
    double? ExplicitLineHeight,
    StaticTextClipMode ClipMode,
    bool UsePairAdjustments = true,
    int Supersample = 4,
    MachinaFontWeight Weight = MachinaFontWeight.Regular,
    MachinaFontSlant Slant = MachinaFontSlant.Upright,
    string? DebugLabel = null)
{
    public StaticTextRenderRequest Validate()
    {
        ArgumentNullException.ThrowIfNull(Text);
        ArgumentNullException.ThrowIfNull(Rect);
        ArgumentNullException.ThrowIfNull(Padding);

        if (Text.Length == 0)
        {
            throw new ArgumentException("Static text render requests must contain at least one character.", nameof(Text));
        }

        if (!double.IsFinite(FontSize) || FontSize <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(FontSize));
        }

        ValidateRect(Rect, nameof(Rect));
        ValidatePadding(Padding, nameof(Padding));

        if (LineHeightMode == StaticTextLineHeightMode.Explicit)
        {
            if (ExplicitLineHeight is null || !double.IsFinite(ExplicitLineHeight.Value) || ExplicitLineHeight.Value <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(ExplicitLineHeight));
            }
        }
        else if (ExplicitLineHeight is not null && (!double.IsFinite(ExplicitLineHeight.Value) || ExplicitLineHeight.Value <= 0d))
        {
            throw new ArgumentOutOfRangeException(nameof(ExplicitLineHeight));
        }

        if (Supersample is not 1 and not 2 and not 4)
        {
            throw new ArgumentOutOfRangeException(nameof(Supersample), "Supported supersample levels are 1, 2, and 4.");
        }

        return this;
    }

    private static void ValidateRect(DirectOutlineRect rect, string name)
    {
        if (!double.IsFinite(rect.X)
            || !double.IsFinite(rect.Y)
            || !double.IsFinite(rect.Width)
            || !double.IsFinite(rect.Height)
            || rect.Width < 0d
            || rect.Height < 0d)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private static void ValidatePadding(DirectOutlineTextPadding padding, string name)
    {
        if (!double.IsFinite(padding.Left)
            || !double.IsFinite(padding.Top)
            || !double.IsFinite(padding.Right)
            || !double.IsFinite(padding.Bottom)
            || padding.Left < 0d
            || padding.Top < 0d
            || padding.Right < 0d
            || padding.Bottom < 0d)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}

public sealed record StaticTextRenderResult(
    StaticTextRenderRequest Request,
    DirectOutlineTextBoxLayoutResult Layout,
    RgbaImage Image,
    DirectOutlineRect? InkBounds,
    bool WasClipped,
    IReadOnlyList<DirectOutlineGlyphRenderPlacement> Glyphs,
    IReadOnlyList<FontGenerationDiagnostic> Diagnostics);

public sealed class DirectOutlineStaticTextRenderBridge
{
    private readonly DirectOutlineTextBoxRenderer renderer;

    public DirectOutlineStaticTextRenderBridge(
        IGlyphOutlineSource outlineSource,
        IGlyphPairAdjustmentSource? pairAdjustmentSource = null,
        IDirectOutlineFontMetricsSource? fontMetricsSource = null)
    {
        renderer = new DirectOutlineTextBoxRenderer(outlineSource, pairAdjustmentSource, fontMetricsSource);
    }

    public DirectOutlineTextBoxOptions CreateLayoutOptions(StaticTextRenderRequest request)
    {
        StaticTextRenderRequest validated = ValidateRequest(request);

        return new DirectOutlineTextBoxOptions(
            validated.Text,
            validated.FontFaceId,
            validated.FontSize,
            new DirectOutlineRect(0d, 0d, validated.Rect.Width, validated.Rect.Height),
            validated.Padding,
            MapHorizontalAlignment(validated.HorizontalAlignment),
            MapVerticalAlignment(validated.VerticalAlignment),
            MapLineHeightMode(validated.LineHeightMode),
            validated.ExplicitLineHeight,
            MapClipMode(validated.ClipMode),
            validated.UsePairAdjustments,
            validated.Supersample,
            validated.Weight,
            validated.Slant);
    }

    public async ValueTask<StaticTextRenderResult> RenderAsync(
        StaticTextRenderRequest request,
        Rgba32 foreground,
        Rgba32 background,
        CancellationToken cancellationToken = default)
    {
        StaticTextRenderRequest validated = ValidateRequest(request);
        DirectOutlineTextBoxOptions layout = CreateLayoutOptions(validated);
        int outputWidth = Math.Max(1, (int)Math.Ceiling(validated.Rect.Width));
        int outputHeight = Math.Max(1, (int)Math.Ceiling(validated.Rect.Height));

        DirectOutlineTextBoxRenderResult result = await renderer.RenderAsync(
            new DirectOutlineTextBoxRenderOptions(
                layout,
                outputWidth,
                outputHeight,
                foreground,
                background),
            cancellationToken);

        return new StaticTextRenderResult(
            validated,
            result.Layout,
            result.Image,
            result.Layout.InkBounds,
            result.Layout.WasClipped,
            result.Layout.Glyphs,
            result.Diagnostics);
    }

    private static StaticTextRenderRequest ValidateRequest(StaticTextRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Validate();
    }

    private static DirectOutlineHorizontalAlignment MapHorizontalAlignment(StaticTextHorizontalAlignment alignment)
    {
        return alignment switch
        {
            StaticTextHorizontalAlignment.Left => DirectOutlineHorizontalAlignment.Left,
            StaticTextHorizontalAlignment.Center => DirectOutlineHorizontalAlignment.Center,
            StaticTextHorizontalAlignment.Right => DirectOutlineHorizontalAlignment.Right,
            _ => throw new ArgumentOutOfRangeException(nameof(alignment)),
        };
    }

    private static DirectOutlineVerticalAlignment MapVerticalAlignment(StaticTextVerticalAlignment alignment)
    {
        return alignment switch
        {
            StaticTextVerticalAlignment.Top => DirectOutlineVerticalAlignment.Top,
            StaticTextVerticalAlignment.Middle => DirectOutlineVerticalAlignment.Middle,
            StaticTextVerticalAlignment.Bottom => DirectOutlineVerticalAlignment.Bottom,
            StaticTextVerticalAlignment.Baseline => DirectOutlineVerticalAlignment.Baseline,
            _ => throw new ArgumentOutOfRangeException(nameof(alignment)),
        };
    }

    private static DirectOutlineLineHeightMode MapLineHeightMode(StaticTextLineHeightMode mode)
    {
        return mode switch
        {
            StaticTextLineHeightMode.FontMetrics => DirectOutlineLineHeightMode.FontMetrics,
            StaticTextLineHeightMode.Explicit => DirectOutlineLineHeightMode.Explicit,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    private static DirectOutlineTextClipMode MapClipMode(StaticTextClipMode mode)
    {
        return mode switch
        {
            StaticTextClipMode.None => DirectOutlineTextClipMode.None,
            StaticTextClipMode.ClipToContentRect => DirectOutlineTextClipMode.ClipToContentRect,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }
}
