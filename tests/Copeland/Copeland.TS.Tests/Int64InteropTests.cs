using System.Reflection;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class Int64InteropTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(9007199254740993L)]
    [InlineData(-9007199254740993L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void Native_arguments_returns_properties_records_and_arrays_are_exact(long input)
    {
        MethodInfo run = Compile("""
            using System;
            record Timing { ticks: long; }
            function run(value: long): long {
                const clock = new TimeSpan(value);
                const timing: Timing = { ticks: clock.Ticks };
                const samples: long[] = [timing.ticks];
                return samples[0];
            }
            """);
        Assert.Equal(typeof(long), run.ReturnType);
        Assert.Equal(typeof(long), run.GetParameters()[0].ParameterType);
        Assert.Equal(input, run.Invoke(null, [input]));
    }

    [Theory]
    [InlineData("9007199254740993L", 9007199254740993L)]
    [InlineData("-9223372036854775808L", long.MinValue)]
    [InlineData("9223372036854775807L", long.MaxValue)]
    [InlineData("1", 1L)]
    [InlineData("-2147483648", -2147483648L)]
    public void Literals_never_pass_through_double(string expression, long expected)
    {
        Assert.Equal(expected, Compile($"function run(): long {{ return {expression}; }}").Invoke(null, null));
    }

    [Fact]
    public void Arithmetic_comparison_and_invariant_string_conversion_execute_natively()
    {
        var run = Compile("""
            function run(value: long): string {
                const next: long = value + 1;
                if (next > value && next != value) { return String.From(next); }
                return "wrong";
            }
            """);
        Assert.Equal("9007199254740994", run.Invoke(null, [9007199254740993L]));
    }

    [Theory]
    [InlineData("value + 1", long.MaxValue)]
    [InlineData("value - 1", long.MinValue)]
    [InlineData("value * 2", long.MaxValue)]
    [InlineData("-value", long.MinValue)]
    [InlineData("value / -1", long.MinValue)]
    public void Dynamic_arithmetic_overflow_is_checked(string expression, long input)
    {
        var run = Compile($"function run(value: long): long {{ return {expression}; }}");
        var failure = Assert.Throws<TargetInvocationException>(() => run.Invoke(null, [input]));
        Assert.IsType<OverflowException>(failure.InnerException);
    }

    [Fact]
    public void Clr_widening_and_checked_narrowing_have_explicit_native_calls()
    {
        var wide = Compile("using System; function run(value: int): long { return Convert.ToInt64(value); }");
        Assert.Equal((long)int.MinValue, wide.Invoke(null, [int.MinValue]));
        var narrow = Compile("using System; function run(value: long): int { return Convert.ToInt32(value); }");
        Assert.Equal(42, narrow.Invoke(null, [42L]));
        Assert.IsType<OverflowException>(Assert.Throws<TargetInvocationException>(() => narrow.Invoke(null, [9007199254740993L])).InnerException);
        var parsed = Compile("using System; function run(): long { return Int64.Parse(\"9007199254740993\"); }");
        Assert.Equal(9007199254740993L, parsed.Invoke(null, null));
        var field = Compile("using System; function run(): long { return Int64.MaxValue; }");
        Assert.Equal(long.MaxValue, field.Invoke(null, null));
    }

    [Theory]
    [InlineData("function run(): long { return 9223372036854775809L; }")]
    [InlineData("function run(): long { return 9223372036854775808L; }")]
    [InlineData("function run(): long { return 9223372036854775807L + 1L; }")]
    [InlineData("function run(): long { return 1L / 0L; }")]
    [InlineData("function run(value: int): long { return value; }")]
    [InlineData("function run(value: float): long { return value; }")]
    [InlineData("function run(value: long): int { return value; }")]
    [InlineData("function run(value: long): long { const n: int = 1; return value + n; }")]
    [InlineData("function add(x: long): long { return x + 1; } function run(): long { return static add(9223372036854775807L); }")]
    [InlineData("function negate(x: long): long { return -x; } function run(): long { return static negate(-9223372036854775808L); }")]
    public void Unsupported_or_lossy_conversions_have_source_diagnostics(string source)
    {
        var result = CopelandCompiler.CompileToMir(source, new() { SourcePath = "exact.ts" });
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void Imported_Int64_and_generic_CLR_arguments_use_the_same_native_type()
    {
        var run = Compile("""
            using System;
            using System.Collections.Generic;
            function run(value: Int64): Int64 {
                const values = new List<long>();
                values.Add(value);
                return values[0];
            }
            """);
        Assert.Equal(9007199254740993L, run.Invoke(null, [9007199254740993L]));
    }

    [Fact]
    public void Static_evaluation_retains_exact_Int64_values()
    {
        var run = Compile("""
            function increment(value: long): long { return value + 1; }
            function run(): long { return static increment(9007199254740993L); }
            """);
        Assert.Equal(9007199254740994L, run.Invoke(null, null));
    }

    [Fact]
    public void Binary64_TSON_transport_requires_explicit_decimal_string_projection()
    {
        var result = CopelandCompiler.CompileToMir("""
            const $schema: string = "copeland://tests/int64";
            record Timing { ticks: long; }
            function encode(value: Timing): string ! TsonEncodeError { return tsonEncode(value); }
            """);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Id == "COPE-TSON-ENCODE-0003");
    }

    [Fact]
    public void JavaScript_does_not_silently_emit_Int64_as_a_number()
    {
        var result = CopelandCompiler.CompileToMir("function run(): long { return 9007199254740993L; }");
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotEmpty(JavaScriptBackend.Emit(result.MirCompilation!.Program!).Diagnostics);
    }

    private static MethodInfo Compile(string source)
    {
        var result = CopelandCompiler.CompileToMir(source, new() { SourcePath = "exact.ts" });
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var emitted = CSharpBackend.Emit(result.MirCompilation!.Program!);
        Assert.Empty(emitted.Diagnostics);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compiler = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("Int64_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(emitted.SourceText)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var output = compiler.Emit(bytes);
        Assert.True(output.Success, string.Join(Environment.NewLine, output.Diagnostics));
        return Assembly.Load(bytes.ToArray()).GetType("Copeland.Generated.CopelandModule")!.GetMethod("run")!;
    }
}
