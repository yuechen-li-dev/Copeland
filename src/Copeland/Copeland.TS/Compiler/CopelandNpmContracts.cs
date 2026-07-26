using Copeland.TS.Manifest;

namespace Copeland.TS.Compiler;

/// <summary>
/// A deliberately narrow, already-resolved npm dependency description.  This is
/// compiler input, not a package-manager request: package acquisition, version
/// selection, lockfiles, and lifecycle execution stay outside Copeland.
/// </summary>
public sealed record CopelandNpmPackageContract(
    string PackageName,
    string Version,
    IReadOnlyList<CopelandNpmFunctionContract> Exports,
    string? MaterializationPath = null,
    bool IsMaterialized = true,
    bool IsAvailableToJavaScript = true,
    bool IsAvailableToClrSidecar = true);

public sealed record CopelandNpmFunctionContract(
    string ExportName,
    IReadOnlyList<string> ParameterTypes,
    string ResultType,
    string? RemoteErrorType = null,
    bool IsPromise = false);

/// <summary>
/// The compilation-owned projection of validated manifest IR.  Tests may inject
/// this shape directly, but production callers obtain it from the manifest
/// projection rather than maintaining a second package registry.
/// </summary>
public sealed class CopelandNpmDependencyGraph(IEnumerable<CopelandNpmPackageContract> packages)
{
    private readonly Dictionary<string, CopelandNpmPackageContract> _packages = packages.ToDictionary(package => package.PackageName, StringComparer.Ordinal);

    public bool TryGetPackage(string name, out CopelandNpmPackageContract? package)
        => _packages.TryGetValue(name, out package);
}

internal sealed class CopelandNpmContractResolver(CopelandNpmDependencyGraph graph)
{
    public bool TryGetPackage(string name, out CopelandNpmPackageContract? package)
        => graph.TryGetPackage(name, out package);
}

/// <summary>
/// Reads only the resolved npm rows exposed by validated manifest IR.  The
/// projection deliberately does not interpret ranges or inspect node_modules.
/// A row must already contain a resolved version, materialization status, and
/// the static function contract selected by TSPack.
/// </summary>
public static class CopelandNpmManifestProjection
{
    public static CopelandNpmDependencyGraph Create(CopelandManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var packages = new List<CopelandNpmPackageContract>();
        foreach (ManifestPackage package in manifest.Packages)
        {
            if (package.Dependencies is not ManifestValue.Object dependencies)
            {
                continue;
            }

            foreach (ManifestValue dependency in dependencies.Properties.Values)
            {
                if (dependency is not ManifestValue.Object row
                    || !TryGetString(row, "kind", out string? kind)
                    || !string.Equals(kind, "npm", StringComparison.Ordinal)
                    || !TryGetString(row, "package", out string? packageName)
                    || !TryGetString(row, "resolvedVersion", out string? version))
                {
                    continue;
                }

                bool materialized = TryGetBoolean(row, "materialized", out bool materializedValue) && materializedValue;
                bool javascript = !TryGetBoolean(row, "javascriptAvailable", out bool javascriptValue) || javascriptValue;
                bool clr = !TryGetBoolean(row, "clrSidecarAvailable", out bool clrValue) || clrValue;
                string? materializationPath = TryGetString(row, "materialization", out string? path) ? path : null;
                IReadOnlyList<CopelandNpmFunctionContract> exports = ReadExports(row);
                packages.Add(new CopelandNpmPackageContract(packageName!, version!, exports, materializationPath, materialized, javascript, clr));
            }
        }

        return new CopelandNpmDependencyGraph(packages);
    }

    private static IReadOnlyList<CopelandNpmFunctionContract> ReadExports(ManifestValue.Object row)
    {
        if (!row.Properties.TryGetValue("exports", out ManifestValue? value)
            || value is not ManifestValue.Array exports)
        {
            return [];
        }

        var result = new List<CopelandNpmFunctionContract>();
        foreach (ManifestValue entry in exports.Values)
        {
            if (entry is not ManifestValue.Object contract
                || !TryGetString(contract, "name", out string? name)
                || !TryGetString(contract, "result", out string? resultType))
            {
                continue;
            }

            string[] parameters = contract.Properties.TryGetValue("parameters", out ManifestValue? parametersValue)
                && parametersValue is ManifestValue.Array parameterValues
                ? parameterValues.Values.OfType<ManifestValue.String>().Select(parameter => parameter.Text).ToArray()
                : [];
            string? remoteError = TryGetString(contract, "remoteError", out string? error) ? error : null;
            bool isPromise = TryGetBoolean(contract, "promise", out bool promise) && promise;
            result.Add(new CopelandNpmFunctionContract(name!, parameters, resultType!, remoteError, isPromise));
        }

        return result;
    }

    private static bool TryGetString(ManifestValue.Object value, string name, out string? text)
    {
        text = value.Properties.TryGetValue(name, out ManifestValue? item) && item is ManifestValue.String stringValue
            ? stringValue.Text
            : null;
        return text is not null;
    }

    private static bool TryGetBoolean(ManifestValue.Object value, string name, out bool result)
    {
        result = value.Properties.TryGetValue(name, out ManifestValue? item) && item is ManifestValue.Boolean booleanValue && booleanValue.BooleanValue;
        return value.Properties.TryGetValue(name, out item) && item is ManifestValue.Boolean;
    }
}
