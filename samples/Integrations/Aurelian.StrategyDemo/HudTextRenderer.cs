using System.Runtime.InteropServices;
using Machina.Core.Styling;
using Machina.Core.Measurement;
using Machina.Fonts;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Presentation;
using SkiaSharp;

namespace Aurelian.StrategyDemo;

internal sealed class HudTextRenderer : IDisposable, ITextMeasurer
{
    private readonly DirectOutlineStaticTextRenderBridge bridge;
    private readonly DirectOutlineTextBoxLayouter layouter;
    private readonly Dictionary<string, (string Key, SKBitmap Bitmap)> cache = [];
    private static readonly FontFaceId Serif = new("strategy-serif");
    private static readonly FontFaceId Mono = new("strategy-mono");

    public HudTextRenderer(string directory)
    {
        var source = new TypographyGlyphOutlineSource(new Dictionary<FontFaceId, TypographyFontFaceSource>
        {
            [Serif] = new(Serif, Path.Combine(directory, "CrimsonText-Regular.ttf")),
            [Mono] = new(Mono, Path.Combine(directory, "SpaceMono-Regular.ttf")),
        });
        bridge = new(source);
        layouter = new(source);
    }

    public IntrinsicSize MeasureText(string value, TextStyle style)
    {
        bool small = style.Size == TextSize.Sm;
        double size = FontSize(style.Size);
        var request = new StaticTextRenderRequest(value, small ? Mono : Serif, new(0, 0, 4000, 100), size,
            DirectOutlineTextPadding.Zero, StaticTextHorizontalAlignment.Left, StaticTextVerticalAlignment.Top,
            StaticTextLineHeightMode.FontMetrics, null, StaticTextClipMode.None, Supersample: 2);
        DirectOutlineTextBoxLayoutResult layout = layouter.LayoutAsync(bridge.CreateLayoutOptions(request)).AsTask().GetAwaiter().GetResult();
        double width = layout.InkBounds?.Right ?? 0;
        double height = Math.Max(layout.InkBounds?.Bottom ?? size, size * 1.25);
        return new(Math.Ceiling(width + 2), Math.Ceiling(height + 2));
    }

    private static double FontSize(TextSize size) => size switch
    {
        TextSize.Sm => 12,
        TextSize.H1 => 30,
        _ => 22,
    };

    public void Draw(SKCanvas canvas, IEnumerable<PositionedTextOperation> operations)
    {
        foreach (PositionedTextOperation operation in operations)
        {
            bool small = operation.Style.Size == TextSize.Sm;
            double size = FontSize(operation.Style.Size);
            string key = $"{operation.Text}|{operation.Color.Rgba}|{operation.Rect}|{size}";
            if (!cache.TryGetValue(operation.SourceId, out var cached) || cached.Key != key)
            {
                cached.Bitmap?.Dispose();
                uint color = operation.Color.Rgba;
                var request = new StaticTextRenderRequest(operation.Text, small ? Mono : Serif,
                    new(0, 0, operation.Rect.Width, operation.Rect.Height), size, DirectOutlineTextPadding.Zero,
                    StaticTextHorizontalAlignment.Left, StaticTextVerticalAlignment.Top, StaticTextLineHeightMode.FontMetrics,
                    null, StaticTextClipMode.ClipToContentRect, Supersample: 2);
                StaticTextRenderResult result = bridge.RenderAsync(request,
                    new((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color), Rgba32.Transparent)
                    .AsTask().GetAwaiter().GetResult();
                if (result.Diagnostics.Any(d => d.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException("HUD font could not be realized.");
                }
                var bitmap = new SKBitmap(new SKImageInfo(result.Image.Width, result.Image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
                byte[] bytes = new byte[result.Image.Pixels.Length * 4];
                for (int index = 0; index < result.Image.Pixels.Length; index++)
                {
                    Rgba32 pixel = result.Image.Pixels[index];
                    bytes[index * 4] = pixel.R;
                    bytes[index * 4 + 1] = pixel.G;
                    bytes[index * 4 + 2] = pixel.B;
                    bytes[index * 4 + 3] = pixel.A;
                }
                Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
                cached = (key, bitmap);
                cache[operation.SourceId] = cached;
            }
            canvas.DrawBitmap(cached.Bitmap, (float)operation.Rect.X, (float)operation.Rect.Y);
        }
    }

    public void Dispose()
    {
        foreach (var cached in cache.Values)
        {
            cached.Bitmap.Dispose();
        }
    }
}
