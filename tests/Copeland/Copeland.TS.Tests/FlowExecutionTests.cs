using Copeland.TS.Compiler;
using Copeland.TS.Lowering;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Mir;
using Copeland.TS.Templates;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class FlowExecutionTests
{
    [Fact]
    public void Typed_child_frames_bind_a_closed_bounded_stack()
    {
        var compilation = MirLowerer.Lower(SyntaxTree.Parse("""
            flow Child(count: int) -> int ! int {
                board { remaining: int = count; }
                state Work initial {
                    if (board.remaining < 0) { fail board.remaining; }
                    if (board.remaining == 0) { finish count; }
                    board.remaining = board.remaining - 1;
                    yield;
                }
            }
            flow Parent(count: int) -> int ! int {
                stack 3;
                state Begin initial { push Child(count) -> Accept ! Reject; }
                state Accept(value: int) { finish value + 10; }
                state Reject(error: int) { fail error; }
            }
            function main(): int {
                let machine = Parent.start(2);
                machine = Parent.step(machine);
                machine = Parent.step(machine);
                machine = Parent.step(machine);
                machine = Parent.step(machine);
                machine = Parent.step(machine);
                return match machine.outcome {
                    case ParentOutcome.Active => 100,
                    case ParentOutcome.Completed(value) => value,
                    case ParentOutcome.Fault(code) => 200 + code,
                    case ParentOutcome.Failed(error) => error,
                };
            }
            """));
        Assert.Empty(compilation.Diagnostics);
        Assert.Equal(2, compilation.Program!.Flows.Count);
        Assert.NotNull(compilation.Program.Flows.Single(flow => flow.Name == "Parent").Execution);
    }
    public const string Countdown = """
        flow Countdown(limit: int) -> int {
            board { remaining: int = limit; }
            state Counting initial {
                if (board.remaining == 0) { finish limit; }
                board.remaining = board.remaining - 1;
                yield;
            }
        }
        function main(): int {
            let frame = Countdown.start(3);
            frame = Countdown.step(frame);
            frame = Countdown.step(frame);
            frame = Countdown.step(frame);
            frame = Countdown.step(frame);
            return match frame.outcome {
                case CountdownOutcome.Active => 100,
                case CountdownOutcome.Yielded => 200,
                case CountdownOutcome.Completed(value) => value,
            };
        }
        """;

    [Fact]
    public void Execution_states_retain_a_flow_plan_and_use_ordinary_typed_mir()
    {
        var compilation = MirLowerer.Lower(SyntaxTree.Parse(Countdown));
        Assert.Empty(compilation.Diagnostics);
        var flow = Assert.Single(compilation.Program!.Flows);
        Assert.NotNull(flow.Execution);
        Assert.Contains(compilation.Program.Functions, function => function.Name == flow.Execution.StartFunction);
        Assert.Contains(compilation.Program.Functions, function => function.Name == flow.Execution.StepFunction);
        var state = Assert.Single(flow.Execution.States);
        Assert.Equal("Counting", state.Name);
        Assert.Contains(state.Controls, control => control.Kind == "yield");
        var changed = MirLowerer.Lower(SyntaxTree.Parse(Countdown.Replace("remaining - 1", "remaining - 2", StringComparison.Ordinal)));
        Assert.Empty(changed.Diagnostics);
        Assert.NotEqual(MirFlowSemanticHash.Compute(flow), MirFlowSemanticHash.Compute(Assert.Single(changed.Program!.Flows)));
    }

    [Theory]
    [InlineData("state A initial { transition Missing; }", "COPE-FLOW-0015")]
    [InlineData("state A initial { let x: int = 0; }", "COPE-FLOW-EXEC-0003")]
    [InlineData("state A initial { yield 3; }", "COPE-FLOW-EXEC-0004")]
    [InlineData("state A initial { yield break; }", "COPE-FLOW-EXEC-0004")]
    [InlineData("state A initial { yield return 3; }", "COPE-FLOW-EXEC-0004")]
    [InlineData("state A initial { return 3; }", "COPE-FLOW-EXEC-0005")]
    [InlineData("state A initial { run Invalid(limit) -> Accept; }", "COPE-FLOW-EXEC-0017")]
    [InlineData("state A initial { if (true) { yield; let x: int = 2; } yield; }", "COPE-FLOW-EXEC-0006")]
    public void Invalid_execution_paths_fail_before_emission(string state, string diagnostic)
    {
        var compilation = MirLowerer.Lower(SyntaxTree.Parse("flow Invalid(limit: int) -> int { " + state + " }"));
        Assert.Contains(compilation.Diagnostics, item => item.Id == diagnostic);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("match")]
    [InlineData("switch")]
    public void Optional_case_prefix_keeps_payload_and_exhaustiveness_semantics(string spelling)
    {
        string source = $$"""
            enum Value { Empty, Item(value: int) }
            function read(value: Value): int {
                return {{spelling}} (value) {
                    case Empty => 0,
                    case Item(item) => item,
                };
            }
            function main(): int { return read(Value.Item(7)); }
            """;
        var withCase = CopelandCompiler.CompileToMir(source);
        var withoutCase = CopelandCompiler.CompileToMir(source.Replace("case ", "", StringComparison.Ordinal));
        Assert.True(withCase.Success, string.Join("; ", withCase.Diagnostics));
        Assert.True(withoutCase.Success, string.Join("; ", withoutCase.Diagnostics));
        Assert.Equal(CSharpBackend.Emit(withoutCase.MirCompilation!.Program!).SourceText,
            CSharpBackend.Emit(withCase.MirCompilation!.Program!).SourceText);
        Assert.Equal(JavaScriptBackend.Emit(withoutCase.MirCompilation.Program!).SourceText,
            JavaScriptBackend.Emit(withCase.MirCompilation.Program!).SourceText);
    }

    [Fact]
    public void Case_remains_a_legal_existing_variant_name()
    {
        var compilation = CopelandCompiler.CompileToMir("""
            enum Variant { case(value: int), Empty }
            function main(): int {
                return match Variant.case(7) {
                    case(value) => value,
                    case Empty => 0,
                };
            }
            """);
        Assert.True(compilation.Success, string.Join("; ", compilation.Diagnostics));
    }

    [Fact]
    public void Case_prefixes_preserve_static_template_evaluation()
    {
        const string source = """
            template<> Demo: ProjectTree {
                static match true {
                    case true => { emit(textFile("selected.txt", "yes")); }
                    case false => { emit(textFile("not-selected.txt", "no")); }
                }
            }
            """;
        var prefixed = TemplateCompiler.Evaluate(source, "Demo");
        var original = TemplateCompiler.Evaluate(source.Replace("case ", "", StringComparison.Ordinal), "Demo");
        Assert.True(prefixed.Success, string.Join("; ", prefixed.Diagnostics));
        Assert.True(original.Success, string.Join("; ", original.Diagnostics));
        Assert.Equal(original.Project!.Files.Select(file => (file.Path, file.Sha256)),
            prefixed.Project!.Files.Select(file => (file.Path, file.Sha256)));
    }

    [Theory]
    [InlineData("stack 1.5;", "push Child(1) -> Accept;", "state Accept(value: int) { finish value; }", "COPE-FLOW-EXEC-0009")]
    [InlineData("stack 2;", "push Missing(1) -> Accept;", "state Accept(value: int) { finish value; }", "COPE-FLOW-EXEC-0011")]
    [InlineData("stack 2;", "push Child(1) -> Missing;", "state Accept(value: int) { finish value; }", "COPE-FLOW-EXEC-0013")]
    [InlineData("stack 2;", "push Child(1) -> Accept;", "state Accept { finish 0; }", "COPE-FLOW-EXEC-0013")]
    public void Invalid_child_contracts_are_diagnosed(string capacity, string push, string continuation, string diagnostic)
    {
        string source = "flow Child(input: int) -> int { state Work initial { finish input; } }"
            + "flow Parent(input: int) -> int { " + capacity
            + "state Begin initial { " + push + " } " + continuation + " }";
        var compilation = CopelandCompiler.CompileToMir(source);
        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, item => item.Id == diagnostic);
    }
}
