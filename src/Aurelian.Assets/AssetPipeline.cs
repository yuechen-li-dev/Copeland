using Aurelian.Shaders.Artifacts;
using Aurelian.Shaders.Parsing;
using Tomlyn;
using Tomlyn.Model;

namespace Aurelian.Assets;

public sealed class AssetManifest
{
    public List<ShaderAssetRecord> Shaders { get; } = [];
    public List<AssetShaderReference> ShaderReferences { get; } = [];
    public List<ShaderSpecializationRecord> Specializations { get; } = [];
    public List<ShaderEffectRecord> Effects { get; } = [];
}

public sealed record ShaderAssetRecord(string Id, string Source, string Entry, string Backend, string Profile);
public sealed record AssetShaderReference(string Id, string Path);
public sealed record ShaderSpecializationRecord(string Shader, string Name, string Type, object? Value);
public sealed record ShaderEffectRecord(string Shader, string Name, string? Namespace);

public sealed record AssetDiagnostic(string Code, string Severity, string Message, string SourcePath, int Line = 1, int Column = 1, bool Fatal = true);
public sealed record ShaderAssetBuildResult(string ShaderId, string OutputDirectory, string ManifestPath, bool Success);
public sealed record AssetPipelineResult(IReadOnlyList<AssetDiagnostic> Diagnostics, IReadOnlyList<ShaderAssetBuildResult> Built, IReadOnlyList<string> FailedShaderIds);

public static class AssetDiagnosticCodes
{
    public const string TomlParseFailed = "AM100";
    public const string LegacyDuplicateShaderId = "AM200";
    public const string LegacyShaderRequiredFieldMissing = "AM201";
    public const string LegacyShaderSourcePathInvalid = "AM202";
    public const string LegacyShaderBackendProfileUnsupported = "AM203";
    public const string LegacyUnknownShaderReference = "AM204";
    public const string LegacyDuplicateSpecialization = "AM205";
    public const string LegacyUnknownSpecializationParameter = "AM206";
    public const string LegacyInvalidSpecialization = "AM207";

    public const string ShaderIdMissing = "AA2001";
    public const string ShaderPathMissing = "AA2002";
    public const string DuplicateShaderId = "AA2003";
    public const string ShaderPathAbsoluteUnsupported = "AA2004";
    public const string ShaderPathTraversalUnsupported = "AA2005";
    public const string ShaderArtifactMissing = "AA2006";
    public const string ShaderArtifactLoadFailed = "AA2007";
    public const string ShaderReferencesMalformed = "AA2008";
}

public static class AssetManifestParser
{
    public static (AssetManifest? Manifest, IReadOnlyList<AssetDiagnostic> Diagnostics) Parse(string tomlText, string manifestPath)
    {
        if (!Toml.TryToModel<TomlTable>(tomlText, out var table, out var diagnostics) || table is null)
        {
            return (null, [new AssetDiagnostic(AssetDiagnosticCodes.TomlParseFailed, "error", diagnostics?.ToString() ?? "TOML parse failure.", manifestPath)]);
        }

        var manifest = new AssetManifest();
        var assetDiagnostics = new List<AssetDiagnostic>();
        if (table.TryGetValue("shader", out var shaderObj) && shaderObj is TomlTableArray shaderTables)
        {
            foreach (var item in shaderTables.OfType<TomlTable>())
            {
                manifest.Shaders.Add(new ShaderAssetRecord(
                    item.TryGetValue("id", out var id) ? id?.ToString() ?? string.Empty : string.Empty,
                    item.TryGetValue("source", out var source) ? source?.ToString() ?? string.Empty : string.Empty,
                    item.TryGetValue("entry", out var entry) ? entry?.ToString() ?? string.Empty : string.Empty,
                    item.TryGetValue("backend", out var backend) ? backend?.ToString() ?? string.Empty : string.Empty,
                    item.TryGetValue("profile", out var profile) ? profile?.ToString() ?? string.Empty : string.Empty));
            }

            foreach (var item in shaderTables.OfType<TomlTable>().Where(x => x.TryGetValue("specialization", out _)))
            {
                if (item["specialization"] is TomlTableArray specializationArray)
                foreach (var s in specializationArray.OfType<TomlTable>())
                    manifest.Specializations.Add(new ShaderSpecializationRecord(s["shader"]?.ToString() ?? string.Empty, s["name"]?.ToString() ?? string.Empty, s["type"]?.ToString() ?? string.Empty, s.TryGetValue("value", out var v) ? v : null));
            }

            foreach (var item in shaderTables.OfType<TomlTable>().Where(x => x.TryGetValue("effect", out _)))
            {
                if (item["effect"] is TomlTableArray effectArray)
                foreach (var e in effectArray.OfType<TomlTable>())
                    manifest.Effects.Add(new ShaderEffectRecord(e["shader"]?.ToString() ?? string.Empty, e["name"]?.ToString() ?? string.Empty, e.TryGetValue("namespace", out var n) ? n?.ToString() : null));
            }
        }

        if (table.TryGetValue("shaders", out var shaderReferencesObj))
        {
            if (shaderReferencesObj is TomlTableArray shaderReferenceTables)
            {
                foreach (var item in shaderReferenceTables.OfType<TomlTable>())
                {
                    manifest.ShaderReferences.Add(new AssetShaderReference(
                        item.TryGetValue("id", out var id) ? id?.ToString() ?? string.Empty : string.Empty,
                        item.TryGetValue("path", out var path) ? path?.ToString() ?? string.Empty : string.Empty));
                }
            }
            else
            {
                assetDiagnostics.Add(new AssetDiagnostic(AssetDiagnosticCodes.ShaderReferencesMalformed, "error", "The 'shaders' manifest section must use repeated TOML tables: [[shaders]].", manifestPath));
            }
        }

        return (manifest, assetDiagnostics);
    }
}

