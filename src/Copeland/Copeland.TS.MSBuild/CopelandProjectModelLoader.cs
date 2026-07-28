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
            if (globalProperties is not null)
            {
                foreach ((string key, string value) in globalProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    startInfo.ArgumentList.Add("/property:" + key + "=" + value);
                }
            }

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet msbuild.");
            string standardError = process.StandardError.ReadToEnd();
            string standardOutput = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
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
        CopelandTsXmlProfile profile = CopelandTsXmlProfile.None;
        var sources = new List<CopelandProjectSource>();
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
            if (parts.Length == 3 && parts[0] == "property" && parts[1] == "tsXmlProfile")
            {
                profile = ParseTsXmlProfile(parts[2]);
                continue;
            }

            if (parts.Length != 2) continue;
            string path = Path.GetFullPath(parts[1], projectDirectory);
            switch (parts[0])
            {
                case "Source" when File.Exists(path):
                    sources.Add(new CopelandProjectSource(Path.GetRelativePath(projectDirectory, path).Replace('\\', '/'), path, File.ReadAllText(path)));
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

        CopelandCompilationOptions options = new()
        {
            TargetStage = CopelandCompilationStage.Bound,
            ProjectRoot = projectDirectory,
            TsXmlProfile = profile,
            ClrReferences = references.DistinctBy(reference => reference.AssemblyPath, StringComparer.OrdinalIgnoreCase).ToArray(),
            PackageContracts = contracts.DistinctBy(contract => contract.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            NpmDependencies = CreateNpmDependencyGraph(npmContracts),
        };
        return new CopelandEvaluatedProject(projectPath, projectDirectory, sources.OrderBy(source => source.LogicalPath, StringComparer.OrdinalIgnoreCase).ToArray(), options);
    }

    private static CopelandTsXmlProfile ParseTsXmlProfile(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return CopelandTsXmlProfile.None;
        if (value.Equals("react-m0", StringComparison.OrdinalIgnoreCase)) return CopelandTsXmlProfile.ReactM0;
        throw new InvalidOperationException("CopelandTsXmlProfile must be empty or 'react-m0'.");
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
