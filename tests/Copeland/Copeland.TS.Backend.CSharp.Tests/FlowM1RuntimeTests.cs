using Copeland.TS.Backend.CSharp.Tests.Runtime;
using Copeland.TS.Lowering;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class FlowM1RuntimeTests
{
    [Fact]
    public void Flow_session_executes_a_typed_event_transition_and_exposes_a_read_only_board_snapshot()
    {
        var lowered = MirLowerer.Lower(SyntaxTree.Parse("""
            function nextAttempt(value: number): number { return value + 1; }
            flow Door {
                board { attempts: number = 0; }
                event Open();
                event Reset();
                state Closed initial { on Open() -> Opened { board.attempts = nextAttempt(board.attempts); }; }
                state Opened { on Reset() -> Closed; }
            }
            """));

        Assert.Empty(lowered.Diagnostics);
        CSharpCompilation emitted = CSharpBackend.Emit(lowered.Program!);
        Assert.Empty(emitted.Diagnostics);
        Assert.Contains("public static class Door", emitted.SourceText, StringComparison.Ordinal);
        Assert.Contains("CopelandModule.nextAttempt(", emitted.SourceText, StringComparison.Ordinal);
        Assert.Equal(1, emitted.SourceText.Split("CopelandModule.nextAttempt(", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Dictionary", emitted.SourceText, StringComparison.Ordinal);

        var generated = RoslynCompileHelper.CompileGeneratedSource(emitted.SourceText);
        Assert.True(generated.Success, string.Join(Environment.NewLine, generated.Diagnostics));

        Type flow = generated.Assembly!.GetType("Copeland.Generated.Door", throwOnError: true)!;
        object session = flow.GetMethod("Start")!.Invoke(null, null)!;
        object transition = session.GetType().GetMethod("SendOpen")!.Invoke(session, null)!;

        Assert.Equal("Transitioned", transition.GetType().GetProperty("Kind")!.GetValue(transition));
        Assert.Equal("Opened", session.GetType().GetProperty("State")!.GetValue(session));
        object board = session.GetType().GetProperty("Board")!.GetValue(session)!;
        double attempts = Assert.IsType<double>(board.GetType().GetProperties().Single().GetValue(board));
        Assert.Equal(1d, attempts);
    }
}
