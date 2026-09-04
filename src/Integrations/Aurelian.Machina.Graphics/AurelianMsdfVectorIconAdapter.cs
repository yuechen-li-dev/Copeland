using Aurelian.Graphics.Vulkan.Native2D;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;
using Machina.VectorAssets;

namespace Aurelian.Machina;

public sealed record AurelianMsdfVectorAtlasResource
{
    public AurelianMsdfVectorAtlasResource(VectorIconAtlas atlas, byte[] rgbaPixels)
    {
        Atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
        RgbaPixels = rgbaPixels ?? throw new ArgumentNullException(nameof(rgbaPixels));
        if (atlas.RowOrder != VectorAtlasRowOrder.TopToBottom)
        {
            throw new ArgumentOutOfRangeException(nameof(atlas), "Vector atlas row orientation must be explicitly top-to-bottom.");
        }
        if (rgbaPixels.Length != checked(atlas.Width * atlas.Height * 4))
        {
            throw new ArgumentException("Vector atlas RGBA payload does not match its dimensions.", nameof(rgbaPixels));
        }
    }

    public VectorIconAtlas Atlas { get; }

    public byte[] RgbaPixels { get; }
}

public sealed class AurelianMsdfVectorAtlasCache : IDisposable
{
    private readonly VulkanOrderedQuadRenderer renderer;
    private readonly Dictionary<string, CacheEntry> entries = [];
    private bool disposed;

    public AurelianMsdfVectorAtlasCache(VulkanOrderedQuadRenderer renderer)
    {
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        if (renderer.PipelineKind != Native2DPipelineKind.MsdfText)
        {
            throw new ArgumentException("Vector fields use the qualified native MSDF pipeline.", nameof(renderer));
        }
    }

    public int UploadCount { get; private set; }

    public Native2DTextureHandle Resolve(AurelianMsdfVectorAtlasResource resource)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(resource);
        if (entries.TryGetValue(resource.Atlas.Identity, out CacheEntry? cached))
        {
            if (!string.Equals(cached.AtlasHash, resource.Atlas.AtlasHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A vector atlas identity was reused with different content.");
            }
            return cached.Texture;
        }

        byte[] nativeRows = FlipRows(resource.RgbaPixels, resource.Atlas.Width, resource.Atlas.Height);
        Native2DTextureHandle texture = renderer.CreateTexture(
            (uint)resource.Atlas.Width,
            (uint)resource.Atlas.Height,
            nativeRows);
        entries.Add(resource.Atlas.Identity, new CacheEntry(resource.Atlas.AtlasHash, texture));
        UploadCount++;
        return texture;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        foreach (CacheEntry entry in entries.Values)
        {
            renderer.DisposeTexture(entry.Texture);
        }
        entries.Clear();
        disposed = true;
    }

    private static byte[] FlipRows(byte[] source, int width, int height)
    {
        int rowBytes = checked(width * 4);
        byte[] result = new byte[source.Length];
        for (int sourceRow = 0; sourceRow < height; sourceRow++)
        {
            int destinationRow = height - sourceRow - 1;
            source.AsSpan(sourceRow * rowBytes, rowBytes).CopyTo(result.AsSpan(destinationRow * rowBytes, rowBytes));
        }
        return result;
    }

    private sealed record CacheEntry(string AtlasHash, Native2DTextureHandle Texture);
}

