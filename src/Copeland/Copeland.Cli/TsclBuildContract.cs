using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.Diagnostics;

namespace Copeland.Cli;

/// <summary>
/// The intentionally small project-shaped contract consumed by TSPack. It is
/// not a package manager: all npm materialization and static contracts arrive
/// as already-resolved compiler input.
/// </summary>
internal static class TsclBuildContract
{
    private const int CompileFailureExitCode = 1;
    private const int UsageErrorExitCode = 2;
    private const int FileIoErrorExitCode = 3;

    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public static int Run(string[] args)
    {
        if (!TryParseArguments(args, out string? projectPath, out string? resultPath))
        {
            Console.Error.WriteLine("COPE-TSCL-0001 error: Usage: tscl build --project <project.json> --result <result.json>.");
            return UsageErrorExitCode;
        }

        TsclBuildResult result;
        try
        {
            TsclBuildRequest request = ReadRequest(projectPath!);
            result = Build(request);
        }
        catch (TsclContractException exception)
        {
            result = TsclBuildResult.Failure(exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            result = TsclBuildResult.Failure("COPE-TSCL-0002", exception.Message);
        }

        try
        {
            WriteResult(resultPath!, result);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"COPE-TSCL-0003 error: Failed to write build result: {exception.Message}");
            return FileIoErrorExitCode;
        }

        return result.Success ? 0 : CompileFailureExitCode;
    }

    private static bool TryParseArguments(string[] args, out string? projectPath, out string? resultPath)
    {
        projectPath = null;
        resultPath = null;
        if (args.Length < 5 || !string.Equals(args[0], "build", StringComparison.Ordinal))
        {
            return false;
        }

        for (int index = 1; index < args.Length; index += 1)
        {
            if (args[index] == "--project" && index + 1 < args.Length)
            {
                projectPath = args[++index];
                continue;
            }

            if (args[index] == "--result" && index + 1 < args.Length)
            {
                resultPath = args[++index];
                continue;
            }

            return false;
        }

        return !string.IsNullOrWhiteSpace(projectPath) && !string.IsNullOrWhiteSpace(resultPath);
    }

    private static TsclBuildRequest ReadRequest(string projectPath)
    {
        using FileStream stream = File.OpenRead(projectPath);
        TsclBuildRequest? request = JsonSerializer.Deserialize<TsclBuildRequest>(stream, JsonOptions);
        if (request is null)
        {
            throw new TsclContractException("COPE-TSCL-0004", "Project contract is empty.");
        }

        if (!string.Equals(request.JavaScriptRuntime, "node", StringComparison.Ordinal)
            && !string.Equals(request.JavaScriptRuntime, "browser", StringComparison.Ordinal))
        {
            throw new TsclContractException("COPE-TSCL-0005", "tscl build supports javascriptRuntime='node' or javascriptRuntime='browser'.");
        }
        if (!string.Equals(request.JavaScriptProfile, "production", StringComparison.Ordinal))
        {
            throw new TsclContractException("COPE-TSCL-0006", "tscl build M1 requires javascriptProfile='production'.");
        }
        if (string.IsNullOrWhiteSpace(request.ProjectRoot) || string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            throw new TsclContractException("COPE-TSCL-0007", "projectRoot and outputDirectory are required.");
        }
        if (request.Sources.Count == 0)
        {
            throw new TsclContractException("COPE-TSCL-0008", "At least one project source is required.");
        }
        if (request.Entry is null || string.IsNullOrWhiteSpace(request.Entry.Module) || string.IsNullOrWhiteSpace(request.Entry.Export))
        {
            throw new TsclContractException("COPE-TSCL-0009", "entry.module and entry.export are required.");
        }

        return request;
    }

