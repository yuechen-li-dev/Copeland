using System.Reflection;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Copeland.TS.Lowering;
using Copeland.TS.Mir;
using CopelandSyntaxTree = Copeland.TS.Syntax.SyntaxTree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests.Runtime;

public sealed class M0hRuntimeTests
{
    [Fact]
    public void Executes_Number_Return()
    {
        var assembly = CompileCopelandSource("""
            function one(): number {
              return 1;
            }
            """);

        var value = GeneratedModuleInvoker.Invoke(assembly, "one");
        Assert.Equal(1.0, Assert.IsType<double>(value));
    }

    [Fact]
    public void Executes_Numeric_Add()
    {
        var assembly = CompileCopelandSource("""
            function add(a: number, b: number): number {
              return a + b;
            }
            """);

        var value = GeneratedModuleInvoker.Invoke(assembly, "add", 2.0, 3.0);
        Assert.Equal(5.0, Assert.IsType<double>(value));
    }

    [Fact]
    public void Executes_String_Return()
    {
        var assembly = CompileCopelandSource("""
            function greet(): string {
              return "hello";
            }
            """);

        var value = GeneratedModuleInvoker.Invoke(assembly, "greet");
        Assert.Equal("hello", Assert.IsType<string>(value));
    }

    [Fact]
    public void Executes_Boolean_Logical_Expression()
    {
        var assembly = CompileCopelandSource("""
            function both(a: boolean, b: boolean): boolean {
              return a && b;
            }
            """);

        var value = GeneratedModuleInvoker.Invoke(assembly, "both", true, false);
        Assert.False(Assert.IsType<bool>(value));
    }

    [Fact]
    public void Executes_Let_Assignment()
    {
        var assembly = CompileCopelandSource("""
            function main(): number {
              let x: number = 1;
              x = 2;
              return x;
            }
            """);

        var value = GeneratedModuleInvoker.Invoke(assembly, "main");
        Assert.Equal(2.0, Assert.IsType<double>(value));
    }

