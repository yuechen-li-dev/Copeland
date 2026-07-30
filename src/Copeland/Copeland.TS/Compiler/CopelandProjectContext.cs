using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
            TsXmlProfile = string.Equals(descriptor.TsXmlProfile, "react-m0", StringComparison.OrdinalIgnoreCase)
                ? CopelandTsXmlProfile.ReactM0
                : CopelandTsXmlProfile.None,
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
        builder.Append("tsx=").Append(options.TsXmlProfile).Append('\n');
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
    public string Version { get; init; } = string.Empty;
    public string? MaterializationPath { get; init; }
    public bool Materialized { get; init; }
    public List<CopelandProjectContextNpmExport> Exports { get; init; } = [];
    public List<CopelandProjectContextNpmComponent> Components { get; init; } = [];
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
            CopelandProjectContext context = CopelandProjectContext.Load(fullProjectPath);
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
            .Select(CopelandProjectContext.Load)
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