    private static TsclBuildResult Build(TsclBuildRequest request)
    {
        string projectRoot = Path.GetFullPath(request.ProjectRoot);
        string outputDirectory = Path.GetFullPath(request.OutputDirectory);
        CopelandProjectSource[] sources = request.Sources
            .OrderBy(source => source.LogicalPath, StringComparer.Ordinal)
            .Select(source => ReadSource(projectRoot, source))
            .ToArray();
        if (!sources.Any(source => string.Equals(source.LogicalPath, request.Entry!.Module, StringComparison.OrdinalIgnoreCase)))
        {
            throw new TsclContractException("COPE-TSCL-0010", $"Entry module '{request.Entry!.Module}' is not a project source.");
        }

        CopelandNpmDependencyGraph npmGraph = new(request.NpmContracts
            .OrderBy(contract => contract.PackageName, StringComparer.Ordinal)
            .Select(contract => new CopelandNpmPackageContract(
                contract.PackageName,
                contract.Version,
                contract.Exports.Select(export => new CopelandNpmFunctionContract(export.Name, export.Parameters, export.Result, export.RemoteError, export.Promise)).ToArray(),
                contract.MaterializationPath,
                contract.Materialized,
                IsAvailableToJavaScript: true,
                IsAvailableToClrSidecar: false,
                Components: contract.Components.Select(component => new CopelandNpmComponentContract(
                    component.Name,
                    component.Properties.Select(property => new CopelandNpmComponentPropertyContract(property.Name, property.Type, property.Required)).ToArray(),
                    component.Members.Select(member => new CopelandNpmComponentMemberContract(
                        member.Name,
                        member.Properties.Select(property => new CopelandNpmComponentPropertyContract(property.Name, property.Type, property.Required)).ToArray())).ToArray())).ToArray())));
        JavaScriptRuntimeTarget runtimeTarget = request.JavaScriptRuntime == "browser"
            ? JavaScriptRuntimeTarget.Browser
            : JavaScriptRuntimeTarget.Node;
        CopelandCompilationOptions options = new()
        {
            ProjectRoot = projectRoot,
            NpmDependencies = npmGraph,
            JavaScriptHostModules = runtimeTarget == JavaScriptRuntimeTarget.Browser
                ? [CreateBrowserHostContract()]
                : [],
            TsXmlProfile = request.TsXmlProfile == "react-m0" ? CopelandTsXmlProfile.ReactM0 : CopelandTsXmlProfile.None,
        };

        CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir(sources, options);
        if (!compilation.Success)
        {
            return TsclBuildResult.FromDiagnostics(compilation.Diagnostics, sources);
        }

        JavaScriptProjectCompilation emission = JavaScriptProjectEmitter.Emit(
            compilation.MirProjectGraph!,
            new JavaScriptEmissionOptions
            {
                Profile = JavaScriptEmissionProfile.Production,
                RuntimeTarget = runtimeTarget,
            });
        if (!emission.Success)
        {
            return TsclBuildResult.FromJavaScriptDiagnostics(emission.Diagnostics);
        }

        string stagingDirectory = outputDirectory + ".tscl-staging-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            foreach ((string relativePath, string contents) in emission.Files.OrderBy(file => file.Key, StringComparer.Ordinal))
            {
                WriteStagedFile(stagingDirectory, relativePath, contents);
            }

            string entryOutput = string.IsNullOrWhiteSpace(request.EntryOutputPath) ? "entry.js" : request.EntryOutputPath;
            string entryModuleOutput = JavaScriptProjectEmitter.GetOutputPath(new Copeland.TS.Mir.MirModuleId(request.Entry!.Module));
            WriteStagedFile(
                stagingDirectory,
                entryOutput,
                request.JavaScriptRuntime == "browser"
                    ? CreateBrowserEntryLauncher(entryModuleOutput, request.Entry.Export)
                    : CreateNodeEntryLauncher(entryModuleOutput, request.Entry.Export));
            if (request.JavaScriptRuntime == "node")
            {
                WriteStagedFile(stagingDirectory, "package.json", "{\n  \"type\": \"module\"\n}\n");
            }
            PublishOutput(stagingDirectory, outputDirectory);

            return TsclBuildResult.Successful(outputDirectory, entryOutput, request.BuildFingerprint, request.JavaScriptRuntime);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private static CopelandProjectSource ReadSource(string projectRoot, TsclSource source)
    {
        if (string.IsNullOrWhiteSpace(source.LogicalPath) || string.IsNullOrWhiteSpace(source.Path))
        {
            throw new TsclContractException("COPE-TSCL-0011", "Each source requires logicalPath and path.");
        }

        string sourcePath = Path.GetFullPath(source.Path);
        if (!sourcePath.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !string.Equals(sourcePath, projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new TsclContractException("COPE-TSCL-0012", $"Source '{source.LogicalPath}' escapes projectRoot.");
        }

        return new CopelandProjectSource(source.LogicalPath, sourcePath, File.ReadAllText(sourcePath));
    }

    private static string CreateNodeEntryLauncher(string entryModuleOutput, string entryExport)
    {
        string specifier = "./" + entryModuleOutput.Replace('\\', '/');
        return $"import {{ {entryExport} }} from {JsonSerializer.Serialize(specifier)};\n" +
            $"const __cope_result = await {entryExport}();\n" +
            "if (__cope_result !== undefined) {\n    console.log(__cope_result);\n}\n";
    }

    private static string CreateBrowserEntryLauncher(string entryModuleOutput, string entryExport)
    {
        string specifier = "./" + entryModuleOutput.Replace('\\', '/');
        return $"import {{ {entryExport} }} from {JsonSerializer.Serialize(specifier)};\n" +
            $"await {entryExport}();\n";
    }

    private static CopelandJavaScriptHostModuleContract CreateBrowserHostContract()
    {
        var state = new CopelandJavaScriptHostType.TypeParameter("State");
        var @event = new CopelandJavaScriptHostType.TypeParameter("Event");
        return new CopelandJavaScriptHostModuleContract(
            "@copeland/browser-v1",
            [
                new CopelandJavaScriptHostFunctionContract(
                    "setText",
                    [CopelandJavaScriptHostType.String, CopelandJavaScriptHostType.String],
                    CopelandJavaScriptHostType.Void),
                new CopelandJavaScriptHostFunctionContract(
                    "onClick",
                    [
                        CopelandJavaScriptHostType.String,
                        new CopelandJavaScriptHostType.Callable([], CopelandJavaScriptHostType.Void),
                    ],
                    CopelandJavaScriptHostType.Void),
                new CopelandJavaScriptHostFunctionContract(
                    "dispatch",
                    [
                        state,
                        new CopelandJavaScriptHostType.Callable([state, @event], state),
                        new CopelandJavaScriptHostType.Callable([state], CopelandJavaScriptHostType.Void),
                    ],
                    new CopelandJavaScriptHostType.Callable([@event], CopelandJavaScriptHostType.Void),
                    ["State", "Event"]),
                new CopelandJavaScriptHostFunctionContract(
                    "getMountElement",
                    [CopelandJavaScriptHostType.String],
                    new CopelandJavaScriptHostType.Named("ReactMountElement")),
                new CopelandJavaScriptHostFunctionContract(
                    "dispatchReact",
                    [
                        state,
                        new CopelandJavaScriptHostType.Callable([state, @event], state),
                        new CopelandJavaScriptHostType.Callable([state, new CopelandJavaScriptHostType.Callable([@event], CopelandJavaScriptHostType.Void)], CopelandJavaScriptHostType.Void),
                    ],
                    new CopelandJavaScriptHostType.Callable([@event], CopelandJavaScriptHostType.Void),
                    ["State", "Event"]),
                new CopelandJavaScriptHostFunctionContract(
                    "copyText",
                    [
                        CopelandJavaScriptHostType.String,
                        new CopelandJavaScriptHostType.Callable([], CopelandJavaScriptHostType.Void),
                        new CopelandJavaScriptHostType.Callable([], CopelandJavaScriptHostType.Void),
                    ],
                    CopelandJavaScriptHostType.Void),
            ]);
    }

    private static void WriteStagedFile(string stagingDirectory, string relativePath, string contents)
    {
        string fullPath = Path.GetFullPath(Path.Combine(stagingDirectory, relativePath));
        if (!fullPath.StartsWith(stagingDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new TsclContractException("COPE-TSCL-0013", $"Output path '{relativePath}' escapes outputDirectory.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void PublishOutput(string stagingDirectory, string outputDirectory)
    {
        string backupDirectory = outputDirectory + ".tscl-previous-" + Guid.NewGuid().ToString("N");
        bool published = false;
        try
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Move(outputDirectory, backupDirectory);
            }
            Directory.Move(stagingDirectory, outputDirectory);
            published = true;
        }
        finally
        {
            if (!published && Directory.Exists(backupDirectory) && !Directory.Exists(outputDirectory))
            {
                Directory.Move(backupDirectory, outputDirectory);
            }
            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
    }

    private static void WriteResult(string resultPath, TsclBuildResult result)
    {
        string fullResultPath = Path.GetFullPath(resultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullResultPath)!);
        File.WriteAllText(fullResultPath, JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private sealed class TsclContractException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    private sealed class TsclBuildRequest
    {
        public string ProjectRoot { get; init; } = string.Empty;
        public List<TsclSource> Sources { get; init; } = [];
        public TsclEntry? Entry { get; init; }
        public string JavaScriptRuntime { get; init; } = string.Empty;
        public string JavaScriptProfile { get; init; } = string.Empty;
        public string? TsXmlProfile { get; init; }
        public string OutputDirectory { get; init; } = string.Empty;
        public string? EntryOutputPath { get; init; }
        public string? BuildFingerprint { get; init; }
        public List<TsclNpmContract> NpmContracts { get; init; } = [];
    }

    private sealed class TsclSource
    {
        public string LogicalPath { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
    }

    private sealed class TsclEntry
    {
        public string Module { get; init; } = string.Empty;
        public string Export { get; init; } = string.Empty;
    }

    private sealed class TsclNpmContract
    {
        public string PackageName { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string? MaterializationPath { get; init; }
        public bool Materialized { get; init; }
        public List<TsclNpmExport> Exports { get; init; } = [];
        public List<TsclNpmComponent> Components { get; init; } = [];
    }

    private sealed class TsclNpmExport
    {
        public string Name { get; init; } = string.Empty;
        public List<string> Parameters { get; init; } = [];
        public string Result { get; init; } = string.Empty;
        public string? RemoteError { get; init; }
        public bool Promise { get; init; }
    }

    private sealed class TsclNpmComponent
    {
        public string Name { get; init; } = string.Empty;
        public List<TsclNpmProperty> Properties { get; init; } = [];
        public List<TsclNpmMember> Members { get; init; } = [];
    }

    private sealed class TsclNpmMember
    {
        public string Name { get; init; } = string.Empty;
        public List<TsclNpmProperty> Properties { get; init; } = [];
    }

    private sealed class TsclNpmProperty
    {
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public bool Required { get; init; }
    }

    private sealed class TsclBuildResult
    {
        public bool Success { get; init; }
        public string? Target { get; init; }
        public string CompilerVersion { get; init; } = Version;
        public List<TsclDiagnostic> Diagnostics { get; init; } = [];
        public List<TsclOutput> Outputs { get; init; } = [];
        public string? EntryOutputPath { get; init; }
        public string? BuildFingerprint { get; init; }

        public static TsclBuildResult Failure(string code, string message)
            => new()
            {
                Success = false,
                Diagnostics = [new TsclDiagnostic(code, "error", message, null, null, null)],
            };

        public static TsclBuildResult FromDiagnostics(IReadOnlyList<Diagnostic> diagnostics, IReadOnlyList<CopelandProjectSource> sources)
            => new()
            {
                Success = false,
                Diagnostics = diagnostics
                    .OrderBy(diagnostic => diagnostic.SourcePath, StringComparer.Ordinal)
                    .ThenBy(diagnostic => diagnostic.Position)
                    .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
                    .Select(diagnostic => ToDiagnostic(diagnostic, sources))
                    .ToList(),
            };

        public static TsclBuildResult FromJavaScriptDiagnostics(IReadOnlyList<JavaScriptDiagnostic> diagnostics)
            => new()
            {
                Success = false,
                Diagnostics = diagnostics
                    .OrderBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
                    .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
                    .Select(diagnostic => new TsclDiagnostic(diagnostic.Id, "error", diagnostic.Message, null, null, null))
                    .ToList(),
            };

        public static TsclBuildResult Successful(string outputDirectory, string entryOutputPath, string? buildFingerprint, string target)
        {
            List<TsclOutput> outputs = Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
                .Select(path => new TsclOutput(
                    Path.GetRelativePath(outputDirectory, path).Replace('\\', '/'),
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()))
                .OrderBy(output => output.Path, StringComparer.Ordinal)
                .ToList();
            return new TsclBuildResult
            {
                Success = true,
                Target = target,
                Outputs = outputs,
                EntryOutputPath = entryOutputPath,
                BuildFingerprint = buildFingerprint,
            };
        }

        private static TsclDiagnostic ToDiagnostic(Diagnostic diagnostic, IReadOnlyList<CopelandProjectSource> sources)
        {
            CopelandProjectSource? source = sources.FirstOrDefault(candidate => string.Equals(candidate.SourcePath, diagnostic.SourcePath, StringComparison.OrdinalIgnoreCase));
            if (source is null)
            {
                return new TsclDiagnostic(diagnostic.Id, "error", diagnostic.Message, diagnostic.SourcePath, null, null);
            }

            int boundedPosition = Math.Clamp(diagnostic.Position, 0, source.SourceText.Length);
            int line = 1;
            int column = 1;
            for (int index = 0; index < boundedPosition; index += 1)
            {
                if (source.SourceText[index] == '\n')
                {
                    line += 1;
                    column = 1;
                }
                else
                {
                    column += 1;
                }
            }
            return new TsclDiagnostic(diagnostic.Id, "error", diagnostic.Message, source.LogicalPath, line, column);
        }
    }

    private sealed record TsclDiagnostic(string Code, string Severity, string Message, string? File, int? Line, int? Column);
    private sealed record TsclOutput(string Path, string Sha256);
}
