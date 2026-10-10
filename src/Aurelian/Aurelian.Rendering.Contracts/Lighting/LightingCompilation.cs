using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Rendering.Contracts.Shaders;

namespace Aurelian.Rendering.Contracts.Lighting;

public enum LightingArtifactKind
{
    ShadowVisibility,
    DiffuseTransfer,
    ReflectionRadiance,
    BrdfIntegral,
}

/// <summary>Content identities, not mutable scene names. Intensity and colour are excluded only for linear responses.</summary>
public sealed record LightingCompileInputs(
    string Geometry,
    string Materials,
    string LightLayout,
    string ReceiverDomain,
    string ShaderSources);

/// <summary>An explicit finite compilation domain. Changing any baked input produces another artifact.</summary>
public sealed record LightingCompilation
{
    public LightingCompilation(string id, LightingArtifactKind kind, LightingCompileInputs inputs,
        int width, int height, int samplesPerTexel, int traceSteps, bool linearLightResponse = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(inputs);
        string[] identities = [inputs.Geometry, inputs.Materials, inputs.LightLayout, inputs.ReceiverDomain, inputs.ShaderSources];
        if (identities.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Every baked dependency needs an explicit content identity.", nameof(inputs));
        }
        if (!Enum.IsDefined(kind) || width is < 1 or > 1024 || height is < 1 or > 1024
            || samplesPerTexel is < 1 or > 256 || traceSteps is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Compilation extent, sampling and tracing must be bounded.");
        }
        Id = id;
        Kind = kind;
        Inputs = inputs;
        Width = width;
        Height = height;
        SamplesPerTexel = samplesPerTexel;
        TraceSteps = traceSteps;
        LinearLightResponse = linearLightResponse;
        // Slot names are deliberately excluded: two consumers can refer to the same artifact.
        byte[] source = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Schema = "aurelian.lighting.compilation/1", Kind, Inputs, Width, Height,
            SamplesPerTexel, TraceSteps, LinearLightResponse,
        });
        ContentKey = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
    }

    public string Id { get; }
    public LightingArtifactKind Kind { get; }
    public LightingCompileInputs Inputs { get; }
    public int Width { get; }
    public int Height { get; }
    public int SamplesPerTexel { get; }
    public int TraceSteps { get; }
    public bool LinearLightResponse { get; }
    public string ContentKey { get; }
    public long EstimatedRayCount => checked((long)Width * Height * SamplesPerTexel
        * (Kind is LightingArtifactKind.DiffuseTransfer or LightingArtifactKind.ReflectionRadiance ? 2 : 1));
}

public sealed record LightingBakeRequest(LightingCompilation Compilation, long Generation, uint PlantId);

/// <summary>A completion identity is local to a plant. A counter from another plant cannot satisfy it.</summary>
public sealed record LightingBakeTicket(uint PlantId, ulong CompletionValue, string ContentKey, long Generation,
    string CompletionDomain = "command-list");

public sealed record LightingBakeCompletion(LightingBakeTicket Ticket, bool Success, bool FullyResolved,
    double Milliseconds, string? Diagnostic = null);

public sealed record LightingPlantAvailability(uint PlantId, bool Available,
    IReadOnlySet<LightingArtifactKind> SupportedArtifacts, double EstimatedMillisecondsPerRay);

public sealed record LightingScheduleBudget(long MaximumRays, double MaximumEstimatedMilliseconds);

/// <summary>Retain semantic identity and the exact executable bytes, including backend changes.</summary>
public static class LightingProgramIdentity
{
    public static string Compute(CompiledGraphicsProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("aurelian.lighting.program/1");
            writer.Write(program.FormatVersion);
            writer.Write(program.CompilerProfile);
            writer.Write(program.VdMirSha256);
            writer.Write(program.Shaders.Stages.Count);
            foreach (CompiledShaderStage stage in program.Shaders.Stages.OrderBy(item => item.Stage))
            {
                writer.Write((int)stage.Stage);
                writer.Write(stage.EntryPoint);
                writer.Write(stage.Profile);
                writer.Write(stage.SpirvBytes.Length);
                writer.Write(stage.SpirvBytes);
            }
        }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
    }
}

/// <summary>Runtime coefficients for an explicitly compiled linear response, in scene-linear units.</summary>
public static class LightingResponse
{
    public static Vector3 Apply(LightingCompilation compilation, Vector3 response, Vector3 lightColour, float intensity)
    {
        if (!compilation.LinearLightResponse)
        {
            throw new InvalidOperationException("This artifact does not declare a linear light response.");
        }
        float[] values = [response.X, response.Y, response.Z, lightColour.X, lightColour.Y, lightColour.Z, intensity];
        if (values.Any(value => !float.IsFinite(value) || value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(response), "Radiance and light coefficients must be finite and nonnegative.");
        }
        return response * lightColour * intensity;
    }
}
