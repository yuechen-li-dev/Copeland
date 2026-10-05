using System.Security.Cryptography;
using System.Text;

namespace Copeland.TS.Compiler;

public interface ICopelandAssetSource
{
    bool TryRead(string normalizedPath, out string? sourceText);
}

public sealed record CopelandAssetDependency(
    string NormalizedPath,
    string Sha256);

internal sealed record CopelandResolvedAsset(
    string NormalizedPath,
    string SourceText,
    string Sha256);

internal sealed class CopelandAssetResolver(
    string sourcePath,
    string projectRoot,
    ICopelandAssetSource assetSource)
{
    private readonly string _sourcePath = Path.GetFullPath(sourcePath);
    private readonly string _projectRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
    private readonly ICopelandAssetSource _assetSource = assetSource;
    private readonly Dictionary<string, CopelandResolvedAsset> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CopelandAssetDependency> _dependencies = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CopelandAssetDependency> Dependencies => _dependencies.Values
        .OrderBy(dependency => dependency.NormalizedPath, StringComparer.Ordinal)
        .ToArray();

    public bool TryResolve(string authoredPath, out CopelandResolvedAsset? asset, out string? error)
    {
        asset = null;
        error = null;

        if (string.IsNullOrWhiteSpace(authoredPath))
        {
            error = "The TSON asset path cannot be blank.";
            return false;
        }

        string portablePath = authoredPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathFullyQualified(portablePath))
        {
            error = $"TSON asset path '{authoredPath}' must be relative.";
            return false;
        }

        string extension = portablePath.EndsWith(".obj.ts", StringComparison.OrdinalIgnoreCase)
            ? ".obj.ts"
            : Path.GetExtension(portablePath);
        if (!string.Equals(extension, ".obj.ts", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".tson", StringComparison.OrdinalIgnoreCase))
        {
            error = $"TSON asset path '{authoredPath}' must end in '.obj.ts' or '.tson'.";
            return false;
        }

        string sourceDirectory = Path.GetDirectoryName(_sourcePath)!;
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(portablePath, sourceDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"TSON asset path '{authoredPath}' is invalid.";
            return false;
        }

        if (!IsWithinProjectRoot(fullPath))
        {
            error = $"TSON asset path '{authoredPath}' escapes the compilation root.";
            return false;
        }

        string normalizedPath = NormalizeForEvidence(Path.GetRelativePath(_projectRoot, fullPath));
        if (_cache.TryGetValue(fullPath, out asset))
        {
            return true;
        }

        if (!_assetSource.TryRead(fullPath, out string? sourceText) || sourceText is null)
        {
            error = $"TSON asset '{normalizedPath}' could not be read.";
            return false;
        }

        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceText))).ToLowerInvariant();
        asset = new CopelandResolvedAsset(normalizedPath, sourceText, hash);
        _cache.Add(fullPath, asset);
        _dependencies.TryAdd(fullPath, new CopelandAssetDependency(normalizedPath, hash));
        return true;
    }

    private bool IsWithinProjectRoot(string path)
    {
        string relative = Path.GetRelativePath(_projectRoot, path);
        return !Path.IsPathFullyQualified(relative)
            && !string.Equals(relative, "..", StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string NormalizeForEvidence(string path)
    {
        return path.Replace('\\', '/');
    }
}
