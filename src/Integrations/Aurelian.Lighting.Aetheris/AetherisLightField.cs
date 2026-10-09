using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Aetheris.Continuum.Backends.Sdf;
using Aetheris.Kernel.Core.Math;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Lighting.Aetheris;

/// <summary>
/// Explicit mm/Z-up to metre/Y-up adapter. Aetheris owns field evaluation and
/// source lowering; Aurelian owns compilation, visibility policy and rendering.
/// </summary>
public sealed class AetherisLightField
{
    private readonly SdfTape tape;

    private AetherisLightField(SdfNode root, CirVisualTsProgram program)
    {
        Program = program;
        tape = SdfTapeLowerer.Lower(root);
        WorldSource = "// AGPL-3.0-only: generated from Aetheris.Fields; retain source and provenance.\n"
            + "// CIR structural identity: " + program.StructuralHash + "\n"
            + program.FieldSource
            + "\nexport function FieldWorld(p: float3): f32 {\n"
            + "    return Field(p.x * 1000.0, -p.z * 1000.0, p.y * 1000.0) * 0.001;\n}\n";
        WorldSourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WorldSource))).ToLowerInvariant();
    }

    public CirVisualTsProgram Program { get; }
    public string WorldSource { get; }
    public string WorldSourceSha256 { get; }

    public static AetherisLightField Create(SdfNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        int nodeCount = 0;
        ValidateBounds(root, 0, ref nodeCount);
        CirVisualTsLoweringResult result = CirVisualTsLowerer.Lower(root);
        if (!result.Success || !result.Program!.ConservativeExteriorStep)
        {
            throw new ArgumentException(result.FallbackReason ?? "Field has no admitted exterior step bound.", nameof(root));
        }
        return new(root, result.Program);
    }

    public double EvaluateMetres(Vector3 point)
    {
        if (!Finite(point) || Math.Abs(point.X) > 64 || Math.Abs(point.Y) > 64 || Math.Abs(point.Z) > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "Field query coordinates must be finite and within 64 metres.");
        }
        return tape.Evaluate(new Point3D(point.X * 1000.0, -point.Z * 1000.0, point.Y * 1000.0)) * .001;
    }

    /// <summary>Produces real VTS modules, including the AGPL-derived generated field.</summary>
    public IReadOnlyList<GpuSourceFile> Sources(string root, Func<string, string?> runtimeSource)
    {
        return GpuSourceLoader.Load(root, name =>
        {
            if (name == "AetherisField.v.ts")
            {
                return WorldSource;
            }
            return AssetSource(name) ?? runtimeSource(name);
        });
    }

    public static string? AssetSource(string name)
    {
        var assembly = typeof(AetherisLightField).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream("Aurelian.Lighting.Aetheris.Assets." + name);
        if (stream is null)
        {
            return null;
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string LicenseText(string name)
    {
        if (name is not ("GPL-3.0" or "AGPL-3.0"))
        {
            throw new ArgumentException("Unknown field integration licence.", nameof(name));
        }
        using Stream stream = typeof(AetherisLightField).Assembly.GetManifestResourceStream(
            "Aurelian.Lighting.Aetheris." + name + ".txt")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void ValidateBounds(SdfNode node, int depth, ref int nodeCount)
    {
        nodeCount++;
        if (nodeCount > 128)
        {
            throw new ArgumentException("Field tree exceeds the bounded lighting count of 128 nodes.");
        }
        if (depth > 16)
        {
            throw new ArgumentException("Field tree exceeds the bounded lighting depth of 16.");
        }
        switch (node)
        {
            case SdfTransformNode transform:
                ValidateBounds(transform.Child, depth + 1, ref nodeCount);
                break;
            case SdfUnionNode union:
                ValidateBounds(union.Left, depth + 1, ref nodeCount);
                ValidateBounds(union.Right, depth + 1, ref nodeCount);
                break;
            case SdfSubtractNode subtract:
                ValidateBounds(subtract.Left, depth + 1, ref nodeCount);
                ValidateBounds(subtract.Right, depth + 1, ref nodeCount);
                break;
            case SdfIntersectNode intersect:
                ValidateBounds(intersect.Left, depth + 1, ref nodeCount);
                ValidateBounds(intersect.Right, depth + 1, ref nodeCount);
                break;
        }
        SdfBounds bounds = node.Bounds;
        double[] coordinates = [bounds.Min.X, bounds.Min.Y, bounds.Min.Z, bounds.Max.X, bounds.Max.Y, bounds.Max.Z];
        if (coordinates.Any(value => !double.IsFinite(value) || Math.Abs(value) > 32_000))
        {
            throw new ArgumentException("Lighting field bounds must be finite and within 32 metres in source millimetres.");
        }
    }

    private static bool Finite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
