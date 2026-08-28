using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Copeland.Cli;
using Copeland.TS.Manifest;

namespace Copeland.TS.Compiler;

/// <summary>
/// Immutable compiler-visible project world. TSPack writes this resolved
/// descriptor after manifest/package resolution; inspection and editor
/// consumers reopen the same descriptor without materializing packages.
/// </summary>
public sealed class CopelandProjectContext
{
    private CopelandProjectContext(
        string descriptorPath,
        CopelandProjectContextDescriptor descriptor,
        CopelandProjectSource[] sources,
        CopelandCompilationOptions options,
        string fingerprint)
    {
        DescriptorPath = descriptorPath;
        Descriptor = descriptor;
        Sources = sources;
        Options = options;
        Fingerprint = fingerprint;
    }

    public string DescriptorPath { get; }
    public CopelandProjectContextDescriptor Descriptor { get; }
    public string ProjectRoot => Descriptor.ProjectRoot;
    public IReadOnlyList<CopelandProjectSource> Sources { get; }
    public CopelandCompilationOptions Options { get; }
    public string Fingerprint { get; }

    public static CopelandProjectContext Load(string descriptorPath)
    {
        string fullDescriptorPath = Path.GetFullPath(descriptorPath);
        using FileStream stream = File.OpenRead(fullDescriptorPath);
        CopelandProjectContextDescriptor? descriptor = JsonSerializer.Deserialize<CopelandProjectContextDescriptor>(stream, JsonOptions);
        if (descriptor is null)
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0004",
                "Resolved project descriptor is empty.");
        }

        return Create(fullDescriptorPath, descriptor);
    }

    /// <summary>
    /// Loads TSPack-managed resolved truth. No manifest, registry, lockfile, or
    /// ambient node_modules discovery is performed on this path.
    /// </summary>
    public static CopelandProjectContext LoadResolvedContext(string descriptorPath)
    {
        string fullDescriptorPath = Path.GetFullPath(descriptorPath);
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(fullDescriptorPath)))
        {
            if (!document.RootElement.TryGetProperty("schemaVersion", out _))
            {
                // Compatibility for descriptors materialized before M71. New TSPack
                // writes only the versioned compiler-target protocol.
                return Load(fullDescriptorPath);
            }
        }
        CompilerTargetDescriptor target = CompilerTargetDescriptorProtocol.Load(fullDescriptorPath);
        CopelandResolvedPayload? payload = target.CompilerPayload!.Data.Deserialize<CopelandResolvedPayload>(
            CompilerTargetDescriptorProtocol.JsonOptions);
        if (payload is null)
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0010",
                "Copeland compiler payload is empty.");
        }

        ValidatePayloadMirrorsGenericTarget(target, payload);

        IReadOnlyDictionary<string, CompilerPackageBinding> packageBindings = PackageBindingIndex(target.Packages ?? []);
        IReadOnlyList<CopelandProjectContextNpmContract> payloadContracts = payload.NpmContracts ?? [];
        foreach (CopelandProjectContextNpmContract contract in payloadContracts)
        {
            string packageName = NpmPackageRoot(contract.PackageName);
            if (!packageBindings.TryGetValue(packageName, out CompilerPackageBinding? binding))
            {
                throw new CopelandProjectContextException(
                    "COPE-PROJECT-0011",
                    $"Copeland payload package '{contract.PackageName}' has no resolved generic package binding.");
            }
            contract.Version = binding.Version;
            contract.MaterializationPath = binding.MaterializationPath;
            contract.Materialized = Directory.Exists(binding.MaterializationPath);
        }

        IReadOnlyList<CompilerTargetSource> selectedSources = SelectCompilerOwnedSources(target, fullDescriptorPath);
        return Create(
            fullDescriptorPath,
            new CopelandProjectContextDescriptor
            {
                ProjectRoot = target.ProjectRoot,
                JavaScriptRuntime = target.Runtime.Name,
                TsXmlProfile = payload.TsXmlProfile,
                Sources = selectedSources.Select(source => new CopelandProjectContextSource
                {
                    LogicalPath = source.LogicalPath,
                    Path = source.Path,
                }).ToList(),
                NpmContracts = payloadContracts.ToList(),
            });
    }

    private static void ValidatePayloadMirrorsGenericTarget(
        CompilerTargetDescriptor target,
        CopelandResolvedPayload payload)
    {
        if (!string.Equals(
                Path.GetFullPath(target.ProjectRoot),
                Path.GetFullPath(payload.ProjectRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0016",
                "Copeland payload projectRoot conflicts with the generic compiler-target projectRoot.");
        }

        if (!string.Equals(target.Runtime.Name, payload.JavaScriptRuntime, StringComparison.Ordinal))
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0017",
                "Copeland payload JavaScript runtime conflicts with the generic compiler-target runtime.");
        }
    }

    private static IReadOnlyDictionary<string, CompilerPackageBinding> PackageBindingIndex(
        IReadOnlyList<CompilerPackageBinding> bindings)
    {
        var index = new Dictionary<string, CompilerPackageBinding>(StringComparer.Ordinal);
        foreach (CompilerPackageBinding binding in bindings)
        {
            AddPackageBindingKey(index, binding.LocalName, binding);
            AddPackageBindingKey(index, binding.MaterializationName, binding);

            int separator = binding.SemanticIdentity.IndexOf(':');
            if (separator >= 0 && separator + 1 < binding.SemanticIdentity.Length)
            {
                AddPackageBindingKey(index, binding.SemanticIdentity[(separator + 1)..], binding);
            }
        }

        return index;
    }

    private static void AddPackageBindingKey(
        Dictionary<string, CompilerPackageBinding> index,
        string key,
        CompilerPackageBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            index.TryAdd(key, binding);
        }
    }

    private static string NpmPackageRoot(string packageName)
    {
        if (!packageName.StartsWith('@'))
        {
            int slash = packageName.IndexOf('/');
            return slash < 0 ? packageName : packageName[..slash];
        }

        int scopedSlash = packageName.IndexOf('/');
        if (scopedSlash < 0)
        {
            return packageName;
        }

        int subpathSlash = packageName.IndexOf('/', scopedSlash + 1);
        return subpathSlash < 0 ? packageName : packageName[..subpathSlash];
    }

    /// <summary>
    /// Builds compiler context from compiler-owned config and already-present
    /// local packages. This path never contacts a registry, selects a version,
    /// writes a lockfile, or materializes a dependency.
    /// </summary>
    public static CopelandProjectContext LoadStandalone(string projectRoot)
    {
        string fullProjectRoot = Path.GetFullPath(projectRoot);
        ManifestProjectLoadResult manifestResult = CopelandProject.LoadRootManifest(fullProjectRoot);
        if (!manifestResult.Success)
        {
            var diagnostic = manifestResult.Diagnostics[0];
            throw new CopelandProjectContextException(diagnostic.Id, diagnostic.Message);
        }

        string configPath = Path.Combine(fullProjectRoot, "tsconfig.tsx");
        CopelandWorkspaceOwnershipResult ownership = CopelandWorkspaceOwnership.Resolve(configPath);
        if (!ownership.Success)
        {
            CopelandWorkspaceOwnershipDiagnostic diagnostic = ownership.Diagnostics[0];
            throw new CopelandProjectContextException(diagnostic.Code, diagnostic.Message);
        }

        var contracts = new List<CopelandProjectContextNpmContract>();
        foreach ((string source, string packageName) in StandalonePackageRequirements(manifestResult.Manifest!))
        {
            string materializationName = source == "jsr"
                ? JsrMaterializationName(packageName)
                : packageName;
            string materializationPath = Path.Combine(
                fullProjectRoot,
                "node_modules",
                materializationName.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(materializationPath))
            {
                throw new CopelandProjectContextException(
                    "COPE-PROJECT-0015",
                    $"Required package {source}:{packageName} is not available in the local project environment. " +
                    "Install it using your package manager, or use TSPack to resolve/materialize the project.");
            }
            contracts.Add(new CopelandProjectContextNpmContract
            {
                PackageName = packageName,
                Version = ReadLocalPackageVersion(materializationPath),
                MaterializationPath = materializationPath,
                Materialized = true,
            });
        }

        return Create(
            "<standalone-copeland-project>",
            new CopelandProjectContextDescriptor
            {
                ProjectRoot = fullProjectRoot,
                JavaScriptRuntime = "node",
                TsXmlProfile = ProjectTypesProfile(ownership.ProjectTypes),
                Sources = ownership.Sources.Select(source => new CopelandProjectContextSource
                {
                    LogicalPath = Path.GetRelativePath(fullProjectRoot, source.Path).Replace('\\', '/'),
                    Path = source.Path,
                }).ToList(),
                NpmContracts = contracts,
            });
    }

    private static IReadOnlyList<(string Source, string Package)> StandalonePackageRequirements(
        CopelandManifest manifest)
    {
        var requirements = new HashSet<(string Source, string Package)>();
        foreach (ManifestPackage package in manifest.Packages)
        {
            VisitManifestValue(package.Dependencies, requirements);
        }
        return requirements.OrderBy(requirement => requirement.Source, StringComparer.Ordinal)
            .ThenBy(requirement => requirement.Package, StringComparer.Ordinal)
            .ToArray();
    }

    private static void VisitManifestValue(
        ManifestValue? value,
        ISet<(string Source, string Package)> requirements)
    {
        switch (value)
        {
            case ManifestValue.Array array:
                foreach (ManifestValue item in array.Values)
                {
                    VisitManifestValue(item, requirements);
                }
                break;
            case ManifestValue.Object item:
                if (item.Properties.TryGetValue("kind", out ManifestValue? dependencyKindValue)
                    && dependencyKindValue is ManifestValue.String dependencyKind
                    && dependencyKind.Text == "tool")
                {
                    return;
                }
                if (item.Properties.TryGetValue("kind", out ManifestValue? kindValue)
                    && kindValue is ManifestValue.String kind
                    && item.Properties.TryGetValue("package", out ManifestValue? packageValue)
                    && packageValue is ManifestValue.String package
                    && kind.Text is "npm" or "jsr")
                {
                    requirements.Add((kind.Text, package.Text));
                }
                foreach (ManifestValue child in item.Properties.Values)
                {
                    VisitManifestValue(child, requirements);
                }
                break;
        }
    }

    private static string JsrMaterializationName(string packageName)
    {
        string withoutScopeMarker = packageName.TrimStart('@');
        string[] parts = withoutScopeMarker.Split('/', 2);
        return parts.Length == 2
            ? $"@jsr/{parts[0]}__{parts[1]}"
            : $"@jsr/{withoutScopeMarker}";
    }

    private static string ReadLocalPackageVersion(string packageRoot)
    {
        string packageJsonPath = Path.Combine(packageRoot, "package.json");
        if (!File.Exists(packageJsonPath))
        {
            return "local";
        }
        using JsonDocument packageJson = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        return packageJson.RootElement.TryGetProperty("version", out JsonElement version)
            ? version.GetString() ?? "local"
            : "local";
    }

    private static string? ProjectTypesProfile(IReadOnlyList<string>? projectTypes)
    {
        bool react = projectTypes?.Contains("ReactComponents", StringComparer.Ordinal) == true;
        bool text = projectTypes?.Contains("TextDocuments", StringComparer.Ordinal) == true;
        return (react, text) switch
        {
            (true, true) => "react-m0+text-m0",
            (true, false) => "react-m0",
            (false, true) => "text-m0",
            _ => null,
        };
    }

    private static IReadOnlyList<CompilerTargetSource> SelectCompilerOwnedSources(
        CompilerTargetDescriptor target,
        string descriptorPath)
    {
        if (string.IsNullOrWhiteSpace(target.CompilerConfig.Path))
        {
            return target.Sources;
        }

        string projectRoot = target.ProjectRoot;
        string configPath = Path.GetFullPath(target.CompilerConfig.Path, projectRoot);
        if (!File.Exists(configPath))
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0012",
                $"Copeland compiler config '{configPath}' does not exist.");
        }

        CopelandWorkspaceOwnershipResult ownership = CopelandWorkspaceOwnership.Resolve(configPath);
        if (!ownership.Success)
        {
            CopelandWorkspaceOwnershipDiagnostic diagnostic = ownership.Diagnostics[0];
            throw new CopelandProjectContextException(diagnostic.Code, diagnostic.Message);
        }

        var declaredSources = target.Sources.ToDictionary(
            source => Path.GetFullPath(source.Path),
            StringComparer.OrdinalIgnoreCase);
        var selected = new List<CompilerTargetSource>();
        foreach (CopelandWorkspaceOwnedSource source in ownership.Sources)
        {
            string fullSourcePath = Path.GetFullPath(source.Path);
            if (!declaredSources.TryGetValue(fullSourcePath, out CompilerTargetSource? declared))
            {
                throw new CopelandProjectContextException(
                    "COPE-PROJECT-0013",
                    $"tsconfig.tsx assigns '{fullSourcePath}' to tscl, but the TSPack descriptor did not provide it as a project input.");
            }
            selected.Add(declared);
        }
        if (selected.Count == 0)
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0014",
                "tsconfig.tsx assigns no descriptor source to tscl.");
        }
        return selected;
    }

    public static CopelandProjectContext Create(string descriptorPath, CopelandProjectContextDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.ProjectRoot))
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0004",
                "Resolved project descriptor requires projectRoot.");
        }

        string projectRoot = Path.GetFullPath(descriptor.ProjectRoot);
        if (descriptor.Sources.Count == 0)
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0004",
                "Resolved project descriptor requires at least one source.");
        }

        CopelandProjectSource[] sources = descriptor.Sources
            .OrderBy(source => source.LogicalPath, StringComparer.Ordinal)
            .Select(source => ReadSource(projectRoot, source))
            .ToArray();
        CopelandNpmDependencyGraph npmDependencies = new(descriptor.NpmContracts
            .OrderBy(contract => contract.PackageName, StringComparer.Ordinal)
            .Select(ToNpmContract));
        bool browser = string.Equals(descriptor.JavaScriptRuntime, "browser", StringComparison.Ordinal);
        var options = new CopelandCompilationOptions
        {
            ProjectRoot = projectRoot,
            NpmDependencies = npmDependencies,
            JavaScriptHostModules = browser ? [CopelandProjectHostContracts.Browser()] : [],
            ProjectTypes = descriptor.TsXmlProfile?.ToLowerInvariant() switch
            {
                "react-m0" => CopelandProjectTypeSet.ReactComponents,
                "text-m0" => CopelandProjectTypeSet.TextDocuments,
                "react-m0+text-m0" or "text-m0+react-m0" => CopelandProjectTypeSet.ReactComponents | CopelandProjectTypeSet.TextDocuments,
                _ => CopelandProjectTypeSet.None,
            },
        };
        return new CopelandProjectContext(
            Path.GetFullPath(descriptorPath),
            descriptor with { ProjectRoot = projectRoot },
            sources,
            options,
            FingerprintFor(sources, descriptor, options));
    }

    public CopelandProjectSnapshot CreateSnapshot(IReadOnlyDictionary<string, string>? overlays = null)
    {
        CopelandProjectSource[] sources = Sources.Select(source =>
        {
            if (overlays is not null && overlays.TryGetValue(Path.GetFullPath(source.SourcePath), out string? text))
            {
                return source with { SourceText = text };
            }
            return source;
        }).ToArray();
        return CopelandProjectCompiler.CreateSnapshot(sources, Options);
    }

    private static CopelandProjectSource ReadSource(string projectRoot, CopelandProjectContextSource source)
    {
        if (string.IsNullOrWhiteSpace(source.LogicalPath) || string.IsNullOrWhiteSpace(source.Path))
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0004",
                "Each resolved source requires logicalPath and path.");
        }

        string sourcePath = Path.GetFullPath(source.Path);
        if (!sourcePath.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sourcePath, projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0005",
                $"Resolved source '{source.LogicalPath}' is outside projectRoot.");
        }

        if (!File.Exists(sourcePath))
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0006",
                $"Resolved source '{source.LogicalPath}' does not exist.");
        }

        return new CopelandProjectSource(source.LogicalPath, sourcePath, File.ReadAllText(sourcePath));
    }

    private static CopelandNpmPackageContract ToNpmContract(CopelandProjectContextNpmContract contract)
    {
        CopelandNpmFunctionContract[] exports = contract.Exports
            .Select(export => new CopelandNpmFunctionContract(
                export.Name,
                export.Parameters,
                export.Result,
                export.RemoteError,
                export.Promise))
            .ToArray();
        CopelandNpmComponentContract[] components = contract.Components
            .Select(component => new CopelandNpmComponentContract(
                component.Name,
                component.Properties
                    .Select(ToNpmProperty)
                    .ToArray(),
                component.Members
                    .Select(member => new CopelandNpmComponentMemberContract(
                        member.Name,
                        member.Properties.Select(ToNpmProperty).ToArray()))
                    .ToArray()))
            .ToArray();
        return new CopelandNpmPackageContract(
            contract.PackageName,
            contract.Version,
            exports,
            contract.MaterializationPath,
            contract.Materialized,
            true,
            false,
            components);
    }

    private static CopelandNpmComponentPropertyContract ToNpmProperty(CopelandProjectContextNpmProperty property)
        => new(property.Name, property.Type, property.Required);

    private static string FingerprintFor(IEnumerable<CopelandProjectSource> sources, CopelandProjectContextDescriptor descriptor, CopelandCompilationOptions options)
    {
        var builder = new StringBuilder();
        builder.Append("runtime=").Append(descriptor.JavaScriptRuntime).Append('\n');
        builder.Append("tsx=").Append(CopelandProjectTypes.ToTransport(options.ProjectTypes)).Append('\n');
        foreach (CopelandProjectSource source in sources.OrderBy(source => source.LogicalPath, StringComparer.Ordinal))
        {
            builder.Append(source.LogicalPath)
                .Append(':')
                .Append(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.SourceText))))
                .Append('\n');
        }

        foreach (CopelandProjectContextNpmContract contract in descriptor.NpmContracts.OrderBy(contract => contract.PackageName, StringComparer.Ordinal))
        {
            builder.Append(contract.PackageName)
                .Append('@')
                .Append(contract.Version)
                .Append(':')
                .Append(contract.Materialized)
                .Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}

