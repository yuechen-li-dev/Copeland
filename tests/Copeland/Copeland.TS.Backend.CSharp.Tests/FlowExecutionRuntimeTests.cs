using Copeland.TS.Backend.CSharp.Tests.Runtime;
using Copeland.TS.Lowering;
using Copeland.TS.Syntax;
using Copeland.TS.Tests.TestSupport;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class FlowExecutionRuntimeTests
{
    [Theory]
    [MemberData(nameof(FlowExecutionCases.All), MemberType = typeof(FlowExecutionCases))]
    public void Typed_steps_run_in_generated_csharp(string source, int expected)
    {
        var lowered = MirLowerer.Lower(SyntaxTree.Parse(source));
        Assert.Empty(lowered.Diagnostics);
        var emitted = CSharpBackend.Emit(lowered.Program!);
        Assert.Empty(emitted.Diagnostics);
        var compiled = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
        Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Diagnostics));
        var module = compiled.Assembly!.GetType("Copeland.Generated.CopelandModule", true)!;
        Assert.Equal(expected, module.GetMethod("main")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("CopelandModule")]
    [InlineData("GameModule")]
    public void Countdown_runs_and_old_frames_remain_independent_snapshots(string moduleName)
    {
        var lowered = MirLowerer.Lower(SyntaxTree.Parse("""
            flow Countdown(limit: int) -> int {
                board { remaining: int = limit; }
                state Counting initial {
                    if (board.remaining == 0) { finish limit; }
                    board.remaining = board.remaining - 1;
                    yield;
                }
            }
            function main(): int {
                const initial = Countdown.start(3);
                let frame = Countdown.step(initial);
                frame = Countdown.step(frame);
                frame = Countdown.step(frame);
                frame = Countdown.step(frame);
                frame = Countdown.step(frame);
                return match frame.outcome {
                    case CountdownOutcome.Active => 100,
                    case CountdownOutcome.Yielded => 200,
                    case CountdownOutcome.Completed(value) => value + initial.board.remaining,
                };
            }
            """));
        Assert.Empty(lowered.Diagnostics);
        var emitted = CSharpBackend.Emit(lowered.Program!, moduleName);
        Assert.Empty(emitted.Diagnostics);
        var compiled = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
        Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Diagnostics));
        var module = compiled.Assembly!.GetType("Copeland.Generated." + moduleName, true)!;
        Assert.Equal(6, module.GetMethod("main")!.Invoke(null, null));
    }
}
