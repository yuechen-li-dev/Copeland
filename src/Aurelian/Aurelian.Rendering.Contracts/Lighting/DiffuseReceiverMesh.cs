using System.Collections.Immutable;
using System.Numerics;

namespace Aurelian.Rendering.Contracts.Lighting;

/// <summary>Shared nodal finite elements over a normalized horizontal receiver square.</summary>
public sealed class DiffuseReceiverMesh
{
    private readonly float[] vertices;
    private readonly int[] triangles;
    private readonly float[] weights;
    private readonly double[][] matrices;
    private readonly int[][] nodes;
    private readonly List<(int First, int Second, int Left, int Right)> sharedEdges = [];

    public DiffuseReceiverMesh(IEnumerable<float> vertices, IEnumerable<int> triangles,
        IEnumerable<float> weights, int degree = 2, bool bubble = false)
    {
        this.vertices = vertices.ToArray();
        this.triangles = triangles.ToArray();
        this.weights = weights.ToArray();
        Degree = degree;
        Bubble = bubble;
        if (degree is not (1 or 2)
            || this.vertices.Length is < 6 or > 256 || this.vertices.Length % 2 != 0
            || this.triangles.Length is < 8 or > 512 || this.triangles.Length % 4 != 0
            || this.weights.Length is < 6 or > 3072 || this.weights.Length % 6 != 0
            || this.vertices.Any(value => !float.IsFinite(value) || Math.Abs(value) > 1.000001f)
            || this.weights.Any(value => !float.IsFinite(value)))
        {
            throw new InvalidDataException("Diffuse receiver numeric shape/domain mismatch.");
        }
        matrices = new double[TriangleCount][];
        nodes = new int[TriangleCount][];
        BuildTopology();
    }

    public int Degree { get; }
    public bool Bubble { get; }
    public int TriangleCount => triangles.Length / 4;
    public int VertexCount => vertices.Length / 2;
    public int CoefficientCount => weights.Length / 6;
    public ImmutableArray<float> Vertices => vertices.ToImmutableArray();
    public ImmutableArray<int> Triangles => triangles.ToImmutableArray();
    public ImmutableArray<float> Weights => weights.ToImmutableArray();
    public ImmutableArray<(int First, int Second, int Left, int Right)> SharedEdges => sharedEdges.ToImmutableArray();
    public ImmutableArray<double> Matrix(int triangle) => matrices[triangle].ToImmutableArray();
    public ImmutableArray<int> Nodes(int triangle) => nodes[triangle].ToImmutableArray();