    [Fact]
    public void Executes_If_Else()
    {
        var assembly = CompileCopelandSource("""
            function choose(flag: boolean): number {
              if (flag) {
                return 1;
              } else {
                return 2;
              }
            }
            """);

        Assert.Equal(1.0, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(assembly, "choose", true)));
        Assert.Equal(2.0, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(assembly, "choose", false)));
    }

    [Fact]
    public void Executes_Array_Literal_Return()
    {
        var assembly = CompileCopelandSource("""
            function nums(): number[] {
              const xs: number[] = [1, 2, 3];
              return xs;
            }
            """);

        var value = Assert.IsType<double[]>(GeneratedModuleInvoker.Invoke(assembly, "nums"));
        Assert.Equal([1.0, 2.0, 3.0], value);
    }

    [Fact]
    public void Executes_Fallible_Function_Returns_Ok()
    {
        var assembly = CompileCopelandSource("""
            function parseNumber(text: string): number ! ParseError {
              return 1;
            }
            """);

        var result = GeneratedModuleInvoker.Invoke(assembly, "parseNumber", "x");
        Assert.NotNull(result);
        CopeResultAssertions.AssertCopeResultOk(result, 1.0);
    }

    [Fact]
    public void Executes_Propagation_Success_Path()
    {
        var assembly = CompileCopelandSource("""
            function parseNumber(text: string): number ! ParseError {
              return 1;
            }

            function caller(text: string): number ! ParseError {
              const x: number = parseNumber(text)?;
              return x + 1;
            }
            """);

        var result = GeneratedModuleInvoker.Invoke(assembly, "caller", "x");
        Assert.NotNull(result);
        CopeResultAssertions.AssertCopeResultOk(result, 2.0);
    }

    [Fact]
    public void Executes_Void_Fallible_Returns_Ok_Unit()
    {
        var assembly = CompileCopelandSource("""
            function save(): void ! SaveError {
              return;
            }
            """);

        var result = GeneratedModuleInvoker.Invoke(assembly, "save");
        Assert.NotNull(result);
        CopeResultAssertions.AssertCopeResultOkUnit(result);
    }

    [Fact]
    public void Executes_Enum_ZeroPayload_Return()
    {
        var assembly = CompileCopelandSource("""
            enum Choice {
              A,
            }

            function make(): Choice {
              return Choice.A;
            }
            """);

        var result = GeneratedModuleInvoker.Invoke(assembly, "make");
        RuntimeEnumAssertions.AssertEnumCase(result, "A");
    }

    [Fact]
    public void Executes_Enum_Payload_Return()
    {
        var assembly = CompileCopelandSource("""
            enum Shape {
              Circle(radius: number),
            }

            function make(): Shape {
              return Shape.Circle(10);
            }
            """);

        var result = GeneratedModuleInvoker.Invoke(assembly, "make");
        RuntimeEnumAssertions.AssertEnumCase(result, "Circle");
        RuntimeEnumAssertions.AssertEnumPayload(result!, "radius", 10.0);
    }

    [Fact]
    public void Executes_Enum_MultiPayload_Return()
    {
        var assembly = CompileCopelandSource("""
            enum Shape {
              Rect(width: number, height: number),
            }

            function make(): Shape {
              return Shape.Rect(3, 4);
            }
            """);

        var result = GeneratedModuleInvoker.Invoke(assembly, "make");
        RuntimeEnumAssertions.AssertEnumCase(result, "Rect");
        RuntimeEnumAssertions.AssertEnumPayload(result!, "width", 3.0);
        RuntimeEnumAssertions.AssertEnumPayload(result!, "height", 4.0);
    }

    [Fact]
    public void Executes_Enum_Local_RoundTrip()
    {
        var assembly = CompileCopelandSource("""
            enum Choice {
              A,
              B,
            }

            function make(): Choice {
              const c: Choice = Choice.B;
              return c;
            }
            """);

        var result = GeneratedModuleInvoker.Invoke(assembly, "make");
        RuntimeEnumAssertions.AssertEnumCase(result, "B");
    }

    [Fact]
    public void Executes_Enum_Array_Return()
    {
        var assembly = CompileCopelandSource("""
            enum Choice {
              A,
              B,
            }

            function choices(): Choice[] {
              const xs: Choice[] = [Choice.A, Choice.B];
              return xs;
            }
            """);

        var result = GeneratedModuleInvoker.Invoke(assembly, "choices");
        var values = Assert.IsAssignableFrom<Array>(result);
        Assert.Equal(2, values.Length);
        RuntimeEnumAssertions.AssertEnumCase(values.GetValue(0), "A");
        RuntimeEnumAssertions.AssertEnumCase(values.GetValue(1), "B");
    }

    [Fact]
    public void Executes_Match_Over_ZeroPayload_Enum()
    {
        var assembly = CompileCopelandSource("""
            enum Choice {
              A,
              B,
            }

            function value(choice: Choice): number {
              return match choice {
                A => 1,
                B => 2,
              };
            }

            function valueA(): number {
              return value(Choice.A);
            }

            function valueB(): number {
              return value(Choice.B);
            }
            """);

        Assert.Equal(1.0, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(assembly, "valueA")));
        Assert.Equal(2.0, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(assembly, "valueB")));
    }

    [Fact]
    public void Executes_Match_Over_Single_Payload_Enum()
    {
        var assembly = CompileCopelandSource("""
            enum Shape {
              Point,
              Circle(radius: number),
            }

            function value(shape: Shape): number {
              return match shape {
                Point => 0,
                Circle(radius) => radius,
              };
            }

            function circleValue(): number {
              return value(Shape.Circle(7));
            }
            """);

        Assert.Equal(7.0, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(assembly, "circleValue")));
    }

    [Fact]
    public void Executes_Match_Over_Multi_Payload_Enum()
    {
        var assembly = CompileCopelandSource("""
            enum Shape {
              Rect(width: number, height: number),
            }

            function area(shape: Shape): number {
              return match shape {
                Rect(width, height) => width * height,
              };
            }

            function rectArea(): number {
              return area(Shape.Rect(3, 4));
            }
            """);

        Assert.Equal(12.0, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(assembly, "rectArea")));
    }

    [Fact]
    public void Executes_Match_Over_String_Payload_Enum()
    {
        var assembly = CompileCopelandSource("""
            enum Status {
              Idle,
              Loaded(name: string),
            }

            function label(status: Status): string {
              return match status {
                Idle => "idle",
                Loaded(name) => name,
              };
            }

            function loadedLabel(): string {
              return label(Status.Loaded("Ada"));
            }
            """);

        Assert.Equal("Ada", Assert.IsType<string>(GeneratedModuleInvoker.Invoke(assembly, "loadedLabel")));
    }

    [Theory]
    [InlineData("function f(): number { return \"x\"; }")]
    [InlineData("function f(): number { return null; }")]
    [InlineData("function p(): number ! ParseError { return 1; } function c(): number ! SaveError { const x: number = p()?; return x; }")]
    [InlineData("function p(): number ! ParseError { return 1; } function c(): number { const x: number = p()?; return x; }")]
    public void Invalid_Programs_Are_Gated_Before_Codegen_And_Runtime(string source)
    {
        var mir = MirLowerer.Lower(CopelandSyntaxTree.Parse(source));
        Assert.NotEmpty(mir.Diagnostics);
        Assert.Null(mir.Program);
    }

    private static Assembly CompileCopelandSource(string source)
    {
        var compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.NotNull(compilation.MirCompilation?.Program);

        var csharpCompilation = CSharpBackend.Emit(compilation.MirCompilation.Program);
        Assert.Empty(csharpCompilation.Diagnostics);

        var compile = RoslynCompileHelper.CompileGeneratedSource(csharpCompilation.SourceText);
        Assert.True(compile.Success, string.Join(Environment.NewLine, compile.Diagnostics));
        return compile.Assembly!;
    }
}

