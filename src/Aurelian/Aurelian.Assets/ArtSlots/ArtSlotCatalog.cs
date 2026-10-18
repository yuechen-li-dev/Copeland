using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;
using Svg.Skia;

namespace Aurelian.Assets.ArtSlots;

/// <summary>
/// One piece of 2D art a game needs. Every slot ships with a hand-authored SVG placeholder that fixes the composition:
/// silhouette, proportions, where the feet or pivot sit, and the palette regions. Final raster art (for example a
/// diffusion image painted "within the lines" of the placeholder) replaces it by adding <see cref="Final"/> and its
/// hash; no code changes. See docs/Aurelian/art-slots.md.
/// </summary>
public sealed record ArtSlotDefinition
{
    /// <summary>Stable id, for example "riverside.reeds".</summary>
    public required string Id { get; init; }

    /// <summary>SVG placeholder, relative to the manifest. Its viewBox must be 0 0 Width Height.</summary>
    public required string Placeholder { get; init; }

    /// <summary>Raster art that replaces the placeholder (PNG), relative to the manifest. Absent until painted.</summary>
    public string? Final { get; init; }

    /// <summary>SHA-256 of the final file, so an accidental overwrite fails loudly instead of shipping.</summary>
    public string? FinalSha256 { get; init; }

    /// <summary>Size of the placeholder canvas in pixels. Final art must match its aspect ratio (any resolution).</summary>
    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>Animation or pose grid: the canvas is split into Columns x Rows equal cells (row-major).</summary>
    public int Columns { get; init; } = 1;

    public int Rows { get; init; } = 1;

    /// <summary>World height of the tallest cell's opaque content, in metres, when the art stands in a world.</summary>
    public double? WorldHeight { get; init; }

    /// <summary>How the game uses it: scale, anchor, layering, frames. Written for whoever paints the final.</summary>
    public required string Usage { get; init; }

    /// <summary>What it should look like: subject, style, palette, mood. The prompt seed for the final.</summary>
    public required string Brief { get; init; }
}

/// <summary>A slot realized as straight-alpha RGBA8 pixels, row-major, top row first.</summary>
public sealed record ArtSlotImage(string Id, int Width, int Height, byte[] Rgba, bool IsPlaceholder, string ContentHash);

