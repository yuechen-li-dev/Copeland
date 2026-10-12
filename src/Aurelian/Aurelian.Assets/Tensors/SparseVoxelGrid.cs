using System.Collections.Immutable;

namespace Aurelian.Assets.Tensors;

public readonly record struct VoxelCoordinate(int X, int Y, int Z);

/// <summary>Deterministic CPU topology for a thresholded occupancy volume.
/// Coordinates and neighborhood rows use XYZ order; each neighborhood uses
/// offsets [-1,1] in X/Y/Z, with -1 for absent/boundary cells.</summary>
public sealed class SparseVoxelGrid
{
    public int Resolution { get; }
    public ImmutableArray<VoxelCoordinate> Coordinates { get; }
    public ImmutableArray<int> Neighbors27 { get; }
    public ImmutableArray<int> ActiveIndex { get; }

    private SparseVoxelGrid(int resolution, ImmutableArray<VoxelCoordinate> coordinates,
        ImmutableArray<int> neighbors, ImmutableArray<int> activeIndex)
    {
        Resolution = resolution;
        Coordinates = coordinates;
        Neighbors27 = neighbors;
        ActiveIndex = activeIndex;
    }

    public static SparseVoxelGrid FromLogits(ReadOnlySpan<float> logits, int resolution, int poolFactor = 1)
    {
        if (resolution is < 1 or > 64 || poolFactor < 1 || poolFactor > resolution
            || resolution % poolFactor != 0 || logits.Length != resolution * resolution * resolution)
        {
            throw new InvalidDataException("AUR-OCCUPANCY-001: Expected a bounded cubic grid and a dividing pool factor.");
        }
        int target = resolution / poolFactor;
        var occupied = new bool[target * target * target];
        for (int index = 0; index < logits.Length; index++)
        {
            if (!float.IsFinite(logits[index]))
            {
                throw new InvalidDataException("AUR-OCCUPANCY-001: Logits must be finite before thresholding.");
            }
            if (logits[index] > 0)
            {
                int x = index / (resolution * resolution) / poolFactor;
                int y = index / resolution % resolution / poolFactor;
                int z = index % resolution / poolFactor;
                occupied[(x * target + y) * target + z] = true;
            }
        }
        var coordinates = ImmutableArray.CreateBuilder<VoxelCoordinate>();
        int[] active = Enumerable.Repeat(-1, occupied.Length).ToArray();
        for (int index = 0; index < occupied.Length; index++)
        {
            if (occupied[index])
            {
                active[index] = coordinates.Count;
                coordinates.Add(new(index / (target * target), index / target % target, index % target));
            }
        }
        var neighbors = ImmutableArray.CreateBuilder<int>(coordinates.Count * 27);
        foreach (VoxelCoordinate coordinate in coordinates)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int x = coordinate.X + dx;
                        int y = coordinate.Y + dy;
                        int z = coordinate.Z + dz;
                        bool inside = x >= 0 && x < target && y >= 0 && y < target && z >= 0 && z < target;
                        neighbors.Add(inside ? active[(x * target + y) * target + z] : -1);
                    }
                }
            }
        }
        return new(target, coordinates.ToImmutable(), neighbors.MoveToImmutable(), [.. active]);
    }
}
