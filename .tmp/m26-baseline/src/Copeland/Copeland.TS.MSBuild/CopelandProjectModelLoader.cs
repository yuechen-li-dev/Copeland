using System.Diagnostics;
using Copeland.TS.Compiler;

namespace Copeland.TS.MSBuild;

/// <summary>
/// Evaluates a project through the installed <c>dotnet msbuild</c> host and the
/// SDK's read-only collection target. This deliberately uses the same SDK
/// resolver, imports, NuGet buildTransitive items, and ResolveReferences target
/// as a normal build without invoking Copeland compilation or writing outputs.
/// </summary>
public static class CopelandProjectModelLoader
{
    private const string ModelTarget = "CopelandWriteLanguageServiceModel";

    public static CopelandEvaluatedProject Load(string projectPath, IReadOnlyDictionary<string, string>? globalProperties = null)
    {
        string fullProjectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(fullProjectPath)) throw new FileNotFoundException("Copeland project does not exist.", fullProjectPath);

        string modelPath = Path.Combine(Path.GetTempPath(), "copeland-project-model-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("msbuild");
            startInfo.ArgumentList.Add(fullProjectPath);
            startInfo.ArgumentList.Add("/target:" + ModelTarget);
            startInfo.ArgumentList.Add("/property:CopelandLanguageServiceModelFile=" + modelPath);
            startInfo.ArgumentList.Add("/property:Configuration=Debug");
            startInfo.ArgumentList.Add("/nologo");
            startInfo.ArgumentList.Add("/verbosity:quiet");
            startInfo.ArgumentList.Add("/nodeReuse:false");
            // The language server evaluates projects frequently. Keep both the
            // command-line and environment forms so no reusable MSBuild node
            // survives an evaluation and holds Copeland task assemblies open.
            startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            if (globalProperties is not null)
            {
                foreach ((string key, string value) in globalProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    startInfo.ArgumentList.Add("/property:" + key + "=" + value);
                }
            }

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet msbuild.");
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
            Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();
            string standardError = standardErrorTask.GetAwaiter().GetResult();
            string standardOutput = standardOutputTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || !File.Exists(modelPath))
            {
                throw new InvalidOperationException("MSBuild could not evaluate the Copeland language-service model: " + (standardError + standardOutput).Trim());
            }

