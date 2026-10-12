using System.Collections.Immutable;
using System.Numerics;

namespace Aurelian.Assets.Models;

public sealed record MeshClusterReduction(ImmutableArray<Vector3> Positions,
    ImmutableArray<int> TriangleIndices, ImmutableArray<int> SourceToReduced,
    int CellsPerUnit, float MaximumPositionDeviation);

/// <summary>Deterministic geometric preview LOD. The original mesh remains the
/// authority; mapping explicitly supports aggregation of source attributes.</summary>
public static class MeshClusterReducer
{
    public static MeshClusterReduction Reduce(ImmutableArray<Vector3> positions,
        ImmutableArray<int> triangles, int cellsPerUnit)
    {
        if (positions.IsDefaultOrEmpty || triangles.IsDefaultOrEmpty || triangles.Length % 3 != 0
            || cellsPerUnit is < 8 or > 512 || triangles.Any(index => index < 0 || index >= positions.Length))
        {
            throw new InvalidDataException("AUR-MESH-LOD-001: Clustering requires valid indexed triangles and 8..512 cells per unit.");
        }
        var used = new bool[positions.Length];
        foreach (int index in triangles)
        {
            used[index] = true;
        }
        var cells = new Dictionary<(int X, int Y, int Z), int>();
        var sums = new List<Vector3>();
        var counts = new List<int>();
        int[] mapping = Enumerable.Repeat(-1, positions.Length).ToArray();
        for (int source = 0; source < positions.Length; source++)
        {
            Vector3 point = positions[source];
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)
                || MathF.Abs(point.X) > 100000 || MathF.Abs(point.Y) > 100000 || MathF.Abs(point.Z) > 100000)
            {
                throw new InvalidDataException("AUR-MESH-LOD-001: Mesh positions must be finite and within the bounded clustering coordinate range.");
            }
            if (!used[source])
            {
                continue;
            }
            var key = ((int)MathF.Floor(point.X * cellsPerUnit),
                (int)MathF.Floor(point.Y * cellsPerUnit), (int)MathF.Floor(point.Z * cellsPerUnit));
            if (!cells.TryGetValue(key, out int destination))
            {
                destination = sums.Count;
                cells.Add(key, destination);
                sums.Add(Vector3.Zero);
                counts.Add(0);
            }
            mapping[source] = destination;
            sums[destination] += point;
            counts[destination]++;
        }
        var reduced = new Vector3[sums.Count];
        for (int index = 0; index < sums.Count; index++)
        {
            reduced[index] = sums[index] / counts[index];
        }
        float maximumDeviation = 0;
        for (int source = 0; source < positions.Length; source++)
        {
            if (mapping[source] >= 0)
            {
                maximumDeviation = Math.Max(maximumDeviation, Vector3.Distance(positions[source], reduced[mapping[source]]));
            }
        }
        var output = ImmutableArray.CreateBuilder<int>();
        var faces = new HashSet<(int A, int B, int C)>();
        for (int first = 0; first < triangles.Length; first += 3)
        {
            int a = mapping[triangles[first]];
            int b = mapping[triangles[first + 1]];
            int c = mapping[triangles[first + 2]];
            if (a == b || a == c || b == c
                || Vector3.Cross(reduced[b] - reduced[a], reduced[c] - reduced[a]).LengthSquared() < 1e-20f)
            {
                continue;
            }
            int minimum = Math.Min(a, Math.Min(b, c));
            int maximum = Math.Max(a, Math.Max(b, c));
            if (faces.Add((minimum, a + b + c - minimum - maximum, maximum)))
            {
                output.Add(a);
                output.Add(b);
                output.Add(c);
            }
        }
        return new(reduced.ToImmutableArray(), output.ToImmutable(), mapping.ToImmutableArray(), cellsPerUnit, maximumDeviation);
    }
}
