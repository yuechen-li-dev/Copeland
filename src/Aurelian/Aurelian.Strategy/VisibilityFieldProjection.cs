using System.Security.Cryptography;

namespace Aurelian.Strategy;

public sealed record FogPresentationStyle
{
    public FogPresentationStyle(
        float unexploredOpacity = 0.92f,
        float exploredOpacity = 0.48f,
        float edgeSoftness = 0.72f,
        uint tintRgba = 0x162A24FF,
        float noiseAmount = 0.015f,
        float temporalScale = 0.035f)
    {
        if (!float.IsFinite(unexploredOpacity) || unexploredOpacity is < 0 or > 1
            || !float.IsFinite(exploredOpacity) || exploredOpacity is < 0 or > 1
            || exploredOpacity > unexploredOpacity
            || !float.IsFinite(edgeSoftness) || edgeSoftness is < 0.1f or > 2
            || !float.IsFinite(noiseAmount) || noiseAmount is < 0 or > 0.25f
            || !float.IsFinite(temporalScale) || temporalScale is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(unexploredOpacity));
        }
        UnexploredOpacity = unexploredOpacity;
        ExploredOpacity = exploredOpacity;
        EdgeSoftness = edgeSoftness;
        TintRgba = tintRgba;
        NoiseAmount = noiseAmount;
        TemporalScale = temporalScale;
    }

    public float UnexploredOpacity { get; }

    public float ExploredOpacity { get; }

    public float EdgeSoftness { get; }

    public uint TintRgba { get; }

    public float NoiseAmount { get; }

    public float TemporalScale { get; }
}

public sealed record VisibilityFieldProjection(
    int Width,
    int Height,
    byte[] RgbaPixels,
    string SemanticHash)
{
    public int UploadBytes => RgbaPixels.Length;
}

public static class VisibilityFieldProjector
{
    public static VisibilityFieldProjection Project(VisibilityGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        byte[] pixels = new byte[checked(grid.Width * grid.Height * 4)];
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                byte value = grid[x, y] switch
                {
                    CellVisibility.Unknown => 0,
                    CellVisibility.Explored => 128,
                    CellVisibility.Visible => 255,
                    _ => throw new InvalidOperationException("Unknown visibility state."),
                };
                int offset = ((y * grid.Width) + x) * 4;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
        }
        string hash = Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
        return new VisibilityFieldProjection(grid.Width, grid.Height, pixels, hash);
    }
}

public sealed class VisibilityFieldUploadTracker
{
    public string? UploadedSemanticHash { get; private set; }

    public int UploadCount { get; private set; }

    public long UploadedBytes { get; private set; }

    public bool ShouldUpload(VisibilityFieldProjection field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (string.Equals(UploadedSemanticHash, field.SemanticHash, StringComparison.Ordinal))
        {
            return false;
        }
        UploadedSemanticHash = field.SemanticHash;
        UploadCount++;
        UploadedBytes += field.UploadBytes;
        return true;
    }
}