public sealed class CopelandProjectContextException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record CopelandProjectContextDescriptor
{
    public string ProjectRoot { get; init; } = string.Empty;
    public List<CopelandProjectContextSource> Sources { get; init; } = [];
    public string JavaScriptRuntime { get; init; } = string.Empty;
    public string? TsXmlProfile { get; init; }
    public List<CopelandProjectContextNpmContract> NpmContracts { get; init; } = [];
}

public sealed record CopelandProjectContextSource
{
    public string LogicalPath { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
}

public sealed record CopelandProjectContextNpmContract
{
    public string PackageName { get; init; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? MaterializationPath { get; set; }
    public bool Materialized { get; set; }
    public List<CopelandProjectContextNpmExport> Exports { get; init; } = [];
    public List<CopelandProjectContextNpmComponent> Components { get; init; } = [];
}

internal sealed record CopelandResolvedPayload
{
    public string ProjectRoot { get; init; } = string.Empty;
    public string JavaScriptRuntime { get; init; } = string.Empty;
    public string? TsXmlProfile { get; init; }
    public List<CopelandProjectContextNpmContract> NpmContracts { get; init; } = [];
}

public sealed record CopelandProjectContextNpmExport
{
    public string Name { get; init; } = string.Empty;
    public List<string> Parameters { get; init; } = [];
    public string Result { get; init; } = string.Empty;
    public string? RemoteError { get; init; }
    public bool Promise { get; init; }
}

public sealed record CopelandProjectContextNpmComponent
{
    public string Name { get; init; } = string.Empty;
    public List<CopelandProjectContextNpmProperty> Properties { get; init; } = [];
    public List<CopelandProjectContextNpmMember> Members { get; init; } = [];
}

public sealed record CopelandProjectContextNpmMember
{
    public string Name { get; init; } = string.Empty;
    public List<CopelandProjectContextNpmProperty> Properties { get; init; } = [];
}

public sealed record CopelandProjectContextNpmProperty
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool Required { get; init; }
}

/// <summary>
/// Resolves a manifest project to the deterministic compiler-context descriptor
/// emitted by the normal TSPack materialization path. It never materializes
/// packages or starts browser lifecycle work itself.
/// </summary>
public static class CopelandProjectContextResolver
{
    public static string? DiscoverManifest(string sourceOrWorkspacePath)
    {
        string fullPath = Path.GetFullPath(sourceOrWorkspacePath);
        DirectoryInfo? directory = File.Exists(fullPath)
            ? new DirectoryInfo(Path.GetDirectoryName(fullPath)!)
            : new DirectoryInfo(fullPath);

        while (directory is not null)
        {
            string manifestPath = Path.Combine(directory.FullName, "manifest.tsx");
            if (File.Exists(manifestPath))
            {
                return manifestPath;
            }

            directory = directory.Parent;
        }

        return null;
    }