public static class AurelianMsdfVectorIconAdapter
{
    public static NativeMsdfQuadSubmission Adapt(
        MachinaVectorIconPresentationPrimitive primitive,
        AurelianMsdfVectorAtlasResource resource,
        AurelianMsdfVectorAtlasCache cache)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(cache);
        Native2DTextureHandle texture = cache.Resolve(resource);
        return Adapt(primitive, resource.Atlas, texture);
    }

    public static NativeMsdfQuadSubmission Adapt(
        MachinaVectorIconPresentationPrimitive primitive,
        VectorIconAtlas atlas,
        Native2DTextureHandle texture)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        ArgumentNullException.ThrowIfNull(atlas);
        if (texture.Value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(texture), "A native vector atlas texture must be valid.");
        }
        if (!atlas.Entries.TryGetValue(primitive.Icon, out VectorIconAtlasEntry? entry))
        {
            throw new InvalidOperationException($"Vector atlas does not contain icon '{primitive.Icon}'.");
        }
        ValidateEntry(entry, atlas);

        Rect target = Contain(primitive.DestinationRect, entry.PlaneBounds.Width / entry.PlaneBounds.Height);
        double scale = target.Width / entry.PlaneBounds.Width;
        double fieldX = target.X + ((entry.FieldBounds.MinX - entry.PlaneBounds.MinX) * scale);
        double fieldY = target.Y + ((entry.PlaneBounds.MaxY - entry.FieldBounds.MaxY) * scale);
        NativeMsdfQuadSubmission submission = new(
            new Native2DRect(
                Checked(fieldX, "field X"),
                Checked(fieldY, "field Y"),
                CheckedPositive(entry.FieldBounds.Width * scale, "field width"),
                CheckedPositive(entry.FieldBounds.Height * scale, "field height")),
            AurelianMsdfAtlasUpload.NormalizeUv(new Native2DUvRect(
                Checked(entry.U0, "u0"),
                Checked(entry.V0, "v0"),
                Checked(entry.U1, "u1"),
                Checked(entry.V1, "v1"))),
            texture,
            ToTint(primitive.Tint),
            NativeMsdfParameters.Create(
                CheckedPositive(entry.PixelRange, "pixel range"),
                CheckedPositive(scale / entry.ProjectionScale, "field scale")));

        if (primitive.ClipRect is Rect clip && !TryClip(submission, clip, out submission))
        {
            throw new InvalidOperationException("The vector icon is fully outside its explicit clip rectangle.");
        }
        return submission;
    }

    private static Rect Contain(Rect destination, double aspect)
    {
        double width = destination.Width;
        double height = width / aspect;
        if (height > destination.Height)
        {
            height = destination.Height;
            width = height * aspect;
        }
        return new Rect(
            destination.X + ((destination.Width - width) / 2),
            destination.Y + ((destination.Height - height) / 2),
            width,
            height);
    }

    private static bool TryClip(NativeMsdfQuadSubmission source, Rect clip, out NativeMsdfQuadSubmission result)
    {
        float left = Math.Max(source.Destination.X, Checked(clip.X, "clip X"));
        float top = Math.Max(source.Destination.Y, Checked(clip.Y, "clip Y"));
        float right = Math.Min(source.Destination.X + source.Destination.Width, Checked(clip.X + clip.Width, "clip right"));
        float bottom = Math.Min(source.Destination.Y + source.Destination.Height, Checked(clip.Y + clip.Height, "clip bottom"));
        if (right <= left || bottom <= top)
        {
            result = default;
            return false;
        }
        float uPerPixel = (source.Uv.U1 - source.Uv.U0) / source.Destination.Width;
        float vPerPixel = (source.Uv.V1 - source.Uv.V0) / source.Destination.Height;
        result = source with
        {
            Destination = new Native2DRect(left, top, right - left, bottom - top),
            Uv = new Native2DUvRect(
                source.Uv.U0 + ((left - source.Destination.X) * uPerPixel),
                source.Uv.V0 + ((top - source.Destination.Y) * vPerPixel),
                source.Uv.U1 - (((source.Destination.X + source.Destination.Width) - right) * uPerPixel),
                source.Uv.V1 - (((source.Destination.Y + source.Destination.Height) - bottom) * vPerPixel)),
        };
        return true;
    }

    private static void ValidateEntry(VectorIconAtlasEntry entry, VectorIconAtlas atlas)
    {
        if (entry.X < 0 || entry.Y < 0 || entry.Width <= 0 || entry.Height <= 0
            || entry.X + entry.Width > atlas.Width || entry.Y + entry.Height > atlas.Height)
        {
            throw new InvalidOperationException($"Vector atlas entry '{entry.Identity}' lies outside the atlas.");
        }
        if (entry.U0 < 0 || entry.V0 < 0 || entry.U1 > 1 || entry.V1 > 1
            || entry.U0 >= entry.U1 || entry.V0 >= entry.V1)
        {
            throw new InvalidOperationException($"Vector atlas entry '{entry.Identity}' has invalid UV bounds.");
        }
    }

    private static Native2DTint ToTint(ColorToken color)
    {
        const float scale = 1f / 255f;
        return new Native2DTint(
            (byte)(color.Rgba >> 24) * scale,
            (byte)(color.Rgba >> 16) * scale,
            (byte)(color.Rgba >> 8) * scale,
            (byte)color.Rgba * scale);
    }

    private static float CheckedPositive(double value, string name)
    {
        float result = Checked(value, name);
        return result > 0 ? result : throw new InvalidOperationException($"Vector icon {name} must be positive.");
    }

    private static float Checked(double value, string name)
    {
        float result = (float)value;
        return float.IsFinite(result) ? result : throw new InvalidOperationException($"Vector icon {name} must be finite.");
    }
}
