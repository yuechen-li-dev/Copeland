using System.Globalization;
using System.Numerics;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Lighting.Aetheris;

public sealed record LightingReceiverPlane(Vector2 Minimum, Vector2 Size, float Height = 0);

/// <summary>
/// An explicit static horizontal receiver recipe. Diffuse response has sixteen fixed directions;
/// reflections are sharp and tied to an authored eye. Arbitrary receiver UVs and glossy BRDFs are not admitted.
/// </summary>
public sealed class AetherisLightingBakeRecipe
{
    private readonly AetherisLightingScene scene;
    private readonly float[] uniformValues;
    private readonly int traceSteps;
    private readonly float surfaceHitToleranceMetres;

    public AetherisLightingBakeRecipe(AetherisLightingScene scene, LightingArtifactKind kind,
        LightingReceiverPlane receiver, Vector3 lightDirection, Vector3 reflectionEye = default,
        int traceSteps = 256, float reflectionMissRadiance = .02f, float surfaceHitToleranceMetres = .0001f)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(receiver);
        if (kind is not (LightingArtifactKind.ShadowVisibility or LightingArtifactKind.DiffuseTransfer or LightingArtifactKind.ReflectionRadiance))
        {
            throw new NotSupportedException("This field recipe admits visibility, one-bounce diffuse response and sharp reflection radiance.");
        }
        float[] domain = [receiver.Minimum.X, receiver.Minimum.Y, receiver.Size.X, receiver.Size.Y, receiver.Height,
            lightDirection.X, lightDirection.Y, lightDirection.Z, reflectionEye.X, reflectionEye.Y, reflectionEye.Z, reflectionMissRadiance];
        if (domain.Any(value => !float.IsFinite(value)) || receiver.Size.X <= 0 || receiver.Size.Y <= 0
            || Math.Abs(receiver.Minimum.X) > 16 || Math.Abs(receiver.Minimum.Y) > 16 || Math.Abs(receiver.Height) > 16
            || Math.Abs(receiver.Minimum.X + receiver.Size.X) > 16 || Math.Abs(receiver.Minimum.Y + receiver.Size.Y) > 16
            || !float.IsFinite(lightDirection.LengthSquared()) || lightDirection.LengthSquared() < .000001f
            || traceSteps is < 1 or > 256 || reflectionMissRadiance < 0
            || reflectionMissRadiance > 60000 || !float.IsFinite(surfaceHitToleranceMetres)
            || surfaceHitToleranceMetres is < .0001f or > .0005f
            || kind == LightingArtifactKind.ReflectionRadiance && reflectionEye.Y <= receiver.Height)
        {
            throw new ArgumentException("Receiver must be finite and within 16m; reflection eye must be above it; tracing and radiance must be bounded.");
        }
        this.scene = scene;
        this.traceSteps = traceSteps;
        this.surfaceHitToleranceMetres = surfaceHitToleranceMetres;
        Kind = kind;
        Vector3 light = Vector3.Normalize(lightDirection);
        Vector3 eye = kind == LightingArtifactKind.ReflectionRadiance ? reflectionEye : Vector3.Zero;
        float miss = kind == LightingArtifactKind.ReflectionRadiance ? reflectionMissRadiance : 0;
        int mode = kind switch
        {
            LightingArtifactKind.ShadowVisibility => 0,
            LightingArtifactKind.DiffuseTransfer => 1,
            _ => 2,
        };
        uniformValues = [receiver.Minimum.X, receiver.Height, receiver.Minimum.Y, 0,
            receiver.Size.X, 0, 0, 0, 0, 0, receiver.Size.Y, 0,
            mode, kind == LightingArtifactKind.DiffuseTransfer ? 16 : 1, traceSteps, 48,
            light.X, light.Y, light.Z, miss, eye.X, eye.Y, eye.Z, 0];
    }

    public LightingArtifactKind Kind { get; }

    public float[] UniformValues() => (float[])uniformValues.Clone();

    public IReadOnlyList<GpuSourceFile> Sources(Func<string, string?> runtimeSources)
    {
        var overrides = new Dictionary<string, string>
        {
            ["FieldTraceBudget.v.ts"] = "export function TraceBudget(): u32 {\n    return "
                + traceSteps.ToString(CultureInfo.InvariantCulture) + ";\n}\n",
            ["FieldSurfacePrecision.v.ts"] = "export function SurfaceHitTolerance(): f32 {\n    return "
                + surfaceHitToleranceMetres.ToString("R", CultureInfo.InvariantCulture) + ";\n}\n",
        };
        return scene.Sources("FieldBake3D.v.ts", runtimeSources, overrides);
    }

    public CompiledGraphicsProgram Compile(Func<string, string?> runtimeSources)
    {
        var module = GpuGraphicsBinder.Compile(new(Sources(runtimeSources)));
        if (!module.Success)
        {
            throw new InvalidOperationException(string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        }
        var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
        if (!backend.Vertex.SpirvValidated || !backend.Pixel.SpirvValidated)
        {
            throw new InvalidOperationException(backend.Vertex.DxcOutput + backend.Pixel.DxcOutput + backend.Pixel.SpirvValidationOutput);
        }
        return CompiledGraphicsProgramExporter.Export(module, backend);
    }

    /// <summary>The host supplies the content identity of the exact uploaded uniform bytes.</summary>
    public LightingCompilation Compilation(string id, CompiledGraphicsProgram program,
        string uniformIdentity, int width = 128, int height = 128)
    {
        return new(id, Kind,
            new(scene.Field.Program.StructuralHash, scene.MaterialIdentity, "fixed-white-directional-plus-fixed-emission",
                uniformIdentity, LightingProgramIdentity.Compute(program)), width, height,
            Kind == LightingArtifactKind.DiffuseTransfer ? 16 : 1, traceSteps,
            linearLightResponse: Kind != LightingArtifactKind.ShadowVisibility);
    }
}
