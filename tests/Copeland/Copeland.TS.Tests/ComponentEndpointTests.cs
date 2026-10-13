using Copeland.TS.Compiler;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class ComponentEndpointTests
{
    [Fact]
    public void Shorthand_and_typed_endpoint_reuse_component_state_facts()
    {
        CopelandProjectCompilation project = Compile("""
            record Model { count: int; }
            function Present(model: Model, add: (value: int) => void): ReactNode {
                return <button onClick={capture { add } () => add(2)}>{model.count}</button>;
            }
            export function Counter(): ReactNode {
                state: Model = { count: 0 };
                on Add(value: int) => state with { count: state.count + value };
                return Present(state, on.Add);
            }
            """);
        Assert.True(project.Success, string.Join("\n", project.Diagnostics.Select(diagnostic => diagnostic.Message)));
        BoundComponentStateModel state = Assert.Single(project.Modules).BoundCompilation!.Program.ComponentDefinitions
            .Single(component => component.Function.Name == "Counter").State!;
        Assert.Equal("state", state.State.Name);
        Assert.Equal("Add", Assert.Single(state.Transitions).Name);
        Assert.NotNull(project.MirProjectGraph!.AggregateProgram.Functions.Single(function => function.Name == "Counter").ComponentState);
    }

    [Fact]
    public void Execution_calls_in_state_handler_and_effect_arguments_are_rewritten()
    {
        CopelandProjectCompilation project = Compile("""
            flow CounterFlow(limit: int) -> int {
                board { count: int = 0; }
                state Counting initial {
                    if (board.count == limit) { finish board.count; }
                    board.count = board.count + 1;
                    yield;
                }
            }
            function Observe(frame: CounterFlowFrame): void { }
            function Counter(): ReactNode {
                state frame: CounterFlowFrame = CounterFlow.start(2);
                on Done(next: CounterFlowFrame) => next;
                on Tick() => CounterFlow.step(frame)
                    effect Observe(CounterFlow.step(frame)) => Done(CounterFlow.step(frame));
                return <span>{frame.board.count}</span>;
            }
            """);
        Assert.True(project.Success, string.Join("\n", project.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    [Theory]
    [InlineData("return <button onClick={on.Missing}>x</button>;", "COPE-COMPONENT-ON-0001")]
    [InlineData("return <button onClick={() => on.Add(1)}>x</button>;", "COPE-COMPONENT-ON-0002")]
    [InlineData("return <button onClick={capture { add: on.Add } () => add(\"wrong\")}>x</button>;", "COPE-CALL-0006")]
    public void Endpoints_check_ownership_capture_and_payload(string presentation, string diagnostic)
    {
        CopelandProjectCompilation project = Compile("""
            function Counter(): ReactNode {
                state: int = 0;
                on Add(value: int) => state + value;
            """ + presentation + "}");
        Assert.Contains(project.Diagnostics, result => result.Id == diagnostic);
    }

    [Fact]
    public void Aliased_mutation_in_a_helper_is_rejected_with_a_repair()
    {
        CopelandProjectCompilation project = Compile("""
            function Next(storage: MutableArray<int>): int {
                storage[0] = storage[0] + 1;
                return storage[0];
            }
            function Counter(storage: MutableArray<int>): ReactNode {
                state: int = 0;
                on Add() => Next(storage);
                return <button onClick={on.Add}>{state}</button>;
            }
            """);
        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-ON-0007"
            && diagnostic.Message.Contains("pure helpers", StringComparison.Ordinal));
    }

    [Fact]
    public void Local_builder_and_known_pure_callback_are_qualified_at_the_call_site()
    {
        CopelandProjectCompilation project = Compile("""
            function Map(items: int[], project: (item: int) => int): int[] {
                const result: MutableArray<int> = MutableArray<int>(items.length);
                for (let index: int = 0; index < items.length; index = index + 1) {
                    result[index] = project(items[index]);
                }
                return result.freeze();
            }
            function AddOne(value: int): int { return value + 1; }
            function Counter(): ReactNode {
                state: int[] = [0];
                on Add() => Map(state, AddOne);
                return <button onClick={on.Add}>{state[0]}</button>;
            }
            """);
        Assert.True(project.Success, string.Join("\n", project.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    [Fact]
    public void Render_local_values_must_be_sent_as_payload()
    {
        CopelandProjectCompilation project = Compile("""
            function Counter(amount: int): ReactNode {
                state: int = 0;
                on Add() => state + amount;
                return <button onClick={on.Add}>{state}</button>;
            }
            """);
        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-ON-0007"
            && diagnostic.Message.Contains("event payload", StringComparison.Ordinal));
    }

    [Fact]
    public void Attachment_settlement_requires_a_real_acknowledgement_contract()
    {
        CopelandProjectCompilation project = Compile("""
            function Persist(): void { }
            function Counter(): ReactNode {
                state: int = 0;
                on Add() => state + 1 after AttachmentsSettled effect Persist();
                return <button onClick={on.Add}>{state}</button>;
            }
            """);
        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-ON-0005");
    }

    [Fact]
    public void Effects_capture_arguments_and_bind_typed_async_results()
    {
        CopelandProjectCompilation project = Compile("""
            async function Fetch(value: int): int { return value; }
            function Counter(): ReactNode {
                state: int = 0;
                on Loaded(request: int, value: int) => request + value;
                on Load(value: int) => state + 1 effect Fetch(value) => Loaded(state, result);
                return <button onClick={capture { load: on.Load } () => load(2)}>{state}</button>;
            }
            """);
        Assert.True(project.Success, string.Join("\n", project.Diagnostics.Select(diagnostic => diagnostic.Message)));
        MirComponentStateModel model = project.MirProjectGraph!.AggregateProgram.Functions
            .Single(function => function.Name == "Counter").ComponentState!;
        MirComponentEffect effect = Assert.Single(model.Endpoints.Single(endpoint => endpoint.Name == "Load").Effects);
        Assert.Equal("StateCommitted", effect.Phase);
        Assert.Equal("Loaded", effect.CompletionEvent);
        Assert.IsType<MirAsyncType>(effect.ReturnType);
        Assert.Empty(MirValidator.Validate(project.MirProjectGraph.AggregateProgram));
    }

    [Fact]
    public void Named_state_endpoint_owners_use_state_commit_effects_and_typed_results()
    {
        CopelandProjectCompilation project = Compile("""
            function Next(): int { return 2; }
            function Counter(): ReactNode {
                state current: int = 0;
                on Done(value: int) => value;
                on Apply() => current + 1 effect Next() => Done(result);
                return <button onClick={on.Apply}>{current}</button>;
            }
            """);
        Assert.True(project.Success, string.Join("\n", project.Diagnostics.Select(diagnostic => diagnostic.Message)));
        MirComponentStateModel model = project.MirProjectGraph!.AggregateProgram.Functions
            .Single(function => function.Name == "Counter").ComponentState!;
        Assert.Equal("StateCommitted", Assert.Single(model.Endpoints.Single(endpoint => endpoint.Name == "Apply").Effects).Phase);
        Assert.Contains("complete Done result int", MirTextWriter.Write(project.MirProjectGraph.AggregateProgram));
    }

    [Fact]
    public void Effect_captures_cannot_run_external_work_before_commit()
    {
        CopelandProjectCompilation project = Compile("""
            import { getViewportWidth } from "@copeland/browser-v1";
            function Fetch(value: int): int { return value; }
            function Counter(): ReactNode {
                state: int = 0;
                on Loaded(value: int) => value;
                on Load() => state effect Fetch(getViewportWidth()) => Loaded(result);
                return <button onClick={on.Load}>{state}</button>;
            }
            """);
        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-EFFECT-0107");
    }

    [Fact]
    public void State_only_shorthand_still_has_a_lifetime_owner()
    {
        CopelandProjectCompilation project = Compile("""
            export function Counter(seed: int): ReactNode {
                state: int = seed;
                return <span>{state}</span>;
            }
            """);
        Assert.True(project.Success);
        MirComponentStateModel model = project.MirProjectGraph!.AggregateProgram.Functions
            .Single(function => function.Name == "Counter").ComponentState!;
        Assert.NotNull(model);
        Assert.Empty(model.Endpoints);
    }

    [Fact]
    public void State_cannot_retain_mutable_storage_inside_an_immutable_record()
    {
        CopelandProjectCompilation project = Compile("""
            record Model { storage: MutableArray<int>; }
            function Counter(storage: MutableArray<int>): ReactNode {
                state: Model = { storage };
                return <span>{state.storage[0]}</span>;
            }
            """);
        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-ON-0009");
    }

    [Fact]
    public void Presentation_cannot_invoke_a_deferred_endpoint_through_a_helper()
    {
        CopelandProjectCompilation project = Compile("""
            function Dispatch(send: () => void): ReactNode { send(); return <span>x</span>; }
            function Counter(): ReactNode {
                state: int = 0;
                on Add() => state + 1;
                return Dispatch(on.Add);
            }
            """);
        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-ON-0008");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Record_held_callbacks_preserve_effect_provenance(bool mutateInput)
    {
        string helper = mutateInput
            ? "function Next(values: MutableArray<int>): int { values[0] = 1; return values[0]; }"
            : "function Next(values: MutableArray<int>): int { return values[0] + 1; }";
        CopelandProjectCompilation project = Compile("""
            record Operation { apply: (values: MutableArray<int>) => int; }
            function Run(operation: Operation, values: MutableArray<int>): int {
                const apply: (values: MutableArray<int>) => int = operation.apply;
                return apply(values);
            }
            """ + helper + """
            function Counter(values: MutableArray<int>): ReactNode {
                state: int = 0;
                on Add(input: MutableArray<int>) => Run({ apply: Next }, input);
                return <button onClick={capture { send: on.Add, values } () => send(values)}>{state}</button>;
            }
            """);
        if (mutateInput)
        {
            Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-ON-0007");
        }
        else
        {
            Assert.True(project.Success, string.Join("\n", project.Diagnostics.Select(diagnostic => diagnostic.Message)));
        }
    }

    [Fact]
    public void Generic_fresh_builder_does_not_authorize_an_input_with_the_same_name()
    {
        CopelandProjectCompilation project = Compile("""
            function Next<T>(result: MutableArray<int>, item: T): int {
                { const result: MutableArray<int> = MutableArray<int>(1); result[0] = 2; }
                result[0] = 1;
                return result[0];
            }
            function Counter(): ReactNode {
                state: int = 0;
                on Add(input: MutableArray<int>) => Next(input, 1);
                return <span>{state}</span>;
            }
            """);
        Assert.Contains(project.Diagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-ON-0007");
    }

    [Fact]
    public void Single_source_lowering_does_not_bypass_endpoint_validation()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            import { createElement } from "react";
            function Counter(amount: int): ReactNode {
                state: int = 0;
                on Add() => state + amount;
                return <button onClick={on.Add}>{state}</button>;
            }
            """, new CopelandCompilationOptions
            {
                SourcePath = "Main.tsx",
                TsXmlProfile = CopelandTsXmlProfile.ReactM0,
                JavaScriptHostModules = [CopelandProjectHostContracts.Browser()],
                NpmPackages = [new CopelandNpmPackageContract("react", "19.2.7",
                    [new CopelandNpmFunctionContract("createElement", [], "ReactNode")])],
            });
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-COMPONENT-ON-0007");
    }

    [Fact]
    public void Mir_validation_checks_component_metadata_before_emission()
    {
        CopelandProjectCompilation project = Compile("""
            function Counter(): ReactNode {
                state: int = 0;
                on Add() => state + 1;
                return <button onClick={on.Add}>{state}</button>;
            }
            """);
        MirProgram program = project.MirProjectGraph!.AggregateProgram;
        MirFunction owner = program.Functions.Single(function => function.Name == "Counter");
        var forged = new MirFunction(owner.Name, owner.Parameters, owner.ReturnType, owner.Locals, owner.Body)
        {
            ComponentState = owner.ComponentState! with { InitializerFunction = "absent" },
        };
        var invalid = new MirProgram(program.Enums, program.Records, program.Tables, program.TsonEncodingPlans,
            program.NpmImports, program.Functions.Select(function => function == owner ? forged : function).ToArray());
        Assert.Contains(MirValidator.Validate(invalid), diagnostic => diagnostic.Message.Contains("absent initializer", StringComparison.Ordinal));
    }

    private static CopelandProjectCompilation Compile(string source)
        => CopelandProjectCompiler.CompileToMir(
            [new CopelandProjectSource("Main.tsx", "Main.tsx", "import { createElement } from \"react\";\n" + source)],
            new CopelandCompilationOptions
            {
                TsXmlProfile = CopelandTsXmlProfile.ReactM0,
                JavaScriptHostModules = [CopelandProjectHostContracts.Browser()],
                NpmPackages = [new CopelandNpmPackageContract("react", "19.2.7",
                    [new CopelandNpmFunctionContract("createElement", [], "ReactNode")])],
            });
}