public static class AssetManifestValidator
{
    public static IReadOnlyList<AssetDiagnostic> Validate(AssetManifest manifest, string manifestPath)
    {
        var diags = new List<AssetDiagnostic>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shader in manifest.Shaders)
        {
            var shaderId = shader.Id ?? string.Empty;
            var shaderSource = shader.Source ?? string.Empty;
            var shaderEntry = shader.Entry ?? string.Empty;
            var shaderBackend = shader.Backend ?? string.Empty;
            var shaderProfile = shader.Profile ?? string.Empty;

            if (!ids.Add(shaderId)) diags.Add(new(AssetDiagnosticCodes.LegacyDuplicateShaderId, "error", $"Duplicate asset ID '{shaderId}'.", manifestPath));
            if (new[] { shaderId, shaderSource, shaderEntry, shaderBackend, shaderProfile }.Any(string.IsNullOrWhiteSpace)) diags.Add(new(AssetDiagnosticCodes.LegacyShaderRequiredFieldMissing, "error", $"Missing required field in shader '{shaderId}'.", manifestPath));
            if (Path.IsPathRooted(shaderSource) || shaderSource.Contains("..") || shaderSource.Contains('\\')) diags.Add(new(AssetDiagnosticCodes.LegacyShaderSourcePathInvalid, "error", $"Invalid source path '{shaderSource}'.", manifestPath));
            if (!string.Equals(shaderBackend, "vulkan", StringComparison.Ordinal) || !string.Equals(shaderProfile, "default", StringComparison.Ordinal)) diags.Add(new(AssetDiagnosticCodes.LegacyShaderBackendProfileUnsupported, "error", $"Unsupported backend/profile '{shaderBackend}/{shaderProfile}'.", manifestPath));
            if (!System.Text.RegularExpressions.Regex.IsMatch(shaderId, "^[a-z0-9._-]+$")) diags.Add(new(AssetDiagnosticCodes.LegacyShaderRequiredFieldMissing, "error", $"Invalid id '{shaderId}'.", manifestPath));
        }

