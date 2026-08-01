using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Copeland.TS.Diagnostics;

namespace Copeland.TS.Templates;

/// <summary>Closed M0 language classifications for typed source artifacts.</summary>
public enum ArtifactLanguage
{
    CopelandTS,
    CopelandTest,
    CSharp,
}

public sealed record TypedSourceBody(
    ArtifactLanguage Language,
    string Text,
    IReadOnlyList<string> ImportedParameters,
    string Provenance);

public abstract record ArtifactNode(string Provenance);

public record FileArtifact(string Path, string Kind, byte[] Bytes, string Provenance) : ArtifactNode(Provenance)
{
    public string Sha256 => Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
}

public sealed record TextFileArtifact(string Path, byte[] Bytes, string Provenance)
    : FileArtifact(Path, "text", Bytes, Provenance);

public sealed record SourceFileArtifact(string Path, byte[] Bytes, string Provenance)
    : FileArtifact(Path, "source", Bytes, Provenance);

public sealed record ProjectFileArtifact(string Path, byte[] Bytes, string Provenance)
    : FileArtifact(Path, "project", Bytes, Provenance);

public sealed record TestFileArtifact(string Path, byte[] Bytes, string Provenance)
    : FileArtifact(Path, "test", Bytes, Provenance);

public sealed record DirectoryArtifact(string Path, IReadOnlyList<ArtifactNode> Children, string Provenance)
    : ArtifactNode(Provenance);

public sealed record NpmDependencyValue(string Name, string Version, string Provenance);

public sealed record NpmPackageManifestValue(
    string Name,
    string Version,
    IReadOnlyList<NpmDependencyValue> Dependencies,
    string Provenance);

public sealed record CopelandSourceSetValue(IReadOnlyList<string> Includes, string Provenance);

public sealed record CopelandProjectTypeSetValue(IReadOnlyList<string> Types, string Provenance);

public sealed record TypeScriptWorkspaceValue(
    string ProjectPath,
    IReadOnlyList<string> Includes,
    IReadOnlyList<string> ProjectTypes,
    string Provenance);

public sealed record DotNetProjectValue(string Name, IReadOnlyList<ArtifactNode> Files, string Provenance);

public sealed record DotNetSolutionValue(
    string Name,
    DotNetProjectValue Project,
    IReadOnlyList<ArtifactNode> RootFiles,
    string Provenance)
{
    public bool TryLower(out ProjectTree? tree, out IReadOnlyList<Diagnostic> diagnostics)
    {
        var nodes = new List<ArtifactNode>(RootFiles)
        {
            new DirectoryArtifact(Project.Name, Project.Files, Project.Provenance),
        };
        return ProjectTree.TryCreate(nodes, out tree, out diagnostics);
    }
}

public sealed record TemplateXmlElementValue(
    string Name,
    IReadOnlyList<KeyValuePair<string, string>> Attributes,
    IReadOnlyList<object> Children);

/// <summary>
/// Immutable, normalized structural result of a template evaluation. The model is
/// intentionally not a virtual filesystem: it contains only declared output files.
/// </summary>
public sealed class ProjectTree
{
    public ProjectTree(IReadOnlyList<FileArtifact> files)
    {
        Files = Array.AsReadOnly(files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyList<FileArtifact> Files { get; }

    public static bool TryCreate(IEnumerable<ArtifactNode> nodes, out ProjectTree? project, out IReadOnlyList<Diagnostic> diagnostics)
    {
        var result = new List<FileArtifact>();
        var errors = new List<Diagnostic>();
        Flatten(nodes, result, errors);

        foreach (IGrouping<string, FileArtifact> group in result.GroupBy(file => file.Path, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
            {
                errors.Add(new Diagnostic(
                    "COPE-ARTIFACT-0002",
                    $"Duplicate artifact path '{group.Key}'. Use an explicit composition boundary instead of overlapping files.",
                    0,
                    0));
            }
        }

        diagnostics = errors;
        project = errors.Count == 0 ? new ProjectTree(result) : null;
        return project is not null;
    }

    public string ToPreviewJson(string templateName, bool includeContents = false)
    {
        var files = Files.Select(file => new
        {
            path = file.Path,
            kind = file.Kind,
            sha256 = file.Sha256,
            encoding = "utf-8",
            newlines = "lf",
            contentBase64 = includeContents ? Convert.ToBase64String(file.Bytes) : null,
        });
        return JsonSerializer.Serialize(new { schemaVersion = 1, template = templateName, files }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void Flatten(IEnumerable<ArtifactNode> nodes, ICollection<FileArtifact> files, ICollection<Diagnostic> diagnostics)
    {
        foreach (ArtifactNode node in nodes)
        {
            switch (node)
            {
                case FileArtifact file:
                    if (TryNormalizePath(file.Path, out string? normalized, out string? error))
                    {
                        files.Add(file with { Path = normalized! });
                    }
                    else
                    {
                        diagnostics.Add(new Diagnostic("COPE-ARTIFACT-0001", error!, 0, 0));
                    }
                    break;
                case DirectoryArtifact directory:
                    if (!TryNormalizePath(directory.Path, out string? prefix, out string? directoryError) && directory.Path.Length > 0)
                    {
                        diagnostics.Add(new Diagnostic("COPE-ARTIFACT-0001", directoryError!, 0, 0));
                        break;
                    }

                    var nested = new List<FileArtifact>();
                    Flatten(directory.Children, nested, diagnostics);
                    foreach (FileArtifact child in nested)
                    {
                        string path = string.IsNullOrEmpty(prefix) ? child.Path : prefix + "/" + child.Path;
                        files.Add(child with { Path = path });
                    }
                    break;
            }
        }
    }

    public static bool TryNormalizePath(string path, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("\\", StringComparison.Ordinal))
        {
            error = $"Invalid artifact path '{path}'. Artifact paths must be non-empty and relative.";
            return false;
        }

        string[] segments = path.Replace('\\', '/').Split("/", StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            error = $"Invalid artifact path '{path}'. '.' and '..' path segments are not permitted.";
            return false;
        }

        normalized = string.Join('/', segments);
        return true;
    }

    public static byte[] EncodeText(string text)
    {
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(normalized);
    }
}
