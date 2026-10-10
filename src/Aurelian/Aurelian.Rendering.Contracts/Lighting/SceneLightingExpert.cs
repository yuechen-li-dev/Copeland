using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Aurelian.Rendering.Contracts.Lighting;

public enum LightingExpertRepresentation
{
    Polynomial,
    CoarseGrid,
    Neural,
    Hybrid,
}

public sealed record LightingExpertQualification(
    string SceneKey,
    LightingExpertRepresentation Representation,
    double RootMeanSquareError,
    double GpuMilliseconds);

/// <summary>
/// Experimental, immutable decoder profile: two fixed diffuse-light bases on a visible horizontal
/// receiver in [-2.7,2.7] metres. This is not a general neural-model loader or arbitrary scene field.
/// </summary>
public sealed class SceneLightingExpert
{
    public const int Width = 32;
    public const int GridSize = 6;
    private static readonly string[] ArrayNames = ["features", "polynomial", "network", "grid", "residual"];
    private static readonly int[] ArrayLengths = [Width * 3, 6 * 6, Width * 6, GridSize * GridSize * 6, Width * 6];
    private readonly Dictionary<string, float[]> arrays;

    private SceneLightingExpert(string sceneKey, string decoderKey, string weightsKey, Dictionary<string, float[]> arrays)
    {
        SceneKey = sceneKey;
        DecoderKey = decoderKey;
        WeightsKey = weightsKey;
        this.arrays = arrays;
    }

    public string SceneKey { get; }
    public string DecoderKey { get; }
    public string WeightsKey { get; }

    public static SceneLightingExpert Load(ReadOnlyMemory<byte> json, string expectedSceneKey, string expectedDecoderKey)
    {
        try
        {
            return LoadCore(json, expectedSceneKey, expectedDecoderKey);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("Malformed scene expert manifest: " + exception.Message, exception);
        }
    }

    private static SceneLightingExpert LoadCore(ReadOnlyMemory<byte> json, string expectedSceneKey, string expectedDecoderKey)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.GetProperty("schema").GetString() != "aurelian.scene-expert/1"
            || root.GetProperty("sceneKey").GetString() != expectedSceneKey
            || root.GetProperty("decoderKey").GetString() != expectedDecoderKey
            || root.GetProperty("width").GetInt32() != Width || root.GetProperty("gridSize").GetInt32() != GridSize
            || root.GetProperty("colourSpace").GetString() != "scene-linear Rec.709"
            || root.GetProperty("domain").GetString() != "visible floor, Y=0, X/Z in [-2.7,2.7] metres"
            || root.GetProperty("basis").GetString() != "unit sun indirect RGB; unit lamp direct-plus-indirect RGB")
        {
            throw new InvalidDataException("Expert schema, scene or decoder identity does not match the consuming scene.");
        }
        var loaded = new Dictionary<string, float[]>(StringComparer.Ordinal);
        using var bytes = new MemoryStream();
        for (int index = 0; index < ArrayNames.Length; index++)
        {
            string name = ArrayNames[index];
            float[] values = root.GetProperty(name).EnumerateArray().Select(item => item.GetSingle()).ToArray();
            if (values.Length != ArrayLengths[index] || values.Any(value => !float.IsFinite(value)))
            {
                throw new InvalidDataException("Expert coefficient shape or finite-value contract failed: " + name);
            }
            loaded.Add(name, values);
            bytes.Write(MemoryMarshal.AsBytes(values.AsSpan()));
        }
        string hash = Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
        if (root.GetProperty("weightsSha256").GetString() != hash)
        {
            throw new InvalidDataException("Expert coefficient checksum failed.");
        }
        return new(expectedSceneKey, expectedDecoderKey, hash, loaded);
    }

    public float[] Coefficients(string name) => (float[])arrays[name].Clone();

    public Vector3 Evaluate(Vector2 position, LightingExpertRepresentation representation, float sun = 1, float lamp = 1)
    {
        if (!Enum.IsDefined(representation) || !float.IsFinite(position.X) || !float.IsFinite(position.Y)
            || Math.Abs(position.X) > 2.7f || Math.Abs(position.Y) > 2.7f
            || !float.IsFinite(sun) || !float.IsFinite(lamp) || sun < 0 || lamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Expert queries require the declared receiver and nonnegative finite coefficients.");
        }
        Vector2 normalized = position / 2.7f;
        Vector3 sunlight = Basis(normalized, representation, 0);
        Vector3 emission = Basis(normalized, representation, 3);
        return sunlight * sun + emission * lamp;
    }

    public static LightingExpertQualification Select(IReadOnlyList<LightingExpertQualification> candidates,
        string sceneKey, double maximumRootMeanSquareError)
    {
        return Admit(candidates, sceneKey, maximumRootMeanSquareError).FirstOrDefault()
            ?? throw new InvalidOperationException("No valid expert meets the declared measured error/cost contract.");
    }

    public static IReadOnlyList<LightingExpertQualification> Admit(IReadOnlyList<LightingExpertQualification> candidates,
        string sceneKey, double maximumRootMeanSquareError)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (!double.IsFinite(maximumRootMeanSquareError) || maximumRootMeanSquareError < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRootMeanSquareError));
        }
        return candidates
            .Where(item => item.SceneKey == sceneKey && Enum.IsDefined(item.Representation)
                && double.IsFinite(item.RootMeanSquareError) && item.RootMeanSquareError >= 0
                && item.RootMeanSquareError <= maximumRootMeanSquareError
                && double.IsFinite(item.GpuMilliseconds) && item.GpuMilliseconds > 0)
            .OrderBy(item => item.GpuMilliseconds)
            .ThenBy(item => item.RootMeanSquareError)
            .ThenBy(item => item.Representation)
            .ToArray();
    }

    private Vector3 Basis(Vector2 position, LightingExpertRepresentation representation, int channel)
    {
        Vector3 result = Vector3.Zero;
        if (representation is LightingExpertRepresentation.CoarseGrid or LightingExpertRepresentation.Hybrid)
        {
            result = Grid(position, channel);
        }
        if (representation != LightingExpertRepresentation.CoarseGrid)
        {
            string weightsName = representation switch
            {
                LightingExpertRepresentation.Polynomial => "polynomial",
                LightingExpertRepresentation.Neural => "network",
                _ => "residual",
            };
            float[] weights = arrays[weightsName];
            int count = representation == LightingExpertRepresentation.Polynomial ? 6 : Width;
            for (int index = 0; index < count; index++)
            {
                float feature = Feature(position, index);
                int offset = index * 6 + channel;
                result += new Vector3(weights[offset], weights[offset + 1], weights[offset + 2]) * feature;
            }
        }
        // Clamp each light basis before combination, preserving coefficient linearity.
        return Vector3.Max(result, Vector3.Zero);
    }

    private float Feature(Vector2 position, int index)
    {
        float x = position.X;
        float z = position.Y;
        float[] parameters = arrays["features"];
        return index switch
        {
            0 => 1,
            1 => x,
            2 => z,
            3 => x * x,
            4 => x * z,
            5 => z * z,
            _ => Math.Max(x * parameters[index * 3] + z * parameters[index * 3 + 1] + parameters[index * 3 + 2], 0),
        };
    }

    private Vector3 Grid(Vector2 position, int channel)
    {
        Vector2 coordinate = Vector2.Clamp((position + Vector2.One) * .5f * GridSize - new Vector2(.5f),
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
}
