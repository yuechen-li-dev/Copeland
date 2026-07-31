using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Copeland.TS.MSBuild;

/// <summary>
/// Compiles explicit Copeland source items to deterministic intermediate C#.
/// Restore and reference resolution deliberately remain MSBuild/NuGet concerns.
/// </summary>
public sealed class CopelandCompile : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    public ITaskItem[] ClrReferencePaths { get; set; } = [];

    /// <summary>Exact package contract paths contributed by buildTransitive targets after NuGet restore.</summary>
    public ITaskItem[] CopelandPackageContracts { get; set; } = [];

    /// <summary>Exact resolved npm contract paths contributed by the project build target.</summary>
    public ITaskItem[] CopelandNpmContracts { get; set; } = [];

    public ITaskItem[] CSharpSources { get; set; } = [];

    public string AssemblyName { get; set; } = string.Empty;

    public string LangVersion { get; set; } = string.Empty;

    public string DefineConstants { get; set; } = string.Empty;

    public string Nullable { get; set; } = string.Empty;

    [Required]
    public string IntermediateOutputPath { get; set; } = string.Empty;

    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    /// <summary>The evaluated TS-XML semantic profile selected by the project.</summary>
    public string TsXmlProfile { get; set; } = string.Empty;

    public string RootNamespace { get; set; } = "Copeland";

    [Output]
    public ITaskItem[] GeneratedSources { get; private set; } = [];

    public override bool Execute()
    {
        try
        {
            return ExecuteCore();
        }
        catch (Exception exception)
        {
            Exception rootCause = exception.GetBaseException();
            string summary = rootCause.Message.Replace('\r', ' ').Replace('\n', ' ');
            if (summary.Length > 500)
            {
                summary = summary[..500] + "…";
            }

            Log.LogError(
                "COPE-MSBUILD-0001",
                "",
                "",
                ProjectDirectory,
                0,
                0,
                0,
                0,
                $"Copeland compilation failed unexpectedly: {rootCause.GetType().Name}: {summary}");
            return false;
        }
    }

    private bool ExecuteCore()
    {
        string projectDirectory = Path.GetFullPath(ProjectDirectory);
        string generatedDirectory = Path.Combine(Path.GetFullPath(IntermediateOutputPath), "Copeland");
        var sourcePaths = new List<string>();

        foreach (ITaskItem source in Sources)
        {
            string sourcePath = Path.GetFullPath(source.ItemSpec, projectDirectory);
            if (!File.Exists(sourcePath))
            {
                Log.LogError("COPE-MSBUILD-0002", "", "", sourcePath, 0, 0, 0, 0, "Copeland input file does not exist.");
                continue;
            }

            sourcePaths.Add(sourcePath);
        }

        if (sourcePaths.Count != sourcePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            Log.LogError("COPE-MSBUILD-0003", "", "", null, 0, 0, 0, 0, "The CopelandCompile item contains the same source file more than once.");
        }

        if (Log.HasLoggedErrors)
        {
            return false;
        }

        if (!TryParseTsXmlProfile(out CopelandTsXmlProfile tsXmlProfile))
        {
            return false;
        }

        Directory.CreateDirectory(generatedDirectory);
        IReadOnlyDictionary<string, string> moduleNames = CreateModuleNames(sourcePaths);
        var activePaths = new List<string>();
        var generatedItems = new List<ITaskItem>();
        CopelandClrReference[] references = ClrReferencePaths
            .Select(item => item.ItemSpec)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new CopelandClrReference(path))
            .ToArray();

        IReadOnlyList<string> authoredCSharpSources = GetAuthoredCSharpSources(projectDirectory, generatedDirectory);
        if (!RoslynDeclarationProjection.TryCreate(
                authoredCSharpSources,
                references,
                string.IsNullOrWhiteSpace(AssemblyName) ? Path.GetFileName(projectDirectory) : AssemblyName,
                NormalizeNamespace(RootNamespace) + ".Copeland",
                LangVersion,
                DefineConstants,
                Nullable,
                out CopelandClrReference? projectDeclarations,
                out IReadOnlyList<RoslynDeclarationProjectionDiagnostic> projectionDiagnostics))
        {
            foreach (RoslynDeclarationProjectionDiagnostic diagnostic in projectionDiagnostics)
            {
                Log.LogError(diagnostic.Id, "", "", diagnostic.FilePath, diagnostic.Line, diagnostic.Column, diagnostic.Line, diagnostic.Column, diagnostic.Message);
            }

            return false;
        }

        CopelandClrReference[] effectiveReferences = projectDeclarations is null
            ? references
            : references.Append(projectDeclarations).ToArray();
        CopelandPackageContract[] packageContracts = ReadPackageContracts(projectDirectory);
        CopelandNpmPackageContract[] npmContracts = ReadNpmContracts(projectDirectory);
        if (Log.HasLoggedErrors)
        {
            return false;
        }

        CopelandProjectSource[] projectSources = sourcePaths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new CopelandProjectSource(
                Path.GetRelativePath(projectDirectory, path),
                path,
                File.ReadAllText(path)))
            .ToArray();
        if (CopelandProjectCompiler.ContainsRelativeImports(projectSources))
        {
            return CompileProjectGraph(
                projectSources,
                projectDirectory,
                effectiveReferences,
                packageContracts,
                npmContracts,
                tsXmlProfile,
                RootNamespace,
                generatedDirectory,
                authoredCSharpSources);
        }

        foreach (string sourcePath in sourcePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string moduleName = moduleNames[sourcePath];
            string outputPath = Path.Combine(generatedDirectory, moduleName + ".g.cs");
            string mirPath = Path.Combine(generatedDirectory, moduleName + ".cope");
            string stampPath = Path.Combine(generatedDirectory, moduleName + ".stamp");
            activePaths.Add(outputPath);
            activePaths.Add(mirPath);
            activePaths.Add(stampPath);

            string fingerprint = CreateFingerprint(sourcePath, effectiveReferences, packageContracts, npmContracts, authoredCSharpSources, RootNamespace, moduleName);
            if (!IsCurrent(stampPath, outputPath, mirPath, fingerprint))
            {
                if (!Compile(sourcePath, projectDirectory, effectiveReferences, packageContracts, npmContracts, tsXmlProfile, RootNamespace, moduleName, outputPath, mirPath))
                {
                    continue;
                }

                File.WriteAllText(stampPath, fingerprint, new UTF8Encoding(false));
            }

            generatedItems.Add(new TaskItem(outputPath));
        }

        RemoveStaleOutputs(generatedDirectory, activePaths);
        GeneratedSources = generatedItems.ToArray();
        return !Log.HasLoggedErrors;
    }

    private bool CompileProjectGraph(
        IReadOnlyList<CopelandProjectSource> sources,
        string projectDirectory,
        IReadOnlyList<CopelandClrReference> references,
        IReadOnlyList<CopelandPackageContract> packageContracts,
        IReadOnlyList<CopelandNpmPackageContract> npmContracts,
        CopelandTsXmlProfile tsXmlProfile,
        string rootNamespace,
        string generatedDirectory,
        IReadOnlyList<string> authoredCSharpSources)
    {
        const string graphArtifactName = "CopelandProject";
        string outputPath = Path.Combine(generatedDirectory, graphArtifactName + ".g.cs");
        string mirPath = Path.Combine(generatedDirectory, graphArtifactName + ".cope");
        string stampPath = Path.Combine(generatedDirectory, graphArtifactName + ".stamp");
        string fingerprint = CreateProjectFingerprint(sources, references, packageContracts, npmContracts, authoredCSharpSources, rootNamespace, graphArtifactName);

        if (!IsCurrent(stampPath, outputPath, mirPath, fingerprint))
        {
            CopelandProjectCompilation project = CopelandProjectCompiler.CreateSnapshot(
                sources,
                new CopelandCompilationOptions
                {
                    SourcePath = sources[0].SourcePath,
                    ProjectRoot = projectDirectory,
                    AssetSource = FileSystemAssetSource.Instance,
                    ClrReferences = references,
                    PackageContracts = packageContracts,
                    NpmDependencies = new CopelandNpmDependencyGraph(npmContracts),
                    TsXmlProfile = tsXmlProfile,
                }).CompileToMir();
            if (!project.Success)
            {
                // A graph failure must not leave a previously valid project
                // artifact available for a later compiler invocation.
                RemoveStaleOutputs(generatedDirectory, []);
                foreach (var diagnostic in project.Diagnostics)
                {
                    string sourcePath = diagnostic.SourcePath ?? sources[0].SourcePath;
                    string sourceText = sources.FirstOrDefault(source => string.Equals(source.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase))?.SourceText ?? string.Empty;
                    (int line, int column) = GetLineAndColumn(sourceText, diagnostic.Position);
                    Log.LogError(diagnostic.Id, "", "", sourcePath, line, column, line, column + Math.Max(1, diagnostic.Length), diagnostic.Message);
                }

                return false;
            }

            CSharpCompilation emitted = CSharpBackend.Emit(project.Compilation!.MirCompilation!.Program!);
            if (emitted.Diagnostics.Count > 0)
            {
                foreach (CSharpDiagnostic diagnostic in emitted.Diagnostics)
                {
                    Log.LogError(diagnostic.Id, "", "", sources[0].SourcePath, 0, 0, 0, 0, diagnostic.Message);
                }

                return false;
            }

            string publicModuleName = sources.Any(source => string.Equals(Path.GetFileNameWithoutExtension(source.LogicalPath), "Main", StringComparison.OrdinalIgnoreCase))
                ? "Main"
                : graphArtifactName;
            string generatedNamespace = NormalizeNamespace(rootNamespace) + ".Copeland";
            string generatedSource = emitted.SourceText
                .Replace("namespace Copeland.Generated;", "namespace " + generatedNamespace + ";", StringComparison.Ordinal)
                .Replace("public static class CopelandModule", "public static class " + publicModuleName, StringComparison.Ordinal);
            generatedSource = ScopeProjectFunctionAccessibility(generatedSource, project.MirProjectGraph!, publicModuleName);
            generatedSource = ScopeRecordCarrierNames(generatedSource, graphArtifactName);
            WriteIfChanged(outputPath, generatedSource);
            WriteIfChanged(mirPath, project.Compilation.MirText!);
            File.WriteAllText(stampPath, fingerprint, new UTF8Encoding(false));
        }

        RemoveStaleOutputs(generatedDirectory, [outputPath, mirPath, stampPath]);
        GeneratedSources = [new TaskItem(outputPath)];
        return true;
    }

    private bool Compile(
        string sourcePath,
        string projectDirectory,
        IReadOnlyList<CopelandClrReference> references,
        IReadOnlyList<CopelandPackageContract> packageContracts,
        IReadOnlyList<CopelandNpmPackageContract> npmContracts,
        CopelandTsXmlProfile tsXmlProfile,
        string rootNamespace,
        string moduleName,
        string outputPath,
        string mirPath)
    {
        string sourceText;
        try
        {
            sourceText = File.ReadAllText(sourcePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.LogError("COPE-MSBUILD-0004", "", "", sourcePath, 0, 0, 0, 0, $"Could not read Copeland source: {exception.Message}");
            return false;
        }

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(
            sourceText,
            new CopelandCompilationOptions
            {
                SourcePath = sourcePath,
                ProjectRoot = projectDirectory,
                AssetSource = FileSystemAssetSource.Instance,
                ClrReferences = references,
                PackageContracts = packageContracts,
                NpmDependencies = new CopelandNpmDependencyGraph(npmContracts),
                TsXmlProfile = tsXmlProfile,
            });

        if (!compilation.Success)
        {
            foreach (var diagnostic in compilation.Diagnostics)
            {
                (int line, int column) = GetLineAndColumn(sourceText, diagnostic.Position);
                Log.LogError(diagnostic.Id, "", "", sourcePath, line, column, line, column + Math.Max(1, diagnostic.Length), diagnostic.Message);
            }

            return false;
        }

        CSharpCompilation emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        if (emitted.Diagnostics.Count > 0)
        {
            foreach (CSharpDiagnostic diagnostic in emitted.Diagnostics)
            {
                Log.LogError(diagnostic.Id, "", "", sourcePath, 0, 0, 0, 0, diagnostic.Message);
            }

            return false;
        }

        string generatedNamespace = NormalizeNamespace(rootNamespace) + ".Copeland";
        string generatedSource = emitted.SourceText
            .Replace("namespace Copeland.Generated;", "namespace " + generatedNamespace + ";", StringComparison.Ordinal)
            .Replace("public static class CopelandModule", "public static class " + moduleName, StringComparison.Ordinal);
        generatedSource = ScopeRecordCarrierNames(generatedSource, moduleName);

        WriteIfChanged(outputPath, generatedSource);
        WriteIfChanged(mirPath, compilation.MirText!);
        return true;
    }

    private bool TryParseTsXmlProfile(out CopelandTsXmlProfile profile)
    {
        if (string.IsNullOrWhiteSpace(TsXmlProfile))
        {
            profile = CopelandTsXmlProfile.None;
            return true;
        }

        if (string.Equals(TsXmlProfile, "react-m0", StringComparison.OrdinalIgnoreCase))
        {
            profile = CopelandTsXmlProfile.ReactM0;
            return true;
        }

        if (string.Equals(TsXmlProfile, "text-m0", StringComparison.OrdinalIgnoreCase))
        {
            profile = CopelandTsXmlProfile.TextDocumentsM0;
            return true;
        }

        if (string.Equals(TsXmlProfile, "react-m0+text-m0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TsXmlProfile, "text-m0+react-m0", StringComparison.OrdinalIgnoreCase))
        {
            profile = CopelandTsXmlProfile.ReactM0 | CopelandTsXmlProfile.TextDocumentsM0;
            return true;
        }

        profile = CopelandTsXmlProfile.None;
        Log.LogError("COPE-MSBUILD-0008", "", "", ProjectDirectory, 0, 0, 0, 0, "CopelandTsXmlProfile must be empty, 'react-m0', 'text-m0', or their '+' composition.");
        return false;
    }

    private static string ScopeRecordCarrierNames(string generatedSource, string moduleName)
    {
        return Regex.Replace(
            generatedSource,
            @"__CopeRecord_(?<recordId>[A-Za-z0-9_]+)",
            match => "__CopeRecord_" + moduleName + "_" + match.Groups["recordId"].Value);
    }

    private static string ScopeProjectFunctionAccessibility(string generatedSource, MirProjectGraph graph, string moduleClassName)
    {
        var exportedFunctions = graph.Modules
            .SelectMany(module => module.Exports.Select(export => export.Name))
            .ToHashSet(StringComparer.Ordinal);
        string classMarker = "public static class " + moduleClassName;
        int classStart = generatedSource.IndexOf(classMarker, StringComparison.Ordinal);
        if (classStart < 0)
        {
            return generatedSource;
        }

        int openBrace = generatedSource.IndexOf('{', classStart);
        if (openBrace < 0)
        {
            return generatedSource;
        }

        int depth = 0;
        int classEnd = openBrace;
        for (; classEnd < generatedSource.Length; classEnd += 1)
        {
            if (generatedSource[classEnd] == '{') depth += 1;
            else if (generatedSource[classEnd] == '}' && --depth == 0)
            {
                classEnd += 1;
                break;
            }
        }

        string classSource = generatedSource[classStart..classEnd];
        string scopedClassSource = Regex.Replace(
            classSource,
            @"public static (?<returnType>[A-Za-z0-9_:.<>,?\[\]\s]+) (?<name>[A-Za-z_][A-Za-z0-9_]*)\(",
            match => exportedFunctions.Contains(match.Groups["name"].Value)
                ? match.Value
                : "internal static " + match.Groups["returnType"].Value + " " + match.Groups["name"].Value + "(");
        return generatedSource[..classStart] + scopedClassSource + generatedSource[classEnd..];
    }

    private static IReadOnlyDictionary<string, string> CreateModuleNames(IReadOnlyList<string> sourcePaths)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, string> group in sourcePaths.GroupBy(path => Path.GetFileNameWithoutExtension(path) ?? "Module", StringComparer.OrdinalIgnoreCase))
        {
            bool hasCollision = group.Count() > 1;
            foreach (string path in group)
            {
                string suffix = hasCollision ? "_" + ShortHash(path) : string.Empty;
                result.Add(path, SanitizeIdentifier(Path.GetFileNameWithoutExtension(path) ?? "Module") + suffix);
            }
        }

        return result;
    }

    private CopelandPackageContract[] ReadPackageContracts(string projectDirectory)
    {
        var contracts = new List<CopelandPackageContract>();
        foreach (ITaskItem item in CopelandPackageContracts
            .OrderBy(item => item.ItemSpec, StringComparer.OrdinalIgnoreCase))
        {
            string path = Path.GetFullPath(item.ItemSpec, projectDirectory);
            if (!CopelandPackageContractReader.TryRead(path, out CopelandPackageContract? contract, out string? error))
            {
                Log.LogError("COPE-MSBUILD-0005", "", "", path, 0, 0, 0, 0, error ?? "Copeland package contract could not be read.");
                continue;
            }

            string expectedPackageId = item.GetMetadata("PackageId");
            if (!string.IsNullOrWhiteSpace(expectedPackageId)
                && !string.Equals(contract!.PackageId, expectedPackageId, StringComparison.Ordinal))
            {
                Log.LogError("COPE-PACKAGE-0015", "", "", path, 0, 0, 0, 0, $"Copeland package contract declares package '{contract.PackageId}', but the package target exposed it as '{expectedPackageId}'.");
                continue;
            }

            contracts.Add(contract!);
        }

        return contracts.ToArray();
    }

    private CopelandNpmPackageContract[] ReadNpmContracts(string projectDirectory)
    {
        var contracts = new List<CopelandNpmPackageContract>();
        foreach (ITaskItem item in CopelandNpmContracts
            .OrderBy(item => item.ItemSpec, StringComparer.OrdinalIgnoreCase))
        {
            string path = Path.GetFullPath(item.ItemSpec, projectDirectory);
            if (!CopelandNpmContractReader.TryRead(path, out CopelandNpmPackageContract? contract, out string? error))
            {
                Log.LogError("COPE-MSBUILD-0006", "", "", path, 0, 0, 0, 0, error ?? "npm contract could not be read.");
                continue;
            }

            contracts.Add(contract!);
        }

        if (contracts.GroupBy(contract => contract.PackageName, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            Log.LogError("COPE-MSBUILD-0007", "", "", null, 0, 0, 0, 0, "The CopelandNpmContract items contain the same npm package more than once.");
        }

        return contracts.ToArray();
    }

    private static void AppendPackageContractFingerprints(IncrementalHash hash, IReadOnlyList<CopelandPackageContract> contracts)
    {
        foreach (CopelandPackageContract contract in contracts.OrderBy(contract => contract.SourcePath, StringComparer.OrdinalIgnoreCase))
        {
            Append(hash, contract.SourcePath);
            Append(hash, File.ReadAllText(contract.SourcePath));
        }
    }

    private static void AppendNpmContractFingerprints(IncrementalHash hash, IReadOnlyList<CopelandNpmPackageContract> contracts)
    {
        foreach (CopelandNpmPackageContract contract in contracts.OrderBy(contract => contract.SourcePath, StringComparer.OrdinalIgnoreCase))
        {
            if (contract.SourcePath is null)
            {
                continue;
            }

            Append(hash, contract.SourcePath);
            Append(hash, File.ReadAllText(contract.SourcePath));
        }
    }

    private static string CreateFingerprint(
        string sourcePath,
        IReadOnlyList<CopelandClrReference> references,
        IReadOnlyList<CopelandPackageContract> packageContracts,
        IReadOnlyList<CopelandNpmPackageContract> npmContracts,
        IReadOnlyList<string> authoredCSharpSources,
        string rootNamespace,
        string moduleName)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, File.ReadAllText(sourcePath));
        Append(hash, rootNamespace);
        Append(hash, moduleName);
        AppendCompilerPayloadFingerprint(hash, typeof(CopelandCompile).Assembly);
        AppendCompilerPayloadFingerprint(hash, typeof(CopelandCompiler).Assembly);
        AppendCompilerPayloadFingerprint(hash, typeof(CSharpBackend).Assembly);
        foreach (CopelandClrReference reference in references)
        {
            if (reference.AssemblyPath is not null)
            {
                var info = new FileInfo(reference.AssemblyPath);
                Append(hash, info.FullName);
                Append(hash, info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Append(hash, info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        AppendPackageContractFingerprints(hash, packageContracts);
        AppendNpmContractFingerprints(hash, npmContracts);

        foreach (string source in authoredCSharpSources)
        {
            Append(hash, source);
            Append(hash, File.ReadAllText(source));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string CreateProjectFingerprint(
        IReadOnlyList<CopelandProjectSource> sources,
        IReadOnlyList<CopelandClrReference> references,
        IReadOnlyList<CopelandPackageContract> packageContracts,
        IReadOnlyList<CopelandNpmPackageContract> npmContracts,
        IReadOnlyList<string> authoredCSharpSources,
        string rootNamespace,
        string moduleName)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (CopelandProjectSource source in sources.OrderBy(source => source.LogicalPath, StringComparer.OrdinalIgnoreCase))
        {
            Append(hash, source.LogicalPath);
            Append(hash, source.SourceText);
        }
        Append(hash, rootNamespace);
        Append(hash, moduleName);
        AppendCompilerPayloadFingerprint(hash, typeof(CopelandCompile).Assembly);
        AppendCompilerPayloadFingerprint(hash, typeof(CopelandCompiler).Assembly);
        AppendCompilerPayloadFingerprint(hash, typeof(CSharpBackend).Assembly);
        foreach (CopelandClrReference reference in references)
        {
            if (reference.AssemblyPath is null) continue;
            var info = new FileInfo(reference.AssemblyPath);
            Append(hash, info.FullName);
            Append(hash, info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        AppendPackageContractFingerprints(hash, packageContracts);
        AppendNpmContractFingerprints(hash, npmContracts);
        foreach (string source in authoredCSharpSources)
        {
            Append(hash, source);
            Append(hash, File.ReadAllText(source));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendCompilerPayloadFingerprint(IncrementalHash hash, System.Reflection.Assembly assembly)
    {
        Append(hash, assembly.GetName().Name ?? "unknown");
        Append(hash, assembly.GetName().Version?.ToString() ?? "unknown");

        string assemblyPath = assembly.Location;
        if (!File.Exists(assemblyPath))
        {
            return;
        }

        var assemblyFile = new FileInfo(assemblyPath);
        Append(hash, assemblyFile.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, assemblyFile.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool IsCurrent(string stampPath, string outputPath, string mirPath, string fingerprint)
        => File.Exists(stampPath)
            && File.Exists(outputPath)
            && File.Exists(mirPath)
            && string.Equals(File.ReadAllText(stampPath), fingerprint, StringComparison.Ordinal);

    private static void RemoveStaleOutputs(string directory, IReadOnlyCollection<string> activePaths)
    {
        var active = activePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            if ((path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".cope", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".stamp", StringComparison.OrdinalIgnoreCase))
                && !active.Contains(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void WriteIfChanged(string path, string text)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), text, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static (int Line, int Column) GetLineAndColumn(string source, int position)
    {
        int line = 1;
        int column = 1;
        int limit = Math.Clamp(position, 0, source.Length);
        for (int index = 0; index < limit; index += 1)
        {
            if (source[index] == '\n')
            {
                line += 1;
                column = 1;
            }
            else
            {
                column += 1;
            }
        }

        return (line, column);
    }

    private static string NormalizeNamespace(string value)
    {
        string normalized = string.Join('.', value.Split('.').Select(SanitizeIdentifier));
        return string.IsNullOrWhiteSpace(normalized) ? "Copeland" : normalized;
    }

    private static string SanitizeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length + 1);
        if (value.Length == 0 || !char.IsLetter(value[0]) && value[0] != '_')
        {
            builder.Append('_');
        }

        foreach (char character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        return builder.ToString();
    }

    private static string ShortHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8];

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private IReadOnlyList<string> GetAuthoredCSharpSources(string projectDirectory, string generatedDirectory)
    {
        string normalizedGeneratedDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(generatedDirectory));
        return CSharpSources
            .Select(item => Path.GetFullPath(item.ItemSpec, projectDirectory))
            .Where(File.Exists)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.StartsWith(normalizedGeneratedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed class FileSystemAssetSource : ICopelandAssetSource
    {
        public static FileSystemAssetSource Instance { get; } = new();

        public bool TryRead(string normalizedPath, out string? sourceText)
        {
            try
            {
                sourceText = File.ReadAllText(normalizedPath);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                sourceText = null;
                return false;
            }
        }
    }
}
