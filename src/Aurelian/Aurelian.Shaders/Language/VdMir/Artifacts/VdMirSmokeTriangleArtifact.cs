using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aurelian.Shaders.Language.Ast;
using Aurelian.Shaders.Language.Artifacts.Files;
using Aurelian.Shaders.Language.Artifacts.Spirv;
using Aurelian.Shaders.Language.Diagnostics;
using Aurelian.Shaders.Language.Parsing;
using Aurelian.Shaders.Language.Validation;
using Aurelian.Shaders.Language.VdMir.Emission.Hlsl;
using Aurelian.Shaders.Language.VdMir.Lowering;

namespace Aurelian.Shaders.Language.VdMir.Artifacts;

public sealed record VdMirSmokeTriangleProof(
    string SourceName,
    string SourceSha256,
    SdslvModule? ParsedModule,
    IReadOnlyList<SdslvDiagnostic> SourceDiagnostics,
    VdMirModule? VdMirModule,
    IReadOnlyList<VdMirDiagnostic> VdMirDiagnostics,
    string Hlsl,
    SpirvShaderArtifact? SpirvArtifact,
    string VdMirHlslEmissionProofStatus,
    string VdMirDxcSpirvProofStatus)
{
    public bool SourceSucceeded => ParsedModule is not null && SourceDiagnostics.All(x => x.Severity != SdslvDiagnosticSeverity.Error);
    public bool VdMirSucceeded => VdMirModule is not null && VdMirDiagnostics.All(x => x.Severity != SdslvDiagnosticSeverity.Error);
}

public static class VdMirSmokeTriangleArtifact
{
    public static VdMirSmokeTriangleProof CompileFromSource(string sourceText, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var effectiveSourceName = string.IsNullOrWhiteSpace(sourceName) ? "smoke_triangle.sdslv" : sourceName;
        var sourceSha256 = ComputeSha256Utf8(sourceText);
        var parse = SdslvParser.ParseModule(sourceText);
        if (!parse.Success || parse.Module is null)
        {
            return new VdMirSmokeTriangleProof(
                effectiveSourceName,
                sourceSha256,
                parse.Module,
                parse.Diagnostics,
                null,
                [],
                string.Empty,
                null,
                "parse-blocked",
                "parse-blocked");
        }

        var validation = SdslvValidator.ValidateModule(parse.Module);
        var sourceDiagnostics = parse.Diagnostics.Concat(validation.Diagnostics).ToArray();
        if (!validation.Success)
        {
            return new VdMirSmokeTriangleProof(
                effectiveSourceName,
                sourceSha256,
                parse.Module,
                sourceDiagnostics,
                null,
                [],
                string.Empty,
                null,
                "validation-blocked",
                "validation-blocked");
        }

        var lowered = VdMirM0Lowerer.LowerModule(parse.Module);
        var loweredDiagnostics = lowered.Diagnostics.ToArray();
        if (!lowered.Success)
        {
            return new VdMirSmokeTriangleProof(
                effectiveSourceName,
                sourceSha256,
                parse.Module,
                sourceDiagnostics,
                lowered,
                loweredDiagnostics,
                string.Empty,
                null,
                "lowering-failed",
                "lowering-failed");
        }

        var emission = VdMirHlslEmitter.EmitModule(lowered);
        if (!emission.Success)
        {
            return new VdMirSmokeTriangleProof(
                effectiveSourceName,
                sourceSha256,
                parse.Module,
                sourceDiagnostics,
                lowered,
                emission.Diagnostics,
                emission.Hlsl,
                null,
                "emission-failed",
                "emission-failed");
        }

        var stages = BuildStageSources(emission.Hlsl, effectiveSourceName, lowered.EntryPoints);
        var spirvArtifact = SpirvShaderArtifactEmitter.EmitFromHlslStages(stages);
        var dxcStatus = spirvArtifact.Diagnostics.Any(x => x.Code == SpirvShaderArtifactDiagnosticCodes.DxcUnavailable)
            ? "dxc-unavailable"
            : spirvArtifact.Success ? "compiled" : "compilation-failed";

        return new VdMirSmokeTriangleProof(
            effectiveSourceName,
            sourceSha256,
            parse.Module,
            sourceDiagnostics,
            lowered,
            emission.Diagnostics,
            emission.Hlsl,
            spirvArtifact,
            "emitted",
            dxcStatus);
    }