internal static class RuntimeEnumAssertions
{
    public static void AssertEnumCase(object? value, string caseName)
    {
        Assert.NotNull(value);
        Assert.Equal(caseName, value!.GetType().Name);
    }

    public static void AssertEnumPayload(object value, string propertyName, object? expected)
    {
        var property = value.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        Assert.Equal(expected, property!.GetValue(value));
    }
}

internal static class RoslynCompileHelper
{
    public static RoslynCompileResult CompileGeneratedSource(string sourceText)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, parseOptions);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.GCSettings).Assembly.Location)
        };

        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            assemblyName: $"Copeland.Generated.Tests.{Guid.NewGuid():N}",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        var diagnostics = emitResult.Diagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToArray();

        if (!emitResult.Success)
            return new RoslynCompileResult(null, diagnostics, false);

        stream.Position = 0;
        return new RoslynCompileResult(Assembly.Load(stream.ToArray()), diagnostics, true);
    }
}

internal sealed class RoslynCompileResult(Assembly? assembly, IReadOnlyList<string> diagnostics, bool success)
{
    public Assembly? Assembly { get; } = assembly;
    public IReadOnlyList<string> Diagnostics { get; } = diagnostics;
    public bool Success { get; } = success;
}

internal static class GeneratedModuleInvoker
{
    public static object? Invoke(Assembly assembly, string methodName, params object?[] args)
    {
        var moduleType = assembly.GetType("Copeland.Generated.CopelandModule", throwOnError: true)!;
        var method = moduleType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        try
        {
            return method.Invoke(null, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}

internal static class CopeResultAssertions
{
    public static void AssertCopeResultOk(object result, object expectedValue)
    {
        Assert.True((bool)GetRequiredProperty(result, "IsOk").GetValue(result)!);
        var value = GetRequiredProperty(result, "Value").GetValue(result);
        Assert.Equal(expectedValue, value);
    }

    public static void AssertCopeResultOkUnit(object result)
    {
        Assert.True((bool)GetRequiredProperty(result, "IsOk").GetValue(result)!);
        var value = GetRequiredProperty(result, "Value").GetValue(result);
        Assert.NotNull(value);
        Assert.Equal("CopeUnit", value.GetType().Name);
    }

    private static PropertyInfo GetRequiredProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        return property!;
    }
}
