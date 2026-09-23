using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Xunit;
using RoslynCSharpCompilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation;

namespace Copeland.TS.Tests;

// Regressions found while porting the TS microbenchmarks to an SDK project.
public sealed class BenchmarkPortRegressionTests
{
    private const string CapturedAdderSource = """
type Adder = (value: int) => int;

function makeAdder(k: int): Adder {
    return capture { k } (value: int) => value + k;
}

export function run(): int {
    const add: Adder = makeAdder(40);
    return add(2);
}
""";

    [Fact]
    public void Captured_callable_environment_uses_the_requested_module_class_name()
    {
        var compilation = CopelandCompiler.CompileToMir(CapturedAdderSource, new CopelandCompilationOptions { SourcePath = "adder.ts" });
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!, "CopelandProject");

        Assert.Empty(emitted.Diagnostics);
        Assert.Contains("public static class CopelandProject", emitted.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("CopelandModule", emitted.SourceText, StringComparison.Ordinal);

        using var assemblyStream = new MemoryStream();
        RoslynCSharpCompilation csharp = RoslynCSharpCompilation.Create(
            "CopelandModuleNameProof",
            [CSharpSyntaxTree.ParseText(emitted.SourceText)],
            GetRuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        EmitResult result = csharp.Emit(assemblyStream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

        var assembly = System.Reflection.Assembly.Load(assemblyStream.ToArray());
        Type module = assembly.GetType("Copeland.Generated.CopelandProject")!;
        Assert.Equal(42, (int)module.GetMethod("run")!.Invoke(null, [])!);
    }

    [Fact]
    public void Default_emission_keeps_the_CopelandModule_name()
    {
        var compilation = CopelandCompiler.CompileToMir(CapturedAdderSource, new CopelandCompilationOptions { SourcePath = "adder.ts" });
        var emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);

        Assert.Contains("public static class CopelandModule", emitted.SourceText, StringComparison.Ordinal);
        Assert.Contains("=> CopelandModule.", emitted.SourceText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1Module")]
    [InlineData("Copeland.Module")]
    public void Invalid_module_class_names_are_rejected(string name)
    {
        var compilation = CopelandCompiler.CompileToMir(CapturedAdderSource, new CopelandCompilationOptions { SourcePath = "adder.ts" });

        Assert.Throws<ArgumentException>(() => CSharpBackend.Emit(compilation.MirCompilation!.Program!, name));
    }

    [Fact]
    public void Method_call_on_a_copeland_value_names_the_value_instead_of_expecting_an_enum()
    {
        var compilation = CopelandCompiler.CompileToMir("""
function run(text: string): int {
    const parts: string[] = text.Split(",");
    return parts.length;
}
""", new CopelandCompilationOptions { SourcePath = "split.ts" });

        Assert.DoesNotContain(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-ENUM-0010");
        var diagnostic = Assert.Single(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-CALL-0021");
        Assert.Contains("'Split'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'text'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Method_call_on_an_undefined_name_still_reports_the_enum_diagnostic()
    {
        var compilation = CopelandCompiler.CompileToMir("""
function run(): int {
    return Missing.Case(1);
}
""", new CopelandCompilationOptions { SourcePath = "missing.ts" });

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-ENUM-0010");
        Assert.DoesNotContain(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-CALL-0021");
    }

    [Fact]
    public void Binder_diagnostics_inside_template_interpolation_use_absolute_positions()
    {
        const string source = """
function run(text: string): string {
    return `size ${text.Length}`;
}
""";
        var compilation = CopelandCompiler.CompileToMir(source, new CopelandCompilationOptions { SourcePath = "interp.ts" });

        var diagnostic = Assert.Single(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-STRING-0001");
        Assert.Equal(source.IndexOf("Length", StringComparison.Ordinal), diagnostic.Position);
    }

    [Fact]
    public void Parse_diagnostics_inside_template_interpolation_keep_absolute_positions()
    {
        const string source = """
function run(): string {
    return `bad ${1 +}`;
}
""";
        var compilation = CopelandCompiler.CompileToMir(source, new CopelandCompilationOptions { SourcePath = "interp-parse.ts" });

        Assert.NotEmpty(compilation.Diagnostics);
        int holeStart = source.IndexOf("${", StringComparison.Ordinal) + 2;
        int holeEnd = source.IndexOf('}', holeStart);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Position >= holeStart && diagnostic.Position <= holeEnd);
    }

    [Theory]
    [InlineData("1e3", 1000.0)]
    [InlineData("2.5E+2", 250.0)]
    [InlineData("1.66007664274403694e-03", 1.66007664274403694e-03)]
    public void Exponent_literals_lex_as_float(string literal, double expected)
    {
        var tokens = Copeland.TS.Syntax.SyntaxTree.ParseTokens(literal);
        var number = Assert.Single(tokens.Tokens, token => token.Kind == Copeland.TS.Syntax.SyntaxKind.NumberToken);
        Assert.Equal(literal, number.Text);
        Assert.Equal(expected, Assert.IsType<double>(number.Value));
    }

    [Fact]
    public void Exponent_literal_binds_as_float_without_a_decimal_point()
    {
        var compilation = CopelandCompiler.CompileToMir("""
function scale(): float {
    const small: float = 1e-3;
    return small * 2e3;
}
""", new CopelandCompilationOptions { SourcePath = "exponent.ts" });

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Theory]
    [InlineData("1e")]
    [InlineData("1ex")]
    [InlineData("2e+")]
    public void Incomplete_exponent_is_still_an_invalid_literal(string literal)
    {
        var compilation = CopelandCompiler.CompileToMir($"function f(): float {{ return {literal}; }}", new CopelandCompilationOptions { SourcePath = "bad-exponent.ts" });

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-LEX-0004" || diagnostic.Id.StartsWith("COPE-PARSE-", StringComparison.Ordinal));
    }

    private static IEnumerable<MetadataReference> GetRuntimeReferences()
    {
        string trustedPlatformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
    }
}