    public static VdMirArtifactWriteResult WriteArtifacts(string sourceText, string sourceName, string outputDirectory)
    {
        var proof = CompileFromSource(sourceText, sourceName);
        return WriteArtifacts(proof, outputDirectory);
    }

    public static VdMirArtifactWriteResult WriteArtifacts(VdMirSmokeTriangleProof proof, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);

        var proofArtifacts = new List<string>();
        var diagnosticsJsonPath = Path.Combine(fullOutputDirectory, "vd-mir-smoke-triangle-diagnostics.json");
        File.WriteAllText(diagnosticsJsonPath, BuildDiagnosticsJson(proof), Encoding.UTF8);
        proofArtifacts.Add(diagnosticsJsonPath);

        string? hlslPath = null;
        if (!string.IsNullOrWhiteSpace(proof.Hlsl))
        {
            hlslPath = Path.Combine(fullOutputDirectory, "vd-mir-smoke-triangle.hlsl");
            File.WriteAllText(hlslPath, proof.Hlsl, Encoding.UTF8);
            proofArtifacts.Add(hlslPath);
        }

        string? vertexSpirvPath = null;
        string? pixelSpirvPath = null;
        if (proof.SpirvArtifact?.Success == true)
        {
            foreach (var stage in proof.SpirvArtifact.Stages.OrderBy(x => x.Stage).ThenBy(x => x.EntryPoint, StringComparer.Ordinal))
            {
                var fileName = stage.Stage switch
                {
                    HlslShaderStageKind.Vertex => "vd-mir-smoke-triangle.vs.spv.hex",
                    HlslShaderStageKind.Fragment => "vd-mir-smoke-triangle.ps.spv.hex",
                    _ => null,
                };

                if (fileName is null)
                {
                    continue;
                }

                var path = Path.Combine(fullOutputDirectory, fileName);
                File.WriteAllText(path, ShaderArtifactSpirvEncoding.EncodeHex(stage.SpirvBytes), Encoding.UTF8);
                proofArtifacts.Add(path);
                if (stage.Stage == HlslShaderStageKind.Vertex)
                {
                    vertexSpirvPath = path;
                }
                else if (stage.Stage == HlslShaderStageKind.Fragment)
                {
                    pixelSpirvPath = path;
                }
            }
        }