        var shaderIds = manifest.Shaders.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var s in manifest.Specializations)
        {
            if (!shaderIds.Contains(s.Shader)) diags.Add(new(AssetDiagnosticCodes.LegacyUnknownShaderReference, "error", $"Unknown shader reference '{s.Shader}'.", manifestPath));
            if (!string.Equals(s.Type, "bool", StringComparison.Ordinal) || s.Value is not bool) diags.Add(new(AssetDiagnosticCodes.LegacyInvalidSpecialization, "error", $"Specialization '{s.Name}' must be bool type with bool value.", manifestPath));
        }

        foreach (var e in manifest.Effects.Where(e => !shaderIds.Contains(e.Shader))) diags.Add(new(AssetDiagnosticCodes.LegacyUnknownShaderReference, "error", $"Unknown shader reference '{e.Shader}'.", manifestPath));

        foreach (var dup in manifest.Specializations.GroupBy(x => (x.Shader, x.Name)).Where(g => g.Count() > 1)) diags.Add(new(AssetDiagnosticCodes.LegacyDuplicateSpecialization, "error", $"Duplicate specialization '{dup.Key.Name}' for shader '{dup.Key.Shader}'.", manifestPath));

        diags.AddRange(ValidateShaderReferences(manifest.ShaderReferences, manifestPath));

        return diags;
    }

    public static IReadOnlyList<AssetDiagnostic> ValidateShaderReferences(IReadOnlyList<AssetShaderReference> shaderReferences, string manifestPath)
    {
        var diags = new List<AssetDiagnostic>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (AssetShaderReference shader in shaderReferences)
        {
            string shaderId = shader.Id ?? string.Empty;
            string shaderPath = shader.Path ?? string.Empty;

            if (string.IsNullOrWhiteSpace(shaderId))
            {
                diags.Add(new AssetDiagnostic(AssetDiagnosticCodes.ShaderIdMissing, "error", "Shader asset reference is missing required field 'id'.", manifestPath));
            }
            else if (!ids.Add(shaderId))
            {
                diags.Add(new AssetDiagnostic(AssetDiagnosticCodes.DuplicateShaderId, "error", $"Duplicate shader asset id '{shaderId}'.", manifestPath));
            }

            if (string.IsNullOrWhiteSpace(shaderPath))
            {
                diags.Add(new AssetDiagnostic(AssetDiagnosticCodes.ShaderPathMissing, "error", $"Shader asset '{shaderId}' is missing required field 'path'.", manifestPath));
                continue;
            }

            if (Path.IsPathRooted(shaderPath))
            {
                diags.Add(new AssetDiagnostic(AssetDiagnosticCodes.ShaderPathAbsoluteUnsupported, "error", $"Shader asset '{shaderId}' path must be relative to the asset manifest directory.", manifestPath));
            }

            if (ContainsParentTraversalSegment(shaderPath))
            {
                diags.Add(new AssetDiagnostic(AssetDiagnosticCodes.ShaderPathTraversalUnsupported, "error", $"Shader asset '{shaderId}' path must not contain '..' segments.", manifestPath));
            }
        }

        return diags;
    }

    internal static bool ContainsParentTraversalSegment(string path) =>
        path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(segment => string.Equals(segment, "..", StringComparison.Ordinal));
}

public sealed class AssetPipelineRunner
{
    public AssetPipelineResult BuildShaders(AssetManifest manifest, string manifestPath, string outputRoot, bool strictDxc = false)
    {
        var diags = AssetManifestValidator.Validate(manifest, manifestPath).ToList();
        var built = new List<ShaderAssetBuildResult>();
        var failed = new List<string>();
        if (diags.Any(d => d.Fatal)) return new(diags, built, manifest.Shaders.Select(s => s.Id).ToList());

        var manifestDir = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var parser = new ShaderParser();
        foreach (var shader in manifest.Shaders)
        {
            var srcPath = Path.GetFullPath(Path.Combine(manifestDir, shader.Source));
            var source = File.ReadAllText(srcPath);
            var parsed = parser.ParseSdslDocument(source);
            var shaderDoc = parsed.Document?.Shaders.FirstOrDefault(s => s.Name == shader.Entry);
            var specs = manifest.Specializations.Where(s => s.Shader == shader.Id).ToList();
            if (shaderDoc is not null)
            {
                var genericNames = shaderDoc.GenericParameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
                foreach (var spec in specs.Where(x => !genericNames.Contains(x.Name))) diags.Add(new(AssetDiagnosticCodes.LegacyUnknownSpecializationParameter, "error", $"Unknown specialization parameter '{spec.Name}' for shader '{shader.Entry}'.", manifestPath));
            }

            var shaderOut = Path.Combine(outputRoot, "shaders", shader.Id);
            var emitter = new ShaderArtifactEmitter();
            var artifact = emitter.Emit(new ShaderArtifactOptions
            {
                SourcePath = srcPath,
                SourceText = source,
                EntryShader = shader.Entry,
                OutputRoot = shaderOut,
                BoolSpecialization = specs.ToDictionary(x => x.Name, x => (bool)x.Value!, StringComparer.Ordinal),
                StrictDxc = strictDxc
            });
            var manifestJsonPath = Path.Combine(shaderOut, "manifest.json");
            File.WriteAllText(manifestJsonPath, ShaderArtifactJsonWriter.ToJson(artifact));
            built.Add(new(shader.Id, shaderOut, manifestJsonPath, true));
        }

        return new(diags, built, failed);
    }
}
