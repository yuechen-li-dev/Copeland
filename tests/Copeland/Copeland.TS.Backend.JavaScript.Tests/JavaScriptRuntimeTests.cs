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
    public async Task Node_propagates_a_result_after_await_without_host_rejection()
    {
        JavaScriptCompilation emitted = Emit("""
            async function parse(value: number): number ! string { return value + 1; }
            async function load(value: number): number ! string {
                const pending: Async<number ! string> = parse(value);
                const parsed: number = await pending?;
                return parsed + 1;
            }
            """);

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        ProcessResult result = await RunNodeAsync(emitted.SourceText + "const result = load(40).value; console.log(result.$tag); console.log(result.$payload[0]);\n");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ok\n42\n", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task Node_propagates_a_result_error_after_await_without_host_rejection()
    {
        JavaScriptCompilation emitted = Emit("""
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

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        ProcessResult result = await RunNodeAsync(emitted.SourceText + "const result = load(-1).value; console.log(result.$tag); console.log(result.$payload[0]);\n");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("err\nnegative\n", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task Node_propagates_an_awaited_result_inside_a_return_expression()
    {
        const string source = """
            async function parse(value: number): number ! string {
                if (value < 0) { return err("negative"); }
                return value + 1;
            }
            async function load(value: number): number ! string {
                return (await parse(value)?) + 1;
            }
            """;
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation diagnostic = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation symbolic = JavaScriptBackend.Emit(compilation.MirCompilation.Program!, new JavaScriptEmissionOptions { Profile = JavaScriptEmissionProfile.Symbolic });

        Assert.True(diagnostic.Success, string.Join(Environment.NewLine, diagnostic.Diagnostics));
        Assert.True(symbolic.Success, string.Join(Environment.NewLine, symbolic.Diagnostics));
        const string suffix = "const ok = load(40).value; const err = load(-1).value; console.log(ok.$tag); console.log(ok.$payload[0]); console.log(err.$tag); console.log(err.$payload[0]);\n";
        ProcessResult result = await RunNodeAsync(diagnostic.SourceText + suffix);
        ProcessResult symbolicResult = await RunNodeAsync(symbolic.SourceText + suffix);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ok\n42\nerr\nnegative\n", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
        Assert.Equal(result, symbolicResult);
    }

    [Fact]
    public async Task Node_recovers_a_typed_result_through_an_async_try_except_handler()
    {
        const string source = """
            async function parse(value: number): number ! string { return value + 1; }
            function failed(): number ! string { return err("negative"); }
            async function load(value: number): number {
                return try {
                    const parsed: number = await parse(value)?;
                    parsed + 1
                } except (error) {
                    0
                };
            }
            """;
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation diagnostic = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation symbolic = JavaScriptBackend.Emit(
            compilation.MirCompilation.Program!,
            new JavaScriptEmissionOptions { Profile = JavaScriptEmissionProfile.Symbolic });

        Assert.True(diagnostic.Success, string.Join(Environment.NewLine, diagnostic.Diagnostics));
        Assert.True(symbolic.Success, string.Join(Environment.NewLine, symbolic.Diagnostics));
        const string suffix = """
            const delayed = __cope_async_pending();
            parse = () => delayed;
            const suspended = load(40);
            console.log(suspended.completed);
            delayed.resolve(failed());
            console.log(suspended.completed);
            console.log(suspended.value);
            """;
        ProcessResult result = await RunNodeAsync(diagnostic.SourceText + suffix);
        ProcessResult symbolicResult = await RunNodeAsync(symbolic.SourceText + suffix);

        Assert.Equal("false\ntrue\n0\n", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
        Assert.Equal(result, symbolicResult);
    }

    [Fact]
    public async Task Node_executes_nested_await_expressions_and_short_circuiting()
    {
        const string source = """
            async function read(value: number): number { return value + 1; }
            async function truth(): boolean { return true; }
            async function falsehood(): boolean { return false; }
            async function combine(value: number): number {
                return (await read(value)) + (await read(1));
            }
            async function shortCircuit(flag: boolean): boolean {
                return flag && await truth();
            }
            async function fallback(flag: boolean): boolean {
                return flag || await falsehood();
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation diagnostic = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation symbolic = JavaScriptBackend.Emit(compilation.MirCompilation.Program!, new JavaScriptEmissionOptions { Profile = JavaScriptEmissionProfile.Symbolic });

        Assert.True(diagnostic.Success, string.Join(Environment.NewLine, diagnostic.Diagnostics));
        Assert.True(symbolic.Success, string.Join(Environment.NewLine, symbolic.Diagnostics));
        const string suffix = "console.log(combine(40).value); console.log(shortCircuit(false).value); console.log(shortCircuit(true).value); console.log(fallback(true).value); console.log(fallback(false).value);\n";
        ProcessResult result = await RunNodeAsync(diagnostic.SourceText + suffix);
        ProcessResult symbolicResult = await RunNodeAsync(symbolic.SourceText + suffix);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("43\nfalse\ntrue\ntrue\nfalse\n", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
        Assert.Equal(result, symbolicResult);
    }

    [Fact]
    public async Task Node_reenters_an_async_loop_condition()
    {
        JavaScriptCompilation emitted = Emit("""
            async function below(value: number): boolean { return value < 3; }
            async function count(): number {
                let value: number = 0;
                while (await below(value)) {
                    value = value + 1;
                }
                return value;
            }
            """);

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        ProcessResult result = await RunNodeAsync(emitted.SourceText + "console.log(count().value);\n");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("3\n", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task Node_executes_async_for_with_continue_through_increment_state()
    {
        JavaScriptCompilation emitted = Emit("""
            async function count(): number {
                let total: number = 0;
                for (let index: number = 0; index < 5; index = index + 1) {
                    if (index == 2) { continue; }
                    total = total + index;
                }
                return total;
            }
            """);

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        ProcessResult result = await RunNodeAsync(emitted.SourceText + "console.log(count().value);\n");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("8\n", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task Node_executes_async_while_with_explicit_loop_state()
    {
        JavaScriptCompilation emitted = Emit("""
            async function count(): number {
                let value: number = 0;
                while (value < 3) { value = value + 1; }
                return value;
            }
            """);

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        ProcessResult result = await RunNodeAsync(emitted.SourceText + "console.log(count().value);\n");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("3\n", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task Node_resumes_an_await_inside_an_explicit_loop_state()
    {
        JavaScriptCompilation emitted = Emit("""
            async function next(value: number): number { return value + 1; }
            async function count(): number {
                let value: number = 0;
                while (value < 3) {
                    const pending: Async<number> = next(value);
                    value = await pending;
                }
                return value;
            }
            """);

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        ProcessResult result = await RunNodeAsync(emitted.SourceText + "console.log(count().value);\n");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("3\n", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task Node_executes_async_loop_break_and_continue_as_state_jumps()
    {
        JavaScriptCompilation emitted = Emit("""
            async function count(): number {
                let value: number = 0;
                let total: number = 0;
                while (value < 5) {
                    value = value + 1;
                    if (value == 2) { continue; }
                    if (value == 4) { break; }
                    total = total + value;
                }
                return total;
            }
            """);

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        ProcessResult result = await RunNodeAsync(emitted.SourceText + "console.log(count().value);\n");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("4\n", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task Node_executes_async_if_through_explicit_state_transition()
    {
        JavaScriptCompilation emitted = Emit("""
            async function choose(flag: boolean): number {
                if (flag) { return 1; }
                return 2;
            }
            """);

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        ProcessResult result = await RunNodeAsync(emitted.SourceText + "console.log(choose(true).value); console.log(choose(false).value);\n");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1\n2\n", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task Internal_async_pending_seam_arbitrates_terminal_outcomes_once()
    {
        JavaScriptCompilation emitted = Emit("async function value(): number { return 1; }");
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        const string suffix = """
            const pending = __cope_async_pending();
            let resumed = 0;
            let cancelled = 0;
            pending.subscribe(() => resumed += 1, () => cancelled += 1, () => { throw new Error("unexpected panic"); });
            pending.cancel();
            pending.resolve(99);
            pending.cancel();
            console.log(pending.completed);
            console.log(pending.cancelled);
            console.log(pending.panicked);
            console.log(resumed);
            console.log(cancelled);
            """;

        ProcessResult result = await RunNodeAsync(emitted.SourceText + suffix);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("true\ntrue\nfalse\n0\n1\n", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task Node_executes_explicit_async_state_machine_in_both_profiles()
    {
        const string source = """
            async function read(value: number): number { return value + 1; }
            async function load(value: number): number {
                const pending: Async<number> = read(value);
                const result: number = await pending;
                return result + 1;
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation diagnostic = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation symbolic = JavaScriptBackend.Emit(compilation.MirCompilation.Program!, new JavaScriptEmissionOptions { Profile = JavaScriptEmissionProfile.Symbolic });
        Assert.True(diagnostic.Success, string.Join(Environment.NewLine, diagnostic.Diagnostics));
        Assert.True(symbolic.Success, string.Join(Environment.NewLine, symbolic.Diagnostics));
        Assert.Contains("switch (frame.state)", diagnostic.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("async function", diagnostic.SourceText, StringComparison.Ordinal);

        const string suffix = """
            const pending = load(40);
            console.log(pending.completed);
            console.log(pending.value);
            console.log(pending.value);
            const delayed = __cope_async_pending();
            read = () => delayed;
            const suspended = load(40);
            console.log(suspended.completed);
            delayed.cancel();
            console.log(suspended.completed);
            console.log(suspended.cancelled);
            """;
        ProcessResult diagnosticResult = await RunNodeAsync(diagnostic.SourceText + suffix);
        ProcessResult symbolicResult = await RunNodeAsync(symbolic.SourceText + suffix);

        Assert.Equal(0, diagnosticResult.ExitCode);
        Assert.Equal("true\n42\n42\nfalse\ntrue\ntrue\n", diagnosticResult.StdOut);
        Assert.Equal(string.Empty, diagnosticResult.StdErr);
        Assert.Equal(diagnosticResult, symbolicResult);
    }

    [Fact]
    public async Task Node_executes_lifted_noncapturing_arrows_with_the_callable_carrier()
    {
        JavaScriptCompilation emitted = Emit("""
            type Operation = (value: number) => number;
            function main(): number {
                const double = (value: number) => value * 2;
                const increment: Operation = value => value + 1;
                return increment(double(20));
            }
            """);

        string script = emitted.SourceText + "console.log(main());\n";
        ProcessResult first = await RunNodeAsync(script);
        ProcessResult second = await RunNodeAsync(script);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal("41\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Node_executes_explicit_capture_through_a_private_immutable_environment()
    {
        JavaScriptCompilation emitted = Emit("""
            function makeAdder(base: number): (value: number) => number {
                return capture { base } (value: number) => base + value;
            }
            function main(): number { return makeAdder(20)(22); }
            """);

        Assert.Contains("__cope_callable_capture", emitted.SourceText, StringComparison.Ordinal);
        Assert.Contains("Object.create(null)", emitted.SourceText, StringComparison.Ordinal);
        string script = emitted.SourceText + "console.log(main());\n";
        ProcessResult first = await RunNodeAsync(script);
        ProcessResult second = await RunNodeAsync(script);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal("42\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Node_explicit_capture_snapshots_a_rebound_let_at_construction_time()
    {
        JavaScriptCompilation emitted = Emit("""
            type Operation = (value: number) => number;
            function make(): Operation {
                let base: number = 10;
                const add = capture { base } (value: number) => base + value;
                base = 20;
                return add;
            }
            function main(): number { return make()(5); }
            """);

        ProcessResult result = await RunNodeAsync(emitted.SourceText + "console.log(main());\n");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("15\n", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task Diagnostic_and_symbolic_profiles_share_explicit_capture_semantics()
    {
        const string source = """
            type Operation = (value: number) => number;
            function make(base: number): Operation { return capture { base } (value: number) => base + value; }
            function main(): number { return make(20)(22); }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation diagnostic = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation symbolic = JavaScriptBackend.Emit(
            compilation.MirCompilation.Program!,
            new JavaScriptEmissionOptions { Profile = JavaScriptEmissionProfile.Symbolic });

        ProcessResult first = await RunNodeAsync(diagnostic.SourceText + "console.log(main());\n");
        ProcessResult second = await RunNodeAsync(symbolic.SourceText + "console.log(main());\n");
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(first.StdOut, second.StdOut);
        Assert.Equal("42\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(string.Empty, second.StdErr);
    }

    [Fact]
    public async Task Node_callable_values_survive_array_record_and_enum_storage()
    {
        JavaScriptCompilation emitted = Emit("""
            type Operation = (value: number) => number;
            record Box { operation: Operation; }
            enum Choice { Value(operation: Operation), }
            function makeAdder(base: number): Operation { return capture { base } (value: number) => base + value; }
            function main(): number {
                const values: Operation[] = [makeAdder(1)];
                const box: Box = { operation: makeAdder(2) };
                const choice: Choice = Choice.Value(box.operation);
                return match choice { Value(operation) => operation(40), };
            }
            """);

        ProcessResult result = await RunNodeAsync(emitted.SourceText + "console.log(main());\n");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("42\n", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
    }

    [Fact]
    public async Task Node_Executes_Foundational_Control_Flow_Repeatedly()
    {
        const string source = """
            function main(): number {
                let total: number = 0;
                for (let index: number = 0; index < 5; index = index + 1) {
                    if (index == 2) { continue; }
                    total = total + index;
                }
                while (total < 8) { total = total + 1; }
                return total;
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        string script = emitted.SourceText + "console.log(main());\n";
        ProcessResult first = await RunNodeAsync(script);
        ProcessResult second = await RunNodeAsync(script);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal("8\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Node_executes_callable_reference_and_rejects_a_plain_host_function()
    {
        const string source = """
            type Operation = (value: number) => number;
            function increment(value: number): number { return value + 1; }
            function apply(operation: Operation, value: number): number { return operation(value); }
            function main(): number { const operation = increment; return apply(operation, 4); }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        string script = emitted.SourceText + """
            console.log(main());
            try {
              __cope_callable_invoke(function () { return 1; }, "(named:number)->named:number", [1]);
              console.log("counterfeit");
            } catch (error) {
              console.log("rejected");
            }
            """;
        ProcessResult first = await RunNodeAsync(script);
        ProcessResult second = await RunNodeAsync(script);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal("5\nrejected\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Node_rejects_callable_carrier_counterfeits_and_preserves_private_provenance()
    {
        const string source = """
            type Operation = (value: number) => number;
            function increment(value: number): number { return value + 1; }
            function main(): number { const operation: Operation = increment; return operation(4); }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        string script = emitted.SourceText + """
            const signature = "(named:number)->named:number";
            const carrier = __cope_callable_ref(signature, increment);
            function category(value, requestedSignature) {
              try { __cope_callable_invoke(value, requestedSignature, [1]); return "accepted"; }
              catch (error) { return "rejected"; }
            }
            const copied = Object.create(null);
            for (const symbol of Object.getOwnPropertySymbols(carrier)) { copied[symbol] = carrier[symbol]; }
            Object.freeze(copied);
            let mutation = "accepted";
            try { carrier.extra = 1; delete carrier.extra; } catch (error) { mutation = "rejected"; }
            console.log(main());
            console.log(Object.getPrototypeOf(carrier) === null);
            console.log(Object.isFrozen(carrier));
            console.log(Object.getOwnPropertyNames(carrier).length === 0 && Object.getOwnPropertySymbols(carrier).length === 0);
            console.log(category(function () { return 1; }, signature));
            console.log(category({}, signature));
            console.log(category(Object.freeze(Object.create(null)), signature));
            console.log(category(copied, signature));
            console.log(category(carrier, "()->named:number"));
            console.log(mutation);
            console.log(category(carrier, signature));
            """;

        ProcessResult first = await RunNodeAsync(script);
        ProcessResult second = await RunNodeAsync(script);

        Assert.Equal("5\ntrue\ntrue\ntrue\nrejected\nrejected\nrejected\nrejected\nrejected\nrejected\naccepted\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Node_Proves_ordinary_arrays_are_mutable_ordered_and_evaluate_selected_elements_once()
    {
        JavaScriptCompilation emitted = Emit("""
            record Entry { label: string; }
            enum State { Off, On(value: number), }
            function first(): number { return 1; }
            function second(): number { return 2; }
            function selected(): number { return 3; }
            function unselected(): number { return 99; }
            function values(): number[][] {
                return [[first(), second()], if true { [selected()] } else { [unselected()] }];
            }
            function entries(): Entry[] { return [{ label: "first" }, { label: "second" }]; }
            function states(): State[] { return [State.Off, State.On(4)]; }
            """);

        string script = emitted.SourceText + """
            const trace = [];
            function wrap(original, name) {
              return (...args) => { trace.push(name); return original(...args); };
            }
            first = wrap(first, "first");
            second = wrap(second, "second");
            selected = wrap(selected, "selected");
            unselected = wrap(unselected, "unselected");
            const rows = values();
            const entryValues = entries();
            const stateValues = states();
            const field = value => value[Object.getOwnPropertySymbols(value)[1]];
            console.log(trace.join(","));
            console.log(JSON.stringify(rows));
            console.log(!Object.isFrozen(rows) && !Object.isFrozen(rows[0]));
            console.log([field(entryValues[0]), field(entryValues[1])].join(","));
            console.log(stateValues.map(value => value.$tag).join(","));
            console.log(stateValues[1].$payload[0]);
            """;

        ProcessResult firstRun = await RunNodeAsync(script);
        ProcessResult secondRun = await RunNodeAsync(script);

        Assert.Equal("first,second,selected\n[[1,2],[3]]\ntrue\nfirst,second\nOff,On\n4\n", firstRun.StdOut);
        Assert.Equal(string.Empty, firstRun.StdErr);
        Assert.Equal(firstRun, secondRun);
    }

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
            const copiedRecord = Object.create(null);
            for (const symbol of Object.getOwnPropertySymbols(first)) { copiedRecord[symbol] = first[symbol]; }
            Object.freeze(copiedRecord);
            const box = makeBox();
            const copiedEnum = Object.create(null);
            for (const symbol of Object.getOwnPropertySymbols(box)) { copiedEnum[symbol] = box[symbol]; }
            Object.freeze(copiedEnum);
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
            console.log(category(() => readFirst(copiedRecord)));
            console.log(category(() => guarded(second)));
            console.log(category(() => readBox(first)));
            console.log(category(() => readBox(copiedEnum)));
            console.log(category(() => readResult(first)));
            try { first[symbols[1]] = 0; } catch (error) {}
            try { first.extra = 1; } catch (error) {}
            try { delete first[symbols[1]]; } catch (error) {}
            console.log(readFirst(first));
            """).Replace("__FLOW_FACTORY__", flowFactory.Groups["name"].Value, StringComparison.Ordinal);

        ProcessResult firstRun = await RunNodeAsync(script);
        ProcessResult secondRun = await RunNodeAsync(script);
        string invariant = "Copeland JavaScript backend invariant failure.\n";
        Assert.Equal("true\ntrue\ntrue\ntrue\n" + string.Concat(Enumerable.Repeat(invariant, 12)) + "42\n", firstRun.StdOut);
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
    public async Task Node_Executes_Nominal_Union_Contextual_Construction_And_Match_Repeatedly()
    {
        const string source = """
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
            """;

        JavaScriptCompilation emitted = Emit(source);
        Assert.True(emitted.Success);

        string script = emitted.SourceText + "console.log(area());\n";
        ProcessResult first = await RunNodeAsync(script);
        ProcessResult second = await RunNodeAsync(script);

        Assert.Equal("16\n", first.StdOut);
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

    [Fact]
    public async Task Node_executes_pure_classes_with_private_provenance_and_no_javascript_class_runtime()
    {
        const string source = """
            class Person {
                public name: string;
                private normalizedName: string;
                public age: number;
                constructor(name: string, age: number): Person {
                    return { name, normalizedName: Person.normalize(name), age };
                }
                private normalize(name: string): string { return name; }
                birthday(person: Person): Person { return person with { age: person.age + 1 }; }
            }
            function main(): number {
                const operation: (person: Person) => Person = Person.birthday;
                return operation(Person("Ada", 41)).age;
            }
            """;

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation diagnostic = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!);
        JavaScriptCompilation symbolic = JavaScriptBackend.Emit(
            compilation.MirCompilation.Program!,
            new JavaScriptEmissionOptions { Profile = JavaScriptEmissionProfile.Symbolic });
        Assert.DoesNotContain("class ", diagnostic.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("new Person", diagnostic.SourceText, StringComparison.Ordinal);
        Assert.Contains("new WeakSet()", diagnostic.SourceText, StringComparison.Ordinal);
        string suffix = """
            console.log(main());
            try { Person__birthday(Object.freeze(Object.create(null))); }
            catch (error) { console.log(error.message); }
            """;
        ProcessResult first = await RunNodeAsync(diagnostic.SourceText + suffix);
        ProcessResult second = await RunNodeAsync(symbolic.SourceText + suffix);

        Assert.Equal("42\nCopeland JavaScript backend invariant failure.\n", first.StdOut);
        Assert.Equal(string.Empty, first.StdErr);
        Assert.Equal(first.StdOut, second.StdOut);
        Assert.Equal(string.Empty, second.StdErr);
    }

    [Fact]
    public async Task Node_resumes_a_delayed_tson_transport_response_once()
    {
        JavaScriptCompilation emitted = Emit("""
            const $schema: string = "copeland://transport/test";
            record Request { value: number; }
            record Response { value: number; label: string; }
            record RemoteError { message: string; }
            function makeRequest(value: number): Request { return { value }; }
            function makeResponse(): Response { return { value: 42, label: "ok" }; }
            function readResponse(response: Response): number { return response.value; }
            async function load(value: number): Response ! RemoteError {
                const request: Request = makeRequest(value);
                const pending: Async<Response ! RemoteError> = tsonCall<Response, RemoteError>("double", request);
                return await pending;
            }
            """);

        Match tsonRuntime = Regex.Match(emitted.SourceText!, @"const (?<name>__cope_[A-Za-z0-9_]+) = \(\(\) => \{\r?\n\s+function makeWriter");
        Assert.True(tsonRuntime.Success);
        string script = emitted.SourceText + """
            let request = "";
            __cope_tson_transport.setDispatch(value => { request = value; });
            const pending = load(21);
            console.log(pending.completed);
            const payload = __TSON_RUNTIME__["tson1"](makeResponse()).$payload[0];
            console.log(readResponse(__TSON_RUNTIME__["tson1"].decode(payload)));
            console.log(__cope_tson_transport.receive(__cope_tson_transport.envelope("1", "ok", "", payload)));
            console.log(__cope_tson_transport.receive(__cope_tson_transport.envelope("1", "ok", "", payload)));
            console.log(pending.completed);
            console.log(readResponse(pending.value.$payload[0]));
            console.log(request.includes("copeland://interop/transport/v1"));
            """.Replace("__TSON_RUNTIME__", tsonRuntime.Groups["name"].Value, StringComparison.Ordinal);
        ProcessResult result = await RunNodeAsync(script);

        Assert.Equal("false\n42\ntrue\nfalse\ntrue\n42\ntrue\n", result.StdOut);
        Assert.Equal(string.Empty, result.StdErr);
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
