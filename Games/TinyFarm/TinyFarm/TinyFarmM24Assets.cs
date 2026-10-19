using System.Security.Cryptography;
using Aurelian.GameWorld2D;
using Dominatus.SpriteForge;
using TinyFarm.Core;

namespace TinyFarm.Native;

internal sealed class TinyFarmM24Assets
{
    private const string MeadowApprovedFileHash = "c0222629a63f12067bbe4c78ce7cb864fa263702d875e6cbd4a7b84c6ce987ee";
    private const string TreeApprovedFileHash = "abf86042eb4ec4497cf7a300488cf7cf5a5c15de73aeddbc2dad2847675c4d6f";

    private readonly IReadOnlyDictionary<SpriteAssetId, SpriteFrameMetadata> frames;

    private TinyFarmM24Assets(
        SpriteAtlasResource meadow,
        SpriteAtlasResource farmhouse,
        SpriteAtlasResource tree)
    {
        Meadow = meadow;
        Farmhouse = farmhouse;
        Tree = tree;
        Resources = [meadow, farmhouse, tree];
        frames = new Dictionary<SpriteAssetId, SpriteFrameMetadata>
        {
            [farmhouse.Id] = FullFrame(farmhouse, TinyFarmPainterlyPolicy.FarmhouseHeightAt48PixelsPerMetre),
            [tree.Id] = FullFrame(tree, TinyFarmPainterlyPolicy.TreeHeightAt48PixelsPerMetre),
        };
    }

    public SpriteAtlasResource Meadow { get; }
    public SpriteAtlasResource Farmhouse { get; }
    public SpriteAtlasResource Tree { get; }
    public IReadOnlyList<SpriteAtlasResource> Resources { get; }

    public static TinyFarmM24Assets Load(string assetDirectory)
    {
        return new TinyFarmM24Assets(
            LoadResource(
                "tinyfarm-m24-meadow",
                Path.Combine(assetDirectory, "meadow-slab.png"),
                MeadowApprovedFileHash, TinyFarmPainterlyPolicy.MeadowSampling),
            LoadResource(
                "tinyfarm-m25-farmhouse-three-quarter",
                Path.Combine(assetDirectory, "..", TinyFarmSemanticSpatialScene.FarmhousePresentationAsset),
                TinyFarmSemanticSpatialScene.FarmhousePresentationArt.ApprovedArtifactSha256, TinyFarmPainterlyPolicy.FarmhouseSampling),
            LoadResource(
                "tinyfarm-m24-tree",
                Path.Combine(assetDirectory, "tree.png"),
                TreeApprovedFileHash, TinyFarmPainterlyPolicy.TreeSampling));
    }

    public SpriteFrameMetadata Frame(SpriteAssetId id)
    {
        return frames.TryGetValue(id, out SpriteFrameMetadata? frame)
            ? frame
            : throw new KeyNotFoundException($"No M24 frame metadata exists for '{id}'.");
    }

    private static SpriteFrameMetadata FullFrame(SpriteAtlasResource resource, double targetHeightPixels)
    {
        return new SpriteFrameMetadata(
            resource.Id.Value + ":full",
            0,
            0,
            (int)resource.Width,
            (int)resource.Height,
            resource.Width / 2.0,
            resource.Height,
            0,
            0,
            targetHeightPixels / resource.Height,
            new UvRect(0, 0, 1, 1));
    }

    internal static SpriteAtlasResource LoadResource(string id, string path, string approvedFileHash, SpriteSampling sampling)
    {
        string actualFileHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        if (!string.Equals(actualFileHash, approvedFileHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Pinned presentation asset '{Path.GetFileName(path)}' has hash '{actualFileHash}', expected '{approvedFileHash}'.");
        }

        TinyFarmImage image = TinyFarmImage.Load(path);
        int width = image.Width;
        int height = image.Height;
        {
            byte[] rgba = image.ToRgba();

            string hash = Convert.ToHexString(SHA256.HashData(rgba)).ToLowerInvariant();
            return new SpriteAtlasResource(
                new SpriteAssetId(id),
                hash,
                (uint)width,
                (uint)height,
                rgba,
                sampling);
        }
    }
}
