using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Aetheris.Continuum.Backends.Sdf;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Lighting.Aetheris;

/// <summary>Explicit static opaque surfaces. One material owns each analytic body, including its Boolean cuts.</summary>
public sealed record AetherisLightingBody(string Id, SdfNode Geometry, Vector3 Albedo, Vector3 Emission);

/// <summary>
/// Reuses Aetheris lowering for geometry. Material selection and generated source remain Aurelian policy.
/// At union boundaries the closest body boundary owns the hit; exact ties use declaration order.
/// </summary>
public sealed class AetherisLightingScene
{
    private readonly Dictionary<string, string> sources = new(StringComparer.Ordinal);

    public AetherisLightingScene(IEnumerable<AetherisLightingBody> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        AetherisLightingBody[] bodies = declarations.ToArray();
        if (bodies.Length is < 1 or > 16 || bodies.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != bodies.Length)
        {
            throw new ArgumentException("Lighting requires between one and sixteen uniquely named bodies.");
        }
        SdfNode root = bodies[0].Geometry;
        var material = new StringBuilder("// GPL-3.0-only: authored surface ownership and material constants.\n");
        for (int index = 0; index < bodies.Length; index++)
        {
            AetherisLightingBody body = bodies[index];
            ArgumentException.ThrowIfNullOrWhiteSpace(body.Id);
            float[] values = [body.Albedo.X, body.Albedo.Y, body.Albedo.Z, body.Emission.X, body.Emission.Y, body.Emission.Z];
            if (values.Any(value => !float.IsFinite(value) || value < 0) || values.Take(3).Any(value => value > 1))
            {
                throw new ArgumentException("Opaque Lambertian albedo must be in [0,1]; emission must be finite and nonnegative.");
            }
            var field = AetherisLightField.Create(body.Geometry);
            sources["LightingBody" + index + ".v.ts"] = field.WorldSource
                + "\nexport function Body" + index + "(p: float3): f32 {\n    return FieldWorld(p);\n}\n";
            material.AppendLine($"import {{ Body{index} }} from \"./LightingBody{index}\";");
            if (index > 0)
            {
                root = new SdfUnionNode(root, body.Geometry);
            }
        }
        Field = AetherisLightField.Create(root);
        material.AppendLine("export function SurfaceId(p: float3): f32 {");
        material.AppendLine("    var nearest: f32 = Abs(Body0(p));");
        material.AppendLine("    var identity: f32 = 0.0;");
        for (int index = 1; index < bodies.Length; index++)
        {
            material.AppendLine($"    const distance{index}: f32 = Abs(Body{index}(p));");
            material.AppendLine($"    if (distance{index} < nearest) {{");
            material.AppendLine($"        nearest = distance{index};");
            material.AppendLine($"        identity = {index}.0;");
            material.AppendLine("    }");
        }
        material.AppendLine("    return identity;\n}");
        AppendProperty("SurfaceAlbedo", body => body.Albedo);
        AppendProperty("SurfaceEmission", body => body.Emission);
        sources["LightingSurfaces.v.ts"] = material.ToString();
        MaterialIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString()))).ToLowerInvariant();

        void AppendProperty(string name, Func<AetherisLightingBody, Vector3> property)
        {
            material.AppendLine($"export function {name}(p: float3): float3 {{");
            material.AppendLine("    const identity: f32 = SurfaceId(p);");
            for (int index = 0; index < bodies.Length; index++)
            {
                Vector3 value = property(bodies[index]);
                material.AppendLine($"    if (identity == {index}.0) {{");
                material.AppendLine($"        return float3({Literal(value.X)}, {Literal(value.Y)}, {Literal(value.Z)});");
                material.AppendLine("    }");
            }
            material.AppendLine("    return float3(0.0, 0.0, 0.0);\n}");
        }
    }

    public AetherisLightField Field { get; }
    public string MaterialIdentity { get; }

    public IReadOnlyList<GpuSourceFile> Sources(string root, Func<string, string?> runtimeSources,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        return GpuSourceLoader.Load(root, name => overrides?.GetValueOrDefault(name)
            ?? sources.GetValueOrDefault(name)
            ?? (name == "AetherisField.v.ts" ? Field.WorldSource : null)
            ?? AetherisLightField.AssetSource(name) ?? runtimeSources(name));
    }

    private static string Literal(float value)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        if (!text.Contains('.') && !text.Contains('E'))
        {
            text += ".0";
        }
        return text;
    }
}
