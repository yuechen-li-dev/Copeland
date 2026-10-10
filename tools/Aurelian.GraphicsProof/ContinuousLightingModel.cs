using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aurelian.Rendering.Contracts.Lighting;

/// <summary>Bounded experimental finite-element profile; not a new production asset API.</summary>
internal sealed class ContinuousLightingModel
{
    private readonly SceneLocalLightingExpert partition;
    private readonly float[] vertices;
    private readonly int[] triangles;
    private readonly float[] weights;
    private readonly double[][] matrices;
    private readonly int[][] nodes;
    private readonly List<(int First, int Second, int Left, int Right)> sharedEdges = [];

    private ContinuousLightingModel(SceneLocalLightingExpert partition, float[] vertices, int[] triangles,
        float[] weights, JsonElement compilation, string weightsKey)
    {
        this.partition = partition;
        this.vertices = vertices;
        this.triangles = triangles;
        this.weights = weights;
        Compilation = compilation;
        WeightsKey = weightsKey;
        Degree = compilation.GetProperty("BoundaryDegree").GetInt32();
        Bubble = compilation.GetProperty("InteriorBubble").GetBoolean();
        if (Degree is not (1 or 2))
        {
            throw new InvalidDataException("Continuous boundary degree must be one or two.");
        }
        matrices = new double[TriangleCount][];
        nodes = new int[TriangleCount][];
        BuildTopology();
    }

    public JsonElement Compilation { get; }
    public string WeightsKey { get; }
    public int Degree { get; }
    public bool Bubble { get; }
    public int TriangleCount => triangles.Length / 4;
    public int VertexCount => vertices.Length / 2;
    public int CoefficientCount => weights.Length / 6;
    public int SharedEdgeCount => sharedEdges.Count;
    public int PayloadBytes => (vertices.Length + triangles.Length + weights.Length + 21 + 14) * 4;

