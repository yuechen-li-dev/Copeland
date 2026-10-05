using Copeland.TS.Manifest;
using System.Text.Json;

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
    bool IsAvailableToClrSidecar = true,
    IReadOnlyList<CopelandNpmComponentContract>? Components = null,
    string? SourcePath = null)
{
    public IReadOnlyList<CopelandNpmComponentContract> ComponentExports { get; } = Components ?? [];
}

public sealed record CopelandNpmFunctionContract(
    string ExportName,
    IReadOnlyList<string> ParameterTypes,
    string ResultType,
    string? RemoteErrorType = null,
    bool IsPromise = false);

/// <summary>
/// A deliberately bounded projection of a React component export. This is not
/// a TypeScript declaration model: it names only the component values and
/// props selected by the package owner for this Copeland target.
/// </summary>
public sealed record CopelandNpmComponentContract(
    string ExportName,
    IReadOnlyList<CopelandNpmComponentPropertyContract>? Properties = null,
    IReadOnlyList<CopelandNpmComponentMemberContract>? Members = null)
{
    public IReadOnlyList<CopelandNpmComponentPropertyContract> ComponentProperties { get; } = Properties ?? [];
    public IReadOnlyList<CopelandNpmComponentMemberContract> CompoundMembers { get; } = Members ?? [];
}

public sealed record CopelandNpmComponentMemberContract(
    string MemberName,
    IReadOnlyList<CopelandNpmComponentPropertyContract> Properties);

public sealed record CopelandNpmComponentPropertyContract(
    string PropertyName,
    string TypeName,
    bool IsRequired = false);

/// <summary>
/// The compilation-owned projection of validated manifest IR.  Tests may inject
/// this shape directly, but production callers obtain it from the manifest
/// projection rather than maintaining a second package registry.
/// </summary>
public sealed class CopelandNpmDependencyGraph
{
    private readonly Dictionary<string, CopelandNpmPackageContract> _packages;

    public CopelandNpmDependencyGraph(IEnumerable<CopelandNpmPackageContract> packages)
    {
        Packages = packages
            .OrderBy(package => package.PackageName, StringComparer.Ordinal)
            .ToArray();
        _packages = Packages.ToDictionary(package => package.PackageName, StringComparer.Ordinal);
    }

    public IReadOnlyList<CopelandNpmPackageContract> Packages { get; }

    public bool TryGetPackage(string name, out CopelandNpmPackageContract? package)
        => _packages.TryGetValue(name, out package);
}

/// <summary>
/// Reads the exact npm contract item supplied by a project build target. This
/// is a resolved package-manager result: the compiler neither inspects
/// node_modules nor resolves package versions.
/// </summary>
public static class CopelandNpmContractReader
{
    public const int SchemaVersion = 1;

