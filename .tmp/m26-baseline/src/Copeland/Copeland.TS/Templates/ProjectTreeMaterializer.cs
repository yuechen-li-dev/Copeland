namespace Copeland.TS.Templates;

/// <summary>
/// Writes a validated project tree to a new directory. This is deliberately a
/// small terminal operation over <see cref="ProjectTree"/>, not a filesystem API
/// exposed to templates.
/// </summary>
public static class ProjectTreeMaterializer
{
    public static ProjectTreeMaterializationResult Materialize(ProjectTree project, string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            return ProjectTreeMaterializationResult.Failure("COPE-TEMPLATE-CLI-0006", "Materialize requires '--output <path>'.");
        }

        string root = Path.GetFullPath(outputRoot);
        if (File.Exists(root) || Directory.Exists(root))
        {
            return ProjectTreeMaterializationResult.Failure("COPE-TEMPLATE-CLI-0009", $"Output directory '{root}' already exists. Templates never merge into an existing directory.");
        }

        try
        {
            foreach (FileArtifact file in project.Files)
            {
                if (!ProjectTree.TryNormalizePath(file.Path, out string? normalized, out string? error))
                {
                    return ProjectTreeMaterializationResult.Failure("COPE-ARTIFACT-0001", error!);
                }

                string destination = Path.GetFullPath(Path.Combine(root, normalized!.Replace('/', Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return ProjectTreeMaterializationResult.Failure("COPE-ARTIFACT-0001", $"Artifact path '{file.Path}' escapes output root '{root}'.");
                }
            }

            Directory.CreateDirectory(root);
            foreach (FileArtifact file in project.Files)
            {
                string destination = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, file.Bytes);
            }

            return ProjectTreeMaterializationResult.Success(project.Files.Select(file => file.Path).ToArray());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ProjectTreeMaterializationResult.Failure("COPE-TEMPLATE-CLI-0007", $"Template materialization failed: {exception.Message}");
        }
    }
}

public sealed record ProjectTreeMaterializationResult(bool Succeeded, string? DiagnosticId, string? Message, IReadOnlyList<string> Files)
{
    public static ProjectTreeMaterializationResult Success(IReadOnlyList<string> files) => new(true, null, null, files);

    public static ProjectTreeMaterializationResult Failure(string diagnosticId, string message) => new(false, diagnosticId, message, []);
}