    public static ContinuousLightingModel Load(byte[] json, SceneLocalLightingExpert partition, string decoderKey)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.GetProperty("schema").GetString() != "aurelian.continuous-expert/1"
            || root.GetProperty("sceneKey").GetString() != partition.SceneKey
            || root.GetProperty("decoderKey").GetString() != decoderKey
            || root.GetProperty("colourSpace").GetString() != "scene-linear Rec.709"
            || root.GetProperty("domain").GetString() != "visible floor, Y=0, X/Z in [-2.7,2.7] metres"
            || root.GetProperty("basis").GetString() != "unit sun indirect RGB; unit lamp direct-plus-indirect RGB")
        {
            throw new InvalidDataException("Continuous model scene/decoder/domain mismatch.");
        }
        float[] vertices = root.GetProperty("vertices").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        int[] triangles = root.GetProperty("triangles").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        float[] weights = root.GetProperty("weights").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (vertices.Length is < 6 or > 256 || vertices.Length % 2 != 0
            || triangles.Length is < 12 or > 512 || triangles.Length % 4 != 0
            || weights.Length is < 6 or > 3072 || weights.Length % 6 != 0
            || vertices.Any(value => !float.IsFinite(value) || Math.Abs(value) > 1.000001f)
            || weights.Any(value => !float.IsFinite(value)))
        {
            throw new InvalidDataException("Continuous model numeric shape/domain mismatch.");
        }
        using var bytes = new MemoryStream();
        bytes.Write(MemoryMarshal.AsBytes(vertices.AsSpan()));
        bytes.Write(MemoryMarshal.AsBytes(triangles.AsSpan()));
        bytes.Write(MemoryMarshal.AsBytes(weights.AsSpan()));
        string hash = Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
        if (root.GetProperty("weightsSha256").GetString() != hash)
        {
            throw new InvalidDataException("Continuous model payload checksum mismatch.");
        }
        JsonElement compilation = root.GetProperty("compilation").Clone();
        if (compilation.GetProperty("CoefficientCount").GetInt32() != weights.Length / 6)
        {
            throw new InvalidDataException("Continuous coefficient count mismatch.");
        }
        return new(partition, vertices, triangles, weights, compilation, hash);
    }

    public Vector3 Evaluate(Vector2 metres, float sun = 1, float lamp = 1)
    {
        if (!float.IsFinite(sun) || !float.IsFinite(lamp) || sun < 0 || lamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sun));
        }
        Vector2 point = metres / 2.7f;
        int leaf = partition.Route(point);
        for (int triangle = 0; triangle < TriangleCount; triangle++)
        {
            if (triangles[triangle * 4 + 3] != leaf)
            {
                continue;
            }
            double[] barycentric = Barycentric(triangle, point.X, point.Y);
            if (barycentric.Min() >= -.000001)
            {
                return Vector3.Max(Value(triangle, point.X, point.Y, 0), Vector3.Zero) * sun
                    + Vector3.Max(Value(triangle, point.X, point.Y, 3), Vector3.Zero) * lamp;
            }
        }
        throw new InvalidDataException("Continuous mesh does not cover the routed receiver.");
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

    public string GenerateWeights()
    {
        var source = new StringBuilder("// Native-reloaded continuous mesh; shared boundary nodes and local bubbles.\n");
        source.AppendLine("import { Add3, Scale3 } from \"./Lighting3D\";");
        for (int triangle = 0; triangle < TriangleCount; triangle++)
        {
            source.AppendLine($"function Triangle{triangle}(p: float2, channel: u32): float3 {{");
            EmitBarycentric(triangle, "    ");
            string[] features = Degree == 1 ? ["a", "b", "c"]
                : ["a * (2.0 * a - 1.0)", "b * (2.0 * b - 1.0)", "c * (2.0 * c - 1.0)",
                    "4.0 * a * b", "4.0 * b * c", "4.0 * c * a"];
            var allFeatures = features.ToList();
            if (Bubble)
            {
                allFeatures.Add("27.0 * a * b * c");
            }
            for (int index = 0; index < allFeatures.Count; index++)
            {
                source.AppendLine($"    let feature{index}: f32 = {allFeatures[index]};");
            }
            source.AppendLine("    var result: float3 = float3(0.0, 0.0, 0.0);\n    if (channel == 0) {");
            EmitChannel(0);
            source.AppendLine("    } else {");
            EmitChannel(3);
            source.AppendLine("    }\n    return result;\n}");

            void EmitChannel(int channel)
            {
                for (int index = 0; index < nodes[triangle].Length; index++)
                {
                    int offset = nodes[triangle][index] * 6 + channel;
                    string value = $"float3({Literal(weights[offset])}, {Literal(weights[offset + 1])}, {Literal(weights[offset + 2])})";
                    source.AppendLine($"        result = Add3(result, Scale3({value}, feature{index}));");
                }
            }
        }
        for (int leaf = 0; leaf < 8; leaf++)
        {
            source.AppendLine($"function Leaf{leaf}(p: float2, channel: u32): float3 {{");
            for (int triangle = 0; triangle < TriangleCount; triangle++)
            {
                if (triangles[triangle * 4 + 3] != leaf)
                {
                    continue;
                }
                source.AppendLine("    {");
                EmitBarycentric(triangle, "        ");
                source.AppendLine($"        if (a >= -0.000001 && b >= -0.000001 && c >= -0.000001) {{\n            return Triangle{triangle}(p, channel);\n        }}\n    }}");
            }
            source.AppendLine("    return float3(0.0, 0.0, 0.0);\n}");
        }
        source.AppendLine("export function ContinuousRadiance(p: float2, channel: u32): float3 {");
        EmitNode(0, "    ");
        source.AppendLine("}");
        source.AppendLine("export function ContinuousSeamDifference(p: float2): float3 {");
        for (int index = 0; index < sharedEdges.Count; index++)
        {
            var edge = sharedEdges[index];
            float bound = -1 + 2f * (index + 1) / sharedEdges.Count;
            source.AppendLine($"    if (p.y <= {Literal(bound)}) {{");
            source.AppendLine("        let t: f32 = (p.x + 1.0) * 0.5;");
            source.AppendLine($"        let q: float2 = float2({Literal(vertices[edge.First * 2])} + t * {Literal(vertices[edge.Second * 2] - vertices[edge.First * 2])}, {Literal(vertices[edge.First * 2 + 1])} + t * {Literal(vertices[edge.Second * 2 + 1] - vertices[edge.First * 2 + 1])});");
            source.AppendLine($"        let sunLeft: float3 = Triangle{edge.Left}(q, 0);\n        let sunRight: float3 = Triangle{edge.Right}(q, 0);");
            source.AppendLine($"        let lampLeft: float3 = Triangle{edge.Left}(q, 3);\n        let lampRight: float3 = Triangle{edge.Right}(q, 3);");
            source.AppendLine("        return float3(Abs(sunLeft.x - sunRight.x) + Abs(lampLeft.x - lampRight.x), Abs(sunLeft.y - sunRight.y) + Abs(lampLeft.y - lampRight.y), Abs(sunLeft.z - sunRight.z) + Abs(lampLeft.z - lampRight.z));\n    }");
        }
        source.AppendLine("    return float3(0.0, 0.0, 0.0);\n}");
        return source.ToString();

        void EmitBarycentric(int triangle, string indent)
        {
            double[] matrix = matrices[triangle];
            for (int row = 0; row < 3; row++)
            {
                string name = new[] { "a", "b", "c" }[row];
                int offset = row * 3;
                source.AppendLine($"{indent}let {name}: f32 = p.x * {Literal((float)matrix[offset])} + p.y * {Literal((float)matrix[offset + 1])} + {Literal((float)matrix[offset + 2])};");
            }
        }

        void EmitNode(int node, string indent)
        {
            if (node < 0)
            {
                source.AppendLine(indent + $"return Leaf{-node - 1}(p, channel);");
                return;
            }
            float[] planes = partition.Coefficients("planes");
            int[] children = partition.Children();
            source.AppendLine(indent + $"if (p.x * {Literal(planes[node * 3])} + p.y * {Literal(planes[node * 3 + 1])} + {Literal(planes[node * 3 + 2])} <= 0.0) {{");
            EmitNode(children[node * 2], indent + "    ");
            source.AppendLine(indent + "} else {");
            EmitNode(children[node * 2 + 1], indent + "    ");
            source.AppendLine(indent + "}");
        }
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

    private static string Literal(float value)
    {
        string result = value.ToString("R", CultureInfo.InvariantCulture);
        return result.Contains('.') || result.Contains('E') ? result : result + ".0";
    }
}
