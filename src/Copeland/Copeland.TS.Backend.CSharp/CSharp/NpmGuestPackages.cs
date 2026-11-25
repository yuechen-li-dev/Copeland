using System.Text.Json;
using Copeland.TS.Compiler;

namespace Copeland.TS.Backend.CSharp;

public sealed record NpmGuestFile(string Source, string Target);

/// <summary>Projects existing npm materializations into a portable Node directory.</summary>
public static class NpmGuestPackages
{
    public static IReadOnlyList<NpmGuestFile> FromLockFile(
        string projectRoot, string lockPath, IReadOnlyList<CopelandNpmPackageContract> contracts)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(lockPath));
        if (!document.RootElement.TryGetProperty("packages", out JsonElement packageMap) || packageMap.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The npm guest requires a package-lock.json with a packages map. Run npm ci after updating the lockfile.");
        }

        foreach (CopelandNpmPackageContract contract in contracts)
        {
            string key = "node_modules/" + PackageRoot(contract.PackageName);
            if (!packageMap.TryGetProperty(key, out JsonElement entry) || Version(entry) != contract.Version)
            {
                throw new InvalidDataException($"The npm contract for '{contract.PackageName}@{contract.Version}' does not match '{lockPath}'.");
            }
        }

        var files = new List<NpmGuestFile>();
        foreach (JsonProperty package in packageMap.EnumerateObject())
        {
            if (package.Name == "" || Flag(package.Value, "dev"))
            {
                continue;
            }
            string[] segments = package.Name.Split('/');
            if (segments[0] != "node_modules" || segments.Any(InvalidSegment) || Flag(package.Value, "link"))
            {
                throw new InvalidDataException($"The npm guest cannot deploy linked or non-node_modules entry '{package.Name}'.");
            }

            string directory = Path.Combine(projectRoot, Path.Combine(segments));
            if (!Directory.Exists(directory) && Flag(package.Value, "optional"))
            {
                continue;
            }

            using JsonDocument installed = ReadPackage(directory, Version(package.Value));
            AddFiles(directory, package.Name, files);
        }
        return files;
    }

    public static IReadOnlyList<NpmGuestFile> FromTarget(
        string projectRoot, IReadOnlyList<CompilerPackageBinding> bindings, IReadOnlyList<CopelandNpmPackageContract> contracts)
    {
        var files = new List<NpmGuestFile>();
        var roots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (CopelandNpmPackageContract contract in contracts)
        {
            string name = PackageRoot(contract.PackageName);
            CompilerPackageBinding? binding = bindings.FirstOrDefault(candidate =>
                candidate.MaterializationName == name && candidate.SemanticIdentity.StartsWith("npm:", StringComparison.Ordinal)
                && candidate.Version == contract.Version);
            if (binding is null)
            {
                throw new InvalidDataException($"No resolved target package binding for '{contract.PackageName}@{contract.Version}'.");
            }
            roots[name] = ResolveInside(binding.MaterializationPath, projectRoot);
        }
        foreach (var (name, directory) in roots)
        {
            string version = contracts.First(contract => PackageRoot(contract.PackageName) == name).Version;
            VisitTargetPackage(projectRoot, directory, "node_modules/" + name, version, roots, files);
        }
        return files;
    }

    private static void VisitTargetPackage(
        string projectRoot, string directory, string target, string? version,
        IReadOnlyDictionary<string, string> ancestors, List<NpmGuestFile> files)
    {
        using JsonDocument installed = ReadPackage(directory, version);
        AddFiles(directory, target, files);
        JsonElement metadata = installed.RootElement;
        var edges = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (string kind in new[] { "dependencies", "optionalDependencies" })
        {
            if (metadata.TryGetProperty(kind, out JsonElement entries) && entries.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty dependency in entries.EnumerateObject())
                {
                    edges[dependency.Name] = kind == "optionalDependencies";
                }
            }
        }
        if (metadata.TryGetProperty("peerDependencies", out JsonElement peers) && peers.ValueKind == JsonValueKind.Object)
        {
            metadata.TryGetProperty("peerDependenciesMeta", out JsonElement peerMetadata);
            foreach (JsonProperty peer in peers.EnumerateObject())
            {
                bool optional = peerMetadata.ValueKind == JsonValueKind.Object
                    && peerMetadata.TryGetProperty(peer.Name, out JsonElement info) && Flag(info, "optional");
                edges.TryAdd(peer.Name, optional);
            }
        }

        var children = new List<(string Name, string Directory)>();
        var visible = new Dictionary<string, string>(ancestors, StringComparer.Ordinal);
        foreach (var (name, optional) in edges)
        {
            if (name != PackageRoot(name))
            {
                throw new InvalidDataException($"Invalid npm dependency name '{name}'.");
            }
            string? child = FindDependency(projectRoot, directory, name);
            if (child is null)
            {
                if (optional) continue;
                throw new InvalidDataException($"Resolved npm package '{directory}' is missing '{name}'. Run tspack sync.");
            }
            if (!visible.TryGetValue(name, out string? inherited) || inherited != child)
            {
                if (target.Split('/').Length > 64)
                {
                    throw new InvalidDataException($"The npm dependency graph is too deep at '{target}'.");
                }
                visible[name] = child;
                children.Add((name, child));
            }
        }
        foreach (var child in children)
        {
            VisitTargetPackage(projectRoot, child.Directory, target + "/node_modules/" + child.Name, null, visible, files);
        }
    }

    private static string? FindDependency(string projectRoot, string directory, string name)
    {
        for (DirectoryInfo? current = new(directory); current is not null && IsInside(current.FullName, projectRoot); current = current.Parent)
        {
            if (current.Name == "node_modules") continue;
            string candidate = Path.Combine(current.FullName, "node_modules", name);
            if (File.Exists(Path.Combine(candidate, "package.json")))
            {
                return ResolveInside(candidate, projectRoot);
            }
        }
        return null;
    }

    private static bool IsInside(string path, string root)
        => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Contains("..", StringComparer.Ordinal)
            && !Path.IsPathRooted(Path.GetRelativePath(root, path));

    private static string ResolveInside(string path, string projectRoot)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            throw new InvalidDataException($"Missing npm package at '{path}'. Install the locked dependencies before building.");
        }
        string resolved = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName;
        if (!IsInside(resolved, projectRoot))
        {
            throw new InvalidDataException($"Resolved npm package '{resolved}' is outside '{projectRoot}'.");
        }
        return resolved;
    }

    private static void AddFiles(string directory, string target, List<NpmGuestFile> files)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(directory).OrderBy(path => path, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            if (name == "node_modules") continue;
            FileAttributes attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException($"Cannot deploy an npm package containing a link at '{path}'.");
            }
            string targetPath = target + "/" + name;
            if (attributes.HasFlag(FileAttributes.Directory)) AddFiles(path, targetPath, files);
            else files.Add(new(path, targetPath));
        }
    }

    private static JsonDocument ReadPackage(string directory, string? version)
    {
        string path = Path.Combine(directory, "package.json");
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"Missing npm package at '{directory}'. Install the locked dependencies before building.");
        }
        JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (version is not null && Version(document.RootElement) != version)
        {
            document.Dispose();
            throw new InvalidDataException($"Installed npm package at '{directory}' does not match locked version '{version}'.");
        }
        return document;
    }

    private static string? Version(JsonElement entry)
        => entry.TryGetProperty("version", out JsonElement version) ? version.GetString() : null;

    private static bool Flag(JsonElement entry, string name)
        => entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static bool InvalidSegment(string segment)
        => segment.Length == 0 || segment is "." or ".." || segment.IndexOfAny(['\\', ':']) >= 0;

    private static string PackageRoot(string specifier)
    {
        string[] parts = specifier.Split('/');
        int count = specifier.StartsWith('@') ? 2 : 1;
        if (parts.Length < count || parts.Take(count).Any(InvalidSegment))
        {
            throw new InvalidDataException($"Invalid npm package name '{specifier}'.");
        }
        return string.Join('/', parts.Take(count));
    }
}