    public static CopelandProjectContext Load(string projectPath, string? sourcePath = null)
    {
        string fullProjectPath = Path.GetFullPath(projectPath);
        if (fullProjectPath.EndsWith(".request.json", StringComparison.OrdinalIgnoreCase))
        {
            CopelandProjectContext context = CopelandProjectContext.LoadResolvedContext(fullProjectPath);
            EnsureIncludesSource(context, sourcePath, fullProjectPath);
            return context;
        }

        if (!File.Exists(fullProjectPath))
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0001",
                $"Project manifest '{fullProjectPath}' was not found.");
        }

        if (!string.Equals(Path.GetFileName(fullProjectPath), "manifest.tsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0002",
                $"Project '{fullProjectPath}' is not a supported Copeland manifest or resolved project descriptor.");
        }

        string descriptorDirectory = Path.Combine(
            Path.GetDirectoryName(fullProjectPath)!,
            ".tspack",
            "build-manifests");
        if (!Directory.Exists(descriptorDirectory))
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0007",
                $"Manifest '{fullProjectPath}' was resolved, but compiler contracts have not been materialized. Run the normal TSPack build or sync command for this project.");
        }

        string[] descriptorPaths = Directory.EnumerateFiles(
                descriptorDirectory,
                "*.request.json",
                SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (descriptorPaths.Length == 0)
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0007",
                $"Manifest '{fullProjectPath}' was resolved, but no TSPack compiler-context descriptor exists. Run the normal TSPack build for this project.");
        }

        CopelandProjectContext[] candidates = descriptorPaths
            .Select(CopelandProjectContext.LoadResolvedContext)
            .Where(context => IncludesSource(context, sourcePath))
            .ToArray();
        if (candidates.Length == 0)
        {
            string sourceDescription = sourcePath is null
                ? "the requested project operation"
                : $"source '{Path.GetFullPath(sourcePath)}'";
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0008",
                $"{sourceDescription} is not included by manifest project '{fullProjectPath}'.");
        }

        if (candidates.Length > 1)
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0003",
                $"Manifest '{fullProjectPath}' has multiple compiler contexts for the requested operation. Specify a resolved project descriptor.");
        }

        return candidates[0];
    }

    public static CopelandProjectContext LoadFromSource(string sourcePath)
    {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        string? manifestPath = DiscoverManifest(fullSourcePath);
        if (manifestPath is null)
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0001",
                $"No project manifest was found for source '{fullSourcePath}'.");
        }

        return Load(manifestPath, fullSourcePath);
    }

    private static void EnsureIncludesSource(CopelandProjectContext context, string? sourcePath, string projectPath)
    {
        if (!IncludesSource(context, sourcePath))
        {
            throw new CopelandProjectContextException(
                "COPE-PROJECT-0008",
                $"Source '{Path.GetFullPath(sourcePath!)}' is not included by resolved project descriptor '{projectPath}'.");
        }
    }

    private static bool IncludesSource(CopelandProjectContext context, string? sourcePath)
    {
        if (sourcePath is null)
        {
            return true;
        }

        string fullSourcePath = Path.GetFullPath(sourcePath);
        return context.Sources.Any(source => string.Equals(
            Path.GetFullPath(source.SourcePath),
            fullSourcePath,
            StringComparison.OrdinalIgnoreCase));
    }
}

