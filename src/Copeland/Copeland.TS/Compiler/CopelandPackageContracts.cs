using System.Text.Json;

namespace Copeland.TS.Compiler;

/// <summary>
/// The small semantic contract shipped by a native Copeland NuGet package.
/// NuGet owns acquisition and asset selection; this model intentionally owns
/// only module names, exports, nominal identity, and CLR implementation shape.
/// </summary>
public sealed record CopelandPackageContract(
    string SourcePath,
    string PackageId,
    string MinimumCompilerVersion,
    IReadOnlyList<CopelandPackageModuleContract> Modules);

public sealed record CopelandPackageModuleContract(
    string Specifier,
    string NominalScope,
    IReadOnlyList<CopelandPackageExportContract> Exports,
    CopelandClrBinaryRealization? ClrRealization);

public sealed record CopelandPackageExportContract(
    string Name,
    string Kind,
    IReadOnlyList<CopelandPackageParameterContract> Parameters,
    string ReturnType,
    string ClrType,
    string ClrMethod);

public sealed record CopelandPackageParameterContract(string Name, string Type);

public sealed record CopelandClrBinaryRealization(string AssemblyIdentity);

public enum CopelandPackageBackend
{
    Clr,
    JavaScriptNode,
    JavaScriptBrowser,
}

public sealed class CopelandPackageContractMap
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<CopelandPackageModuleContract>> _modules;

    public CopelandPackageContractMap(IEnumerable<CopelandPackageContract> contracts)
    {
        Contracts = contracts.OrderBy(contract => contract.PackageId, StringComparer.Ordinal).ToArray();
        _modules = Contracts
            .SelectMany(contract => contract.Modules.Select(module => (contract, module)))
            .GroupBy(pair => pair.module.Specifier, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CopelandPackageModuleContract>)group.Select(pair => pair.module).ToArray(),
                StringComparer.Ordinal);
    }

    public IReadOnlyList<CopelandPackageContract> Contracts { get; }

    public bool TryGetModules(string specifier, out IReadOnlyList<CopelandPackageModuleContract> modules)
        => _modules.TryGetValue(specifier, out modules!);
}

public static class CopelandPackageContractReader
{
    public const int SchemaVersion = 1;
    public const string CompilerVersion = "1.0";

    public static bool TryRead(string path, out CopelandPackageContract? contract, out string? error)
    {
        contract = null;
        error = null;
        if (!File.Exists(path))
        {
            error = "COPE-PACKAGE-0001: Copeland package contract item path does not exist.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number
                || version.GetInt32() != SchemaVersion)
            {
                error = "COPE-PACKAGE-0003: Copeland package contract has an unsupported schema version.";
                return false;
            }

            string packageId = RequiredString(root, "package", "id");
            string minimum = RequiredString(root, "compiler", "minimum");
            if (!IsCompilerCompatible(minimum))
            {
                error = $"COPE-PACKAGE-0004: Package '{packageId}' requires Copeland compiler {minimum}, but this compiler is {CompilerVersion}.";
                return false;
            }
            var assemblyIdentities = RequiredArray(root, "assemblies").EnumerateArray()
                .Select(assembly => RequiredString(assembly, "identity"))
                .ToHashSet(StringComparer.Ordinal);

            var modules = new List<CopelandPackageModuleContract>();
            foreach (JsonElement module in RequiredArray(root, "modules").EnumerateArray())
            {
                string specifier = RequiredString(module, "specifier");
                string nominalScope = RequiredString(module, "nominalScope");
                CopelandClrBinaryRealization? clr = null;
                if (module.TryGetProperty("realizations", out JsonElement realizations)
                    && realizations.TryGetProperty("clr", out JsonElement clrElement))
                {
                    if (RequiredString(clrElement, "kind") != "binary")
                    {
                        throw new InvalidDataException("CLR realization kind must be 'binary'.");
                    }
                    clr = new CopelandClrBinaryRealization(RequiredString(clrElement, "assembly"));
                    if (!assemblyIdentities.Contains(clr.AssemblyIdentity))
                    {
                        throw new InvalidDataException($"CLR realization assembly '{clr.AssemblyIdentity}' is not declared in assemblies.");
                    }
                }

                var exports = new List<CopelandPackageExportContract>();
                foreach (JsonElement export in RequiredArray(module, "exports").EnumerateArray())
                {
                    JsonElement signature = RequiredObject(export, "contract");
                    var parameters = RequiredArray(signature, "parameters").EnumerateArray()
                        .Select(parameter => new CopelandPackageParameterContract(RequiredString(parameter, "name"), RequiredString(parameter, "type")))
                        .ToArray();
                    JsonElement binding = RequiredObject(export, "clr");
                    exports.Add(new CopelandPackageExportContract(
                        RequiredString(export, "name"),
                        RequiredString(export, "kind"),
                        parameters,
                        RequiredString(signature, "returnType"),
                        RequiredString(binding, "type"),
                        RequiredString(binding, "method")));
                }

                modules.Add(new CopelandPackageModuleContract(specifier, nominalScope, exports, clr));
            }

            if (modules.GroupBy(module => module.Specifier, StringComparer.Ordinal).Any(group => group.Count() > 1))
            {
                error = $"COPE-PACKAGE-0005: Package '{packageId}' declares the same Copeland module specifier more than once.";
                return false;
            }

            contract = new CopelandPackageContract(Path.GetFullPath(path), packageId, minimum, modules);
            return true;
        }
        catch (JsonException exception)
        {
            error = "COPE-PACKAGE-0002: Copeland package contract contains malformed JSON: " + exception.Message;
            return false;
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        {
            error = "COPE-PACKAGE-0002: Copeland package contract is malformed: " + exception.Message;
            return false;
        }
    }

    private static bool IsCompilerCompatible(string minimum)
        => Version.TryParse(minimum, out Version? required)
            && Version.TryParse(CompilerVersion, out Version? current)
            && current >= required;

    private static JsonElement RequiredObject(JsonElement parent, string name)
        => parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidDataException($"Property '{name}' must be an object.");

    private static JsonElement RequiredArray(JsonElement parent, string name)
        => parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidDataException($"Property '{name}' must be an array.");

    private static string RequiredString(JsonElement parent, string name)
        => parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidDataException($"Property '{name}' must be a non-empty string.");

    private static string RequiredString(JsonElement parent, string objectName, string propertyName)
        => RequiredString(RequiredObject(parent, objectName), propertyName);
}
