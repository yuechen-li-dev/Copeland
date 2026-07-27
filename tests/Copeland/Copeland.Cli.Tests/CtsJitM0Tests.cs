using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using CtsJitM0;
using Xunit;

namespace Copeland.Cli.Tests;

public sealed class CtsJitM0Tests
{
    [Fact]
    public void Benchmark_options_reject_invalid_arguments()
    {
        Assert.Throws<BenchmarkUsageException>(() => BenchmarkOptions.Parse(["--cold-runs", "0"]));
        Assert.Throws<BenchmarkUsageException>(() => BenchmarkOptions.Parse(["--output", "..\\outside"]));
        Assert.Throws<BenchmarkUsageException>(() => BenchmarkOptions.Parse(["--unknown", "value"]));
    }

    [Fact]
    public void Workloads_compile_deterministically_for_both_backends()
    {
        string root = FindRepositoryRoot();

        foreach (WorkloadDefinition workload in CtsJitM0Workloads.All)
        {
            string sourcePath = Path.Combine(root, "tools", "CtsJitM0", "Workloads", workload.SourceFileName);
            string source = File.ReadAllText(sourcePath);
            CopelandCompilation compilation = CopelandCompiler.CompileToMir(source, new CopelandCompilationOptions
            {
                SourcePath = sourcePath,
                ProjectRoot = Path.GetDirectoryName(sourcePath),
            });

            Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
            CSharpCompilation firstCSharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
            CSharpCompilation secondCSharp = CSharpBackend.Emit(compilation.MirCompilation.Program!);
            Assert.Empty(firstCSharp.Diagnostics);
            Assert.Equal(firstCSharp.SourceText, secondCSharp.SourceText);

            JavaScriptCompilation firstJavaScript = JavaScriptBackend.Emit(
                compilation.MirCompilation.Program!,
                new JavaScriptEmissionOptions { Profile = JavaScriptEmissionProfile.Symbolic });
            JavaScriptCompilation secondJavaScript = JavaScriptBackend.Emit(
                compilation.MirCompilation.Program!,
                new JavaScriptEmissionOptions { Profile = JavaScriptEmissionProfile.Symbolic });
            Assert.True(firstJavaScript.Success, string.Join(Environment.NewLine, firstJavaScript.Diagnostics));
            Assert.Equal(firstJavaScript.SourceText, secondJavaScript.SourceText);

            JavaScriptCompilation firstProduction = JavaScriptBackend.Emit(
                compilation.MirCompilation.Program!,
                new JavaScriptEmissionOptions { Profile = JavaScriptEmissionProfile.Production });
            JavaScriptCompilation secondProduction = JavaScriptBackend.Emit(
                compilation.MirCompilation.Program!,
                new JavaScriptEmissionOptions { Profile = JavaScriptEmissionProfile.Production });
            Assert.True(firstProduction.Success, string.Join(Environment.NewLine, firstProduction.Diagnostics));
            Assert.Equal(firstProduction.SourceText, secondProduction.SourceText);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
