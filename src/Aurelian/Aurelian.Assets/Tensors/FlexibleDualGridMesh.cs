using System.Collections.Immutable;
using System.Numerics;

namespace Aurelian.Assets.Tensors;

public sealed record FlexibleDualGridMesh(ImmutableArray<Vector3> Positions,
    ImmutableArray<int> TriangleIndices, int Quads, int MissingNeighborQuads)
{
    private static readonly VoxelCoordinate[][] Offsets =
    [
        [new(0, 0, 0), new(0, 0, 1), new(0, 1, 1), new(0, 1, 0)],
        [new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1)],
        [new(0, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, 0, 0)],
    ];

    /// <summary>Pinned TRELLIS.2 inference dual-grid extraction: three learned
    /// offsets, three edge flags and a positive learned diagonal split weight.
    /// Coordinates and output retain the model's XYZ basis and unit AABB.</summary>
    public static FlexibleDualGridMesh Decode(SparseVoxelTopology topology, ReadOnlySpan<float> features)
    {
        int rows = topology.Coordinates.Length;
        if (rows == 0 || features.Length != rows * 7)
        {
            throw new InvalidDataException("AUR-DUAL-GRID-001: Mesh decoding requires seven features per active coordinate.");
        }
        var positions = ImmutableArray.CreateBuilder<Vector3>(rows);
        var split = new float[rows];
        var lookup = new Dictionary<VoxelCoordinate, int>(rows);
        for (int row = 0; row < rows; row++)
        {
            ReadOnlySpan<float> value = features.Slice(row * 7, 7);
            foreach (float component in value)
            {
                if (!float.IsFinite(component))
                {
                    throw new InvalidDataException("AUR-DUAL-GRID-001: Mesh features must be finite.");
                }
            }
            VoxelCoordinate coordinate = topology.Coordinates[row];
            var offset = new Vector3(VertexOffset(value[0]), VertexOffset(value[1]), VertexOffset(value[2]));
            positions.Add((new Vector3(coordinate.X, coordinate.Y, coordinate.Z) + offset) / topology.Resolution - new Vector3(.5f));
            split[row] = value[6] > 20 ? value[6] : MathF.Log(1 + MathF.Exp(value[6]));
            lookup.Add(coordinate, row);
        }
        var triangles = ImmutableArray.CreateBuilder<int>();
        Span<int> quad = stackalloc int[4];
        int quads = 0;
        int missing = 0;
        for (int row = 0; row < rows; row++)
        {
            VoxelCoordinate coordinate = topology.Coordinates[row];
            for (int axis = 0; axis < 3; axis++)
            {
                if (features[row * 7 + 3 + axis] <= 0)
                {
                    continue;
                }
                bool complete = true;
                for (int corner = 0; corner < 4; corner++)
                {
                    VoxelCoordinate delta = Offsets[axis][corner];
                    complete &= lookup.TryGetValue(new(coordinate.X + delta.X,
                        coordinate.Y + delta.Y, coordinate.Z + delta.Z), out quad[corner]);
                }
                if (!complete)
                {
                    missing++;
                    continue;
                }
                if (split[quad[0]] * split[quad[2]] > split[quad[1]] * split[quad[3]])
                {
                    triangles.AddRange(new int[] { quad[0], quad[1], quad[2], quad[0], quad[2], quad[3] });
                }
                else
                {
                    triangles.AddRange(new int[] { quad[0], quad[1], quad[3], quad[3], quad[1], quad[2] });
                }
                quads++;
            }
        }
        return new(positions.MoveToImmutable(), triangles.ToImmutable(), quads, missing);
    }

    private static float VertexOffset(float logit)
    {
        return 2 / (1 + MathF.Exp(-logit)) - .5f;
    }
}
