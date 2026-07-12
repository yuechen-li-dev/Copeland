using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Aurelian.Shaders.Diagnostics;
using Aurelian.Shaders.Lowering;
using Aurelian.Shaders.Parsing;

namespace Aurelian.Shaders.Artifacts;

public sealed class ShaderArtifactEmitter
{
    public ShaderArtifactManifest Emit(ShaderArtifactOptions options)
    {
        Directory.CreateDirectory(options.OutputRoot);
        var generatedDir = Path.Combine(options.OutputRoot, "generated");
        var binDir = Path.Combine(options.OutputRoot, "bin");
        var logsDir = Path.Combine(options.OutputRoot, "logs");
        Directory.CreateDirectory(generatedDir);
        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(logsDir);

        var parsed = new ShaderParser().ParseSdslDocument(options.SourceText);
        var lowered = parsed.Document is null
            ? new LoweringResult(string.Empty, [], new StreamUsageAnalysisResult([], [], []), new StageIoLayout([], [], [], []))
            : new ShaderLowerer().LowerSdslDocumentToHlsl(parsed.Document, options.EntryShader, new ShaderSpecialization(options.BoolSpecialization));

        var vertexHlsl = Path.Combine(generatedDir, "vertex.hlsl");
        var pixelHlsl = Path.Combine(generatedDir, "pixel.hlsl");
        File.WriteAllText(vertexHlsl, lowered.Hlsl, new UTF8Encoding(false));
        File.WriteAllText(pixelHlsl, lowered.Hlsl, new UTF8Encoding(false));

        var diagnostics = new List<ShaderArtifactDiagnosticRecord>();
        diagnostics.AddRange(parsed.Diagnostics.Select(ShaderArtifactDiagnosticRecord.FromDiagnostic));
        diagnostics.AddRange(lowered.Diagnostics.Select(ShaderArtifactDiagnosticRecord.FromDiagnostic));

        var stages = new List<ShaderArtifactStageRecord>
        {
            new("vertex", "VSMain", "vs_6_0", "generated/vertex.hlsl", string.Empty, string.Empty),
            new("pixel", "PSMain", "ps_6_0", "generated/pixel.hlsl", string.Empty, string.Empty)
        };

        var dxc = FindDxc();
        var compilerVersion = "unavailable";
        if (dxc is not null)
        {
            compilerVersion = ProbeVersion(dxc);
            stages[0] = CompileStage(dxc, "vertex", "vs_6_0", "VSMain", vertexHlsl, Path.Combine(binDir, "vertex.spv"), Path.Combine(logsDir, "dxc.vertex.txt"), diagnostics, options.StrictDxc);
            stages[1] = CompileStage(dxc, "pixel", "ps_6_0", "PSMain", pixelHlsl, Path.Combine(binDir, "pixel.spv"), Path.Combine(logsDir, "dxc.pixel.txt"), diagnostics, options.StrictDxc);
        }
        else
        {
            diagnostics.Add(new("DXC000", "warning", "compile", "none", "dxc unavailable; skipped SPIR-V compilation", string.Empty, 1, 1, 0, false));
        }

        var manifest = new ShaderArtifactManifest
        {
            SourcePath = NormalizePath(options.SourcePath),
            SourceHash = ComputeNormalizedTextHash(options.SourceText),
            EntryShader = options.EntryShader,
            SpecializationKey = BuildSpecializationKey(options.EntryShader, options.BoolSpecialization),
            CompilerVersion = compilerVersion,
            Stages = stages,
            Specializations = options.BoolSpecialization.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => new ShaderArtifactSpecializationRecord(options.EntryShader, k.Key, "bool", k.Value ? "true" : "false")).ToList(),
            Io = BuildIo(lowered.StageIo),
            Diagnostics = diagnostics
        };

        var manifestText = ShaderArtifactJsonWriter.ToJson(manifest);
        File.WriteAllText(Path.Combine(options.OutputRoot, "manifest.json"), manifestText + "\n", new UTF8Encoding(false));
        return manifest;
    }

    private static List<ShaderArtifactIoRecord> BuildIo(StageIoLayout stageIo)
    {
        var records = new List<ShaderArtifactIoRecord>();
        records.AddRange(stageIo.VSInput.Select((s, i) => new ShaderArtifactIoRecord("vertex", "input", s.Name, s.Type, s.Semantic, i)));
        records.AddRange(stageIo.VSOutput.Select((s, i) => new ShaderArtifactIoRecord("vertex", "output", s.Name, s.Type, s.Semantic, i)));
        records.AddRange(stageIo.PSInput.Select((s, i) => new ShaderArtifactIoRecord("pixel", "input", s.Name, s.Type, s.Semantic, i)));
        records.AddRange(stageIo.PSOutput.Select((s, i) => new ShaderArtifactIoRecord("pixel", "output", s.Name, s.Type, s.Semantic, i)));
        return records;
    }

    private static string BuildSpecializationKey(string shader, IReadOnlyDictionary<string, bool> spec)
        => spec.Count == 0 ? shader : shader + "|" + string.Join(",", spec.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}={(k.Value ? "true" : "false")}"));

    private static string NormalizePath(string path) => path.Replace('\\', '/');
    private static string ComputeNormalizedTextHash(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static string ComputeBinaryHash(string path)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string? FindDxc()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;
        var names = OperatingSystem.IsWindows() ? new[] { "dxc.exe", "dxc" } : new[] { "dxc" };
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        foreach (var name in names)
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string ProbeVersion(string dxc)
    {
        var result = Run(dxc, "--version");
        var line = (result.StdOut + "\n" + result.StdErr).Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return line?.Trim() ?? "unknown";
    }

    private static ShaderArtifactStageRecord CompileStage(string dxc, string stage, string profile, string entry, string input, string output, string logPath, List<ShaderArtifactDiagnosticRecord> diagnostics, bool strict)
    {
        var args = $"-T {profile} -E {entry} -spirv \"{input}\" -Fo \"{output}\"";
        var result = Run(dxc, args);
        File.WriteAllText(logPath, result.StdOut + "\n" + result.StdErr, new UTF8Encoding(false));
        if (result.ExitCode != 0 || !File.Exists(output))
        {
            diagnostics.Add(new("DXC001", strict ? "error" : "warning", "compile", stage, $"dxc compile failed for {stage} with exit code {result.ExitCode}", string.Empty, 1, 1, 0, strict));
            if (strict)
                throw new InvalidOperationException($"dxc compile failed for {stage}");
            return new(stage, entry, profile, $"generated/{stage}.hlsl", string.Empty, string.Empty);
        }

        return new(stage, entry, profile, $"generated/{stage}.hlsl", $"bin/{stage}.spv", ComputeBinaryHash(output));
    }

    private static (int ExitCode, string StdOut, string StdErr) Run(string fileName, string args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo { FileName = fileName, Arguments = args, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        process.Start();
        var outText = process.StandardOutput.ReadToEnd();
        var errText = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, outText, errText);
    }
}
