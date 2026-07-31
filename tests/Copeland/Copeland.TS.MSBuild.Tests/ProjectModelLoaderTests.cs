using Copeland.TS.MSBuild;
using Copeland.TS.Compiler;
using System.Diagnostics;
using Xunit;

namespace Copeland.TS.MSBuild.Tests;

public sealed class ProjectModelLoaderTests
{
    [Fact]
    public void Loader_evaluates_the_sdk_source_items_references_and_tsxml_profile()
    {
        using var workspace = new TemporaryProject();
        string source = workspace.Write("App.tsx", "function App(): number { return 1; }");
        workspace.Write("Helper.cs", "namespace Fixture; public sealed class Helper { public static string Name => \"fixture\"; }");
        string npmContract = workspace.Write("contracts/dialog.json", """
            {
              "schemaVersion": 1,
              "package": "@fixture/dialog",
              "version": "1.2.3",
              "materialization": "node_modules/@fixture/dialog/index.js",
              "materialized": true,
              "exports": [{ "name": "open", "parameters": ["string"], "result": "int" }],
              "components": [{ "name": "Dialog", "properties": [{ "name": "title", "type": "string", "required": true }] }]
            }
            """);
        string targets = Path.Combine(FindRepositoryRoot(), "src", "Copeland", "Copeland.TS.MSBuild", "build", "Copeland.TS.Sdk.targets");
        string taskAssembly = typeof(CopelandCompile).Assembly.Location;
        string project = workspace.Write("App.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>Fixture</RootNamespace>
                <CopelandTsXmlProfile>react-m0</CopelandTsXmlProfile>
                <CopelandTaskAssembly>{{taskAssembly}}</CopelandTaskAssembly>
              </PropertyGroup>
              <ItemGroup>
                <CopelandCompile Include="App.tsx" />
                <CopelandNpmContract Include="{{npmContract}}" />
              </ItemGroup>
              <Import Project="{{targets}}" />
            </Project>
            """);

        RunDotnetRestore(project);

        CopelandEvaluatedProject evaluated = CopelandProjectModelLoader.Load(project);

        Assert.Equal("ReactM0", evaluated.Options.TsXmlProfile.ToString());
        var loadedSource = Assert.Single(evaluated.Sources);
        Assert.Equal(Path.GetFullPath(source), Path.GetFullPath(loadedSource.SourcePath));
        Assert.NotEmpty(evaluated.Options.ClrReferences);
        Type helper = Assert.Single(
            new CopelandClrMetadataResolver(evaluated.Options.ClrReferences)
                .FindTypesBySimpleName("Helper"));
        Assert.Equal("Fixture.Helper", helper.FullName);
        CopelandNpmPackageContract npm = Assert.Single(evaluated.Options.NpmDependencies!.Packages);
        Assert.Equal("@fixture/dialog", npm.PackageName);
        Assert.Equal(Path.GetFullPath(npmContract), npm.SourcePath);
    }

    private sealed class TemporaryProject : IDisposable
    {
        public TemporaryProject() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "copeland-project-model-" + Guid.NewGuid().ToString("N"));
        public string Path { get; }

        public string Write(string relativePath, string text)
        {
            string path = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static void RunDotnetRestore(string projectPath)
    {
        using Process process = Process.Start(new ProcessStartInfo("dotnet", "restore \"" + projectPath + "\"")
        {
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
