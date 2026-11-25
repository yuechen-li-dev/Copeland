using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Copeland.TS.Mir;

namespace Copeland.Cli;

internal static partial class TsclBuildContract
{
    private const string NugetWorkerConfigName = "nuget-worker.json";
    private const string NugetWorkerFolder = "copeland-nuget";

    private sealed record NugetWorkerPackage(string Id, string Version);
    private sealed record NugetSignature(
        string Package, string Export, string[] Parameters, string[]? WireParameters, NugetWorkerBinding Binding);
    private sealed record NugetWorkerConfiguration(
        IReadOnlyList<NugetWorkerPackage> Packages, IReadOnlyList<NugetWorkerBinding> Operations,
        IReadOnlyList<NugetSignature> Signatures, IReadOnlyDictionary<string, string> BrowserTargets,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Records, string Fingerprint);

    private static NugetWorkerConfiguration? ReadNugetWorker(string projectRoot)
    {
        string path = Path.Combine(projectRoot, NugetWorkerConfigName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string text = File.ReadAllText(path);
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            int schemaVersion = root.GetProperty("schemaVersion").GetInt32();
            if (schemaVersion is not (1 or 2 or 3))
            {
                throw new InvalidDataException("The generated NuGet worker requires schemaVersion 1, 2, or 3.");
            }
            var records = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            if (schemaVersion == 3 && root.TryGetProperty("records", out JsonElement definitions))
            {
                foreach (JsonProperty definition in definitions.EnumerateObject())
                {
                    var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (!IsWorkerIdentifier(definition.Name) || definition.Name is "NugetError" or "Option"
                        || IsNugetScalar(definition.Name) || !records.TryAdd(definition.Name, fields))
                    {
                        throw new InvalidDataException($"Invalid or duplicate NuGet DTO record '{definition.Name}'.");
                    }
                    foreach (JsonProperty field in definition.Value.EnumerateObject())
                    {
                        if (!IsWorkerIdentifier(field.Name) || field.Value.GetString() is not string fieldType
                            || !fields.TryAdd(field.Name, fieldType))
                        {
                            throw new InvalidDataException($"Invalid or duplicate NuGet DTO field '{definition.Name}.{field.Name}'.");
                        }
                    }
                }
                foreach ((string name, IReadOnlyDictionary<string, string> fields) in records)
                {
                    foreach ((string field, string fieldType) in fields)
                    {
                        ValidateNugetWireType(fieldType, records, $"{name}.{field}");
                    }
                }
            }

            var packages = new List<NugetWorkerPackage>();
            var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement row in root.GetProperty("packages").EnumerateArray())
            {
                string id = RequiredWorkerString(row, "id");
                string version = RequiredWorkerString(row, "version");
                if (!id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
                    || !char.IsAsciiDigit(version[0])
                    || !version.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '+'))
                {
                    throw new InvalidDataException($"Expected a NuGet package ID and exact version for '{id}'.");
                }
                if (!packageIds.Add(id))
                {
                    throw new InvalidDataException($"Duplicate NuGet package '{id}'.");
                }
                packages.Add(new(id, version));
            }

