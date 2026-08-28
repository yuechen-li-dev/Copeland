using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Copeland.TS.Diagnostics;
using Copeland.TS.Manifest;

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

    public static string Version => Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion
        .Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "0.0.0";

    public static int Run(string[] args)
    {
        if (!TryParseArguments(args, out string? projectPath, out string? standaloneRoot, out string? targetName, out string? resultPath))
        {
            Console.Error.WriteLine("COPE-TSCL-0001 error: Usage: tscl build (--project <descriptor.json> | --standalone <project-root> [--target <name>]) --result <result.json>.");
            return UsageErrorExitCode;
        }

        TsclBuildResult result;
        try
        {
            TsclBuildRequest request = standaloneRoot is null
                ? ReadRequest(projectPath!)
                : ReadStandaloneRequest(standaloneRoot, targetName);
            result = Build(request);
        }
        catch (TsclContractException exception)
        {
            result = TsclBuildResult.Failure(exception.Code, exception.Message);
        }
        catch (CopelandProjectContextException exception)
        {
            result = TsclBuildResult.Failure(exception.Code, exception.Message);
        }
        catch (CopelandBackendTargetException exception)
        {
            result = TsclBuildResult.Failure(exception.Code, exception.Message);
        }
        catch (ComponentFrameArtifactException exception)
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

    private static bool TryParseArguments(
        string[] args,
        out string? projectPath,
        out string? standaloneRoot,
        out string? targetName,
        out string? resultPath)
    {
        projectPath = null;
        standaloneRoot = null;
        targetName = null;
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
            if (args[index] == "--standalone" && index + 1 < args.Length)
            {
                standaloneRoot = args[++index];
                continue;
            }

            if (args[index] == "--result" && index + 1 < args.Length)
            {
                resultPath = args[++index];
                continue;
            }
            if (args[index] == "--target" && index + 1 < args.Length)
            {
                targetName = args[++index];
                continue;
            }

            return false;
        }

        bool hasOneProjectMode = string.IsNullOrWhiteSpace(projectPath) != string.IsNullOrWhiteSpace(standaloneRoot);
        return hasOneProjectMode
            && !string.IsNullOrWhiteSpace(resultPath)
            && (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(targetName));
    }

    private static TsclBuildRequest ReadStandaloneRequest(string projectRoot, string? requestedTarget)
    {
        string fullProjectRoot = Path.GetFullPath(projectRoot);
        CopelandProjectContext context = CopelandProjectContext.LoadStandalone(fullProjectRoot);
        ManifestProjectLoadResult manifestResult = CopelandProject.LoadRootManifest(fullProjectRoot);
        ManifestTarget[] availableTargets = manifestResult.Manifest!.Packages.SelectMany(package => package.Targets).ToArray();
        ManifestTarget[] targets = string.IsNullOrWhiteSpace(requestedTarget)
            ? availableTargets
            : availableTargets.Where(target => string.Equals(target.Name, requestedTarget, StringComparison.Ordinal)).ToArray();
        if (targets.Length != 1 || targets[0].Row is not ManifestValue.Object row)
        {
            throw new TsclContractException(
                "COPE-TSCL-0011",
                string.IsNullOrWhiteSpace(requestedTarget)
                    ? "Standalone build requires exactly one compiler-relevant manifest target, or --target <name>."
                    : $"Standalone build target '{requestedTarget}' was not found or was ambiguous.");
        }
        string entry = RequiredManifestString(row, "entry");
        string runtime = RequiredManifestString(row, "runtime");
        CopelandWorkspaceOwnershipResult ownership = CopelandWorkspaceOwnership.Resolve(Path.Combine(fullProjectRoot, "tsconfig.tsx"));
        CopelandWorkspaceTarget? configuredTarget = null;
        ownership.Targets?.TryGetValue(targets[0].Name, out configuredTarget);
        return new TsclBuildRequest
        {
            Context = context,
            ProjectRoot = fullProjectRoot,
            Sources = context.Sources.Select(source => new TsclSource
            {
                LogicalPath = source.LogicalPath,
                Path = source.SourcePath,
            }).ToList(),
            Entry = new TsclEntry { Module = entry, Export = "Main" },
            JavaScriptRuntime = "node",
            Backend = configuredTarget?.Backend ?? "javascript",
            ExecutionRuntime = configuredTarget?.Runtime ?? "node",
            TargetFramework = configuredTarget?.TargetFramework,
            RuntimeIdentifier = configuredTarget?.RuntimeIdentifier,
            JavaScriptProfile = "production",
            OutputDirectory = Path.Combine(fullProjectRoot, Path.GetDirectoryName(runtime) ?? string.Empty),
            EntryOutputPath = Path.GetFileName(runtime),
            BuildFingerprint = context.Fingerprint,
        };
    }

    private static string RequiredManifestString(ManifestValue.Object row, string name)
    {
        if (row.Properties.TryGetValue(name, out ManifestValue? value)
            && value is ManifestValue.String text
            && !string.IsNullOrWhiteSpace(text.Text))
        {
            return text.Text;
        }
        throw new TsclContractException("COPE-TSCL-0012", $"Standalone target requires string field '{name}'.");
    }

    private static TsclBuildRequest ReadRequest(string projectPath)
    {
        string fullProjectPath = Path.GetFullPath(projectPath);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(fullProjectPath));
        TsclBuildRequest? request;
        if (document.RootElement.TryGetProperty("schemaVersion", out _))
        {
            CompilerTargetDescriptor descriptor = CompilerTargetDescriptorProtocol.Load(fullProjectPath);
            request = descriptor.CompilerPayload!.Data.Deserialize<TsclBuildRequest>(JsonOptions);
            if (request is not null)
            {
                request.DescriptorPath = fullProjectPath;
                ApplyCompilerOwnedTarget(descriptor, request);
            }
        }
        else
        {
            request = document.RootElement.Deserialize<TsclBuildRequest>(JsonOptions);
        }
        if (request is null)
        {
            throw new TsclContractException("COPE-TSCL-0004", "Project contract is empty.");
        }

        if (!string.IsNullOrWhiteSpace(request.JavaScriptProfile)
            && !string.Equals(request.JavaScriptProfile, "production", StringComparison.Ordinal))
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

    private static void ApplyCompilerOwnedTarget(CompilerTargetDescriptor descriptor, TsclBuildRequest request)
    {
        string configPath = Path.GetFullPath(descriptor.CompilerConfig.Path, descriptor.ProjectRoot);
        CopelandWorkspaceOwnershipResult ownership = CopelandWorkspaceOwnership.Resolve(configPath);
        if (!ownership.Success)
        {
            CopelandWorkspaceOwnershipDiagnostic diagnostic = ownership.Diagnostics[0];
            throw new CopelandProjectContextException(diagnostic.Code, diagnostic.Message);
        }
        if (ownership.Targets is null || !ownership.Targets.TryGetValue(descriptor.Target.Name, out CopelandWorkspaceTarget? target))
        {
            return;
        }
        request.Backend = target.Backend;
        request.ExecutionRuntime = target.Runtime;
        request.TargetFramework = target.TargetFramework;
        request.RuntimeIdentifier = target.RuntimeIdentifier;
    }

    private static TsclBuildResult Build(TsclBuildRequest request)
    {
        string projectRoot = Path.GetFullPath(request.ProjectRoot);
        string outputDirectory = Path.GetFullPath(request.OutputDirectory);
        CopelandProjectContext context = request.Context ?? (string.IsNullOrWhiteSpace(request.DescriptorPath)
            ? CopelandProjectContext.Create(
                "<legacy-tscl-build-request>",
                new CopelandProjectContextDescriptor
                {
                    ProjectRoot = request.ProjectRoot,
                    JavaScriptRuntime = request.JavaScriptRuntime,
                    TsXmlProfile = request.TsXmlProfile,
                    Sources = request.Sources.Select(source => new CopelandProjectContextSource { LogicalPath = source.LogicalPath, Path = source.Path }).ToList(),
                    NpmContracts = request.NpmContracts.Select(ToContextContract).ToList(),
                })
            : CopelandProjectContext.LoadResolvedContext(request.DescriptorPath));
        CopelandProjectSource[] sources = context.Sources.ToArray();
        if (!sources.Any(source => string.Equals(source.LogicalPath, request.Entry!.Module, StringComparison.OrdinalIgnoreCase)))
        {
            throw new TsclContractException("COPE-TSCL-0010", $"Entry module '{request.Entry!.Module}' is not a project source.");
        }

        CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir(sources, context.Options);
        if (!compilation.Success)
        {
            return TsclBuildResult.FromDiagnostics(compilation.Diagnostics, sources);
        }

        CopelandBackendTarget target = CopelandBackendTarget.Create(
            request.Backend,
            request.ExecutionRuntime ?? request.JavaScriptRuntime,
            request.TargetFramework,
            request.RuntimeIdentifier);
        if (target.Backend == CopelandBackend.JavaScript)
        {
            return BuildJavaScript(request, context, compilation, sources, projectRoot, outputDirectory, target);
        }
        return BuildDotNet(request, context, compilation, sources, outputDirectory, target);
    }

    private static TsclBuildResult BuildJavaScript(
        TsclBuildRequest request,
        CopelandProjectContext context,
        CopelandProjectCompilation compilation,
        IReadOnlyList<CopelandProjectSource> sources,
        string projectRoot,
        string outputDirectory,
        CopelandBackendTarget target)
    {
        JavaScriptRuntimeTarget runtimeTarget = target.Runtime == CopelandExecutionRuntime.Browser
            ? JavaScriptRuntimeTarget.Browser
            : JavaScriptRuntimeTarget.Node;

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
            if (target.Runtime == CopelandExecutionRuntime.Browser)
            {
                WriteStagedFile(
                    stagingDirectory,
                    ComponentFrameArtifactEmitter.ArtifactFileName,
                    ComponentFrameArtifactEmitter.Emit(compilation));
            }

            string entryOutput = string.IsNullOrWhiteSpace(request.EntryOutputPath) ? "entry.js" : request.EntryOutputPath;
            string entryModuleOutput = JavaScriptProjectEmitter.GetOutputPath(new Copeland.TS.Mir.MirModuleId(request.Entry!.Module));
            WriteStagedFile(
                stagingDirectory,
                entryOutput,
                target.Runtime == CopelandExecutionRuntime.Browser
                    ? CreateBrowserEntryLauncher(entryModuleOutput, request.Entry.Export, emission.Files.ContainsKey("generated/layouts.css"))
                    : CreateNodeEntryLauncher(entryModuleOutput, request.Entry.Export));
            if (target.Runtime == CopelandExecutionRuntime.Node)
            {
                WriteStagedFile(stagingDirectory, "package.json", "{\n  \"type\": \"module\"\n}\n");
            }
            PublishOutput(stagingDirectory, outputDirectory);

            return TsclBuildResult.Successful(
                outputDirectory,
                entryOutput,
                request.BuildFingerprint,
                context.Fingerprint,
                target,
                entryOutput);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private static TsclBuildResult BuildDotNet(
        TsclBuildRequest request,
        CopelandProjectContext context,
        CopelandProjectCompilation compilation,
        IReadOnlyList<CopelandProjectSource> sources,
        string outputDirectory,
        CopelandBackendTarget target)
    {
        CSharpCompilation emission = CSharpBackend.Emit(compilation.MirProjectGraph!.AggregateProgram);
        if (emission.Diagnostics.Count > 0)
        {
            return TsclBuildResult.FromCSharpDiagnostics(emission.Diagnostics);
        }

        string stagingDirectory = outputDirectory + ".tscl-staging-" + Guid.NewGuid().ToString("N");
        string projectDirectory = Path.Combine(stagingDirectory, "project");
        string publishDirectory = Path.Combine(stagingDirectory, "publish");
        Directory.CreateDirectory(projectDirectory);
        try
        {
            string assemblyName = Path.GetFileNameWithoutExtension(request.EntryOutputPath) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                assemblyName = "CopelandTarget_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.BuildFingerprint ?? context.Fingerprint)))[..12];
            }
            WriteStagedFile(projectDirectory, "Generated.cs", emission.SourceText);
            WriteStagedFile(projectDirectory, "Program.cs", DotNetHostSource);
            WriteStagedFile(projectDirectory, assemblyName + ".csproj", CreateDotNetProject(assemblyName, target));
            RunDotNetPublish(projectDirectory, assemblyName + ".csproj", publishDirectory, target);

            string entryOutput = target.Runtime switch
            {
                CopelandExecutionRuntime.RyuJit => assemblyName + ".dll",
                CopelandExecutionRuntime.NativeAot => assemblyName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty),
                CopelandExecutionRuntime.DotNetWasm => PrepareWasmEntry(publishDirectory, request.EntryOutputPath),
                _ => throw new InvalidOperationException("Unsupported .NET target."),
            };
            string publishedOutput = outputDirectory + ".tscl-publish-" + Guid.NewGuid().ToString("N");
            Directory.Move(publishDirectory, publishedOutput);
            PublishOutput(publishedOutput, outputDirectory);
            return TsclBuildResult.Successful(outputDirectory, entryOutput, request.BuildFingerprint, context.Fingerprint, target, entryOutput);
        }
        catch (TsclContractException exception)
        {
            return TsclBuildResult.Failure(exception.Code, exception.Message);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private static string CreateDotNetProject(string assemblyName, CopelandBackendTarget target)
    {
        string runtimeIdentifier = target.RuntimeIdentifier is null
            ? string.Empty
            : $"\n    <RuntimeIdentifier>{target.RuntimeIdentifier}</RuntimeIdentifier>";
        string targetProperties = target.Runtime switch
        {
            CopelandExecutionRuntime.RyuJit => "<SelfContained>false</SelfContained>",
            CopelandExecutionRuntime.NativeAot => "<PublishAot>true</PublishAot>\n    <SelfContained>true</SelfContained>\n    <InvariantGlobalization>true</InvariantGlobalization>",
            CopelandExecutionRuntime.DotNetWasm => "<RuntimeIdentifier>browser-wasm</RuntimeIdentifier>\n    <WasmBuildNative>true</WasmBuildNative>",
            _ => throw new InvalidOperationException("Unsupported .NET runtime."),
        };
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <AssemblyName>{assemblyName}</AssemblyName>
                <TargetFramework>{target.TargetFramework}</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <Optimize>true</Optimize>
                <DebugType>none</DebugType>
                {targetProperties}{runtimeIdentifier}
              </PropertyGroup>
            </Project>
            """;
    }

    private static void RunDotNetPublish(string projectDirectory, string projectFile, string publishDirectory, CopelandBackendTarget target)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            WorkingDirectory = projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(projectFile);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(publishDirectory);
        startInfo.ArgumentList.Add("--nologo");
        System.Diagnostics.Process process;
        try
        {
            process = System.Diagnostics.Process.Start(startInfo)!;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new TsclContractException(
                "COPE-TARGET-0005",
                $"The .NET SDK is required for runtime={target.RuntimeId}, but 'dotnet' could not be started: {exception.Message}");
        }
        using (process)
        {
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            string prerequisite = target.Runtime == CopelandExecutionRuntime.DotNetWasm
                ? " Install the matching .NET WebAssembly workload with 'dotnet workload install wasm-tools'."
                : string.Empty;
            throw new TsclContractException("COPE-TARGET-0002", $"dotnet publish failed for runtime={target.RuntimeId}.{prerequisite}\n{standardOutput}\n{standardError}".Trim());
        }
        }
    }

    private static string FindWasmEntry(string publishDirectory)
    {
        string? path = Directory.EnumerateFiles(publishDirectory, "*.wasm", SearchOption.AllDirectories)
            .OrderBy(candidate => candidate, StringComparer.Ordinal)
            .FirstOrDefault();
        if (path is null)
        {
            throw new TsclContractException("COPE-TARGET-0003", "The .NET WebAssembly publish succeeded but produced no .wasm artifact.");
        }
        return Path.GetRelativePath(publishDirectory, path).Replace('\\', '/');
    }

    private static string PrepareWasmEntry(string publishDirectory, string? requestedEntry)
    {
        string runtimeModule = FindWasmEntry(publishDirectory);
        if (string.IsNullOrWhiteSpace(requestedEntry) || string.Equals(runtimeModule, requestedEntry, StringComparison.Ordinal))
        {
            return runtimeModule;
        }
        string sourcePath = Path.Combine(publishDirectory, runtimeModule.Replace('/', Path.DirectorySeparatorChar));
        string targetPath = Path.GetFullPath(Path.Combine(publishDirectory, requestedEntry));
        if (!targetPath.StartsWith(publishDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new TsclContractException("COPE-TSCL-0013", $"Output path '{requestedEntry}' escapes outputDirectory.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath, overwrite: true);
        return requestedEntry.Replace('\\', '/');
    }

    private const string DotNetHostSource = """
        using System.Reflection;

        internal static class Program
        {
            private static async Task<int> Main()
            {
                Type module = typeof(Copeland.Generated.CopelandModule);
                MethodInfo? entry = module.GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
                if (entry is null)
                {
                    Console.Error.WriteLine("COPE-TARGET-0004 error: Copeland entry export 'Main' was not emitted as a public function.");
                    return 1;
                }
                object? result = entry.Invoke(null, null);
                if (result is Task task)
                {
                    await task.ConfigureAwait(false);
                    PropertyInfo? resultProperty = task.GetType().GetProperty("Result");
                    result = resultProperty?.GetValue(task);
                }
                if (result is not null)
                {
                    Console.WriteLine(result);
                }
                return 0;
            }
        }
        """;

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
        public CopelandProjectContext? Context { get; set; }
        public string DescriptorPath { get; set; } = string.Empty;
        public string ProjectRoot { get; init; } = string.Empty;
        public List<TsclSource> Sources { get; init; } = [];
        public TsclEntry? Entry { get; init; }
        public string JavaScriptRuntime { get; init; } = string.Empty;
        public string? Backend { get; set; }
        public string? ExecutionRuntime { get; set; }
        public string? TargetFramework { get; set; }
        public string? RuntimeIdentifier { get; set; }
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
        public string? Backend { get; init; }
        public string? Runtime { get; init; }
        public string? ArtifactKind { get; init; }
        public string? TargetFramework { get; init; }
        public string? RuntimeIdentifier { get; init; }
        public string? LaunchExecutable { get; init; }
        public List<string> LaunchArguments { get; init; } = [];
        public List<string> Capabilities { get; init; } = [];
        public Dictionary<string, string> ToolVersions { get; init; } = [];

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

        public static TsclBuildResult FromCSharpDiagnostics(IReadOnlyList<CSharpDiagnostic> diagnostics)
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
            CopelandBackendTarget target,
            string launchArtifact)
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
                Target = target.RuntimeId,
                Backend = target.BackendId,
                Runtime = target.RuntimeId,
                ArtifactKind = target.PrimaryArtifactId,
                TargetFramework = target.Backend == CopelandBackend.CSharp ? target.TargetFramework : null,
                RuntimeIdentifier = target.RuntimeIdentifier,
                LaunchExecutable = target.Runtime switch
                {
                    CopelandExecutionRuntime.Node => "node",
                    CopelandExecutionRuntime.RyuJit => "dotnet",
                    CopelandExecutionRuntime.NativeAot => launchArtifact,
                    _ => null,
                },
                LaunchArguments = target.Runtime is CopelandExecutionRuntime.Node or CopelandExecutionRuntime.RyuJit
                    ? [launchArtifact]
                    : [],
                Capabilities = target.Capabilities.ToList(),
                ToolVersions = ReadTargetToolVersions(target),
                Outputs = outputs,
                EntryOutputPath = entryOutputPath,
                BuildFingerprint = buildFingerprint,
                GraphFingerprint = graphFingerprint,
            };
        }

        private static string ReadDotNetSdkVersion()
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--version");
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
            string version = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? version : "unknown";
        }

        private static Dictionary<string, string> ReadTargetToolVersions(CopelandBackendTarget target)
        {
            if (target.Backend != CopelandBackend.CSharp)
            {
                return [];
            }
            var versions = new Dictionary<string, string> { ["dotnetSdk"] = ReadDotNetSdkVersion() };
            if (target.Runtime != CopelandExecutionRuntime.DotNetWasm)
            {
                return versions;
            }
            var startInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("workload");
            startInfo.ArgumentList.Add("list");
            startInfo.ArgumentList.Add("--machine-readable");
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                versions["wasmToolsManifest"] = "unknown";
                return versions;
            }
            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement update = document.RootElement.GetProperty("updateAvailable")
                .EnumerateArray()
                .FirstOrDefault(item => item.GetProperty("workloadId").GetString() == "wasm-tools");
            versions["wasmToolsManifest"] = update.ValueKind == JsonValueKind.Object
                ? update.GetProperty("existingManifestVersion").GetString() ?? "unknown"
                : "installed";
            return versions;
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
