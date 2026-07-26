namespace Copeland.TS.Compiler;

/// <summary>Explicit project configuration for the deliberately small npm interop surface.</summary>
public sealed record CopelandNpmPackageContract(
    string PackageName,
    string Version,
    IReadOnlyList<CopelandNpmFunctionContract> Exports);

public sealed record CopelandNpmFunctionContract(
    string ExportName,
    IReadOnlyList<string> ParameterTypes,
    string ResultType,
    string? RemoteErrorType = null,
    bool IsPromise = false);

internal sealed class CopelandNpmContractResolver(IEnumerable<CopelandNpmPackageContract> packages)
{
    private readonly Dictionary<string, CopelandNpmPackageContract> _packages = packages.ToDictionary(package => package.PackageName, StringComparer.Ordinal);

    public bool TryGetPackage(string name, out CopelandNpmPackageContract? package)
        => _packages.TryGetValue(name, out package);
}
