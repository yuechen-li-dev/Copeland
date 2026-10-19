using Aurelian.Assets.ArtSlots;
using Aurelian.GameWorld2D;

namespace TinyFarm.Native;

/// <summary>
/// TinyFarm's art slots (Assets/art/art-slots.json) as sprite resources. Ids follow the world they dress:
/// "object.&lt;scene object&gt;", "forage.&lt;forage node&gt;" (plus ".depleted"), "item.&lt;item&gt;", "actor.&lt;actor&gt;"
/// (a 4 x 4 walk sheet like the gardener's) and "prop.&lt;name&gt;" for presentation-only props.
/// </summary>
internal sealed class TinyFarmArtSlots
{
    private readonly Dictionary<string, SpriteAtlasResource> resources = new(StringComparer.Ordinal);
    private readonly Dictionary<SpriteAssetId, IReadOnlyList<SpriteFrameMetadata>> frames = [];

    public TinyFarmArtSlots(string manifestPath)
    {
        Catalog = ArtSlotCatalog.Load(manifestPath);
        foreach (ArtSlotDefinition slot in Catalog.Slots)
        {
            // Placeholders rasterize at twice their canvas so they stay crisp at 1080p and above.
            ArtSlotImage image = Catalog.Realize(slot.Id, scale: 2);
            var resource = new SpriteAtlasResource(new SpriteAssetId("art-slot:" + slot.Id), image.ContentHash,
                (uint)image.Width, (uint)image.Height, image.Rgba, SpriteSampling.Linear);
            double heightAt48 = (slot.WorldHeight ?? 1) * 48;
            resources[slot.Id] = resource;
            frames[resource.Id] = GridSpriteSheet.Import(resource, slot.Columns, slot.Rows, heightAt48);
        }
    }

    public ArtSlotCatalog Catalog { get; }

    public IEnumerable<SpriteAtlasResource> Resources => resources.Values;

    public bool TryGet(string id, out SpriteAtlasResource resource) => resources.TryGetValue(id, out resource!);

    public bool Owns(SpriteAssetId asset) => frames.ContainsKey(asset);

    public SpriteFrameMetadata Frame(SpriteAssetId asset, string spriteId)
    {
        IReadOnlyList<SpriteFrameMetadata> sheet = frames[asset];
        int index = int.Parse(spriteId, System.Globalization.CultureInfo.InvariantCulture);
        return sheet[index];
    }
}
