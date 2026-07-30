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
        CopelandProjectContext context = CopelandProjectContext.Create(
            "<tscl-build-request>",
            new CopelandProjectContextDescriptor
            {
                ProjectRoot = request.ProjectRoot,
                JavaScriptRuntime = request.JavaScriptRuntime,
                TsXmlProfile = request.TsXmlProfile,
                Sources = request.Sources.Select(source => new CopelandProjectContextSource { LogicalPath = source.LogicalPath, Path = source.Path }).ToList(),
                NpmContracts = request.NpmContracts.Select(ToContextContract).ToList(),
            });
        CopelandProjectSource[] sources = context.Sources.ToArray();
        if (!sources.Any(source => string.Equals(source.LogicalPath, request.Entry!.Module, StringComparison.OrdinalIgnoreCase)))
        {
            throw new TsclContractException("COPE-TSCL-0010", $"Entry module '{request.Entry!.Module}' is not a project source.");
        }

        JavaScriptRuntimeTarget runtimeTarget = request.JavaScriptRuntime == "browser"
            ? JavaScriptRuntimeTarget.Browser
            : JavaScriptRuntimeTarget.Node;
        CopelandCompilationOptions options = context.Options;

        CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir(sources, options);
        if (!compilation.Success)
        {
            return TsclBuildResult.FromDiagnostics(compilation.Diagnostics, sources);
        }

        JavaScriptProjectCompilation baseEmission = JavaScriptProjectEmitter.Emit(
            compilation.MirProjectGraph!,
            new JavaScriptEmissionOptions
            {
                Profile = JavaScriptEmissionProfile.Production,
                RuntimeTarget = runtimeTarget,
            });
        JavaScriptProjectCompilation emission = LayoutJavaScriptProjectEmitter.AddLayouts(baseEmission, compilation.Modules);
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

            // HostAttachmentMir is the compiler fact. Emit its transport form
            // beside browser output; TSPack owns all later materialization.
            WriteStagedFile(
                stagingDirectory,
                AttachmentPlanArtifactEmitter.ArtifactFileName,
                AttachmentPlanArtifactEmitter.Emit(compilation, projectRoot));

            string entryOutput = string.IsNullOrWhiteSpace(request.EntryOutputPath) ? "entry.js" : request.EntryOutputPath;
            string entryModuleOutput = JavaScriptProjectEmitter.GetOutputPath(new Copeland.TS.Mir.MirModuleId(request.Entry!.Module));
            WriteStagedFile(
                stagingDirectory,
                entryOutput,
                request.JavaScriptRuntime == "browser"
                    ? CreateBrowserEntryLauncher(entryModuleOutput, request.Entry.Export, emission.Files.ContainsKey("generated/layouts.css"))
                    : CreateNodeEntryLauncher(entryModuleOutput, request.Entry.Export));
            if (request.JavaScriptRuntime == "node")
            {
                WriteStagedFile(stagingDirectory, "package.json", "{\n  \"type\": \"module\"\n}\n");
            }
            PublishOutput(stagingDirectory, outputDirectory);

            return TsclBuildResult.Successful(
                outputDirectory,
                entryOutput,
                request.BuildFingerprint,
                context.Fingerprint,
                request.JavaScriptRuntime);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private static CopelandProjectContextNpmContract ToContextContract(TsclNpmContract contract)
        => new()
        {
            PackageName = contract.PackageName,
            Version = contract.Version,
            MaterializationPath = contract.MaterializationPath,
            Materialized = contract.Materialized,
            Exports = contract.Exports.Select(export => new CopelandProjectContextNpmExport { Name = export.Name, Parameters = export.Parameters, Result = export.Result, RemoteError = export.RemoteError, Promise = export.Promise }).ToList(),
            Components = contract.Components.Select(component => new CopelandProjectContextNpmComponent
            {
                Name = component.Name,
                Properties = component.Properties.Select(property => new CopelandProjectContextNpmProperty { Name = property.Name, Type = property.Type, Required = property.Required }).ToList(),
                Members = component.Members.Select(member => new CopelandProjectContextNpmMember
                {
                    Name = member.Name,
                    Properties = member.Properties.Select(property => new CopelandProjectContextNpmProperty { Name = property.Name, Type = property.Type, Required = property.Required }).ToList(),
                }).ToList(),
            }).ToList(),
        };

    private static string CreateNodeEntryLauncher(string entryModuleOutput, string entryExport)
    {
        string specifier = "./" + entryModuleOutput.Replace('\\', '/');
        return $"import {{ {entryExport} }} from {JsonSerializer.Serialize(specifier)};\n" +
            $"const __cope_result = await {entryExport}();\n" +
            "if (__cope_result !== undefined) {\n    console.log(__cope_result);\n}\n";
    }

    private static string CreateBrowserEntryLauncher(string entryModuleOutput, string entryExport, bool hasLayouts)
    {
        string specifier = "./" + entryModuleOutput.Replace('\\', '/');
        string cssLoader = hasLayouts
            ? """
                const __cope_layout_stylesheet = new URL("./generated/layouts.css", import.meta.url).href;
                if (document.querySelector("link[data-copeland-layout-stylesheet]") === null) {
                    const __cope_layout_link = document.createElement("link");
                    __cope_layout_link.rel = "stylesheet";
                    __cope_layout_link.href = __cope_layout_stylesheet;
                    __cope_layout_link.setAttribute("data-copeland-layout-stylesheet", "true");
                    await new Promise((resolve, reject) => {
                        __cope_layout_link.addEventListener("load", resolve, { once: true });
                        __cope_layout_link.addEventListener("error", reject, { once: true });
                        document.head.append(__cope_layout_link);
                    });
                }
                """
            : string.Empty;
        return cssLoader + $"import {{ {entryExport} }} from {JsonSerializer.Serialize(specifier)};\n" +
            $"await {entryExport}();\n";
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
        public string? GraphFingerprint { get; init; }

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

        public static TsclBuildResult Successful(
            string outputDirectory,
            string entryOutputPath,
            string? buildFingerprint,
            string graphFingerprint,
            string target)
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
                GraphFingerprint = graphFingerprint,
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
