using System.Diagnostics;
using System.Text;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests.Runtime;

public sealed class ResultBackendParityTests
{
    [Fact]
    public async Task JavaScript_And_CSharp_Table_Vertical_Program_Returns_8_Repeatedly()
    {
        const string source = """
            record Point { x: number; }
            enum State { Value(point: Point), Empty, }
            record table Values {
                point: Point = [{ x: 2 }];
                state: State = [State.Value({ x: 3 })];
                result: number ! string = [ok(6)];
            }
            function main(): number {
                const cell: number ! string = Values.result[0]!;
                const value: number = match cell { ok(payload) => payload, err(error) => 0, };
                const row: Values.Row = Values[0]!;
                return value + row.point.x;
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program!);
        Assert.True(javaScript.Success, string.Join(Environment.NewLine, javaScript.Diagnostics));
        Assert.Empty(csharp.Diagnostics);

        ProcessResult firstNode = await RunNodeAsync(javaScript.SourceText + "console.log(main());\n");
        ProcessResult secondNode = await RunNodeAsync(javaScript.SourceText + "console.log(main());\n");
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));

        Assert.Equal("8\n", firstNode.StdOut);
        Assert.Equal(firstNode, secondNode);
        Assert.Equal(8d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
        Assert.Equal(8d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }

    [Fact]
    public async Task JavaScript_And_CSharp_Record_Closeout_Matrix_Preserves_Control_Flow_And_Exactly_Once_Order()
    {
        const string source = """
            record Point { x: number; y: number; }
            record Box { point: Point; }
            enum Choice { Some(point: Point), None, }

            function make(x: number, y: number): Point { return { x: x, y: y }; }
            function good(): Point ! string { return ok({ x: 40, y: 2 }); }
            function bad(): Point ! string { return err("bad"); }
            function consume(point: Point, later: number): number { return point.x + point.y + later; }

            function main(): number {
              let trace: number = 0;
              const nested: Box = {
                point: {
                  y: trace = trace * 10 + 2,
                  x: trace = trace * 10 + 1,
                },
              };
              const argumentOrder: number = consume(
                { x: trace = trace * 10 + 3, y: 0 },
                trace = trace * 10 + 4
              );
              const conditionalAccess: number = (if true { make(5, 0) } else { bad()! }).x;
              const enumAccess: number = (match Choice.Some(make(6, 0)) {
                Some(point) => point,
                None => bad()!,
              }).x;
              const resultAccess: number = (match good() {
                ok(point) => point,
                err(error) => bad()!,
              }).x;
              const unwrapAccess: number = good()!.y;
              const handlerAccess: number = (try { good()? } except (error) { bad()! }).x;
              const original: Point = { x: 7, y: 8 };
              const updated: Point = (original with { x: 9 }) with { y: 10 };
              const withArgument: number = consume(updated, 1);
              const shortCircuited: boolean = false && bad()!.x == 0;
              const branchValue: number = if shortCircuited { bad()!.x } else { 0 };
              return trace
                + nested.point.x + nested.point.y
                + argumentOrder
                + conditionalAccess
                + enumAccess
                + resultAccess
                + unwrapAccess
                + handlerAccess
                + original.x + original.y
                + updated.x + updated.y
                + withArgument
                + branchValue;
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program!);
        Assert.True(javaScript.Success, string.Join(Environment.NewLine, javaScript.Diagnostics));
        Assert.Empty(csharp.Diagnostics);

        ProcessResult firstNode = await RunNodeAsync(javaScript.SourceText + "console.log(main());\n");
        ProcessResult secondNode = await RunNodeAsync(javaScript.SourceText + "console.log(main());\n");
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));

        Assert.Equal("4651\n", firstNode.StdOut);
        Assert.Equal(firstNode, secondNode);
        Assert.Equal(4651d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
        Assert.Equal(4651d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }

    [Fact]
    public async Task JavaScript_And_CSharp_Record_Vertical_Program_Returns_42_Repeatedly()
    {
        const string source = """
            record ScreenPoint { x: number; y: number; }
            record WorldPoint { x: number; y: number; }
            record Envelope { point: ScreenPoint; }
            enum Event { Moved(point: ScreenPoint), }
            function bad(): ScreenPoint ! string { return err("bad"); }
            function fallback(): ScreenPoint { return { x: 40, y: 2 }; }
            function recovered(): ScreenPoint { return try { bad()? } except (error) { fallback() }; }
            function moved(point: ScreenPoint): ScreenPoint {
              let updated: ScreenPoint = point;
              updated = updated with { y: 2, x: 40 };
              return updated;
            }
            function main(): number {
              const other: WorldPoint = { x: 40, y: 2 };
              const envelope: Envelope = { point: moved({ x: 1, y: 1 }) };
              const event: Event = Event.Moved(recovered());
              const recoveredPoint: ScreenPoint = match event { Moved(point) => point, };
              return envelope.point.x + recoveredPoint.y;
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(javaScript.Success, string.Join(Environment.NewLine, javaScript.Diagnostics));
        ProcessResult firstNode = await RunNodeAsync(javaScript.SourceText + "console.log(main());\n");
        ProcessResult secondNode = await RunNodeAsync(javaScript.SourceText + "console.log(main());\n");

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program!);
        Assert.Empty(csharp.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));

        Assert.Equal("42\n", firstNode.StdOut);
        Assert.Equal(firstNode, secondNode);
        Assert.Equal(42d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
        Assert.Equal(42d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }

    [Fact]
    public async Task JavaScript_And_CSharp_Preserve_Argument_And_Record_Initializer_Order()
    {
        const string source = """
            record Point { x: number; }
            function combine(first: number, point: Point): number { return first * 1000 + point.x * 10; }
            function main(): number {
              let trace: number = 0;
              const result: number = combine(
                trace = trace * 10 + 1,
                { x: trace = trace * 10 + 2 }
              );
              return result + trace;
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(javaScript.Success, string.Join(Environment.NewLine, javaScript.Diagnostics));
        ProcessResult node = await RunNodeAsync(javaScript.SourceText + "console.log(main());\n");

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program!);
        Assert.Empty(csharp.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));

        Assert.Equal("1132\n", node.StdOut);
        Assert.Equal(1132d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }

    [Fact]
    public async Task JavaScript_And_CSharp_Repeat_The_Ratified_Fallibility_Matrix_Deterministically()
    {
        const string source = """
            function good(): number ! string { return ok(4); }
            function bad(): number ! string { return err("bad"); }
            function forward(value: number ! string): number ! string { return value; }
            function inspect(value: number ! string): number {
              return match value { ok(resultValue) => resultValue, err(error) => 0, };
            }
            function successWithoutRecovery(): number {
              return try { good()? } except (error) { 90 };
            }
            function localRecovery(): number {
              return try { bad()? } except (error) { 5 };
            }
            function nestedInnerRecovery(): number {
              return try { good()?; try { bad()? } except (inner) { 6 } } except (outer) { 91 };
            }
            function outerRecovery(): number {
              return try { try { bad()? } except (inner) { bad()? } } except (outer) { 7 };
            }
            function handlerToFunction(): number ! string {
              return try { bad()? } except (error) { bad()? };
            }
            function main(): number {
              const matched: number = match good() { ok(value) => value, err(error) => 0, };
              return successWithoutRecovery() + localRecovery() + nestedInnerRecovery() + outerRecovery() + inspect(handlerToFunction()) + inspect(forward(bad())) + matched + good()!;
            }
            """;

        CopelandCompilation firstCompilation = CopelandCompiler.CompileToMir(source);
        CopelandCompilation secondCompilation = CopelandCompiler.CompileToMir(source);
        Assert.True(firstCompilation.Success, string.Join(Environment.NewLine, firstCompilation.Diagnostics));
        Assert.True(secondCompilation.Success, string.Join(Environment.NewLine, secondCompilation.Diagnostics));
        Assert.Equal(firstCompilation.MirText, secondCompilation.MirText);

        JavaScriptCompilation firstJavaScript = JavaScriptBackend.Emit(firstCompilation.MirCompilation!.Program!);
        JavaScriptCompilation secondJavaScript = JavaScriptBackend.Emit(secondCompilation.MirCompilation!.Program!);
        Assert.True(firstJavaScript.Success, string.Join(Environment.NewLine, firstJavaScript.Diagnostics));
        Assert.Equal(firstJavaScript.SourceText, secondJavaScript.SourceText);
        Assert.DoesNotContain("catch", firstJavaScript.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("export ", firstJavaScript.SourceText, StringComparison.Ordinal);

        ProcessResult firstNode = await RunNodeAsync(firstJavaScript.SourceText + "console.log(main());\n");
        ProcessResult secondNode = await RunNodeAsync(firstJavaScript.SourceText + "console.log(main());\n");
        Assert.Equal("30\n", firstNode.StdOut);
        Assert.Equal(string.Empty, firstNode.StdErr);
        Assert.Equal(firstNode, secondNode);

        CSharpCompilation firstCSharp = CSharpBackend.Emit(firstCompilation.MirCompilation.Program!);
        CSharpCompilation secondCSharp = CSharpBackend.Emit(secondCompilation.MirCompilation.Program!);
        Assert.Empty(firstCSharp.Diagnostics);
        Assert.Equal(firstCSharp.SourceText, secondCSharp.SourceText);
        Assert.DoesNotMatch(@"\btry\s*\{", firstCSharp.SourceText);
        Assert.DoesNotMatch(@"\bcatch\s*\(", firstCSharp.SourceText);

        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(firstCSharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Assert.Equal(30d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
        Assert.Equal(30d, Assert.IsType<double>(GeneratedModuleInvoker.Invoke(generated.Assembly!, "main")));
    }

    [Fact]
    public async Task JavaScript_And_CSharp_Observe_The_Same_Typed_Try_Except_Behavior()
    {
        const string source = """
            function bad(): number ! string { return err("bad"); }
            function main(): number {
              return try {
                try { bad()? } except (inner) { bad()? }
              } except (outer) {
                7
              };
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(javaScript.Success, string.Join(Environment.NewLine, javaScript.Diagnostics));
        ProcessResult node = await RunNodeAsync(javaScript.SourceText + "console.log(main());\n");

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program!);
        Assert.Empty(csharp.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        object? csharpResult = GeneratedModuleInvoker.Invoke(generated.Assembly!, "main");

        Assert.Equal(7d, Assert.IsType<double>(csharpResult));
        Assert.Equal("7\n", node.StdOut);
        Assert.Equal(string.Empty, node.StdErr);
    }

    [Fact]
    public async Task JavaScript_And_CSharp_Classify_Unwrap_Panic_The_Same_Way()
    {
        const string source = """
            function bad(): number ! string { return err("bad"); }
            function main(): number { return bad()!; }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(javaScript.Success, string.Join(Environment.NewLine, javaScript.Diagnostics));
        ProcessResult node = await RunNodeAsync(javaScript.SourceText + "try { main(); } catch (error) { console.log(error.message); }\n");

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program!);
        Assert.Empty(csharp.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        Exception panic = Assert.ThrowsAny<Exception>(() => GeneratedModuleInvoker.Invoke(generated.Assembly!, "main"));

        Assert.Equal("COPE-PANIC-UNWRAP: Result unwrap encountered err", panic.Message);
        Assert.Equal("COPE-PANIC-UNWRAP: Result unwrap encountered err\n", node.StdOut);
        Assert.Equal(string.Empty, node.StdErr);
    }

    [Fact]
    public async Task JavaScript_And_CSharp_Observe_The_Same_Result_Behavior()
    {
        const string source = """
            enum Box { Value(outcome: number ! string), }
            function good(): number ! string { return ok(4); }
            function bad(): number ! string { return err("bad"); }
            function forward(value: number ! string): number ! string { return value; }
            function observe(value: number ! string): number {
              return match value { ok(resultValue) => resultValue, err(error) => 0, };
            }
            function stored(): number ! string {
              const outcome: number ! string = good();
              const value: number = outcome?;
              return value + 1;
            }
            function failedPropagation(): number ! string {
              const value: number = bad()?;
              return value + 1;
            }
            function boxed(): Box { return Box.Value(good()); }
            function inspectBox(box: Box): number {
              return match box { Value(outcome) => observe(outcome), };
            }
            function nested(): (number ! string) ! string { return ok(ok(7)); }
            function inspectNested(): number {
              return match nested() { ok(inner) => observe(inner), err(error) => 0, };
            }
            function saved(): void ! string { return; }
            function inspectSaved(): number {
              return match saved() { ok(value) => 1, err(error) => 0, };
            }
            function main(): number {
              return observe(forward(good())) + observe(bad()) + observe(stored()) + observe(failedPropagation()) + inspectBox(boxed()) + inspectNested() + inspectSaved();
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));

        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(javaScript.Success, string.Join(Environment.NewLine, javaScript.Diagnostics));
        ProcessResult node = await RunNodeAsync(javaScript.SourceText + "console.log(main());\n");

        CSharpCompilation csharp = CSharpBackend.Emit(compilation.MirCompilation.Program!);
        Assert.Empty(csharp.Diagnostics);
        RoslynCompileResult generated = RoslynCompileHelper.CompileGeneratedSource(csharp.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));
        object? csharpResult = GeneratedModuleInvoker.Invoke(generated.Assembly!, "main");

        Assert.Equal(21d, Assert.IsType<double>(csharpResult));
        Assert.Equal("21\n", node.StdOut);
        Assert.Equal(string.Empty, node.StdErr);
    }

    private static async Task<ProcessResult> RunNodeAsync(string source)
    {
        string directory = Path.Combine(Path.GetTempPath(), "copeland-result-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "program.js");
        try
        {
            await File.WriteAllTextAsync(scriptPath, source, new UTF8Encoding(false));
            var startInfo = new ProcessStartInfo("node")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(scriptPath);
            using Process process = Process.Start(startInfo)!;
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            return new ProcessResult(stdout, stderr);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed record ProcessResult(string StdOut, string StdErr);
}
