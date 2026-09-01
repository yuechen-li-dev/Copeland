using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class OptionEffectsM0Tests
{
    [Fact]
    public void Parser_distinguishes_optional_fields_chaining_coalescing_and_result_propagation()
    {
        SyntaxTree tree = SyntaxTree.Parse("""
            record User { nickname?: string; }
            function label(user: User): string { return user.nickname ?? "Anonymous"; }
            function city(user: Option<User>): Option<string> { return user?.nickname; }
            function propagated(value: User ! string): Option<string> ! string { return value()?.nickname; }
            """);

        Assert.Empty(tree.Diagnostics);
        var record = Assert.IsType<RecordDeclarationSyntax>(tree.Root.Members[0]);
        Assert.NotNull(Assert.Single(record.Fields).QuestionToken);
        var label = Assert.IsType<FunctionDeclarationSyntax>(tree.Root.Members[1]);
        Assert.IsType<CoalesceExpressionSyntax>(Assert.IsType<ReturnStatementSyntax>(label.Body.Statements[0]).Expression);
        var city = Assert.IsType<FunctionDeclarationSyntax>(tree.Root.Members[2]);
        Assert.IsType<OptionalMemberAccessExpressionSyntax>(Assert.IsType<ReturnStatementSyntax>(city.Body.Statements[0]).Expression);
    }

    [Fact]
    public void Option_uses_closed_payload_enums_and_optional_record_construction_has_no_missing_state()
    {
        var compilation = CopelandCompiler.CompileToMir("""
            record User {
                name: string;
                nickname?: string;
            }

            function omitted(): User { return { name: "Ada" }; }
            function supplied(): User { return { name: "Ada", nickname: "Countess" }; }
            function explicitSome(): Option<string> { return Some("Ada"); }
            function explicitNone(): Option<string> { return None; }
            function nested(): Option<Option<int>> { return Some(None); }
            function choose(value: Option<string>): string {
                return match value { Some(name) => name, None => "fallback", };
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        BoundProgram program = compilation.BoundCompilation!.Program;
        Assert.Equal(3, program.Enums.Count(@enum => @enum.EnumType is OptionTypeSymbol));

        RecordFieldSymbol nickname = Assert.Single(program.Records).RecordType.Fields.Single(field => field.Name == "nickname");
        Assert.True(nickname.IsOptional);
        Assert.Equal("Option<string>", nickname.Type.Name);

        BoundRecordConstructionExpression omitted = Assert.IsType<BoundRecordConstructionExpression>(
            Assert.IsType<BoundReturnStatement>(program.Functions.Single(function => function.Symbol.Name == "omitted").Body.Statements[0]).Expression);
        Assert.Equal(2, omitted.Initializers.Count);
        Assert.Equal("None", Assert.IsType<BoundEnumValueExpression>(omitted.Initializers.Single(field => field.Field.Name == "nickname").Value).Case.Name);

        BoundRecordConstructionExpression supplied = Assert.IsType<BoundRecordConstructionExpression>(
            Assert.IsType<BoundReturnStatement>(program.Functions.Single(function => function.Symbol.Name == "supplied").Body.Statements[0]).Expression);
        Assert.Equal("Some", Assert.IsType<BoundEnumValueExpression>(supplied.Initializers.Single(field => field.Field.Name == "nickname").Value).Case.Name);
    }

    [Fact]
    public void Optional_chaining_flattens_one_projected_option_layer_and_coalescing_is_a_lazy_match()
    {
        var compilation = CopelandCompiler.CompileToMir("""
            record Address { city?: string; }
            record User { address?: Address; }

            function city(user: User): string {
                return user.address?.city ?? "Unknown";
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        MirProgram program = Assert.IsType<MirProgram>(compilation.MirCompilation?.Program);
        MirFunction function = Assert.Single(program.Functions);
        MirMatchExpression coalesce = Assert.IsType<MirMatchExpression>(Assert.IsType<MirReturnStatement>(Assert.Single(function.Body)).Expression);
        MirMatchExpression chain = Assert.IsType<MirMatchExpression>(coalesce.Scrutinee);
        Assert.Equal(2, chain.Arms.Count);
        Assert.Equal(2, coalesce.Arms.Count);
        Assert.Equal("string", coalesce.Type.Name);
        Assert.Equal("Some", coalesce.Arms[0].CaseName);
        Assert.Equal("None", coalesce.Arms[1].CaseName);
    }

    [Theory]
    [InlineData("function bad(value: string): string { return value ?? \"x\"; }", "COPE-OPTION-0006")]
    [InlineData("function bad(): string { return Some(\"x\"); }", "COPE-OPTION-0002")]
    [InlineData("function bad(): Option<string> { return null; }", "COPE-PROFILE-0005")]
    [InlineData("function bad(): Option<string> { return undefined; }", "COPE-BIND-0001")]
    public void Option_rejects_host_nullability_and_uncontextual_sugar(string source, string diagnosticId)
    {
        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Fact]
    public void Effect_classifier_is_transitive_recursion_safe_fail_closed_and_preserves_provenance()
    {
        var compilation = CopelandCompiler.CompileToMir("""
            using System;

            record Box { value: int; }
            enum Choice { Empty, Value(value: int), }

            function arithmetic(value: int): int { return value * value; }
            function safeMiddle(value: int): int { return arithmetic(value); }
            function safeOuter(value: int): int { return safeMiddle(value); }
            function makeRecord(value: int): Box { return { value }; }
            function makeEnum(value: int): Choice { return Choice.Value(value); }
            function successful(value: int): int ! string { return ok(value); }
            function propagate(value: int): int ! string { return successful(value)?; }
            function choose(value: Option<string>): string { return value ?? "default"; }
            function projected(value: Option<MutableArray<int>>): Option<int> {
                return value?.freeze()?.length;
            }
            function first(values: int[]): Option<int> {
                if (values.length == 0) { return None; }
                return values[0];
            }
            function kernel(size: int): int[] {
                const buffer: MutableArray<int> = MutableArray<int>(size);
                let index: int = 0;
                while (index < buffer.length) {
                    buffer[index] = index * index;
                    index = index + 1;
                }
                return buffer.freeze();
            }
            function kernelOuter(size: int): int[] { return kernel(size); }
            function recursive(value: int): int {
                if (value == 0) { return 0; }
                return recursive(value - 1);
            }
            function readHost(value: number): number { return Math.Round(value); }
            function middle(value: number): number { return readHost(value); }
            function outer(value: number): number { return middle(value); }
            """, new CopelandCompilationOptions { SourcePath = "effects.ts" });

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        IReadOnlyDictionary<FunctionSymbol, FunctionEffectSummary> summaries = compilation.BoundCompilation!.Program.FunctionEffects;

        Assert.True(Summary("arithmetic").IsStaticSafe);
        Assert.True(Summary("safeOuter").IsStaticSafe);
        Assert.True(Summary("makeRecord").IsStaticSafe);
        Assert.True(Summary("makeEnum").IsStaticSafe);
        Assert.True(Summary("successful").IsStaticSafe);
        Assert.True(Summary("propagate").IsStaticSafe);
        Assert.True(Summary("choose").IsStaticSafe);
        Assert.True(Summary("projected").IsStaticSafe);
        Assert.True(Summary("first").IsStaticSafe);
        Assert.True(Summary("recursive").IsStaticSafe);
        FunctionEffectSummary kernel = Summary("kernel");
        Assert.True(kernel.IsStaticSafe);
        Assert.Contains(FunctionEffect.LocalMutation, kernel.SafeEffects);
        Assert.Contains(FunctionEffect.LocalMutation, Summary("kernelOuter").SafeEffects);

        FunctionEffectSummary host = Summary("readHost");
        Assert.Equal(FunctionEffect.HostInterop, host.RuntimeEffect);
        FunctionEffectSummary outer = Summary("outer");
        Assert.False(outer.IsStaticSafe);
        Assert.Equal(["outer", "middle", "readHost", "CLR member access crosses the language boundary"], outer.Provenance);

        FunctionEffectSummary Summary(string name)
            => summaries.Single(pair => pair.Key.Name == name).Value;
    }

    [Fact]
    public void Effect_classifier_marks_unresolved_imported_calls_runtime_only_by_default()
    {
        var external = new FunctionSymbol("external", [], PrimitiveTypeSymbol.Int);
        var caller = new FunctionSymbol("caller", [], PrimitiveTypeSymbol.Int);
        var body = new BoundBlockStatement([new BoundReturnStatement(new BoundCallExpression(external, []))]);

        FunctionEffectSummary summary = FunctionEffectClassifier.Classify([new BoundFunctionDeclaration(caller, body)])[caller];

        Assert.False(summary.IsStaticSafe);
        Assert.Equal(FunctionEffect.UnknownCall, summary.RuntimeEffect);
        Assert.Contains("unclassified", summary.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Closed_Option_identity_is_shared_across_project_modules_and_both_backends()
    {
        CopelandProjectCompilation project = CopelandProjectCompiler.CompileToMir(
            [
                new CopelandProjectSource("Library.ts", "Library.ts", "export function Maybe(): Option<int> { return Some(4); }"),
                new CopelandProjectSource("Main.ts", "Main.ts", "import { Maybe } from \"./Library\"; export function Run(): int { const local: Option<int> = Maybe(); return local ?? 0; }"),
            ],
            new CopelandCompilationOptions { SourcePath = "Project.ts" });

        Assert.True(project.Success, string.Join(Environment.NewLine, project.Diagnostics));
        MirProgram program = project.Compilation!.MirCompilation!.Program!;
        Assert.Single(program.Enums, @enum => @enum.Name.StartsWith("__CopeOption_", StringComparison.Ordinal));
        Assert.Empty(Copeland.TS.Backend.CSharp.CSharpBackend.Emit(program).Diagnostics);
        Assert.True(Copeland.TS.Backend.JavaScript.JavaScriptBackend.Emit(program).Success);
        Assert.All(
            project.Modules.SelectMany(module => module.BoundCompilation!.Program.FunctionEffects.Values),
            summary => Assert.True(summary.IsStaticSafe));
    }
}
