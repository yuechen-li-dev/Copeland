using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class JavaScriptRuntimeTests
{
    [Fact]
    public async Task Node_Proves_Table_Columnar_Immutability_Nominality_And_Bounds()
    {
        JavaScriptCompilation emitted = Emit("""
            record table First {
                x: [-0, 2];
                label: string = ["zero", "two"];
            }
            record table Second {
                x: [-0, 2];
                label: string = ["zero", "two"];
            }
            function first(): First { return First; }
            function again(): First { return First; }
            function getColumn(): column number { return First.x; }
            function read(index: number): number ! TableBoundsError { return First.x[index]; }
            function row(index: number): First.Row ! TableBoundsError { return First[index]; }
            function field(): number { const value: First.Row = First[1]!; return value.x; }
            function other(): Second.Row ! TableBoundsError { return Second[0]; }
            function readFirst(value: First.Row): number { return value.x; }
            """);

        string script = emitted.SourceText + """
            const table = first();
            const same = again();
            const columnValue = getColumn();
            const tableSymbols = Object.getOwnPropertySymbols(table);
            const columnSymbols = Object.getOwnPropertySymbols(columnValue);
            function category(action) {
              try { action(); return "accepted"; } catch (error) { return error.message; }
            }
            console.log(table === same);
            console.log(Object.getPrototypeOf(table) === null && Object.isFrozen(table));
            console.log(Object.getPrototypeOf(columnValue) === null && Object.isFrozen(columnValue));
            console.log(Array.isArray(columnValue));
            console.log(tableSymbols.every((symbol) => {
              const descriptor = Object.getOwnPropertyDescriptor(table, symbol);
              return descriptor.writable === false && descriptor.configurable === false;
            }));
            console.log(columnSymbols.every((symbol) => {
              const descriptor = Object.getOwnPropertyDescriptor(columnValue, symbol);
              return descriptor.writable === false && descriptor.configurable === false;
            }));
            console.log(read(0).$payload[0]);
            console.log(Object.is(read(-0).$payload[0], -0));
            console.log(read(NaN).$payload[0].$tag);
            console.log(read(Infinity).$payload[0].$tag);
            console.log(read(0.5).$payload[0].$tag);
            console.log(read(-1).$payload[0].$tag);
            console.log(read(2).$payload[0].$tag);
            console.log(field());
            try { table.extra = 1; } catch (error) {}
            try { columnValue.extra = 1; } catch (error) {}
            console.log(!Object.prototype.hasOwnProperty.call(table, "extra"));
            console.log(!Object.prototype.hasOwnProperty.call(columnValue, "extra"));
            console.log(category(() => readFirst(other(0).$payload[0])));
            """;

        ProcessResult firstRun = await RunNodeAsync(script);
        ProcessResult secondRun = await RunNodeAsync(script);

        string invariant = "Copeland JavaScript backend invariant failure.\n";
        Assert.Equal(
            "true\ntrue\ntrue\nfalse\ntrue\ntrue\n-0\ntrue\nInvalidIndex\nInvalidIndex\nInvalidIndex\nOutOfBounds\nOutOfBounds\n2\ntrue\ntrue\n"
            + invariant,
            firstRun.StdOut);
        Assert.Equal(string.Empty, firstRun.StdErr);
        Assert.Equal(firstRun, secondRun);
    }

    [Fact]
    public async Task Node_Proves_Record_Nominality_Immutability_And_Representation_Isolation()
    {
        JavaScriptCompilation emitted = Emit("""
            record First { x: number; y: number; }
            record Second { x: number; y: number; }
            record Nested { value: First; }
            enum Box { Value(value: First), }
            function makeFirst(): First { return { x: 40, y: 2 }; }
            function makeSecond(): Second { return { x: 40, y: 2 }; }
            function makeNested(): Nested { return { value: { x: 40, y: 2 } }; }
            function makeBox(): Box { return Box.Value({ x: 40, y: 2 }); }
            function makeResult(): First ! string { return ok({ x: 40, y: 2 }); }
            function bad(): First ! string { return err("bad"); }
            function fallback(): First { return { x: 40, y: 2 }; }
            function handled(): First { return try { bad()? } except (error) { fallback() }; }
            function goodNumber(): number ! string { return ok(1); }
            function guarded(value: First): number { return try { goodNumber()?; value.x } except (error) { 0 }; }
            function readFirst(value: First): number { return value.x + value.y; }
            function readBox(value: Box): number { return match value { Value(item) => item.x, }; }
            function readResult(value: First ! string): number { return match value { ok(item) => item.x, err(error) => 0, }; }
            """);

        Match flowFactory = Regex.Match(emitted.SourceText!, @"function (?<name>__cope_m3_flow_value_\d+)\(");
        Assert.True(flowFactory.Success, emitted.SourceText);
        string script = (emitted.SourceText + """
            const first = makeFirst();
            const second = makeSecond();
            const nested = makeNested();
            const symbols = Object.getOwnPropertySymbols(first);
            const descriptorsAreFixed = symbols.every((symbol) => {
              const descriptor = Object.getOwnPropertyDescriptor(first, symbol);
              return descriptor.writable === false && descriptor.configurable === false;
            });
            function category(action) {
              try { action(); return "accepted"; } catch (error) { return error.message; }
            }
            const ordinary = { x: 40, y: 2 };
            const frozen = Object.freeze({ x: 40, y: 2 });
            const nullPrototype = Object.freeze(Object.assign(Object.create(null), { "$record": "r1", "$field": 40 }));
            console.log(Object.getPrototypeOf(first) === null);
            console.log(Object.isFrozen(first));
            console.log(descriptorsAreFixed);
            console.log(Object.isFrozen(nested) && Object.isFrozen(nested[Object.getOwnPropertySymbols(nested)[1]]));
            console.log(category(() => readFirst(second)));
            console.log(category(() => readFirst(ordinary)));
            console.log(category(() => readFirst(frozen)));
            console.log(category(() => readFirst(nullPrototype)));
            console.log(category(() => readFirst(makeBox())));
            console.log(category(() => readFirst(makeResult())));
            console.log(category(() => readFirst(__FLOW_FACTORY__(first))));
            console.log(category(() => guarded(second)));
            console.log(category(() => readBox(first)));
            console.log(category(() => readResult(first)));
            try { first[symbols[1]] = 0; } catch (error) {}
            try { first.extra = 1; } catch (error) {}
            try { delete first[symbols[1]]; } catch (error) {}
            console.log(readFirst(first));
            """).Replace("__FLOW_FACTORY__", flowFactory.Groups["name"].Value, StringComparison.Ordinal);

        ProcessResult firstRun = await RunNodeAsync(script);
        ProcessResult secondRun = await RunNodeAsync(script);
        string invariant = "Copeland JavaScript backend invariant failure.\n";
        Assert.Equal("true\ntrue\ntrue\ntrue\n" + string.Concat(Enumerable.Repeat(invariant, 10)) + "42\n", firstRun.StdOut);
        Assert.Equal(string.Empty, firstRun.StdErr);
        Assert.Equal(firstRun, secondRun);
    }

    [Fact]
    public async Task Node_Proves_Record_Order_ExactlyOnce_With_And_Selected_Branches()
    {
        JavaScriptCompilation emitted = Emit("""
            record Point { x: number; y: number; }
            function first(): number { return 40; }
            function second(): number { return 2; }
            function source(): Point { return { x: 1, y: 1 }; }
            function receiver(): Point { return { x: 42, y: 0 }; }
            function selected(): Point { return { x: 42, y: 0 }; }
            function unselected(): Point { return { x: 0, y: 0 }; }
            function construct(): Point { return { y: second(), x: first() }; }
            function update(): Point { return source() with { y: second(), x: first() }; }
            function read(): number { return receiver().x; }
            function choose(): Point { return if true { selected() } else { unselected() }; }
            function main(): number {
              let point: Point = construct();
              point = point with { y: 2, x: 40 };
              return point.x + point.y;
            }
            """);

        string script = emitted.SourceText + """
            const trace = [];
            function wrap(original, name) {
              return (...args) => { trace.push(name); return original(...args); };
            }
            first = wrap(first, "first");
            second = wrap(second, "second");
            source = wrap(source, "source");
            receiver = wrap(receiver, "receiver");
            selected = wrap(selected, "selected");
            unselected = wrap(unselected, "unselected");
            const original = source();
            trace.length = 0;
            const changed = update();
            console.log(trace.join(","));
            trace.length = 0;
            console.log(read());
            console.log(trace.join(","));
            trace.length = 0;
            const choice = choose();
            console.log(choice[Object.getOwnPropertySymbols(choice)[1]]);
            console.log(trace.join(","));
            console.log(original !== changed);
            console.log(main());
            """;

        ProcessResult firstRun = await RunNodeAsync(script);
        ProcessResult secondRun = await RunNodeAsync(script);
        Assert.Equal("source,second,first\n42\nreceiver\n42\nselected\ntrue\n42\n", firstRun.StdOut);
        Assert.Equal(string.Empty, firstRun.StdErr);
        Assert.Equal(firstRun, secondRun);
    }

    [Fact]
    public async Task Node_Proves_Propagation_Unwrap_And_Selected_Handler_Operands_Run_Exactly_Once()
    {
        JavaScriptCompilation emitted = Emit("""
            function good(): number ! string { return ok(4); }
            function bad(): number ! string { return err("bad"); }
            function recover(): number { return 5; }
            function success(): number {
              return try { good()? } except (error) { recover() };
            }
            function handled(): number {
              return try { bad()? } except (error) { recover() };
            }
            function unwrap(): number { return good()!; }
            """);

        string script = emitted.SourceText + """
            let goodCalls = 0;
            let badCalls = 0;
            let recoverCalls = 0;
            const originalGood = good;
            const originalBad = bad;
            const originalRecover = recover;
            good = () => { goodCalls += 1; return originalGood(); };
            bad = () => { badCalls += 1; return originalBad(); };
            recover = () => { recoverCalls += 1; return originalRecover(); };
            console.log(success());
            console.log(handled());
            console.log(unwrap());
            console.log(`${goodCalls},${badCalls},${recoverCalls}`);
            """;

        ProcessResult first = await RunNodeAsync(script);
        ProcessResult second = await RunNodeAsync(script);

        Assert.Equal("4\n5\n4\n2,1,1\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Node_Executes_Typed_Try_Except_And_Nested_Bubbling_Repeatedly()
    {
        JavaScriptCompilation emitted = Emit("""
            function good(): number ! string { return ok(4); }
            function bad(): number ! string { return err("bad"); }
            function badText(): string ! string { return err("bad"); }

            function success(): number {
              return try {
                const value: number = good()?;
                value + 1
              } except (error) {
                0
              };
            }

            function handled(): string {
              return try {
                badText()?
              } except (error) {
                error
              };
            }

            function nested(): number {
              return try {
                try {
                  bad()?
                } except (inner) {
                  bad()?
                }
              } except (outer) {
                7
              };
            }

            function toFunction(): number ! string {
              return try {
                good()?;
                bad()?
              } except (error) {
                bad()?
              };
            }

            function inspect(value: number ! string): string {
              return match value {
                ok(numberValue) => "ok",
                err(error) => error,
              };
            }
            """);

        string script = emitted.SourceText + "console.log(success());\nconsole.log(handled());\nconsole.log(nested());\nconsole.log(inspect(toFunction()));\n";
        ProcessResult first = await RunNodeAsync(script);
        ProcessResult second = await RunNodeAsync(script);

        Assert.Equal("5\nbad\n7\nbad\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Node_Executes_Main_Returning_42_Repeatedly()
    {
        const string source = """
            function add(left: number, right: number): number {
              return left + right;
            }

            function main(): number {
              const answer: number = add(40, 2);
              return if true {
                answer
              } else {
                0
              };
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(emitted.Success);

        string executableScript = emitted.SourceText + "console.log(main());\n";
        ProcessResult first = await RunNodeAsync(executableScript);
        ProcessResult second = await RunNodeAsync(executableScript);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal("42\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Node_Executes_Primitive_Equality_Edge_Cases_Repeatedly()
    {
        const string source = """
            function booleanEqual(): boolean { return true == true; }
            function booleanNotEqual(): boolean { return true != false; }
            function booleanFalse(): boolean { return false == true; }
            function numberEqual(): boolean { return 42 == 42; }
            function numberNotEqual(): boolean { return 42 != 41; }
            function nanEqual(): boolean {
              const nan: number = 0 / 0;
              return nan == nan;
            }
            function nanNotEqual(): boolean {
              const nan: number = 0 / 0;
              return nan != nan;
            }
            function signedZeroEqual(): boolean {
              const positiveZero: number = 0;
              const negativeZero: number = 0 * (0 - 1);
              return positiveZero == negativeZero;
            }
            function signedZeroNotEqual(): boolean {
              const positiveZero: number = 0;
              const negativeZero: number = 0 * (0 - 1);
              return positiveZero != negativeZero;
            }
            function stringEqual(): boolean { return "same" == "same"; }
            function stringNotEqual(): boolean { return "same" != "different"; }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(emitted.Success);

        string executableScript = emitted.SourceText + """
            console.log(booleanEqual());
            console.log(booleanNotEqual());
            console.log(booleanFalse());
            console.log(numberEqual());
            console.log(numberNotEqual());
            console.log(nanEqual());
            console.log(nanNotEqual());
            console.log(signedZeroEqual());
            console.log(signedZeroNotEqual());
            console.log(stringEqual());
            console.log(stringNotEqual());
            """;

        ProcessResult first = await RunNodeAsync(executableScript);
        ProcessResult second = await RunNodeAsync(executableScript);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal("true\ntrue\nfalse\ntrue\ntrue\nfalse\ntrue\ntrue\nfalse\ntrue\ntrue\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Node_Executes_Payload_Enum_Match_Repeatedly()
    {
        const string source = """
            enum Inner {
              None,
              Number(value: number),
            }

            enum Outer {
              Empty,
              Pair(first: number, second: string),
              Nested(value: Inner),
            }

            function main(): string {
              const outer: Outer = Outer.Nested(Inner.Number(9));
              return match outer {
                Empty => "empty",
                Pair(first, second) => second,
                Nested(inner) => match inner {
                  None => "none",
                  Number(value) => "nested",
                },
              };
            }
            """;

        JavaScriptCompilation emitted = Emit(source);
        Assert.True(emitted.Success);

        string script = emitted.SourceText + "console.log(main());\n";
        ProcessResult first = await RunNodeAsync(script);
        ProcessResult second = await RunNodeAsync(script);

        Assert.Equal("nested\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Node_Executes_Result_Construction_Matching_Forwarding_And_Propagation_Repeatedly()
    {
        const string source = """
            enum Box {
              Value(outcome: number ! string),
            }

            function good(): number ! string { return ok(4); }
            function bad(): number ! string { return err("bad"); }
            function forward(value: number ! string): number ! string { return value; }

            function observe(value: number ! string): number {
              return match value {
                ok(value) => value,
                err(error) => 0,
              };
            }

            function propagatedGood(): number ! string {
              const value: number = good()?;
              return value + 1;
            }

            function propagatedBad(): number ! string {
              const value: number = bad()?;
              return value + 1;
            }

            function stored(): number ! string {
              const value: number ! string = good();
              const numberValue: number = value?;
              return numberValue + 2;
            }

            function boxed(): Box { return Box.Value(good()); }

            function inspectBox(value: Box): number {
              return match value {
                Value(outcome) => observe(outcome),
              };
            }

            function nested(): (number ! string) ! string { return ok(ok(7)); }

            function inspectNested(): number {
              return match nested() {
                ok(inner) => observe(inner),
                err(error) => 0,
              };
            }

            function saved(): void ! string { return; }

            function inspectSaved(): number {
              return match saved() {
                ok(value) => 1,
                err(error) => 0,
              };
            }
            """;

        JavaScriptCompilation emitted = Emit(source);
        string script = emitted.SourceText + """
            console.log(observe(good()));
            console.log(observe(bad()));
            console.log(observe(forward(bad())));
            console.log(observe(propagatedGood()));
            console.log(observe(propagatedBad()));
            console.log(observe(stored()));
            console.log(inspectBox(boxed()));
            console.log(inspectNested());
            console.log(inspectSaved());
            """;

        ProcessResult first = await RunNodeAsync(script);
        ProcessResult second = await RunNodeAsync(script);

        Assert.Equal("4\n0\n0\n5\n0\n6\n4\n7\n1\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Result_Match_Panics_Deterministically_For_Malformed_Private_Values()
    {
        JavaScriptCompilation emitted = Emit("""
            function good(): number ! string { return ok(1); }
            function other(): string ! string { return ok("other"); }
            function inspect(value: number ! string): number {
              return match value {
                ok(payload) => payload,
                err(error) => 0,
              };
            }
            """);

        string script = emitted.SourceText + """
            const valid = good();
            const malformedTag = Object.freeze(Object.assign(Object.create(null), {
              $type: valid.$type, $tag: "unknown", $payload: Object.freeze([1]),
            }));
            const malformedPayload = Object.freeze(Object.assign(Object.create(null), {
              $type: valid.$type, $tag: "ok", $payload: Object.freeze(["wrong"]),
            }));
            for (const value of [other(), malformedTag, malformedPayload]) {
              try { inspect(value); } catch (error) { console.log(error.message); }
            }
            """;

        ProcessResult first = await RunNodeAsync(script);
        ProcessResult second = await RunNodeAsync(script);

        Assert.Equal("Copeland JavaScript backend invariant failure.\nCopeland JavaScript backend invariant failure.\nCopeland JavaScript backend invariant failure.\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Match_Scrutinee_Is_Emitted_Once_And_Invalid_Tag_Panics_Deterministically()
    {
        const string source = """
            enum Choice {
              A,
              B(value: number),
            }

            enum Other {
              A,
            }

            function make(): Choice {
              return Choice.A;
            }

            function inspect(choice: Choice): number {
              return match choice {
                A => 1,
                B(value) => value,
              };
            }

            function other(): Other {
              return Other.A;
            }

            function main(): number {
              return match make() {
                A => 1,
                B(value) => value,
              };
            }
            """;

        JavaScriptCompilation emitted = Emit(source);
        Assert.True(emitted.Success);
        Assert.Single(Regex.Matches(emitted.SourceText!, @"const __cope_m3_match_\d+ = make\(\);").Cast<Match>());
        Assert.Contains("default: return __cope_m3_panic_", emitted.SourceText, StringComparison.Ordinal);

        string script = emitted.SourceText + """
            console.log(main());
            try {
              inspect(other());
            } catch (error) {
              console.log(error.message);
            }
            const valid = make();
            const invalid = Object.freeze(Object.assign(Object.create(null), {
              $type: valid.$type,
              $tag: "Unknown",
              $payload: Object.freeze([]),
            }));
            try {
              inspect(invalid);
            } catch (error) {
              console.log(error.message);
            }
            const malformed = Object.freeze(Object.assign(Object.create(null), {
              $type: valid.$type,
              $tag: "B",
              $payload: Object.freeze([]),
            }));
            try {
              inspect(malformed);
            } catch (error) {
              console.log(error.message);
            }
            """;
        ProcessResult first = await RunNodeAsync(script);
        ProcessResult second = await RunNodeAsync(script);

        Assert.Equal("1\nCopeland JavaScript backend invariant failure.\nCopeland JavaScript backend invariant failure.\nCopeland JavaScript backend invariant failure.\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    private static JavaScriptCompilation Emit(string source)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return emitted;
    }

    private static async Task<ProcessResult> RunNodeAsync(string script)
    {
        string directory = Path.Combine(Path.GetTempPath(), "copeland-javascript-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = Path.Combine(directory, "program.js");
        try
        {
            await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var startInfo = new ProcessStartInfo
            {
                FileName = "node",
                WorkingDirectory = directory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(scriptPath);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start Node.js for JavaScript backend execution.");
            process.StandardInput.Close();
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                KillProcessTree(process);
                await process.WaitForExitAsync();
                throw new TimeoutException(BuildFailureMessage("Node.js timed out", process.ExitCode, await stdoutTask, await stderrTask));
            }

            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                throw new Xunit.Sdk.XunitException(BuildFailureMessage("Node.js failed", process.ExitCode, stdout, stderr));
            }

            return new ProcessResult(process.ExitCode, stdout, stderr);
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Temporary test artifacts are cleaned up best-effort.
            }
            catch (UnauthorizedAccessException)
            {
                // Temporary test artifacts are cleaned up best-effort.
            }
        }
    }

    private static string BuildFailureMessage(string heading, int exitCode, string stdout, string stderr)
    {
        var message = new StringBuilder();
        message.AppendLine(heading);
        message.AppendLine($"Exit code: {exitCode}");
        message.AppendLine("stdout:");
        message.AppendLine(stdout);
        message.AppendLine("stderr:");
        message.AppendLine(stderr);
        return message.ToString();
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