public static class CopelandProjectHostContracts
{
    public static CopelandJavaScriptHostModuleContract Browser()
    {
        var state = new CopelandJavaScriptHostType.TypeParameter("State");
        var @event = new CopelandJavaScriptHostType.TypeParameter("Event");
        return new CopelandJavaScriptHostModuleContract("@copeland/browser-v1", [
            new CopelandJavaScriptHostFunctionContract("setText", [CopelandJavaScriptHostType.String, CopelandJavaScriptHostType.String], CopelandJavaScriptHostType.Void),
            new CopelandJavaScriptHostFunctionContract("onClick", [CopelandJavaScriptHostType.String, new CopelandJavaScriptHostType.Callable([], CopelandJavaScriptHostType.Void)], CopelandJavaScriptHostType.Void),
            new CopelandJavaScriptHostFunctionContract("dispatch", [state, new CopelandJavaScriptHostType.Callable([state, @event], state), new CopelandJavaScriptHostType.Callable([state], CopelandJavaScriptHostType.Void)], new CopelandJavaScriptHostType.Callable([@event], CopelandJavaScriptHostType.Void), ["State", "Event"]),
            new CopelandJavaScriptHostFunctionContract("getMountElement", [CopelandJavaScriptHostType.String], new CopelandJavaScriptHostType.Named("ReactMountElement")),
            new CopelandJavaScriptHostFunctionContract("dispatchReact", [state, new CopelandJavaScriptHostType.Callable([state, @event], state), new CopelandJavaScriptHostType.Callable([state, new CopelandJavaScriptHostType.Callable([@event], CopelandJavaScriptHostType.Void)], CopelandJavaScriptHostType.Void)], new CopelandJavaScriptHostType.Callable([@event], CopelandJavaScriptHostType.Void), ["State", "Event"]),
            new CopelandJavaScriptHostFunctionContract("copyText", [CopelandJavaScriptHostType.String, new CopelandJavaScriptHostType.Callable([], CopelandJavaScriptHostType.Void), new CopelandJavaScriptHostType.Callable([], CopelandJavaScriptHostType.Void)], CopelandJavaScriptHostType.Void),
            new CopelandJavaScriptHostFunctionContract("getViewportWidth", [], CopelandJavaScriptHostType.Int),
            new CopelandJavaScriptHostFunctionContract("subscribeViewport", [new CopelandJavaScriptHostType.Callable([], CopelandJavaScriptHostType.Void)], new CopelandJavaScriptHostType.Callable([], CopelandJavaScriptHostType.Void)),
            new CopelandJavaScriptHostFunctionContract("scheduleRendererAttachment", [new CopelandJavaScriptHostType.Callable([], CopelandJavaScriptHostType.Void)], CopelandJavaScriptHostType.Void),
            new CopelandJavaScriptHostFunctionContract("attachRenderer", [CopelandJavaScriptHostType.String, CopelandJavaScriptHostType.String, CopelandJavaScriptHostType.String, CopelandJavaScriptHostType.String, CopelandJavaScriptHostType.String, CopelandJavaScriptHostType.String], CopelandJavaScriptHostType.Void),
            new CopelandJavaScriptHostFunctionContract("updateRenderer", [CopelandJavaScriptHostType.String, CopelandJavaScriptHostType.String, CopelandJavaScriptHostType.String, CopelandJavaScriptHostType.String], CopelandJavaScriptHostType.Void),
            new CopelandJavaScriptHostFunctionContract("detachRenderer", [CopelandJavaScriptHostType.String, CopelandJavaScriptHostType.String], CopelandJavaScriptHostType.Void),
            new CopelandJavaScriptHostFunctionContract("scheduleTextFit", [], CopelandJavaScriptHostType.Void),
        ]);
    }
}
