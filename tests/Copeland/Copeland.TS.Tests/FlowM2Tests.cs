using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class FlowM2Tests
{
    [Fact]
    public void Flow_updates_accept_direct_nested_multiple_argument_record_generic_and_associated_pure_calls()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            record Position { x: int; y: int; }

            class Counter {
                value: int;
                constructor(value: int): Counter { return { value }; }
                next(value: int): int { return value + 1; }
            }

            function increment(value: int): int { return value + 1; }
            function nextSequence(value: int): int { return increment(value); }
            function clamp(value: int, minimum: int, maximum: int): int {
                if (value < minimum) { return minimum; }
                if (value > maximum) { return maximum; }
                return value;
            }
            function moveX(position: Position, delta: int): Position {
                return position with { x: position.x + delta };
            }
            function identity<T>(value: T): T { return value; }

            flow Example {
                board {
                    sequence: int = 0;
                    position: Position = { x: 1, y: 2 };
                }
                event Advance(delta: int);
                state Ready initial {
                    on Advance(delta) -> Ready {
                        board.sequence = identity<int>(Counter.next(clamp(nextSequence(board.sequence), 0, 100)));
                        board.position = moveX(board.position, delta);
                    };
                }
            }
            """);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        MirFlowTransition transition = Assert.Single(Assert.Single(compilation.MirCompilation!.Program!.Flows).States[0].Transitions);
        Assert.Equal(2, transition.Updates.Count);
        Assert.IsType<MirCallExpression>(transition.Updates[0].Value);
        Assert.IsType<MirCallExpression>(transition.Updates[1].Value);
    }

    [Theory]
    [InlineData(
        "function mutate(size: int): int { const values: MutableArray<int> = MutableArray<int>(size); values[0] = 1; return values[0]; }",
        "mutate",
        "LocalMutation")]
    [InlineData(
        "function host(value: int): int { csharp { return value; } }",
        "host",
        "HostInterop")]
    public void Flow_updates_reject_effectful_helpers_with_the_classified_effect(
        string declaration,
        string helperName,
        string expectedEffect)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir($$"""
            {{declaration}}
            flow Invalid {
                board { value: int = 1; }
                event Go();
                state Ready initial {
                    on Go() -> Ready { board.value = {{helperName}}(board.value); };
                }
            }
            """);

        var diagnostic = Assert.Single(compilation.Diagnostics, item => item.Id == "COPE-FLOW-0024");
        Assert.Contains(helperName, diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(expectedEffect, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Flow_updates_reject_unknown_indirect_calls_fail_closed()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            function increment(value: int): int { return value + 1; }
            function apply(value: int): int {
                const operation: (value: int) => int = increment;
                return operation(value);
            }
            flow Invalid {
                board { value: int = 1; }
                event Go();
                state Ready initial { on Go() -> Ready { board.value = apply(board.value); }; }
            }
            """);

        var diagnostic = Assert.Single(compilation.Diagnostics, item => item.Id == "COPE-FLOW-0024");
        Assert.Contains("cannot prove", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Flow_guards_keep_the_separate_m1_no_call_rule()
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir("""
            function positive(value: int): boolean { return value > 0; }
            flow Guarded {
                board { value: int = 1; }
                event Go();
                state Ready initial { on Go() when positive(board.value) -> Ready; }
            }
            """);

        Assert.Contains(compilation.Diagnostics, item => item.Id == "COPE-FLOW-0018");
        Assert.DoesNotContain(compilation.Diagnostics, item => item.Id == "COPE-FLOW-0024");
    }
}
