using Copeland.TS.Compiler;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Diagnostics;
using Copeland.TS.Mir;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class StaticMetaM1Tests
{
    [Fact]
    public void Parser_represents_static_as_an_ordinary_prefix_expression()
    {
        SyntaxTree tree = SyntaxTree.Parse("function value(): int { return static build(5); }");

        Assert.Empty(tree.Diagnostics);
        var function = Assert.IsType<FunctionDeclarationSyntax>(Assert.Single(tree.Root.Members));
        var returned = Assert.IsType<ReturnStatementSyntax>(Assert.Single(function.Body.Statements));
        var staticExpression = Assert.IsType<StaticExpressionSyntax>(returned.Expression);
        Assert.IsType<CallExpressionSyntax>(staticExpression.Expression);
    }

    [Fact]
    public void Static_safe_ordinary_function_with_local_mutation_embeds_an_immutable_array()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            function makeSquares(size: int): int[] {
                const values: MutableArray<int> = MutableArray<int>(size);
                let index: int = 0;
                while (index < values.length) {
                    values[index] = index * index;
                    index = index + 1;
                }
                return values.freeze();
            }

            function answer(): int {
                const squares: int[] = static makeSquares(5);
                return squares[4];
            }
            """, new CopelandCompilationOptions { SourcePath = "static.ts" });

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        BoundFunctionDeclaration answer = compilation.BoundCompilation!.Program.Functions
            .Single(function => function.Symbol.Name == "answer");
        var declaration = Assert.IsType<BoundVariableDeclaration>(answer.Body.Statements[0]);
        var staticExpression = Assert.IsType<BoundStaticExpression>(declaration.Initializer);
        BoundArrayExpression embedded = Assert.IsType<BoundArrayExpression>(staticExpression.EvaluatedExpression);
        Assert.Equal([0, 1, 4, 9, 16], embedded.Elements.Cast<BoundLiteralExpression>().Select(value => value.Value));

        MirFunction mirAnswer = compilation.MirCompilation!.Program!.Functions
            .Single(function => function.Name == "answer");
        MirVariableDeclarationStatement mirDeclaration = Assert.IsType<MirVariableDeclarationStatement>(mirAnswer.Body[0]);
        Assert.IsType<MirArrayExpression>(mirDeclaration.Initializer);

        string csharp = CSharpBackend.Emit(compilation.MirCompilation.Program).SourceText;
        string javascript = Assert.IsType<string>(JavaScriptBackend.Emit(compilation.MirCompilation.Program).SourceText);
        Assert.Equal(1, csharp.Split("makeSquares(", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, javascript.Split("makeSquares(", StringSplitOptions.None).Length - 1);
        Assert.Contains("0, 1, 4, 9, 16", csharp, StringComparison.Ordinal);
        Assert.Contains("0, 1, 4, 9, 16", javascript, StringComparison.Ordinal);
    }

    [Fact]
    public void Static_option_match_and_records_use_normal_semantic_values()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            record User { name: string; nickname?: string; }

            function label(user: User): string {
                return user.nickname ?? user.name;
            }

            function answer(): string {
                return static label({ name: "Ada" });
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        MirFunction answer = compilation.MirCompilation!.Program!.Functions.Single(function => function.Name == "answer");
        MirReturnStatement returned = Assert.IsType<MirReturnStatement>(Assert.Single(answer.Body));
        Assert.Equal("Ada", Assert.IsType<MirLiteralExpression>(returned.Expression).Value);
    }

    [Fact]
    public void Static_rejects_runtime_only_calls_with_effect_provenance()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            using System;
            function host(value: number): number { return Math.Round(value); }
            function middle(value: number): number { return host(value); }
            function answer(): number { return static middle(3.2); }
            """, new CopelandCompilationOptions { SourcePath = "effects.ts" });

        var diagnostic = Assert.Single(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-STATIC-0012");
        Assert.Contains("middle -> host -> CLR member access crosses the language boundary", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal("effects.ts", diagnostic.SourcePath);
    }

    [Fact]
    public void Static_distinguishes_semantic_failure_from_ineligibility()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            function fail(): int {
                const values: int[] = [1];
                return values[2];
            }
            function answer(): int { return static fail(); }
            """);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-STATIC-0014");
        Assert.DoesNotContain(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-STATIC-0012");
    }

    [Fact]
    public void Static_result_values_embed_through_the_existing_Result_path()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            function compute(): int ! string { return ok(7); }
            function answer(): int {
                const result: int ! string = static compute();
                return result!;
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        BoundFunctionDeclaration answer = compilation.BoundCompilation!.Program.Functions.Single(function => function.Symbol.Name == "answer");
        BoundVariableDeclaration declaration = Assert.IsType<BoundVariableDeclaration>(answer.Body.Statements[0]);
        BoundStaticExpression staticExpression = Assert.IsType<BoundStaticExpression>(declaration.Initializer);
        Assert.IsType<BoundOkExpression>(staticExpression.EvaluatedExpression);
    }

    [Fact]
    public void Recursive_static_cycles_fail_with_a_bounded_diagnostic()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            function recurse(value: int): int { return recurse(value); }
            function answer(): int { return static recurse(1); }
            """);

        Diagnostic diagnostic = Assert.Single(compilation.Diagnostics, item => item.Id == "COPE-STATIC-0015");
        Assert.Contains("Recursive static call cycle", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_static_evaluation_can_call_an_imported_static_safe_function()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
            [
                new CopelandProjectSource("Library.ts", "Library.ts", "export function square(value: int): int { return value * value; }"),
                new CopelandProjectSource("Main.ts", "Main.ts", "import { square } from \"./Library\"; export function answer(): int { return static square(6); }"),
            ]);

        Assert.True(project.Success, string.Join(Environment.NewLine, project.Diagnostics));
        MirFunction answer = project.Compilation!.MirCompilation!.Program!.Functions.Single(function => function.Name == "answer");
        MirReturnStatement returned = Assert.IsType<MirReturnStatement>(Assert.Single(answer.Body));
        Assert.Equal(36, Assert.IsType<MirLiteralExpression>(returned.Expression).Value);
    }
}