    public static bool TryRead(string path, out CopelandNpmPackageContract? contract, out string? error)
    {
        contract = null;
        error = null;
        if (!File.Exists(path))
        {
            error = "COPE-NPM-CONTRACT-0001: npm contract item path does not exist.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out JsonElement schemaVersion)
                || schemaVersion.ValueKind != JsonValueKind.Number
                || schemaVersion.GetInt32() != SchemaVersion)
            {
                error = "COPE-NPM-CONTRACT-0002: npm contract has an unsupported schema version.";
                return false;
            }

            string packageName = RequiredString(root, "package");
            string version = RequiredString(root, "version");
            bool materialized = OptionalBoolean(root, "materialized", defaultValue: true);
            bool javascriptAvailable = OptionalBoolean(root, "javascriptAvailable", defaultValue: true);
            bool clrSidecarAvailable = OptionalBoolean(root, "clrSidecarAvailable", defaultValue: true);
            string? materialization = OptionalString(root, "materialization");
            CopelandNpmFunctionContract[] exports = ReadExports(root).ToArray();
            CopelandNpmComponentContract[] components = ReadComponents(root).ToArray();
            contract = new CopelandNpmPackageContract(
                packageName,
                version,
                exports,
                materialization,
                materialized,
                javascriptAvailable,
                clrSidecarAvailable,
                components,
                Path.GetFullPath(path));
            return true;
        }
        catch (JsonException exception)
        {
            error = "COPE-NPM-CONTRACT-0003: npm contract contains malformed JSON: " + exception.Message;
            return false;
        }
        catch (InvalidDataException exception)
        {
            error = "COPE-NPM-CONTRACT-0003: npm contract is malformed: " + exception.Message;
            return false;
        }
    }

    private static IEnumerable<CopelandNpmFunctionContract> ReadExports(JsonElement root)
    {
        if (!root.TryGetProperty("exports", out JsonElement exports) || exports.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement export in exports.EnumerateArray())
        {
            string name = RequiredString(export, "name");
            string result = RequiredString(export, "result");
            string[] parameters = export.TryGetProperty("parameters", out JsonElement parameterElement)
                && parameterElement.ValueKind == JsonValueKind.Array
                ? parameterElement.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? throw new InvalidDataException("npm export parameter types must be strings.")
                    : throw new InvalidDataException("npm export parameter types must be strings.")).ToArray()
                : [];
            yield return new CopelandNpmFunctionContract(
                name,
                parameters,
                result,
                OptionalString(export, "remoteError"),
                OptionalBoolean(export, "promise", defaultValue: false));
        }
    }

    private static IEnumerable<CopelandNpmComponentContract> ReadComponents(JsonElement root)
    {
        if (!root.TryGetProperty("components", out JsonElement components) || components.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement component in components.EnumerateArray())
        {
            yield return new CopelandNpmComponentContract(
                RequiredString(component, "name"),
                ReadProperties(component, "properties").ToArray(),
                ReadMembers(component).ToArray());
        }
    }

    private static IEnumerable<CopelandNpmComponentMemberContract> ReadMembers(JsonElement component)
    {
        if (!component.TryGetProperty("members", out JsonElement members) || members.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement member in members.EnumerateArray())
        {
            yield return new CopelandNpmComponentMemberContract(
                RequiredString(member, "name"),
                ReadProperties(member, "properties").ToArray());
        }
    }

    private static IEnumerable<CopelandNpmComponentPropertyContract> ReadProperties(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out JsonElement properties) || properties.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement property in properties.EnumerateArray())
        {
            yield return new CopelandNpmComponentPropertyContract(
                RequiredString(property, "name"),
                RequiredString(property, "type"),
                OptionalBoolean(property, "required", defaultValue: false));
        }
    }

    private static string RequiredString(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidDataException("Property '" + name + "' must be a non-empty string.");

    private static string? OptionalString(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool OptionalBoolean(JsonElement element, string name, bool defaultValue)
        => element.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind == JsonValueKind.True
                ? true
                : value.ValueKind == JsonValueKind.False
                    ? false
                    : throw new InvalidDataException("Property '" + name + "' must be a boolean.")
            : defaultValue;
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
                IReadOnlyList<CopelandNpmComponentContract> components = ReadComponents(row);
                packages.Add(new CopelandNpmPackageContract(packageName!, version!, exports, materializationPath, materialized, javascript, clr, components));
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

    private static IReadOnlyList<CopelandNpmComponentContract> ReadComponents(ManifestValue.Object row)
    {
        if (!row.Properties.TryGetValue("components", out ManifestValue? value)
            || value is not ManifestValue.Array components)
        {
            return [];
        }

        var result = new List<CopelandNpmComponentContract>();
        foreach (ManifestValue entry in components.Values)
        {
            if (entry is not ManifestValue.Object component
                || !TryGetString(component, "name", out string? name))
            {
                continue;
            }

            result.Add(new CopelandNpmComponentContract(
                name!,
                ReadComponentProperties(component, "properties"),
                ReadComponentMembers(component)));
        }

        return result;
    }

    private static IReadOnlyList<CopelandNpmComponentMemberContract> ReadComponentMembers(ManifestValue.Object component)
    {
        if (!component.Properties.TryGetValue("members", out ManifestValue? value)
            || value is not ManifestValue.Array members)
        {
            return [];
        }

        var result = new List<CopelandNpmComponentMemberContract>();
        foreach (ManifestValue entry in members.Values)
        {
            if (entry is ManifestValue.Object member
                && TryGetString(member, "name", out string? name))
            {
                result.Add(new CopelandNpmComponentMemberContract(name!, ReadComponentProperties(member, "properties")));
            }
        }

        return result;
    }

    private static IReadOnlyList<CopelandNpmComponentPropertyContract> ReadComponentProperties(ManifestValue.Object owner, string propertyName)
    {
        if (!owner.Properties.TryGetValue(propertyName, out ManifestValue? value)
            || value is not ManifestValue.Array properties)
        {
            return [];
        }

        var result = new List<CopelandNpmComponentPropertyContract>();
        foreach (ManifestValue entry in properties.Values)
        {
            if (entry is not ManifestValue.Object property
                || !TryGetString(property, "name", out string? name)
                || !TryGetString(property, "type", out string? type))
            {
                continue;
            }

            bool required = TryGetBoolean(property, "required", out bool requiredValue) && requiredValue;
            result.Add(new CopelandNpmComponentPropertyContract(name!, type!, required));
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
