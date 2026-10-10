using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Aurelian.Rendering.Contracts.Lighting;

/// <summary>Experimental eight-leaf cubic decoder and two storage controls for the fixed diffuse receiver.</summary>
public sealed class SceneLocalLightingExpert
{
    public const int Leaves = 8;
    public const int Features = 10;
    public const int GridSize = 10;
    private static readonly string[] ArrayNames =
        ["planes", "children", "transforms", "weights", "uniformTransforms", "uniformWeights", "grid"];
    private readonly Dictionary<string, float[]> arrays;
    private readonly int[] children;

    private SceneLocalLightingExpert(string sceneKey, string decoderKey, string weightsKey,
        Dictionary<string, float[]> arrays, int[] children)
    {
        SceneKey = sceneKey;
        DecoderKey = decoderKey;
        WeightsKey = weightsKey;
        this.arrays = arrays;
        this.children = children;
    }

    public string SceneKey { get; }
    public string DecoderKey { get; }
    public string WeightsKey { get; }
    public float[] Coefficients(string name) => (float[])arrays[name].Clone();
    public int[] Children() => (int[])children.Clone();

    public static SceneLocalLightingExpert Load(ReadOnlyMemory<byte> json, string sceneKey, string decoderKey)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "aurelian.local-expert/1"
                || root.GetProperty("sceneKey").GetString() != sceneKey
                || root.GetProperty("decoderKey").GetString() != decoderKey
                || root.GetProperty("leaves").GetInt32() != Leaves
                || root.GetProperty("features").GetInt32() != Features
                || root.GetProperty("gridSize").GetInt32() != GridSize
                || root.GetProperty("colourSpace").GetString() != "scene-linear Rec.709"
                || root.GetProperty("domain").GetString() != "visible floor, Y=0, X/Z in [-2.7,2.7] metres"
                || root.GetProperty("basis").GetString() != "unit sun indirect RGB; unit lamp direct-plus-indirect RGB")
            {
                throw new InvalidDataException("Local expert identity, domain or decoder profile mismatch.");
            }
            int[] children = root.GetProperty("children").EnumerateArray().Select(item => item.GetInt32()).ToArray();
            if (children.Length != (Leaves - 1) * 2)
            {
                throw new InvalidDataException("Local expert routing shape mismatch.");
            }
            var arrays = new Dictionary<string, float[]>(StringComparer.Ordinal);
            using var bytes = new MemoryStream();
            foreach (string name in ArrayNames)
            {
                if (name == "children")
                {
                    bytes.Write(MemoryMarshal.AsBytes(children.AsSpan()));
                    continue;
                }
                int length = name switch
                {
                    "planes" => (Leaves - 1) * 3,
                    "transforms" or "uniformTransforms" => Leaves * 4,
                    "grid" => GridSize * GridSize * 6,
                    _ => Leaves * Features * 6,
                };
                float[] values = root.GetProperty(name).EnumerateArray().Select(item => item.GetSingle()).ToArray();
                if (values.Length != length || values.Any(value => !float.IsFinite(value)))
                {
                    throw new InvalidDataException("Local coefficient shape/finite contract failed: " + name);
                }
                arrays.Add(name, values);
                bytes.Write(MemoryMarshal.AsBytes(values.AsSpan()));
            }
            string hash = Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
            if (root.GetProperty("weightsSha256").GetString() != hash)
            {
                throw new InvalidDataException("Local expert coefficient/routing checksum failed.");
            }
            ValidateTree(children, arrays["planes"]);
            foreach (string name in new[] { "transforms", "uniformTransforms" })
            {
                for (int leaf = 0; leaf < Leaves; leaf++)
                {
                    if (arrays[name][leaf * 4 + 2] <= 0 || arrays[name][leaf * 4 + 3] <= 0)
                    {
                        throw new InvalidDataException("Local expert coordinate scales must be positive.");
                    }
                }
            }
            return new(sceneKey, decoderKey, hash, arrays, children);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("Malformed local expert manifest: " + exception.Message, exception);
        }
    }

    public int Route(Vector2 normalized)
    {
        if (!float.IsFinite(normalized.X) || !float.IsFinite(normalized.Y)
            || Math.Abs(normalized.X) > 1 || Math.Abs(normalized.Y) > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(normalized), "Routing requires the normalized receiver domain.");
        }
        int node = 0;
        float[] planes = arrays["planes"];
        for (int depth = 0; depth < Leaves; depth++)
        {
            if (node < 0)
            {
                return -node - 1;
            }
            int offset = node * 3;
            float side = normalized.X * planes[offset] + normalized.Y * planes[offset + 1] + planes[offset + 2];
            node = children[node * 2 + (side <= 0 ? 0 : 1)];
        }
        throw new InvalidDataException("Validated local routing did not reach a leaf.");
    }

    public Vector3 Evaluate(Vector2 position, LightingExpertRepresentation representation, float sun = 1, float lamp = 1)
    {
        if (representation is < LightingExpertRepresentation.GeometryLocal or > LightingExpertRepresentation.MatchedGrid
            || !float.IsFinite(position.X) || !float.IsFinite(position.Y)
            || Math.Abs(position.X) > 2.7f || Math.Abs(position.Y) > 2.7f
            || !float.IsFinite(sun) || !float.IsFinite(lamp) || sun < 0 || lamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Invalid local receiver query or light scale.");
        }
        Vector2 normalized = position / 2.7f;
        return Basis(normalized, representation, 0) * sun + Basis(normalized, representation, 3) * lamp;
    }

    private Vector3 Basis(Vector2 point, LightingExpertRepresentation representation, int channel)
    {
        if (representation == LightingExpertRepresentation.MatchedGrid)
        {
            return Vector3.Max(Grid(point, channel), Vector3.Zero);
        }
        bool geometry = representation == LightingExpertRepresentation.GeometryLocal;
        int leaf;
        if (geometry)
        {
            leaf = Route(point);
        }
        else
        {
            int column = Math.Clamp((int)((point.X + 1) * 2), 0, 3);
            int row = Math.Clamp((int)(point.Y + 1), 0, 1);
            leaf = row * 4 + column;
        }
        float[] transforms = arrays[geometry ? "transforms" : "uniformTransforms"];
        float[] weights = arrays[geometry ? "weights" : "uniformWeights"];
        int offset = leaf * 4;
        float x = (point.X - transforms[offset]) * transforms[offset + 2];
        float z = (point.Y - transforms[offset + 1]) * transforms[offset + 3];
        ReadOnlySpan<float> features = [1, x, z, x * x, x * z, z * z, x * x * x, x * x * z, x * z * z, z * z * z];
        Vector3 result = Vector3.Zero;
        for (int index = 0; index < Features; index++)
        {
            int weight = (leaf * Features + index) * 6 + channel;
            result += new Vector3(weights[weight], weights[weight + 1], weights[weight + 2]) * features[index];
        }
        return Vector3.Max(result, Vector3.Zero);
    }

    private Vector3 Grid(Vector2 point, int channel)
    {
        Vector2 coordinate = Vector2.Clamp((point + Vector2.One) * (GridSize * .5f) - new Vector2(.5f),
            Vector2.Zero, new Vector2(GridSize - 1));
        int x = (int)coordinate.X;
        int z = (int)coordinate.Y;
        int right = Math.Min(x + 1, GridSize - 1);
        int top = Math.Min(z + 1, GridSize - 1);
        Vector3 lower = Vector3.Lerp(Read(x, z), Read(right, z), coordinate.X - x);
        Vector3 upper = Vector3.Lerp(Read(x, top), Read(right, top), coordinate.X - x);
        return Vector3.Lerp(lower, upper, coordinate.Y - z);

        Vector3 Read(int column, int row)
        {
            int offset = (row * GridSize + column) * 6 + channel;
            float[] values = arrays["grid"];
            return new(values[offset], values[offset + 1], values[offset + 2]);
        }
    }

    private static void ValidateTree(int[] children, float[] planes)
    {
        var visited = new HashSet<int>();
        var leaves = new HashSet<int>();
        Visit(0);
        if (visited.Count != Leaves - 1 || leaves.Count != Leaves)
        {
            throw new InvalidDataException("Local routing contains unreachable nodes or leaves.");
        }

        void Visit(int node)
        {
            if (node < 0)
            {
                if (node < -Leaves || !leaves.Add(-node - 1))
                {
                    throw new InvalidDataException("Local routing repeats or exceeds its declared leaves.");
                }
                return;
            }
            if (node >= Leaves - 1 || !visited.Add(node))
            {
                throw new InvalidDataException("Local routing contains a cycle, shared node or invalid index.");
            }
            float a = planes[node * 3];
            float b = planes[node * 3 + 1];
            if (Math.Abs(a * a + b * b - 1) > .001f)
            {
                throw new InvalidDataException("Local split normals must be normalized.");
            }
            Visit(children[node * 2]);
            Visit(children[node * 2 + 1]);
        }
    }
}
