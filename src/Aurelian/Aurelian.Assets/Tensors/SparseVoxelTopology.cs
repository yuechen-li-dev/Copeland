using System.Collections.Immutable;

namespace Aurelian.Assets.Tensors;

public sealed record SparseVoxelSubdivision(SparseVoxelTopology Topology,
    ImmutableArray<int> ParentRows, ImmutableArray<int> ChildSlots);

/// <summary>Sparse CPU coordinate authority without a dense resolution-cubed
/// allocation. Input row order is preserved, including learned subdivisions.</summary>
public sealed class SparseVoxelTopology
{
    public int Resolution { get; }
    public ImmutableArray<VoxelCoordinate> Coordinates { get; }
    public ImmutableArray<int> Neighbors27 { get; }

    private SparseVoxelTopology(int resolution, ImmutableArray<VoxelCoordinate> coordinates, ImmutableArray<int> neighbors)
    {
        Resolution = resolution;
        Coordinates = coordinates;
        Neighbors27 = neighbors;
    }

    public static SparseVoxelTopology Create(int resolution, IEnumerable<VoxelCoordinate> coordinates)
    {
        if (resolution is < 1 or > 1024)
        {
            throw Stop("Resolution must be 1..1024.");
        }
        var rows = ImmutableArray.CreateBuilder<VoxelCoordinate>();
        var indices = new Dictionary<VoxelCoordinate, int>();
        foreach (VoxelCoordinate coordinate in coordinates)
        {
            if (rows.Count == 1048576 || coordinate.X < 0 || coordinate.X >= resolution
                || coordinate.Y < 0 || coordinate.Y >= resolution || coordinate.Z < 0 || coordinate.Z >= resolution
                || !indices.TryAdd(coordinate, rows.Count))
            {
                throw Stop("Coordinates must be unique, in range, and contain at most 1048576 rows.");
            }
            rows.Add(coordinate);
        }
        var neighbors = ImmutableArray.CreateBuilder<int>(checked(rows.Count * 27));
        foreach (VoxelCoordinate coordinate in rows)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var other = new VoxelCoordinate(coordinate.X + dx, coordinate.Y + dy, coordinate.Z + dz);
                        neighbors.Add(indices.TryGetValue(other, out int row) ? row : -1);
                    }
                }
            }
        }
        return new(resolution, rows.ToImmutable(), neighbors.MoveToImmutable());
    }

    /// <summary>Upstream sparse Channel2Spatial order is parent row, then child
    /// slot: X is the least-significant bit, followed by Y and Z. It differs
    /// from the dense decoder's channel-wise pixel-shuffle order.</summary>
    public SparseVoxelSubdivision Subdivide(ReadOnlySpan<float> childLogits)
    {
        if (Resolution > 512 || childLogits.Length != Coordinates.Length * 8)
        {
            throw Stop("Subdivision needs eight logits per parent and a doubled resolution <=1024.");
        }
        var children = ImmutableArray.CreateBuilder<VoxelCoordinate>();
        var parents = ImmutableArray.CreateBuilder<int>();
        var slots = ImmutableArray.CreateBuilder<int>();
        for (int row = 0; row < Coordinates.Length; row++)
        {
            VoxelCoordinate parent = Coordinates[row];
            for (int slot = 0; slot < 8; slot++)
            {
                float logit = childLogits[row * 8 + slot];
                if (!float.IsFinite(logit))
                {
                    throw Stop("Subdivision logits must be finite.");
                }
                if (logit > 0)
                {
                    if (children.Count == 1048576)
                    {
                        throw Stop("Subdivision exceeds 1048576 output rows.");
                    }
                    children.Add(new(parent.X * 2 + (slot & 1), parent.Y * 2 + (slot >> 1 & 1), parent.Z * 2 + (slot >> 2 & 1)));
                    parents.Add(row);
                    slots.Add(slot);
                }
            }
        }
        return new(Create(Resolution * 2, children), parents.ToImmutable(), slots.ToImmutable());
    }

    private static InvalidDataException Stop(string message) => new("AUR-SPARSE-TOPOLOGY-001: " + message);
}
