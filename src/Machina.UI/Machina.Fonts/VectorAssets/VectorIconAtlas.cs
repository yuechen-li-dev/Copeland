using System.Security.Cryptography;
using Machina.Core.Assets;

using Copeland.Profile;

namespace Machina.VectorAssets;

public enum VectorAtlasRowOrder
{
    Unspecified,
    TopToBottom,
}

public sealed record VectorIconAtlasEntry(
    MachinaVectorIconId Identity,
    int X,
    int Y,
    int Width,
    int Height,
    double U0,
    double V0,
    double U1,
    double V1,
    VectorBounds PlaneBounds,
    VectorBounds FieldBounds,
    double PixelRange,
    double ProjectionScale,
    string FieldHash);

public sealed record VectorIconAtlas(
    string Identity,
    int Width,
    int Height,
    int Padding,
    VectorAtlasRowOrder RowOrder,
    IReadOnlyDictionary<MachinaVectorIconId, VectorIconAtlasEntry> Entries,
    ReadOnlyMemory<float> Pixels,
    string AtlasHash)
{
    public int ChannelCount => 3;
}

public static class VectorIconAtlasPacker
{
    public static VectorIconAtlas Pack(
        IReadOnlyList<VectorIconMsdfArtifact> artifacts,
        int width = 256,
        int height = 256,
        int padding = 2,
        VectorAtlasRowOrder rowOrder = VectorAtlasRowOrder.TopToBottom)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        if (artifacts.Count == 0)
        {
            throw new ArgumentException("At least one vector icon is required.", nameof(artifacts));
        }
        if (width <= 0 || height <= 0 || padding < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        if (rowOrder != VectorAtlasRowOrder.TopToBottom)
        {
            throw new ArgumentOutOfRangeException(nameof(rowOrder), "Vector atlas row orientation must be explicit and top-to-bottom.");
        }
        if (artifacts.Select(static artifact => artifact.Identity).Distinct().Count() != artifacts.Count)
        {
            throw new ArgumentException("Vector icon identities must be unique within an atlas.", nameof(artifacts));
        }

        VectorIconMsdfArtifact[] ordered = artifacts
            .OrderByDescending(static artifact => artifact.Height)
            .ThenByDescending(static artifact => artifact.Width)
            .ThenBy(static artifact => artifact.Identity.Value, StringComparer.Ordinal)
            .ToArray();

        float[] pixels = new float[checked(width * height * 3)];
        Dictionary<MachinaVectorIconId, VectorIconAtlasEntry> entries = [];
        int cursorX = 0;
        int cursorY = 0;
        int shelfHeight = 0;
        foreach (VectorIconMsdfArtifact artifact in ordered)
        {
            int strideWidth = artifact.Width + padding;
            int strideHeight = artifact.Height + padding;
            if (cursorX + strideWidth > width)
            {
                cursorX = 0;
                cursorY += shelfHeight;
                shelfHeight = 0;
            }
            if (cursorY + strideHeight > height)
            {
                throw new InvalidOperationException($"Vector icon atlas {width}x{height} cannot contain the requested corpus.");
            }

            Copy(artifact, pixels, width, cursorX, cursorY);
            entries.Add(artifact.Identity, new VectorIconAtlasEntry(
                artifact.Identity,
                cursorX,
                cursorY,
                artifact.Width,
                artifact.Height,
                cursorX / (double)width,
                cursorY / (double)height,
                (cursorX + artifact.Width) / (double)width,
                (cursorY + artifact.Height) / (double)height,
                artifact.PlaneBounds,
                artifact.FieldBounds,
                artifact.PixelRange,
                artifact.ProjectionScale,
                artifact.FieldHash));
            cursorX += strideWidth;
            shelfHeight = Math.Max(shelfHeight, strideHeight);
        }

        byte[] bytes = new byte[checked(pixels.Length * sizeof(float))];
        Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string identity = "vector-atlas-sha256-" + hash;
        return new VectorIconAtlas(identity, width, height, padding, rowOrder, entries, pixels, hash);
    }

    public static byte[] ToRgba8(VectorIconAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        byte[] result = new byte[checked(atlas.Width * atlas.Height * 4)];
        ReadOnlySpan<float> source = atlas.Pixels.Span;
        for (int pixel = 0; pixel < atlas.Width * atlas.Height; pixel++)
        {
            result[pixel * 4] = Quantize(source[pixel * 3]);
            result[(pixel * 4) + 1] = Quantize(source[(pixel * 3) + 1]);
            result[(pixel * 4) + 2] = Quantize(source[(pixel * 3) + 2]);
            result[(pixel * 4) + 3] = 255;
        }
        return result;
    }

    private static byte Quantize(float value)
    {
        return (byte)Math.Clamp((int)Math.Round(value * 255f, MidpointRounding.AwayFromZero), 0, 255);
    }

    private static void Copy(VectorIconMsdfArtifact artifact, float[] page, int pageWidth, int x, int y)
    {
        ReadOnlySpan<float> source = artifact.FieldPixels.Span;
        for (int row = 0; row < artifact.Height; row++)
        {
            int sourceOffset = row * artifact.Width * 3;
            int destinationOffset = ((y + row) * pageWidth + x) * 3;
            source.Slice(sourceOffset, artifact.Width * 3).CopyTo(page.AsSpan(destinationOffset, artifact.Width * 3));
        }
    }
}
