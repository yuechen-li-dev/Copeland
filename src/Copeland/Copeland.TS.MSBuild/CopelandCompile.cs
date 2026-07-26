using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
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

    public ITaskItem[] CSharpSources { get; set; } = [];

    public string AssemblyName { get; set; } = string.Empty;

    public string LangVersion { get; set; } = string.Empty;

    public string DefineConstants { get; set; } = string.Empty;

    public string Nullable { get; set; } = string.Empty;

    [Required]
    public string IntermediateOutputPath { get; set; } = string.Empty;

    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    public string RootNamespace { get; set; } = "Copeland";

    [Output]
    public ITaskItem[] GeneratedSources { get; private set; } = [];

    public override bool Execute()
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

        foreach (string sourcePath in sourcePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string moduleName = moduleNames[sourcePath];
            string outputPath = Path.Combine(generatedDirectory, moduleName + ".g.cs");
            string mirPath = Path.Combine(generatedDirectory, moduleName + ".cope");
            string stampPath = Path.Combine(generatedDirectory, moduleName + ".stamp");
            activePaths.Add(outputPath);
            activePaths.Add(mirPath);
            activePaths.Add(stampPath);

            string fingerprint = CreateFingerprint(sourcePath, effectiveReferences, authoredCSharpSources, RootNamespace, moduleName);
            if (!IsCurrent(stampPath, outputPath, mirPath, fingerprint))
            {
                if (!Compile(sourcePath, projectDirectory, effectiveReferences, RootNamespace, moduleName, outputPath, mirPath))
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

    private bool Compile(
        string sourcePath,
        string projectDirectory,
        IReadOnlyList<CopelandClrReference> references,
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

    private static string ScopeRecordCarrierNames(string generatedSource, string moduleName)
    {
        return Regex.Replace(
            generatedSource,
            @"__CopeRecord_(?<recordId>[A-Za-z0-9_]+)",
            match => "__CopeRecord_" + moduleName + "_" + match.Groups["recordId"].Value);
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

    private static string CreateFingerprint(
        string sourcePath,
        IReadOnlyList<CopelandClrReference> references,
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
