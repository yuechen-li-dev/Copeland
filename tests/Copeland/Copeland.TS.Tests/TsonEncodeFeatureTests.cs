using Copeland.TS.Compiler;
using Copeland.TS.Semantics.Bound;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TsonEncodeFeatureTests
{
    private const string Schema = """
        const $schema: string = "copeland://tests/encode-feature";
        record Inner { text: string; }
        enum Choice { None, Value(inner: Inner), }
        record Root { enabled: boolean; count: number; inner: Inner; choice: Choice; }
        """;

    [Fact]
    public void Intrinsic_builds_one_deduplicated_reachable_plan_and_result_expression()
    {
        CopelandCompilation compilation = Compile(Schema + """
            function first(value: Root): string ! TsonEncodeError { return tsonEncode(value); }
            function second(value: Root): string ! TsonEncodeError {
                const encoded: string ! TsonEncodeError = tsonEncode(value);
                return encoded;
            }
            """);

        Assert.True(compilation.Success, Describe(compilation));
        BoundTsonEncodingPlan plan = Assert.Single(compilation.BoundCompilation!.Program.TsonEncodingPlans);
        Assert.Equal(new[] { "Choice", "Inner", "Root" }, plan.Definitions.Select(type => type.Name));
        Assert.Single(compilation.MirCompilation!.Program!.TsonEncodingPlans);
        Assert.Equal(2, Count(compilation.MirText!, "tson-encode [tson0]"));
        Assert.Contains("TsonEncodeError", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Intrinsic_composes_in_ordinary_result_expression_positions()
    {
        CopelandCompilation compilation = Compile(Schema + """
            function pass(value: string ! TsonEncodeError): string ! TsonEncodeError { return value; }
            function returned(value: Root): string ! TsonEncodeError { return tsonEncode(value); }
            function argument(value: Root): string ! TsonEncodeError { return pass(tsonEncode(value)); }
            function conditional(value: Root): string ! TsonEncodeError {
                return if true { tsonEncode(value) } else { tsonEncode(value) };
            }
            function matched(value: Root): string {
                return match tsonEncode(value) { ok(text) => text, err(error) => "failed", };
            }
            function propagated(value: Root): string ! TsonEncodeError {
                const text: string = tsonEncode(value)?;
                return text;
            }
            function unwrapped(value: Root): string { return tsonEncode(value)!; }
            function handled(value: Root): string {
                return try { tsonEncode(value)? } except (error) { "failed" };
            }
            """);

        Assert.True(compilation.Success, Describe(compilation));
        Assert.Single(compilation.MirCompilation!.Program!.TsonEncodingPlans);
    }

    [Theory]
    [InlineData("function bad(value: number): string ! TsonEncodeError { return tsonEncode(value); }", "COPE-TSON-ENCODE-0001")]
    [InlineData("function bad(value: Root): string ! TsonEncodeError { return tsonEncode(); }", "COPE-TSON-ENCODE-0001")]
    [InlineData("function bad(value: Root): string ! TsonEncodeError { return tsonEncode(value, value); }", "COPE-TSON-ENCODE-0001")]
    [InlineData("function bad(value: Root): void { tsonEncode; }", "COPE-TSON-ENCODE-0001")]
    [InlineData("function tsonEncode(value: Root): Root { return value; }", "COPE-TSON-ENCODE-0001")]
    [InlineData("function bad(tsonEncode: Root): Root { return tsonEncode; }", "COPE-TSON-ENCODE-0001")]
    [InlineData("record TsonEncodeError { value: string; }", "COPE-TSON-ENCODE-0001")]
    [InlineData("function TsonEncodeError(): void { return; }", "COPE-TSON-ENCODE-0001")]
    public void Malformed_intrinsic_use_has_stable_diagnostic(string source, string diagnosticId)
    {
        CopelandCompilation compilation = Compile(Schema + source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, diagnostic =>
            diagnostic.Id == diagnosticId && diagnostic.Length > 0);
    }

    [Theory]
    [InlineData("record Bad { values: number[]; } function bad(value: Bad): string ! TsonEncodeError { return tsonEncode(value); }")]
    [InlineData("record Bad { value: number ! string; } function bad(value: Bad): string ! TsonEncodeError { return tsonEncode(value); }")]
    [InlineData("record table Values { value: [1]; } record Bad { value: Values.Row; } function bad(value: Bad): string ! TsonEncodeError { return tsonEncode(value); }")]
    public void Unsupported_reachable_types_are_compile_time_errors(string source)
    {
        CopelandCompilation compilation = Compile(
            "const $schema: string = \"copeland://tests/unsupported\"; " + source);

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSON-ENCODE-0003");
    }

    [Fact]
    public void Missing_schema_is_a_compile_time_error()
    {
        CopelandCompilation compilation = Compile(
            "record Root { value: string; } function bad(value: Root): string ! TsonEncodeError { return tsonEncode(value); }");

        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-TSON-ENCODE-0002");
    }

    [Fact]
    public void Program_without_intrinsic_has_no_plan_or_error_enum()
    {
        CopelandCompilation compilation = Compile("record Root { value: string; } function value(root: Root): string { return root.value; }");

        Assert.True(compilation.Success, Describe(compilation));
        Assert.Empty(compilation.MirCompilation!.Program!.TsonEncodingPlans);
        Assert.DoesNotContain(compilation.MirCompilation.Program.Enums, @enum => @enum.Name == "TsonEncodeError");
        Assert.DoesNotContain("tson", compilation.MirText!, StringComparison.OrdinalIgnoreCase);
    }

    private static CopelandCompilation Compile(string source)
        => CopelandCompiler.CompileToMir(source);

    private static string Describe(CopelandCompilation compilation)
        => string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => $"{diagnostic.Id}: {diagnostic.Message}"));

    private static int Count(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
