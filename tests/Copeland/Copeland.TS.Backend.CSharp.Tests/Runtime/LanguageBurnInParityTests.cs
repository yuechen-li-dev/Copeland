using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Copeland.TS.TestSupport;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests.Runtime;

public sealed class LanguageBurnInParityTests
{
    [Theory]
    [InlineData("Application.ts", 1354d)]
    [InlineData("Tables.ts", 194d)]
    public void Generated_csharp_matches_node_observations_for_sync_burn_in_programs(
        string fileName,
        double expected)
    {
        string sourcePath = Path.Combine(GetBurnInRoot(), fileName);
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(
            File.ReadAllText(sourcePath),
            new CopelandCompilationOptions { SourcePath = sourcePath });
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        CSharpCompilation emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));

        Assert.Equal(expected, Assert.IsType<double>(
            GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }

    [Fact]
    public void Generated_csharp_compiles_record_and_class_access_after_await()
    {
        const string source = """
            class LoadedValue {
                raw: number;
                private scaled: number;
                constructor(raw: number, scaled: number): LoadedValue {
                    return { raw, scaled };
                }
                total(value: LoadedValue): number {
                    return value.raw + value.scaled;
                }
            }
            async function load(value: number): number { return value + 1; }
            async function compose(value: number): number {
                const pending: Async<number> = load(value);
                const loaded: number = await pending;
                const boxed: LoadedValue = LoadedValue(loaded, loaded * 2);
                const local = { value: loaded, doubled: loaded * 2 };
                return LoadedValue.total(boxed) + local.value + local.doubled;
            }
            """;
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        CSharpCompilation emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);

        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
    }

    private static string GetBurnInRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string root = Path.Combine(
                directory.FullName,
                "tests",
                "Copeland",
                "Copeland.TS.Tests",
                "TestData",
                "BurnIn");
            if (Directory.Exists(root))
            {
                return root;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate the burn-in corpus.");
    }
}
