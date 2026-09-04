using Aurelian.Graphics.Vulkan.Native2D;
using Machina.Fonts;
using Machina.Presentation;

namespace Aurelian.Machina;

public enum AurelianMsdfAtlasRowOrder
{
    Unspecified,
    TopToBottom,
}

public sealed record AurelianMsdfAtlasResource
{
    public AurelianMsdfAtlasResource(
        MachinaFontAtlasId identity,
        FontAtlasSnapshot snapshot,
        IReadOnlyDictionary<int, byte[]> rgbaPages,
        AurelianMsdfAtlasRowOrder rowOrder)
    {
        if (rowOrder != AurelianMsdfAtlasRowOrder.TopToBottom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rowOrder),
                rowOrder,
                "Machina MSDF atlas rows must explicitly declare the top-to-bottom artifact convention.");
        }

        Identity = identity;
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        RgbaPages = rgbaPages ?? throw new ArgumentNullException(nameof(rgbaPages));
        RowOrder = rowOrder;
    }

    public MachinaFontAtlasId Identity { get; }

    public FontAtlasSnapshot Snapshot { get; }

    public IReadOnlyDictionary<int, byte[]> RgbaPages { get; }

    public AurelianMsdfAtlasRowOrder RowOrder { get; }
}

/// <summary>
/// Integration-owned mapping from Machina atlas identity to persistent native textures.
/// </summary>
public sealed class AurelianMsdfAtlasCache : IDisposable
{
    private readonly VulkanOrderedQuadRenderer renderer;
    private readonly Dictionary<MachinaFontAtlasId, CacheEntry> entries = [];
    private bool disposed;

    public AurelianMsdfAtlasCache(VulkanOrderedQuadRenderer renderer)
    {
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        if (renderer.PipelineKind != Native2DPipelineKind.MsdfText)
        {
            throw new ArgumentException("The atlas cache requires an MSDF text renderer.", nameof(renderer));
        }
    }

    public int UploadCount { get; private set; }

    public int AtlasCount => entries.Count;

    public IReadOnlyDictionary<int, Native2DTextureHandle> Resolve(AurelianMsdfAtlasResource resource)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(resource.Snapshot);
        ArgumentNullException.ThrowIfNull(resource.RgbaPages);

        if (entries.TryGetValue(resource.Identity, out CacheEntry? cached))
        {
            if (!ReferenceEquals(cached.Snapshot, resource.Snapshot) && cached.Snapshot.Version != resource.Snapshot.Version)
            {
                throw new InvalidOperationException(
                    $"Atlas identity '{resource.Identity}' was reused for snapshot version {resource.Snapshot.Version}; use a new content identity.");
            }

            return cached.Textures;
        }

        Dictionary<int, Native2DTextureHandle> textures = [];
        try
        {
            foreach (FontAtlasPage page in resource.Snapshot.Pages.OrderBy(static page => page.Index))
            {
                if (!resource.RgbaPages.TryGetValue(page.Index, out byte[]? rgba))
                {
                    throw new InvalidOperationException(
                        $"Atlas '{resource.Identity}' has no RGBA payload for page {page.Index}.");
                }

                // Machina atlas artifacts use conventional top-to-bottom rows. Vulkan's native
                // presentation UV convention is bottom-to-top, so normalize once at upload.
                byte[] nativeRows = AurelianMsdfAtlasUpload.NormalizeRows(resource, page, rgba);
                textures.Add(page.Index, renderer.CreateTexture((uint)page.Width, (uint)page.Height, nativeRows));
                UploadCount++;
            }
        }
        catch
        {
            foreach (Native2DTextureHandle texture in textures.Values)
            {
                renderer.DisposeTexture(texture);
            }
            throw;
        }

        var entry = new CacheEntry(resource.Snapshot, textures);
        entries.Add(resource.Identity, entry);
        return entry.Textures;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        foreach (CacheEntry entry in entries.Values)
        {
            foreach (Native2DTextureHandle texture in entry.Textures.Values)
            {
                renderer.DisposeTexture(texture);
            }
        }

        entries.Clear();
        disposed = true;
    }

    private sealed record CacheEntry(
        FontAtlasSnapshot Snapshot,
        IReadOnlyDictionary<int, Native2DTextureHandle> Textures);
}

public static class AurelianMsdfAtlasUpload
{
    /// <summary>
    /// Converts a Machina top-to-bottom atlas interval to the native bottom-to-top page.
    /// This must accompany <see cref="NormalizeRows"/>; keeping both here prevents the
    /// common half-fix where glyphs are inverted or disappear into another packed row.
    /// </summary>
    public static Native2DUvRect NormalizeUv(Native2DUvRect source)
    {
        return new Native2DUvRect(source.U0, 1f - source.V1, source.U1, 1f - source.V0);
    }

    public static byte[] NormalizeRows(
        AurelianMsdfAtlasResource resource,
        FontAtlasPage page,
        byte[] source)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(source);
        if (resource.RowOrder != AurelianMsdfAtlasRowOrder.TopToBottom)
        {
            throw new InvalidOperationException("MSDF atlas upload received an unsupported or unspecified row order.");
        }

        int width = page.Width;
        int height = page.Height;
        int rowBytes = checked(width * 4);
        if (source.Length != checked(rowBytes * height))
        {
            throw new InvalidOperationException("Atlas RGBA payload length does not match its page extent.");
        }

        byte[] result = new byte[source.Length];
        for (int sourceRow = 0; sourceRow < height; sourceRow++)
        {
            int destinationRow = height - sourceRow - 1;
            source.AsSpan(sourceRow * rowBytes, rowBytes).CopyTo(result.AsSpan(destinationRow * rowBytes, rowBytes));
        }
        return result;
    }
}