/// <summary>A game's art slots (a JSON manifest beside the art), realized from final art or placeholders.</summary>
public sealed class ArtSlotCatalog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    private readonly Dictionary<string, ArtSlotDefinition> slots;

    private ArtSlotCatalog(string directory, IReadOnlyList<ArtSlotDefinition> slots)
    {
        Directory = directory;
        Slots = slots;
        this.slots = slots.ToDictionary(slot => slot.Id, StringComparer.Ordinal);
    }

    /// <summary>The directory the manifest's paths are relative to.</summary>
    public string Directory { get; }

    public IReadOnlyList<ArtSlotDefinition> Slots { get; }

    /// <summary>Slots still showing their placeholder.</summary>
    public IReadOnlyList<ArtSlotDefinition> Pending => Slots.Where(slot => slot.Final is null).ToArray();

    public ArtSlotDefinition this[string id] => slots.TryGetValue(id, out ArtSlotDefinition? slot)
        ? slot
        : throw new KeyNotFoundException($"No art slot '{id}' in {Directory}.");

    public static ArtSlotCatalog Load(string manifestPath)
    {
        string fullPath = Path.GetFullPath(manifestPath);
        ArtSlotManifest manifest = JsonSerializer.Deserialize<ArtSlotManifest>(File.ReadAllText(fullPath), Json)
            ?? throw new InvalidDataException($"Empty art slot manifest: {manifestPath}");
        if (manifest.SchemaVersion != 1 || manifest.Slots is null)
        {
            throw new InvalidDataException($"Art slot manifest {manifestPath} must be schemaVersion 1 with a slots array.");
        }
        var catalog = new ArtSlotCatalog(Path.GetDirectoryName(fullPath)!, manifest.Slots);
        catalog.Validate();
        return catalog;
    }

    /// <summary>Check ids, sizes, files and hashes without rasterizing.</summary>
    public void Validate()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ArtSlotDefinition slot in Slots)
        {
            if (string.IsNullOrWhiteSpace(slot.Id) || !ids.Add(slot.Id))
            {
                throw new InvalidDataException($"Art slot ids must be unique and non-empty ('{slot.Id}').");
            }
            if (slot.Width is < 1 or > 4096 || slot.Height is < 1 or > 4096)
            {
                throw new InvalidDataException($"Art slot '{slot.Id}' size must be 1..4096 pixels.");
            }
            if (slot.Columns is < 1 or > 64 || slot.Rows is < 1 or > 64 || slot.Width % slot.Columns != 0 || slot.Height % slot.Rows != 0)
            {
                throw new InvalidDataException($"Art slot '{slot.Id}' grid must divide its canvas into whole cells.");
            }
            if (slot.WorldHeight is { } metres && (!double.IsFinite(metres) || metres <= 0 || metres > 100))
            {
                throw new InvalidDataException($"Art slot '{slot.Id}' worldHeight must be a positive number of metres.");
            }
            if (string.IsNullOrWhiteSpace(slot.Usage) || string.IsNullOrWhiteSpace(slot.Brief))
            {
                throw new InvalidDataException($"Art slot '{slot.Id}' needs a usage and a brief.");
            }
            if (!File.Exists(PathOf(slot.Placeholder)))
            {
                throw new FileNotFoundException($"Art slot '{slot.Id}' placeholder is missing.", PathOf(slot.Placeholder));
            }
            if (slot.Final is null)
            {
                continue;
            }
            string final = PathOf(slot.Final);
            if (!File.Exists(final))
            {
                throw new FileNotFoundException($"Art slot '{slot.Id}' final art is missing.", final);
            }
            if (slot.FinalSha256 is null || !string.Equals(Hash(File.ReadAllBytes(final)), slot.FinalSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Art slot '{slot.Id}' final art does not match its finalSha256.");
            }
        }
    }

    /// <summary>The final art when present, otherwise the placeholder rasterized at the slot's size (times <paramref name="scale"/>).</summary>
    public ArtSlotImage Realize(string id, float scale = 1)
    {
        ArtSlotDefinition slot = this[id];
        if (slot.Final is not null)
        {
            return Decode(slot, PathOf(slot.Final));
        }
        return Rasterize(slot, PathOf(slot.Placeholder), scale);
    }

    /// <summary>The placeholder only, even when final art exists (contact sheets, comparisons).</summary>
    public ArtSlotImage RealizePlaceholder(string id, float scale = 1)
    {
        ArtSlotDefinition slot = this[id];
        return Rasterize(slot, PathOf(slot.Placeholder), scale);
    }

    /// <summary>A Markdown worklist of pending slots, for whoever paints the finals.</summary>
    public string PendingReport()
    {
        var writer = new System.Text.StringBuilder();
        writer.Append("# Pending art slots\n\n");
        foreach (ArtSlotDefinition slot in Pending)
        {
            writer.Append("## ").Append(slot.Id).Append("\n\n");
            writer.Append("- Placeholder: `").Append(slot.Placeholder).Append("` (").Append(slot.Width).Append('x')
                .Append(slot.Height).Append(")\n");
            if (slot.Columns > 1 || slot.Rows > 1)
            {
                writer.Append("- Grid: ").Append(slot.Columns).Append(" columns x ").Append(slot.Rows).Append(" rows\n");
            }
            writer.Append("- Usage: ").Append(slot.Usage).Append('\n');
            writer.Append("- Brief: ").Append(slot.Brief).Append("\n\n");
        }
        return writer.ToString();
    }

    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private string PathOf(string relative) => Path.GetFullPath(Path.Combine(Directory, relative));

    private static ArtSlotImage Rasterize(ArtSlotDefinition slot, string path, float scale)
    {
        if (!float.IsFinite(scale) || scale <= 0 || scale > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }
        using var svg = new SKSvg();
        using SKPicture picture = svg.Load(path)
            ?? throw new InvalidDataException($"Art slot '{slot.Id}' placeholder is not a readable SVG.");
        SKRect bounds = picture.CullRect;
        if (Math.Abs(bounds.Width - slot.Width) > .5f || Math.Abs(bounds.Height - slot.Height) > .5f)
        {
            throw new InvalidDataException(
                $"Art slot '{slot.Id}' placeholder is {bounds.Width}x{bounds.Height}; its viewBox must be 0 0 {slot.Width} {slot.Height}.");
        }
        int width = (int)MathF.Round(slot.Width * scale);
        int height = (int)MathF.Round(slot.Height * scale);
        // Skia draws premultiplied; the engine's sprite path takes straight alpha.
        using var drawn = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(drawn))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(width / bounds.Width, height / bounds.Height);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
        }
        byte[] rgba = Straight(drawn);
        return new ArtSlotImage(slot.Id, width, height, rgba, IsPlaceholder: true, Hash(rgba));
    }

    private static byte[] Straight(SKBitmap premultiplied)
    {
        var info = new SKImageInfo(premultiplied.Width, premultiplied.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var straight = new SKBitmap(info);
        if (!premultiplied.PeekPixels().ReadPixels(info, straight.GetPixels(), info.RowBytes, 0, 0))
        {
            throw new InvalidDataException("Could not convert art to straight alpha.");
        }
        return straight.Bytes;
    }

    private static ArtSlotImage Decode(ArtSlotDefinition slot, string path)
    {
        using SKCodec codec = SKCodec.Create(path)
            ?? throw new InvalidDataException($"Art slot '{slot.Id}' final art is not a readable image.");
        double expected = (double)slot.Width / slot.Height;
        double actual = (double)codec.Info.Width / codec.Info.Height;
        if (Math.Abs(actual - expected) > expected * .01)
        {
            throw new InvalidDataException(
                $"Art slot '{slot.Id}' final art is {codec.Info.Width}x{codec.Info.Height}; it must keep the placeholder's {slot.Width}:{slot.Height} aspect ratio.");
        }
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            throw new InvalidDataException($"Art slot '{slot.Id}' final art could not be decoded: {result}.");
        }
        byte[] rgba = bitmap.Bytes;
        return new ArtSlotImage(slot.Id, info.Width, info.Height, rgba, IsPlaceholder: false, Hash(rgba));
    }

    private sealed record ArtSlotManifest(int SchemaVersion, IReadOnlyList<ArtSlotDefinition> Slots);
}
