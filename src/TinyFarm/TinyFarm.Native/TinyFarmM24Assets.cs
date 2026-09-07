using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Aurelian.GameWorld2D;
using Dominatus.SpriteForge;
using TinyFarm.Core;

namespace TinyFarm.Native;

internal sealed class TinyFarmM24Assets
{
    private const string MeadowApprovedFileHash = "c0222629a63f12067bbe4c78ce7cb864fa263702d875e6cbd4a7b84c6ce987ee";
    private const string FarmhouseApprovedFileHash = "f2334a458c82f5d39c69d8a9220fe7171595f0daa89d30dc5d9dee83fa9f8529";
    private const string TreeApprovedFileHash = "abf86042eb4ec4497cf7a300488cf7cf5a5c15de73aeddbc2dad2847675c4d6f";

    private readonly IReadOnlyDictionary<SpriteAssetId, SpriteFrameMetadata> frames;

    private TinyFarmM24Assets(
        SpriteAtlasResource meadow,
        SpriteAtlasResource farmhouse,
        SpriteAtlasResource tree,
        bool legacy)
    {
        Meadow = meadow;
        Farmhouse = farmhouse;
        Tree = tree;
        Resources = [meadow, farmhouse, tree];
        frames = new Dictionary<SpriteAssetId, SpriteFrameMetadata>
        {
            [farmhouse.Id] = FullFrame(farmhouse, legacy ? 238 : TinyFarmPainterlyPolicy.FarmhouseHeightAt48PixelsPerMetre),
            [tree.Id] = FullFrame(tree, TinyFarmPainterlyPolicy.TreeHeightAt48PixelsPerMetre),
        };
    }

    public SpriteAtlasResource Meadow { get; }
    public SpriteAtlasResource Farmhouse { get; }
    public SpriteAtlasResource Tree { get; }
    public IReadOnlyList<SpriteAtlasResource> Resources { get; }

    public static TinyFarmM24Assets Load(string assetDirectory, bool legacy = false)
    {
        return new TinyFarmM24Assets(
            LoadResource(
                "tinyfarm-m24-meadow",
                Path.Combine(assetDirectory, "meadow-slab.png"),
                MeadowApprovedFileHash, legacy, TinyFarmPainterlyPolicy.MeadowSampling),
            LoadResource(
                legacy ? "tinyfarm-m24-farmhouse" : "tinyfarm-m25-farmhouse-three-quarter",
                legacy ? Path.Combine(assetDirectory, "farmhouse.png") : Path.Combine(assetDirectory, "..", TinyFarmSemanticSpatialScene.FarmhousePresentationAsset),
                legacy ? FarmhouseApprovedFileHash : TinyFarmSemanticSpatialScene.FarmhousePresentationArt.ApprovedArtifactSha256, legacy, TinyFarmPainterlyPolicy.FarmhouseSampling),
            LoadResource(
                "tinyfarm-m24-tree",
                Path.Combine(assetDirectory, "tree.png"),
                TreeApprovedFileHash, legacy, TinyFarmPainterlyPolicy.TreeSampling), legacy);
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

    private static SpriteAtlasResource LoadResource(string id, string path, string approvedFileHash, bool legacy, SpriteSampling sampling)
    {
        string actualFileHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        if (!string.Equals(actualFileHash, approvedFileHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"M24 approved asset '{Path.GetFileName(path)}' has hash '{actualFileHash}', expected '{approvedFileHash}'.");
        }

        using var bitmap = new Bitmap(path);
        int width = bitmap.Width;
        int height = bitmap.Height;
        using var converted = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(converted))
        {
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.DrawImage(bitmap, 0, 0, width, height);
        }

        Rectangle bounds = new(0, 0, width, height);
        BitmapData data = converted.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] rgba = new byte[checked(width * height * 4)];
            byte[] row = new byte[checked(width * 4)];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < width; x++)
                {
                    int source = x * 4;
                    int target = ((y * width) + x) * 4;
                    rgba[target] = row[source + 2];
                    rgba[target + 1] = row[source + 1];
                    rgba[target + 2] = row[source];
                    rgba[target + 3] = row[source + 3];
                }
            }

            string hash = Convert.ToHexString(SHA256.HashData(rgba)).ToLowerInvariant();
            return new SpriteAtlasResource(
                new SpriteAssetId(id),
                hash,
                (uint)width,
                (uint)height,
                rgba,
                legacy ? SpriteSampling.Nearest : sampling);
        }
        finally
        {
            converted.UnlockBits(data);
        }
    }
}