        var manifestJsonPath = Path.Combine(fullOutputDirectory, "vd-mir-m0-smoke-triangle-manifest.json");
        var manifestTxtPath = Path.Combine(fullOutputDirectory, "vd-mir-m0-smoke-triangle-manifest.txt");
        var manifest = BuildManifest(
            proof,
            proofArtifacts
                .Select(path => NormalizeSlashes(Path.GetRelativePath(fullOutputDirectory, path)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray());

        File.WriteAllText(manifestJsonPath, JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine, Encoding.UTF8);
        File.WriteAllText(manifestTxtPath, BuildManifestText(manifest), Encoding.UTF8);

        return new VdMirArtifactWriteResult(
            fullOutputDirectory,
            manifestJsonPath,
            manifestTxtPath,
            hlslPath,
            vertexSpirvPath,
            pixelSpirvPath,
            diagnosticsJsonPath);
    }

    private static IReadOnlyList<HlslShaderStageSource> BuildStageSources(
        string hlsl,
        string sourceName,
        IReadOnlyList<VdMirEntryPoint> entryPoints)
    {
        var sources = new List<HlslShaderStageSource>();
        foreach (var entryPoint in entryPoints.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            switch (entryPoint.Stage)
            {
                case VdMirStageKind.Vertex:
                    sources.Add(new HlslShaderStageSource(HlslShaderStageKind.Vertex, hlsl, entryPoint.Name, "vs_6_0", ToHlslSourceName(sourceName)));
                    break;
                case VdMirStageKind.Pixel:
                    sources.Add(new HlslShaderStageSource(HlslShaderStageKind.Fragment, hlsl, entryPoint.Name, "ps_6_0", ToHlslSourceName(sourceName)));
                    break;
            }
        }

        return sources;
    }

    private static object BuildManifest(VdMirSmokeTriangleProof proof, IReadOnlyList<string> proofArtifacts)
    {
        var validationStatus = proof.SourceSucceeded && proof.VdMirSucceeded
            ? "validated"
            : proof.SourceSucceeded ? "vd-mir-failed" : "source-failed";

        return new
        {
            milestone = "M14a",
            kind = "vd-mir-m0-smoke-triangle-compiler-slice",
            vdMirImplemented = true,
            vdMirScope = "M0 smoke triangle",
            implementationLocation = "src/Aurelian/Aurelian.Shaders/Language/VdMir",
            copelandPackageCreated = false,
            sdslvMigrationPerformed = false,
            directHlslPathPreserved = true,
            visibleTriangleWiredToVdMir = false,
            hlslBackendChangedDefaultBehavior = false,
            vdMirHlslEmissionProofStatus = proof.VdMirHlslEmissionProofStatus,
            vdMirDxcSpirvProofStatus = proof.VdMirDxcSpirvProofStatus,
            ptxBackendImplemented = false,
            slangBackendImplemented = false,
            shaderKernelMirSplitPerformed = false,
            machinaAurelianBridgeImplemented = false,
            vulkanPresenterIntegrationPerformed = false,
            repoRenamed = false,
            proofArtifacts,
            validationStatus,
            deferredWork = new[]
            {
                "Wire Aurelian.VisibleTriangle to VD-MIR only in a later milestone.",
                "Keep the direct AST-to-HLSL path as the default behavior until a broader migration is earned.",
                "Defer Slang, PTX, compute/kernel concepts, resource models, and optimization passes beyond M14a.",
            },
        };
    }

    private static string BuildManifestText(object manifest)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(manifest, JsonOptions));
        var lines = new List<string>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            lines.Add($"{property.Name}: {FormatJsonElement(property.Value)}");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildDiagnosticsJson(VdMirSmokeTriangleProof proof)
    {
        var payload = new
        {
            source = new
            {
                proof.SourceName,
                proof.SourceSha256,
                diagnostics = proof.SourceDiagnostics.Select(diagnostic => new
                {
                    diagnostic.Code,
                    Severity = diagnostic.Severity.ToString(),
                    Phase = diagnostic.Phase.ToString(),
                    diagnostic.Message,
                    diagnostic.Span,
                }),
            },
            vdMir = new
            {
                diagnostics = proof.VdMirDiagnostics.Select(diagnostic => new
                {
                    diagnostic.Code,
                    Severity = diagnostic.Severity.ToString(),
                    diagnostic.Message,
                    diagnostic.Span,
                }),
                entryPoints = proof.VdMirModule?.EntryPoints.Select(entryPoint => new
                {
                    entryPoint.Name,
                    Stage = entryPoint.Stage.ToString(),
                    Span = entryPoint.Span,
                }) ?? [],
            },
            hlsl = new
            {
                proof.VdMirHlslEmissionProofStatus,
                length = proof.Hlsl.Length,
            },
            spirv = new
            {
                proof.VdMirDxcSpirvProofStatus,
                stages = proof.SpirvArtifact?.Stages.Select(stage => new
                {
                    Stage = stage.Stage.ToString(),
                    stage.EntryPoint,
                    stage.Profile,
                    stage.SourceSha256,
                    stage.SpirvSha256,
                }) ?? [],
                diagnostics = proof.SpirvArtifact?.Diagnostics.Select(diagnostic => new
                {
                    diagnostic.Code,
                    Severity = diagnostic.Severity.ToString(),
                    diagnostic.Message,
                }) ?? [],
            },
        };

        return JsonSerializer.Serialize(payload, JsonOptions) + Environment.NewLine;
    }

    private static string FormatJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => "[" + string.Join(", ", element.EnumerateArray().Select(FormatJsonElement)) + "]",
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.Object => "{...}",
            JsonValueKind.Null => "null",
            _ => element.GetRawText(),
        };
    }

    private static string ToHlslSourceName(string sourceName) =>
        Path.ChangeExtension(sourceName, ".hlsl") ?? "smoke_triangle.hlsl";

    private static string ComputeSha256Utf8(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string NormalizeSlashes(string path) =>
        path.Replace('\\', '/');

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

public sealed record VdMirArtifactWriteResult(
    string OutputDirectory,
    string ManifestJsonPath,
    string ManifestTextPath,
    string? HlslPath,
    string? VertexSpirvPath,
    string? PixelSpirvPath,
    string DiagnosticsJsonPath);
