using System.Security.Cryptography;
using System.Text;
using Aurelian.Machina;
using Machina.Core.Styling;
using Machina.Fonts;
using Machina.Fonts.Generation;
using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Presentation;
using FontRgba = Machina.Fonts.ReferenceRendering.Rgba32;

namespace TinyFarm.Native;

internal sealed class SupperNativeUiFont
{
    private const int MediumSize = 16;
    private const int HeadingSize = 24;
    private const string SupportedCharacters =
        " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    private readonly IReadOnlyDictionary<int, SupperFontAtlas> atlases;
    private readonly Dictionary<SupperTextGeometryKey, PositionedTextOperation> textGeometry = [];

    private SupperNativeUiFont(IReadOnlyDictionary<int, SupperFontAtlas> atlases)
    {
        this.atlases = atlases;
    }

    public IReadOnlyCollection<AurelianMsdfAtlasResource> Resources =>
        atlases.Values.Select(static atlas => atlas.Resource).ToArray();

    public int CachedTextRunCount => textGeometry.Count;

    public static SupperNativeUiFont Create(string fontPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontPath);
        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException("TinyFarm's native UI font is missing.", fontPath);
        }

        var atlases = new Dictionary<int, SupperFontAtlas>
        {
            [MediumSize] = BuildAtlasAsync(fontPath, MediumSize).GetAwaiter().GetResult(),
            [HeadingSize] = BuildAtlasAsync(fontPath, HeadingSize).GetAwaiter().GetResult(),
        };
        return new SupperNativeUiFont(atlases);
    }

    public PositionedTextOperation Qualify(PositionedTextOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        int size = ResolveSize(operation.Style.Size);
        var key = new SupperTextGeometryKey(
            operation.SourceId,
            operation.Text,
            operation.Rect,
            operation.Style,
            operation.Color,
            size);
        if (textGeometry.TryGetValue(key, out PositionedTextOperation? cached))
        {
            return cached;
        }

        SupperFontAtlas atlas = atlases[size];
        ValidateCharacters(operation);
        DistanceFieldTextLayoutResult initial = Layout(atlas, operation.Text, 0, 0);
        double x = operation.Style.AlignX switch
        {
            TextAlignX.Center => (operation.Rect.Width - initial.Width) / 2,
            TextAlignX.Right => operation.Rect.Width - initial.Width,
            _ => 0,
        };
        double baseline = operation.Style.AlignY switch
        {
            TextAlignY.Center => ((operation.Rect.Height - size) / 2) + (size * 0.8),
            TextAlignY.Bottom => operation.Rect.Height - (size * 0.2),
            _ => size * 0.8,
        };
        DistanceFieldTextLayoutResult positioned = Layout(atlas, operation.Text, x, baseline);
        var qualified = new PositionedTextOperation(
            operation.SourceId,
            operation.Rect,
            operation.Text,
            operation.Style,
            operation.Color,
            new MachinaTextPresentationPrimitive(
                positioned.GlyphRun,
                atlas.Resource.Identity,
                MachinaTextRenderingMode.Msdf));
        textGeometry.Add(key, qualified);
        return qualified;
    }

    public AurelianMsdfAtlasResource ResourceFor(PositionedTextOperation operation)
    {
        MachinaFontAtlasId identity = operation.Primitive?.AtlasIdentity
            ?? throw new InvalidOperationException($"Text operation '{operation.SourceId}' is not native-qualified.");
        return atlases.Values.Single(atlas => atlas.Resource.Identity == identity).Resource;
    }

    private static int ResolveSize(TextSize size)
    {
        return size switch
        {
            TextSize.H1 => HeadingSize,
            TextSize.Md => MediumSize,
            TextSize.Sm => MediumSize,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unsupported TinyFarm UI text size."),
        };
    }

    private static void ValidateCharacters(PositionedTextOperation operation)
    {
        foreach (Rune rune in operation.Text.EnumerateRunes())
        {
            if (!Rune.IsWhiteSpace(rune) && !SupportedCharacters.Contains(rune.ToString(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"TinyFarm native UI text '{operation.SourceId}' contains unsupported glyph U+{rune.Value:X4}.");
            }
        }
    }

    private static DistanceFieldTextLayoutResult Layout(
        SupperFontAtlas atlas,
        string text,
        double x,
        double baseline)
    {
        DistanceFieldTextRun run = DistanceFieldTextRun.Create(
            text,
            atlas.Face,
            atlas.Size,
            MachinaFontWeight.Regular,
            MachinaFontSlant.Upright);
        return DistanceFieldTextLayout.Layout(
            run,
            atlas.Metrics,
            new DistanceFieldTextRenderOptions(
                1280,
                720,
                atlas.Face,
                atlas.Size,
                MachinaFontWeight.Regular,
                MachinaFontSlant.Upright,
                DistanceFieldKind.Msdf,
                NextPowerOfTwo(Math.Max(32, atlas.Size)),
                NextPowerOfTwo(Math.Max(32, atlas.Size)),
                4,
                FontRgba.White,
                new FontRgba(0, 0, 0, 0),
                x,
                baseline,
                PageWidth: 1024,
                PageHeight: 1024,
                PagePadding: 2));
    }

    private static async Task<SupperFontAtlas> BuildAtlasAsync(string fontPath, int size)
    {
        FontFaceId face = new("SpaceMono-Regular");
        var source = new TypographyGlyphOutlineSource(
            new Dictionary<FontFaceId, TypographyFontFaceSource>
            {
                [face] = new(face, fontPath, 0),
            });
        var pipeline = new GlyphGenerationPipeline(source, new MsdfSharpDistanceFieldGenerator());
        int dimension = NextPowerOfTwo(Math.Max(32, size));
        var settings = new MsdfGenerationSettings(DistanceFieldKind.Msdf, dimension, dimension, 4, 1, "simple", 2);
        var outlineOptions = new GlyphOutlineLoadOptions(size, 0, GlyphHintingMode.None, normalizeToEm: true);
        GlyphKey[] keys = DistanceFieldTextRun.Create(
                SupportedCharacters,
                face,
                size,
                MachinaFontWeight.Regular,
                MachinaFontSlant.Upright)
            .GlyphKeys
            .Distinct()
            .OrderBy(static key => key.Codepoint)
            .ToArray();
        List<GeneratedGlyphDistanceField> fields = [];
        Dictionary<GlyphKey, GlyphMetrics> metrics = [];
        foreach (GlyphKey key in keys)
        {
            GlyphGenerationResult result = await pipeline.GenerateAsync(key, outlineOptions, settings);
            if (result.Metrics is not null)
            {
                metrics[key] = result.Metrics;
            }
            if (!Rune.IsWhiteSpace(new Rune(key.Codepoint)))
            {
                if (!result.Success || result.DistanceField is null)
                {
                    throw new InvalidOperationException($"MSDF generation failed for U+{key.Codepoint:X4}.");
                }
                fields.Add(result.DistanceField);
            }
        }

        GeneratedFieldAtlasPackResult packed = new GeneratedFieldAtlasPacker().Pack(
            fields,
            new GeneratedFieldAtlasPackOptions(1024, 1024, 2, $"tinyfarm-ui-{size}"));
        if (!packed.Success)
        {
            throw new InvalidOperationException(
                "TinyFarm MSDF atlas packing failed: " + string.Join("; ", packed.Diagnostics.Select(item => item.Message)));
        }

        Dictionary<int, byte[]> pages = packed.Pages.ToDictionary(static page => page.Index, EncodeRgba8);
        byte[] identityBytes = pages.OrderBy(static item => item.Key).SelectMany(static item => item.Value).ToArray();
        string contentHash = Convert.ToHexString(SHA256.HashData(identityBytes)).ToLowerInvariant();
        var identity = new MachinaFontAtlasId($"tinyfarm-space-mono-{size}-sha256-{contentHash}");
        var resource = new AurelianMsdfAtlasResource(
            identity,
            packed.Snapshot,
            pages,
            AurelianMsdfAtlasRowOrder.TopToBottom);
        return new SupperFontAtlas(face, size, metrics, resource);
    }

    private static byte[] EncodeRgba8(GeneratedFieldAtlasPage page)
    {
        byte[] result = new byte[checked(page.Width * page.Height * 4)];
        for (int pixel = 0; pixel < page.Width * page.Height; pixel++)
        {
            int source = pixel * 3;
            int target = pixel * 4;
            result[target] = ToByte(page.Data[source]);
            result[target + 1] = ToByte(page.Data[source + 1]);
            result[target + 2] = ToByte(page.Data[source + 2]);
            result[target + 3] = 255;
        }
        return result;
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
        {
            result *= 2;
        }
        return result;
    }

    private sealed record SupperFontAtlas(
        FontFaceId Face,
        int Size,
        IReadOnlyDictionary<GlyphKey, GlyphMetrics> Metrics,
        AurelianMsdfAtlasResource Resource);

    private readonly record struct SupperTextGeometryKey(
        string SourceId,
        string Text,
        Machina.Layout.Geometry.Rect Rect,
        TextStyle Style,
        ColorToken Color,
        int Size);
}
