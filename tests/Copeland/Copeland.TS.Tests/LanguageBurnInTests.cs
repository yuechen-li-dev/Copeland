using Copeland.TS.Compiler;
using Copeland.TS.Syntax;
using Copeland.TS.Templates;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class LanguageBurnInTests
{
    [Theory]
    [InlineData("Application.ts")]
    [InlineData("Tables.ts")]
    [InlineData("Flow.ts")]
    [InlineData("AsyncBatchGenerator.ts")]
    public void Runtime_burn_in_program_reaches_mir_without_diagnostics(string fileName)
    {
        string sourcePath = Path.Combine(GetBurnInRoot(), fileName);
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(
            File.ReadAllText(sourcePath),
            new CopelandCompilationOptions { SourcePath = sourcePath });

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.NotNull(compilation.MirCompilation?.Program);
    }

    [Fact]
    public void Metaprogramming_burn_in_evaluates_all_reflection_queries()
    {
        string sourcePath = Path.Combine(GetBurnInRoot(), "Metaprogramming", "main.ts");
        TemplateEvaluationResult result = TemplateCompiler.Evaluate(
            File.ReadAllText(sourcePath),
            "BurnInMetadata");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(15, result.Project!.Files.Count);
        Assert.Contains(result.Project.Files, file => file.Path == "Service-name.txt");
        Assert.Contains(result.Project.Files, file => file.Path == "mode-Production.txt");
        Assert.Contains(result.Project.Files, file => file.Path.StartsWith("call-", StringComparison.Ordinal));
        Assert.Equal(
            result.Project.Files.Single(file => file.Path == "memo-a.txt").Bytes,
            result.Project.Files.Single(file => file.Path == "memo-b.txt").Bytes);
    }

    [Theory]
    [InlineData(
        "function make(): { x: int; } ! string { return ok({ x: 1 }); }",
        "COPE-REC-0005")]
    [InlineData(
        "record table Values { tags: string[] = [[\"a\"]]; }",
        "COPE-TABLE-0009")]
    [InlineData(
        "function next(value: int): int { return value + 1; } flow F { board { value: int = 0; } event Go(); state A initial { on Go() -> B { board.value = next(board.value); }; } state B { } }",
        "COPE-FLOW-0024")]
    [InlineData(
        "class Person { name: string; constructor(name: string): Person { return { name }; } } function bad(): Person { return new Person(\"Ada\"); }",
        "COPE-CLASS-0013")]
    public void Composition_and_familiarity_probes_retain_focused_diagnostics(
        string source,
        string expectedDiagnostic)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == expectedDiagnostic);
        Assert.InRange(compilation.Diagnostics.Count, 1, 3);
    }

    [Fact]
    public void Constrained_template_type_parameter_forwarding_isolated_as_a_single_composition_gap()
    {
        const string source = """
            interface Named { name: string; }
            record Worker { name: string; }
            template<type T extends Named = Worker> Inner: ProjectTree {
                emit(textFile("inner.txt", reflect nameOf<T>()));
            }
            template<type T extends Named = Worker> Outer: ProjectTree {
                return instantiate Inner<T>;
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(source);

        Assert.Single(compilation.Diagnostics);
        Assert.Equal("COPE-REQUIREMENT-0005", compilation.Diagnostics[0].Id);
    }

    [Fact]
    public void Missing_record_field_comma_exposes_parser_recovery_cascade()
    {
        SyntaxTree tree = SyntaxTree.Parse(
            "function main(): int { const point = { x: 1 y: 2 }; return 0; }");

        Assert.True(tree.Diagnostics.Count >= 10);
        Assert.Equal(11, tree.Diagnostics.Count);
        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Position >= 40);
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
