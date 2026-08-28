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
    public void Executes_mutable_numeric_storage_and_freezes_a_snapshot()
    {
        Assembly assembly = CompileCopelandSource("""
            function main(): int {
                const values: MutableArray<int> = MutableArray<int>(5);
                let index: int = 0;
                while (index < values.length) {
                    values[index] = index * index;
                    index = index + 1;
                }
                const frozen: int[] = values.freeze();
                values[2] = 99;
                return frozen[2] + values[2];
            }
            """);

        Assert.Equal(103, GeneratedModuleInvoker.Invoke(assembly, "main"));
    }

    [Fact]
    public void Executes_explicit_async_state_machine_and_reuses_completed_result()
    {
        var assembly = CompileCopelandSource("""
            async function read(value: number): number { return value + 1; }
            async function load(value: number): number {
                const pending: Async<number> = read(value);
                const result: number = await pending;
                return result + 1;
            }
            """);

        object computation = Assert.IsAssignableFrom<object>(GeneratedModuleInvoker.Invoke(assembly, "load", 40.0));
        Type computationType = computation.GetType();
        Assert.True(Assert.IsType<bool>(computationType.GetProperty("IsCompleted")!.GetValue(computation)));
        Assert.Equal(42.0, Assert.IsType<double>(computationType.GetProperty("Value")!.GetValue(computation)));
        Assert.Equal(42.0, Assert.IsType<double>(computationType.GetProperty("Value")!.GetValue(computation)));
    }

    [Fact]
    public void Async_await_preserves_a_typed_result_value()
    {
        var assembly = CompileCopelandSource("""
            async function parse(value: number): number ! ParseError { return value + 1; }
            async function load(value: number): number ! ParseError {
                const pending: Async<number ! ParseError> = parse(value);
                return await pending;
            }
            """);

        object computation = Assert.IsAssignableFrom<object>(GeneratedModuleInvoker.Invoke(assembly, "load", 41.0));
        object result = Assert.IsAssignableFrom<object>(computation.GetType().GetProperty("Value")!.GetValue(computation));
        Assert.True(Assert.IsType<bool>(result.GetType().GetProperty("IsOk")!.GetValue(result)));
        Assert.Equal(42.0, Assert.IsType<double>(result.GetType().GetProperty("Value")!.GetValue(result)));
    }

    [Fact]
    public void Async_await_question_propagates_result_success_and_error()
    {
        var assembly = CompileCopelandSource("""
            async function parse(value: number): number ! string {
                if (value < 0) { return err("negative"); }
                return value + 1;
            }
            async function load(value: number): number ! string {
                const pending: Async<number ! string> = parse(value);
                const parsed: number = await pending?;
                return parsed + 1;
            }
            """);

        object successful = GeneratedModuleInvoker.Invoke(assembly, "load", 40.0)!;
        object successfulResult = successful.GetType().GetProperty("Value")!.GetValue(successful)!;
        CopeResultAssertions.AssertCopeResultOk(successfulResult, 42.0);

        object failed = GeneratedModuleInvoker.Invoke(assembly, "load", -1.0)!;
        object failedResult = failed.GetType().GetProperty("Value")!.GetValue(failed)!;
        CopeResultAssertions.AssertCopeResultErr(failedResult, "negative");
    }

    [Fact]
    public void Async_await_question_composes_with_a_return_expression()
    {
        var assembly = CompileCopelandSource("""
            async function parse(value: number): number ! string {
                if (value < 0) { return err("negative"); }
                return value + 1;
            }
            async function load(value: number): number ! string {
                return (await parse(value)?) + 1;
            }
            """);

        object successful = GeneratedModuleInvoker.Invoke(assembly, "load", 40.0)!;
        CopeResultAssertions.AssertCopeResultOk(successful.GetType().GetProperty("Value")!.GetValue(successful)!, 42.0);

        object failed = GeneratedModuleInvoker.Invoke(assembly, "load", -1.0)!;
        CopeResultAssertions.AssertCopeResultErr(failed.GetType().GetProperty("Value")!.GetValue(failed)!, "negative");
    }

    [Fact]
    public void Async_try_except_recovers_a_typed_result_after_await()
    {
        var assembly = CompileCopelandSource("""
            async function parse(value: number): number ! string {
                if (value < 0) { return err("negative"); }
                return value + 1;
            }
            async function load(value: number): number {
                return try {
                    const parsed: number = await parse(value)?;
                    parsed + 1
                } except (error) {
                    0
                };
            }
            """);

        object successful = GeneratedModuleInvoker.Invoke(assembly, "load", 40.0)!;
        object recovered = GeneratedModuleInvoker.Invoke(assembly, "load", -1.0)!;

        Assert.Equal(42.0, successful.GetType().GetProperty("Value")!.GetValue(successful));
        Assert.Equal(0.0, recovered.GetType().GetProperty("Value")!.GetValue(recovered));
    }

    [Fact]
    public void Async_await_states_use_their_own_typed_frame_slots()
    {
        var assembly = CompileCopelandSource("""
            async function count(): number { return 1; }
            async function label(): string { return "ready"; }
            async function combine(): string {
                const quantity: number = await count();
                const text: string = await label();
                return text;
            }
            """);

        object computation = GeneratedModuleInvoker.Invoke(assembly, "combine")!;

        Assert.Equal("ready", computation.GetType().GetProperty("Value")!.GetValue(computation));
    }

    [Fact]
    public void Async_nested_await_expressions_preserve_operand_order()
    {
        var assembly = CompileCopelandSource("""
            async function read(value: number): number { return value + 1; }
            async function combine(value: number): number {
                return (await read(value)) + (await read(1));
            }
            """);

        object computation = GeneratedModuleInvoker.Invoke(assembly, "combine", 40.0)!;

        Assert.Equal(43.0, computation.GetType().GetProperty("Value")!.GetValue(computation));
    }

    [Fact]
    public void Async_await_in_a_loop_condition_reenters_the_condition_state()
    {
        var assembly = CompileCopelandSource("""
            async function below(value: number): boolean { return value < 3; }
            async function count(): number {
                let value: number = 0;
                while (await below(value)) {
                    value = value + 1;
                }
                return value;
            }
            """);

        object computation = GeneratedModuleInvoker.Invoke(assembly, "count")!;

        Assert.Equal(3.0, computation.GetType().GetProperty("Value")!.GetValue(computation));
    }

    [Fact]
    public void Internal_async_pending_seam_arbitrates_terminal_outcomes_once()
    {
        var assembly = CompileCopelandSource("async function value(): number { return 1; }");
        Type factory = assembly.GetType("Copeland.Generated.CopeAsyncPending")!;
        MethodInfo create = factory.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(double));
        object pending = create.Invoke(null, null)!;
        int resumed = 0;
        int cancelled = 0;
        Type pendingType = pending.GetType();
        pendingType.GetMethod("Subscribe")!.Invoke(pending, [new Action(() => resumed++), new Action(() => cancelled++), new Action(() => throw new InvalidOperationException("unexpected panic"))]);
        pendingType.GetMethod("Cancel")!.Invoke(pending, null);
        pendingType.GetMethod("Resolve")!.Invoke(pending, [99.0]);
        pendingType.GetMethod("Cancel")!.Invoke(pending, null);

        Assert.True(Assert.IsType<bool>(pendingType.GetProperty("IsCompleted")!.GetValue(pending)));
        Assert.True(Assert.IsType<bool>(pendingType.GetProperty("IsCancelled")!.GetValue(pending)));
        Assert.False(Assert.IsType<bool>(pendingType.GetProperty("IsPanicked")!.GetValue(pending)));
        Assert.Equal(0, resumed);
        Assert.Equal(1, cancelled);
    }

    [Fact]
    public void Executes_Nominal_Union_Contextual_Construction_And_Match()
    {
        var assembly = CompileCopelandSource("""
            record Circle { radius: number; }
            record Rectangle { width: number; height: number; }
            type Shape = Circle | Rectangle;
            function area(): number {
              const circle: Circle = { radius: 4 };
              const shape: Shape = circle;
              return match shape {
                Circle(value) => value.radius * value.radius,
                Rectangle(value) => value.width * value.height,
              };
            }
            """);

        Assert.Equal(16.0, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(assembly, "area")));
    }

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
    public void Executes_First_Class_Result_Construction_Match_And_Forwarding()
    {
        var assembly = CompileCopelandSource("""
            function fail(): number ! string {
              return err("bad");
            }

            function forward(): number ! string {
              return fail();
            }

            function recover(): number {
              return match fail() {
                ok(value) => value,
                err(error) => 42,
              };
            }
            """);

        var forwarded = GeneratedModuleInvoker.Invoke(assembly, "forward");
        Assert.NotNull(forwarded);
        CopeResultAssertions.AssertCopeResultErr(forwarded!, "bad");
        Assert.Equal(42.0, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(assembly, "recover")));
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

        string trustedPlatformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var references = trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();

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

    public static void AssertCopeResultErr(object result, object expectedError)
    {
        Assert.False((bool)GetRequiredProperty(result, "IsOk").GetValue(result)!);
        var error = GetRequiredProperty(result, "Error").GetValue(result);
        Assert.Equal(expectedError, error);
    }

    private static PropertyInfo GetRequiredProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        return property!;
    }
}