            var operations = new List<NugetWorkerBinding>();
            var operationNames = new HashSet<string>(StringComparer.Ordinal);
            var signatures = new List<NugetSignature>();
            var exports = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement row in root.GetProperty("operations").EnumerateArray())
            {
                string name = RequiredWorkerString(row, "name");
                string type = RequiredWorkerString(row, "type");
                string method = RequiredWorkerString(row, "method");
                if (!name.All(character => char.IsAsciiLetterOrDigit(character) || character is ':' or '.' or '-' or '_')
                    || !type.Split('.').All(IsWorkerIdentifier) || !IsWorkerIdentifier(method))
                {
                    throw new InvalidDataException($"NuGet operation '{name}' requires a literal name and qualified CLR static member.");
                }
                if (!operationNames.Add(name))
                {
                    throw new InvalidDataException($"Duplicate NuGet worker operation '{name}'.");
                }
                var binding = new NugetWorkerBinding(name, type, method);
                if (schemaVersion >= 2)
                {
                    string package = RequiredWorkerString(row, "package");
                    string export = RequiredWorkerString(row, "export");
                    if (!packageIds.Contains(package) || !IsWorkerIdentifier(export) || !exports.Add(export))
                    {
                        throw new InvalidDataException($"NuGet operation '{name}' needs a declared package and a unique export identifier.");
                    }
                    string[] parameters = row.GetProperty("parameters").EnumerateArray().Select(parameter =>
                        parameter.GetString() ?? throw new InvalidDataException($"Null parameter type for '{name}'.")).ToArray();
                    string[]? wireParameters = null;
                    if (schemaVersion == 2 && parameters.Any(parameter => parameter is not ("string" or "boolean" or "number" or "string[]" or "boolean[]" or "number[]")))
                    {
                        throw new InvalidDataException($"NuGet operation '{name}' supports string, boolean, number (System.Double), and one-dimensional arrays of these types.");
                    }
                    if (schemaVersion == 3)
                    {
                        string[] typeArguments = ReadNugetStringArray(row, "typeArguments") ?? [];
                        wireParameters = ReadNugetStringArray(row, "wireParameters");
                        if (typeArguments.Any(argument => !records.ContainsKey(argument))
                            || (wireParameters is not null && wireParameters.Length != parameters.Length))
                        {
                            throw new InvalidDataException($"NuGet operation '{name}' needs declared DTO records as generic arguments and one wire type per CLR parameter.");
                        }
                        foreach (string parameter in parameters)
                        {
                            if (parameter != "object")
                            {
                                ValidateNugetWireType(parameter, records, name);
                            }
                        }
                        foreach (string parameter in wireParameters ?? [])
                        {
                            ValidateNugetWireType(parameter, records, name);
                        }
                        binding = binding with
                        {
                            ProjectDto = true,
                            TypeArguments = typeArguments,
                            ObjectParameters = parameters.Select((parameter, index) => (parameter, index))
                                .Where(item => item.parameter == "object").Select(item => item.index).ToHashSet(),
                            RequestRecordName = export + "Request",
                        };
                    }
                    signatures.Add(new(package, export, parameters, wireParameters, binding));
                }
                operations.Add(binding);
            }
            if (packages.Count == 0 || operations.Count == 0)
            {
                throw new InvalidDataException("A NuGet worker requires at least one package and one operation.");
            }
            var browserTargets = new Dictionary<string, string>(StringComparer.Ordinal);
            if (schemaVersion >= 2 && root.TryGetProperty("browserTargets", out JsonElement targets))
            {
                foreach (JsonProperty target in targets.EnumerateObject())
                {
                    string? delivery = target.Value.GetString();
                    if (delivery is not ("wasm" or "service") || !browserTargets.TryAdd(target.Name, delivery))
                    {
                        throw new InvalidDataException($"Browser target '{target.Name}' needs one delivery mode, wasm or service.");
                    }
                }
            }
            return new(packages, operations, signatures, browserTargets, records, WorkerFingerprint(text));
        }
        catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException
            or InvalidOperationException or InvalidDataException or FormatException)
        {
            throw new TsclContractException("COPE-TSCL-NUGET-0001", $"{NugetWorkerConfigName}: {error.Message}");
        }
    }

    private static string[]? ReadNugetStringArray(JsonElement row, string key)
        => row.TryGetProperty(key, out JsonElement items)
            ? items.EnumerateArray().Select(item => item.GetString()
                ?? throw new InvalidDataException($"NuGet worker field '{key}' requires string items.")).ToArray()
            : null;

    private static bool IsNugetScalar(string type) => type is "string" or "boolean" or "number";

    private static void ValidateNugetWireType(string type,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> records, string location)
    {
        string element = type;
        for (int depth = 0; depth <= 16; depth++)
        {
            if (IsNugetScalar(element) || records.ContainsKey(element))
            {
                return;
            }
            if (element.EndsWith("[]", StringComparison.Ordinal))
            {
                element = element[..^2];
            }
            else if (element.StartsWith("Option<", StringComparison.Ordinal) && element.EndsWith('>'))
            {
                element = element[7..^1];
                if (element.StartsWith("Option<", StringComparison.Ordinal))
                {
                    break;
                }
            }
            else
            {
                break;
            }
        }
        throw new InvalidDataException($"NuGet DTO '{location}' uses unsupported type '{type}'. Use a scalar, declared record, array, or Option<T>.");
    }

    private static string RequiredWorkerString(JsonElement row, string key)
        => row.GetProperty(key).GetString() is { Length: > 0 } text
            ? text
            : throw new InvalidDataException($"NuGet worker field '{key}' requires a nonempty string.");

    private static bool IsWorkerIdentifier(string value)
        => value.Length > 0 && (char.IsAsciiLetter(value[0]) || value[0] == '_')
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static string WorkerFingerprint(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static bool IsNugetWorkerOutput(string path, string outputDirectory)
    {
        string relative = Path.GetRelativePath(outputDirectory, Path.GetFullPath(path, outputDirectory));
        string firstSegment = relative.Split(Path.DirectorySeparatorChar)[0];
        return string.Equals(firstSegment, NugetWorkerFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static void PublishNugetWorker(
        string stagingDirectory, MirProjectGraph graph, IReadOnlyList<MirTsonTransportExpression> calls,
        NugetWorkerConfiguration config, string? browserDelivery)
    {
        CSharpNugetWorker worker;
        try
        {
            worker = CSharpBackend.EmitNugetWorker(graph.AggregateProgram, calls, config.Operations, config.Fingerprint,
                useStdio: browserDelivery is null, dtoRecords: GetNugetDtoRecords(graph, config));
        }
        catch (InvalidDataException error)
        {
            throw new TsclContractException("COPE-TSCL-NUGET-0002", error.Message);
        }

        string runtimeAssembly = Path.Combine(AppContext.BaseDirectory, "Copeland.TS.Sidecar.dll");
        if (!File.Exists(runtimeAssembly))
        {
            throw new TsclContractException("COPE-TSCL-NUGET-0003", "The CLR sidecar runtime assembly is missing from this compiler installation.");
        }
        if (browserDelivery is not null)
        {
            PublishBrowserNuget(stagingDirectory, worker, config, browserDelivery, runtimeAssembly);
            return;
        }
        string projectDirectory = stagingDirectory + ".nuget-project";
        string publishDirectory = Path.Combine(stagingDirectory, NugetWorkerFolder, "worker");
        Directory.CreateDirectory(projectDirectory);
        try
        {
            WriteStagedFile(projectDirectory, "Worker.cs", worker.Source);
            WriteStagedFile(projectDirectory, "NugetWorker.csproj", CreateNugetWorkerProject(runtimeAssembly, config.Packages));
            RunDotNetPublish(projectDirectory, "NugetWorker.csproj", publishDirectory,
                CopelandBackendTarget.Create("csharp", "ryujit", "net10.0"));
        }
        finally
        {
            Directory.Delete(projectDirectory, recursive: true);
        }

        string protocol = JsonSerializer.Serialize(CSharpBackend.NugetWorkerProtocol);
        string digest = JsonSerializer.Serialize(worker.Digest);
        WriteStagedFile(stagingDirectory, NugetWorkerFolder + "/host.mjs",
            $"const protocol = {protocol};\nconst digest = {digest};\n" + ReadNugetResource("NugetWire.mjs")
            + "\n" + ReadNugetResource("NugetWorkerHost.mjs"));
    }

    private static IReadOnlySet<MirRecordTypeId> GetNugetDtoRecords(MirProjectGraph graph, NugetWorkerConfiguration config)
    {
        if (config.Records.Count == 0)
        {
            return new HashSet<MirRecordTypeId>();
        }
        MirProjectModule module = graph.Modules.Single(item => item.Id.Value == GeneratedNugetModule);
        return module.Exports
            .Where(item => item.DeclarationKind == "record" && config.Records.ContainsKey(item.Name))
            .Select(item => new MirRecordTypeId(item.RuntimeName!))
            .ToHashSet();
    }

    private static string ReadNugetResource(string name)
    {
        using Stream resource = typeof(TsclBuildContract).Assembly.GetManifestResourceStream("Copeland.Cli." + name)
            ?? throw new InvalidOperationException($"The compiler is missing '{name}'.");
        using var reader = new StreamReader(resource);
        return reader.ReadToEnd();
    }

    private static XElement NugetPackageReferences(IReadOnlyList<NugetWorkerPackage> packages)
    {
        return new XElement("ItemGroup", packages.Select(package =>
            new XElement("PackageReference", new XAttribute("Include", package.Id),
                new XAttribute("Version", $"[{package.Version}]"))));
    }

    private static string CreateNugetWorkerProject(string runtimeAssembly, IReadOnlyList<NugetWorkerPackage> packages)
    {
        XElement references = NugetPackageReferences(packages);
        references.Add(new XElement("Reference", new XAttribute("Include", "Copeland.TS.Sidecar"),
            new XElement("HintPath", runtimeAssembly)));
        var properties = new XElement("PropertyGroup",
            new XElement("OutputType", "Exe"),
            new XElement("UseAppHost", "false"),
            new XElement("SelfContained", "false"),
            new XElement("TargetFramework", "net10.0"),
            new XElement("Nullable", "enable"),
            new XElement("ManagePackageVersionsCentrally", "false"));
        return new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), properties, references).ToString();
    }
}
