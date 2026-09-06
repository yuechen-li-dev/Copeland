using Copeland.TS.Diagnostics;
using Copeland.TS.Syntax;

namespace Copeland.TS.Manifest;

/// <summary>
/// Backend-neutral, compile-time project description. It contains declarative
/// intent only; no member represents a launched process or host object.
/// </summary>
public sealed record CopelandManifest(
    string ProjectRoot,
    string SourcePath,
    ManifestWorkspace Workspace,
    IReadOnlyList<ManifestPackage> Packages,
    IReadOnlyList<ManifestDeploymentBinding> DeploymentBindings,
    IReadOnlyList<ManifestSidecarBinding> Sidecars,
    IReadOnlyList<ManifestPackageReference> PackageReferences,
    ManifestSecurity? Security,
    ManifestUpdatePolicy? UpdatePolicy,
    IReadOnlyList<ManifestCompatFile> CompatFiles,
    ManifestAssetGraph? Assets = null,
    ManifestAssetOutputs? AssetOutputs = null);

public sealed record ManifestAssetGraph(
    string SourceRoot,
    IReadOnlyList<ManifestTextureAsset> Textures,
    IReadOnlyList<ManifestObjectAsset> Objects);

public sealed record ManifestTextureAsset(string Id, string Source);

public sealed record ManifestObjectAsset(
    string Id,
    string Source,
    IReadOnlyList<string> Dependencies);

public sealed record ManifestAssetOutputs(
    bool Toml,
    bool Json,
    bool Runtime,
    bool Audit);

public sealed record ManifestWorkspace(string Name, string Runtime);

public sealed record ManifestPackage(
    string Name,
    string Version,
    string Kind,
    string? License,
    ManifestValue? Dependencies,
    IReadOnlyList<ManifestTarget> Targets,
    IReadOnlyList<ManifestRunTarget> RunTargets,
    IReadOnlyList<ManifestValue> Tools,
    IReadOnlyList<ManifestValue> Boundaries,
    ManifestPublish? Publish,
    ManifestValue? Policies);

public sealed record ManifestTarget(string Name, ManifestValue Row);

/// <summary>
/// A future sidecar deployment consumer may use this immutable declaration.
/// Runtime and command arguments remain structurally distinct and are never
/// combined into a shell command.
/// </summary>
public sealed record ManifestRunTarget(
    string PackageName,
    string Name,
    string? Runtime,
    IReadOnlyList<string> Command,
    string? WorkingDirectory,
    ManifestValue Row);

/// <summary>
/// Root-owned declarative deployment information derived from TSPack's
/// established Package/RunTargets vocabulary. It is data for a future sidecar
/// binding phase, never a launch request.
/// </summary>
public sealed record ManifestDeploymentBinding(
    string LogicalIdentity,
    string PackageName,
    string RunTargetName,
    string? Runtime,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory);

/// <summary>
/// Root-owned logical transport binding. Launch details deliberately remain on
/// the referenced RunTarget; this record cannot become a second command line.
/// </summary>
public sealed record ManifestSidecarBinding(
    string LogicalBindingId,
    string RunTargetIdentity,
    bool IsDefault);

public sealed record ManifestPackageReference(string Name, string Root, string ManifestPath);

public sealed record ManifestSecurity(ManifestValue AcknowledgedCapabilities, ManifestValue AcknowledgedLifecycleCategories);

public sealed record ManifestUpdatePolicy(IReadOnlyList<ManifestValue> Rows);

public sealed record ManifestCompatFile(string Path, ManifestValue Value);

public sealed record ManifestPublish(IReadOnlyList<string> Include, IReadOnlyList<string> Exclude);

public abstract record ManifestValue
{
    public sealed record String(string Text) : ManifestValue;
    public sealed record Number(double NumberValue) : ManifestValue;
    public sealed record Boolean(bool BooleanValue) : ManifestValue;
    public sealed record Null : ManifestValue;
    public sealed record Array(IReadOnlyList<ManifestValue> Values) : ManifestValue;
    public sealed record Object(IReadOnlyDictionary<string, ManifestValue> Properties) : ManifestValue;
}

public enum ManifestBindingContext
{
    RootProject,
    DependencyManifest,
}

public sealed record ManifestBindingResult(CopelandManifest? Manifest, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Manifest is not null && Diagnostics.Count == 0;
}

public sealed record ManifestProjectLoadResult(
    CopelandManifest? Manifest,
    SyntaxTree? SyntaxTree,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Manifest is not null && Diagnostics.Count == 0;
}

/// <summary>
/// The sole project-loading seam for manifest consumers. Ordinary compilation
/// never selects this profile merely from a source filename.
/// </summary>
public static class CopelandProject
{
    public static ManifestProjectLoadResult LoadRootManifest(string projectRoot)
    {
        string normalizedRoot = Path.GetFullPath(projectRoot);
        string manifestPath = Path.Combine(normalizedRoot, "manifest.tsx");
        if (!File.Exists(manifestPath))
        {
            return new ManifestProjectLoadResult(
                null,
                null,
                [new Diagnostic(
                    "COPE-MANIFEST-0001",
                    "A root Copeland project requires a root-level 'manifest.tsx'.",
                    0,
                    1,
                    manifestPath)]);
        }

        string source = File.ReadAllText(manifestPath);
        SyntaxTree tree = SyntaxTree.Parse(source, manifestPath);
        if (tree.Diagnostics.Count > 0)
        {
            return new ManifestProjectLoadResult(null, tree, AttachSourcePath(tree.Diagnostics, manifestPath));
        }

        ManifestBindingResult binding = ManifestBinder.Bind(
            tree,
            normalizedRoot,
            manifestPath,
            ManifestBindingContext.RootProject);
        return new ManifestProjectLoadResult(binding.Manifest, tree, binding.Diagnostics);
    }

    private static IReadOnlyList<Diagnostic> AttachSourcePath(IEnumerable<Diagnostic> diagnostics, string sourcePath)
        => diagnostics.Select(diagnostic => diagnostic with { SourcePath = sourcePath }).ToArray();
}