    public Vector3 EvaluateNormalized(Vector2 point, float sun = 1, float emission = 1)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || Math.Abs(point.X) > 1 || Math.Abs(point.Y) > 1
            || !float.IsFinite(sun) || !float.IsFinite(emission) || sun < 0 || emission < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(point));
        }
        for (int triangle = 0; triangle < TriangleCount; triangle++)
        {
            if (Barycentric(triangle, point.X, point.Y).Min() >= -.000001)
            {
                return Vector3.Max(Value(triangle, point.X, point.Y, 0), Vector3.Zero) * sun
                    + Vector3.Max(Value(triangle, point.X, point.Y, 3), Vector3.Zero) * emission;
            }
        }
        throw new InvalidDataException("Diffuse mesh does not cover the receiver.");
    }

    public double MaximumEdgeMismatch()
    {
        double maximum = 0;
        foreach (var edge in sharedEdges)
        {
            double firstX = vertices[edge.First * 2];
            double firstZ = vertices[edge.First * 2 + 1];
            double secondX = vertices[edge.Second * 2];
            double secondZ = vertices[edge.Second * 2 + 1];
            for (int sample = 0; sample <= 128; sample++)
            {
                double fraction = sample / 128.0;
                double x = firstX + (secondX - firstX) * fraction;
                double z = firstZ + (secondZ - firstZ) * fraction;
                for (int basis = 0; basis <= 3; basis += 3)
                {
                    Vector3 difference = Vector3.Abs(Value(edge.Left, x, z, basis) - Value(edge.Right, x, z, basis));
                    maximum = Math.Max(maximum, Math.Max(difference.X, Math.Max(difference.Y, difference.Z)));
                }
            }
        }
        return maximum;
    }

    private void BuildTopology()
    {
        var edges = new SortedDictionary<(int First, int Second), List<(int Triangle, int From, int To)>>();
        double totalArea = 0;
        for (int triangle = 0; triangle < TriangleCount; triangle++)
        {
            int[] indices = triangles.AsSpan(triangle * 4, 3).ToArray();
            if (indices.Any(index => index < 0 || index >= VertexCount) || indices.Distinct().Count() != 3
                || triangles[triangle * 4 + 3] is < 0 or > 7)
            {
                throw new InvalidDataException("Continuous triangle index/leaf invalid.");
            }
            double ax = vertices[indices[0] * 2];
            double az = vertices[indices[0] * 2 + 1];
            double bx = vertices[indices[1] * 2];
            double bz = vertices[indices[1] * 2 + 1];
            double cx = vertices[indices[2] * 2];
            double cz = vertices[indices[2] * 2 + 1];
            double determinant = (bx - ax) * (cz - az) - (cx - ax) * (bz - az);
            if (determinant <= 1e-10)
            {
                throw new InvalidDataException("Continuous triangle is degenerate or reversed.");
            }
            totalArea += determinant / 2;
            matrices[triangle] = [
                (bz - cz) / determinant, (cx - bx) / determinant, (bx * cz - cx * bz) / determinant,
                (cz - az) / determinant, (ax - cx) / determinant, (cx * az - ax * cz) / determinant,
                (az - bz) / determinant, (bx - ax) / determinant, (ax * bz - bx * az) / determinant,
            ];
            foreach ((int first, int second) in new[] { (0, 1), (1, 2), (2, 0) })
            {
                int from = indices[first];
                int to = indices[second];
                var key = (Math.Min(from, to), Math.Max(from, to));
                if (!edges.TryGetValue(key, out var occurrences))
                {
                    occurrences = [];
                    edges.Add(key, occurrences);
                }
                occurrences.Add((triangle, from, to));
            }
        }
        if (Math.Abs(totalArea - 4) > .00001)
        {
            throw new InvalidDataException("Continuous mesh does not cover the receiver area.");
        }
        var edgeNodes = new Dictionary<(int First, int Second), int>();
        foreach (var pair in edges)
        {
            edgeNodes.Add(pair.Key, VertexCount + edgeNodes.Count);
            if (pair.Value.Count == 2)
            {
                if (pair.Value[0].From != pair.Value[1].To || pair.Value[0].To != pair.Value[1].From)
                {
                    throw new InvalidDataException("Continuous interior edge is folded.");
                }
                sharedEdges.Add((pair.Key.First, pair.Key.Second, pair.Value[0].Triangle, pair.Value[1].Triangle));
            }
            else if (pair.Value.Count != 1 || !OnDomainBoundary(pair.Key.First, pair.Key.Second))
            {
                throw new InvalidDataException("Continuous mesh has a crack or nonmanifold edge.");
            }
        }
        int boundaryCount = VertexCount + (Degree == 2 ? edgeNodes.Count : 0);
        if (CoefficientCount != boundaryCount + (Bubble ? TriangleCount : 0))
        {
            throw new InvalidDataException("Continuous boundary/interior coefficient shape mismatch.");
        }
        for (int triangle = 0; triangle < TriangleCount; triangle++)
        {
            var current = triangles.AsSpan(triangle * 4, 3).ToArray().ToList();
            if (Degree == 2)
            {
                foreach ((int first, int second) in new[] { (0, 1), (1, 2), (2, 0) })
                {
                    current.Add(edgeNodes[(Math.Min(current[first], current[second]), Math.Max(current[first], current[second]))]);
                }
            }
            if (Bubble)
            {
                current.Add(boundaryCount + triangle);
            }
            nodes[triangle] = current.ToArray();
        }
    }

    private bool OnDomainBoundary(int first, int second)
    {
        for (int axis = 0; axis < 2; axis++)
        {
            foreach (int side in new[] { -1, 1 })
            {
                if (Math.Abs(vertices[first * 2 + axis] - side) < .000001
                    && Math.Abs(vertices[second * 2 + axis] - side) < .000001)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private double[] Barycentric(int triangle, double x, double z)
    {
        double[] matrix = matrices[triangle];
        return [matrix[0] * x + matrix[1] * z + matrix[2], matrix[3] * x + matrix[4] * z + matrix[5],
            matrix[6] * x + matrix[7] * z + matrix[8]];
    }

    private Vector3 Value(int triangle, double x, double z, int channel)
    {
        double[] barycentric = Barycentric(triangle, x, z);
        double a = barycentric[0];
        double b = barycentric[1];
        double c = barycentric[2];
        var features = Degree == 1 ? new List<double> { a, b, c }
            : new List<double> { a * (2 * a - 1), b * (2 * b - 1), c * (2 * c - 1), 4 * a * b, 4 * b * c, 4 * c * a };
        if (Bubble)
        {
            features.Add(27 * a * b * c);
        }
        double red = 0;
        double green = 0;
        double blue = 0;
        for (int feature = 0; feature < features.Count; feature++)
        {
            int offset = nodes[triangle][feature] * 6 + channel;
            red += weights[offset] * features[feature];
            green += weights[offset + 1] * features[feature];
            blue += weights[offset + 2] * features[feature];
        }
        return new((float)red, (float)green, (float)blue);
    }

}