            return ReadModel(fullProjectPath, File.ReadLines(modelPath));
        }
        finally
        {
            if (File.Exists(modelPath)) File.Delete(modelPath);
        }
    }

    private static CopelandEvaluatedProject ReadModel(string projectPath, IEnumerable<string> lines)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        CopelandProjectTypeSet projectTypes = CopelandProjectTypeSet.None;
        string? legacyProfile = null;
        string assemblyName = Path.GetFileNameWithoutExtension(projectPath);
        string rootNamespace = assemblyName;
        string langVersion = string.Empty;
        string defineConstants = string.Empty;
        string nullable = string.Empty;
        var sources = new List<CopelandProjectSource>();
        var csharpSources = new List<string>();
        var references = new List<CopelandClrReference>();
        var contracts = new List<CopelandPackageContract>();
        var npmContracts = new List<CopelandNpmPackageContract>();
        foreach (string line in lines)
        {
            string[] parts = line.Split('|', 3);
            if (parts.Length == 3 && parts[0] == "property" && parts[1] == "projectDirectory")
            {
                projectDirectory = parts[2];
                continue;
            }
            if (parts.Length == 3 && parts[0] == "property" && parts[1] == "projectTypes")
            {
                projectTypes = ParseProjectTypes(parts[2]);
                continue;
            }
            if (parts.Length == 3 && parts[0] == "property" && parts[1] == "tsXmlProfile")
            {
                legacyProfile = parts[2];
                continue;
            }
            if (parts.Length == 3 && parts[0] == "property")
            {
                switch (parts[1])
                {
                    case "assemblyName":
                        assemblyName = parts[2];
                        break;
                    case "rootNamespace":
                        rootNamespace = parts[2];
                        break;
                    case "langVersion":
                        langVersion = parts[2];
                        break;
                    case "defineConstants":
                        defineConstants = parts[2];
                        break;
                    case "nullable":
                        nullable = parts[2];
                        break;
                }

                continue;
            }

            if (parts.Length != 2) continue;
            string path = Path.GetFullPath(parts[1], projectDirectory);
            switch (parts[0])
            {
                case "Source" when File.Exists(path):
                    sources.Add(new CopelandProjectSource(Path.GetRelativePath(projectDirectory, path).Replace('\\', '/'), path, File.ReadAllText(path)));
                    break;
                case "CSharpSource" when File.Exists(path):
                    csharpSources.Add(path);
                    break;
                case "ClrReference" when File.Exists(path):
                    references.Add(new CopelandClrReference(path));
                    break;
                case "PackageContract" when File.Exists(path):
                    if (!CopelandPackageContractReader.TryRead(path, out CopelandPackageContract? contract, out string? error))
                    {
                        throw new InvalidOperationException("Could not read Copeland package contract '" + path + "': " + error);
                    }
                    contracts.Add(contract!);
                    break;
                case "NpmContract" when File.Exists(path):
                    if (!CopelandNpmContractReader.TryRead(path, out CopelandNpmPackageContract? npmContract, out string? npmError))
                    {
                        throw new InvalidOperationException("Could not read npm contract '" + path + "': " + npmError);
                    }
                    npmContracts.Add(npmContract!);
                    break;
            }
        }

        if (projectTypes == CopelandProjectTypeSet.None && !string.IsNullOrWhiteSpace(legacyProfile))
        {
            projectTypes = legacyProfile.ToLowerInvariant() switch
            {
                "react-m0" => CopelandProjectTypeSet.ReactComponents,
                "text-m0" => CopelandProjectTypeSet.TextDocuments,
                "react-m0+text-m0" or "text-m0+react-m0" => CopelandProjectTypeSet.ReactComponents | CopelandProjectTypeSet.TextDocuments,
                _ => CopelandProjectTypeSet.None,
            };
        }

        CopelandClrReference[] distinctReferences = references
            .DistinctBy(reference => reference.AssemblyPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!RoslynDeclarationProjection.TryCreate(
                csharpSources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                distinctReferences,
                assemblyName,
                NormalizeNamespace(rootNamespace) + ".Copeland",
                langVersion,
                defineConstants,
                nullable,
                out CopelandClrReference? projectDeclarations,
                out IReadOnlyList<RoslynDeclarationProjectionDiagnostic> projectionDiagnostics))
        {
            string details = string.Join(
                Environment.NewLine,
                projectionDiagnostics.Select(diagnostic => $"{diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): {diagnostic.Id}: {diagnostic.Message}"));
            throw new InvalidOperationException("Could not project authored C# declarations:" + Environment.NewLine + details);
        }

        CopelandClrReference[] effectiveReferences = projectDeclarations is null
            ? distinctReferences
            : distinctReferences.Append(projectDeclarations).ToArray();
        CopelandCompilationOptions options = new()
        {
            TargetStage = CopelandCompilationStage.Bound,
            ProjectRoot = projectDirectory,
            ProjectTypes = projectTypes,
            ClrReferences = effectiveReferences,
            PackageContracts = contracts.DistinctBy(contract => contract.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            NpmDependencies = CreateNpmDependencyGraph(npmContracts),
        };
        return new CopelandEvaluatedProject(projectPath, projectDirectory, sources.OrderBy(source => source.LogicalPath, StringComparer.OrdinalIgnoreCase).ToArray(), options);
    }

    private static string NormalizeNamespace(string value)
        => string.IsNullOrWhiteSpace(value) ? "Copeland.Generated" : value.Trim();

    private static CopelandProjectTypeSet ParseProjectTypes(string value)
    {
        CopelandProjectTypeSet result = CopelandProjectTypes.FromNames(
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            out string? unknownType);
        if (unknownType is null) return result;
        throw new InvalidOperationException($"Unknown Copeland project type '{unknownType}'.");
    }

    private static CopelandNpmDependencyGraph CreateNpmDependencyGraph(IReadOnlyList<CopelandNpmPackageContract> contracts)
    {
        string[] duplicatePackages = contracts
            .GroupBy(contract => contract.PackageName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (duplicatePackages.Length > 0)
        {
            throw new InvalidOperationException("CopelandNpmContract evaluates the same npm package more than once: " + string.Join(", ", duplicatePackages) + ".");
        }

        return new CopelandNpmDependencyGraph(contracts);
    }
}

public sealed class CopelandEvaluatedProject(
    string projectPath,
    string projectDirectory,
    IReadOnlyList<CopelandProjectSource> sources,
    CopelandCompilationOptions options)
{
    public string ProjectPath { get; } = projectPath;
    public string ProjectDirectory { get; } = projectDirectory;
    public IReadOnlyList<CopelandProjectSource> Sources { get; } = sources;
    public CopelandCompilationOptions Options { get; } = options;
    public CopelandProjectSnapshot CreateSnapshot() => CopelandProjectCompiler.CreateSnapshot(Sources, Options);
}
