using Aurelian.Graphics.Vulkan.Native2D;

namespace Aurelian.GameWorld2D;

public sealed record SpriteAtlasResource(
    SpriteAssetId Id,
    string ContentHash,
    uint Width,
    uint Height,
    byte[] Rgba8,
    SpriteSampling Sampling);

public sealed class NativeSpriteResourceScope : IDisposable
{
    private readonly VulkanOrderedQuadRenderer renderer;
    private readonly SpriteSampling sampling;
    private readonly Dictionary<SpriteAssetId, Entry> entries = [];
    private bool disposed;

    public NativeSpriteResourceScope(VulkanOrderedQuadRenderer renderer, SpriteSampling sampling)
    {
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        if (renderer.PipelineKind != Native2DPipelineKind.Textured || !renderer.StraightAlphaBlend)
        {
            throw new ArgumentException("Sprite resource scopes require a straight-alpha textured renderer.", nameof(renderer));
        }
        bool expectsLinear = sampling == SpriteSampling.Linear;
        if (renderer.LinearFiltering != expectsLinear)
        {
            throw new ArgumentException($"Renderer filtering does not match the requested {sampling} sprite scope.", nameof(renderer));
        }
        this.sampling = sampling;
    }

    public int TextureUploads { get; private set; }

    public int Count => entries.Count;

    public Native2DTextureHandle Resolve(SpriteAtlasResource resource)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(resource);
        if (string.IsNullOrWhiteSpace(resource.Id.Value))
        {
            throw new ArgumentException("Sprite asset identity must not be empty.", nameof(resource));
        }
        if (string.IsNullOrWhiteSpace(resource.ContentHash))
        {
            throw new ArgumentException("Sprite asset content hash must not be empty.", nameof(resource));
        }
        if (resource.Sampling != sampling)
        {
            throw new InvalidOperationException($"Sprite asset '{resource.Id}' requests {resource.Sampling} sampling, but this scope is {sampling}.");
        }

        if (entries.TryGetValue(resource.Id, out Entry? existing))
        {
            if (existing.ContentHash == resource.ContentHash)
            {
                return existing.Texture;
            }
            renderer.UpdateTexture(existing.Texture, resource.Width, resource.Height, resource.Rgba8);
            existing.ContentHash = resource.ContentHash;
            TextureUploads++;
            return existing.Texture;
        }

        Native2DTextureHandle texture = renderer.CreateTexture(resource.Width, resource.Height, resource.Rgba8);
        entries.Add(resource.Id, new Entry(resource.ContentHash, texture));
        TextureUploads++;
        return texture;
    }

    public Native2DTextureHandle Get(SpriteAssetId id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return entries.TryGetValue(id, out Entry? entry)
            ? entry.Texture
            : throw new KeyNotFoundException($"Unknown sprite asset '{id}'.");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        foreach (Entry entry in entries.Values)
        {
            renderer.DisposeTexture(entry.Texture);
        }
        entries.Clear();
        disposed = true;
    }

    private sealed class Entry(string contentHash, Native2DTextureHandle texture)
    {
        public string ContentHash { get; set; } = contentHash;
        public Native2DTextureHandle Texture { get; } = texture;
    }
}
