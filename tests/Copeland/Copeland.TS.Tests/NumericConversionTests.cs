using Copeland.TS.Compiler;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class NumericConversionTests
{
    [Fact]
    public void Numeric_literals_conversions_and_interpolation_lower_with_explicit_identity()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            function describe(count: int, ratio: float): string {
                const widened: float = Float.From(count);
                const rendered = String(count);
                const floor: int = Int.Floor(ratio);
                const ceiling: int = Int.Ceil(ratio);
                const rounded: int = Int.Round(ratio);
                const truncated: int = Int.Truncate(ratio);
                return `${rendered}: ${floor}, ${ceiling}, ${rounded}, ${truncated}, ${widened}`;
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("IntToFloat", compilation.MirText, StringComparison.Ordinal);
        Assert.Contains("StringFrom", compilation.MirText, StringComparison.Ordinal);
        Assert.Contains("IntFloor", compilation.MirText, StringComparison.Ordinal);
        Assert.Contains("IntRound", compilation.MirText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("function value(): int { const ratio: float = 2.5; return Int(ratio); }", "COPE-NUM-0005")]
    [InlineData("function value(): float { return Float(\"3\"); }", "COPE-NUM-0006")]
    [InlineData("function value(): string { const count: int = 3; return \"count: \" + count; }", "COPE-TYPE-0007")]
    [InlineData("function value(): float { const count: int = 3; const ratio: float = 2.5; return count + ratio; }", "COPE-NUM-0002")]
    public void Numeric_diagnostics_teach_explicit_operations(string source, string diagnosticId)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Fact]
    public void Number_is_semantically_float_while_integer_literals_remain_int()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            function legacy(value: number): float { return value; }
            function ints(): int { const left: int = 3; const right: int = 4; return left + right; }
            function floats(): float { return 3.0 + 4.5; }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("func legacy(value: number) -> float", compilation.MirText, StringComparison.Ordinal);
        Assert.Contains("func ints() -> int", compilation.MirText, StringComparison.Ordinal);
        Assert.Contains("func floats() -> float", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Expected_numeric_destinations_adapt_integer_literals_without_widening_stored_ints()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            record Measurements { ratio: float; legacy: number; }
            function takeFloat(value: float): float { return value; }
            function generic<T>(value: T): T { return value; }
            function result(): number ! number { return ok(3); }
            function value(): float {
                const ratio: float = 3;
                const legacy: number = 3;
                const measurement: Measurements = { ratio: 3, legacy: 3 };
                const values: float[] = [1, 2, 3];
                const argument = takeFloat(3);
                const explicitGeneric: number = generic<number>(3);
                return ratio + measurement.ratio + argument + explicitGeneric;
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.DoesNotContain("int", compilation.MirText!.Split("func value", StringSplitOptions.None)[1], StringComparison.Ordinal);

        CopelandCompilation rejected = CopelandCompiler.CompileToMir("""
            function invalid(): float {
                const count = 3;
                return count;
            }
            """);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Id == "COPE-TYPE-0003");
    }

    [Fact]
    public void Return_and_result_payload_contexts_adapt_direct_integer_literals()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            function directReturn(): float { return 3; }
            function successful(): number ! string { return ok(3); }
            function failed(): string ! number { return err(3); }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("func directReturn() -> float", compilation.MirText, StringComparison.Ordinal);
        Assert.Contains("func successful() -> number ! string", compilation.MirText, StringComparison.Ordinal);
        Assert.Contains("func failed() -> string ! number", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Local_modules_and_npm_contracts_preserve_expected_number_contexts()
    {
        CopelandProjectCompilation localCompilation = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("Library.ts", "Library.ts", "export function Accept(value: number): number { return value; }"),
            new CopelandProjectSource("Main.ts", "Main.ts", """
                import { Accept } from "./Library";
                export function Run(): number { return Accept(3); }
                """),
        ],
        new CopelandCompilationOptions { SourcePath = "Project.ts" });

        Assert.True(localCompilation.Success, string.Join(Environment.NewLine, localCompilation.Diagnostics.Select(diagnostic => diagnostic.Message)));

        CopelandCompilation npmCompilation = CopelandCompiler.CompileToMir("""
            import { sum } from "@fixture/math";
            const $schema: string = "copeland://numeric/context";
            record RemoteError { message: string; }
            async function Run(): number ! RemoteError {
                const pending: Async<number ! RemoteError> = sum(4, 5);
                return await pending;
            }
            """,
        new CopelandCompilationOptions
        {
            SourcePath = "Main.ts",
            NpmPackages =
            [
                new CopelandNpmPackageContract(
                    "@fixture/math",
                    "1.0.0",
                    [new CopelandNpmFunctionContract("sum", ["number", "number"], "number", "RemoteError")]),
            ],
        });

        Assert.True(npmCompilation.Success, string.Join(Environment.NewLine, npmCompilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    [Fact]
    public void Explicit_number_table_columns_and_flow_updates_adapt_literals_at_known_destinations()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            record table Samples { value: number = [1, 2]; }
            flow Counter -> number {
                board { value: number = 0; }
                event Add(amount: number);
                state Open initial {
                    on Add(amount) -> Open { board.value = 1; };
                }
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Contains("column value [t1.c0]: number = [1, 2]", compilation.MirText, StringComparison.Ordinal);
    }

    [Fact]
    public void Backends_emit_explicit_canonical_conversion_operations()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            function describe(count: int, ratio: float): string {
                return `${String.From(count)} ${Int.Round(ratio)} ${Float(count)}`;
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string csharp = CSharpBackend.Emit(compilation.MirCompilation!.Program!).SourceText!;
        JavaScriptCompilation javaScriptCompilation = JavaScriptBackend.Emit(compilation.MirCompilation.Program!);
        Assert.True(javaScriptCompilation.Success, string.Join(Environment.NewLine, javaScriptCompilation.Diagnostics));
        string javascript = javaScriptCompilation.SourceText!;

        Assert.Contains("CultureInfo.InvariantCulture", csharp, StringComparison.Ordinal);
        Assert.Contains("Math.Ceiling", csharp, StringComparison.Ordinal);
        Assert.Contains("Number.isFinite", javascript, StringComparison.Ordinal);
        Assert.Contains("Math.floor", javascript, StringComparison.Ordinal);
    }
}
