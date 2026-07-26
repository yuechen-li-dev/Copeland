using System.Diagnostics;
using System.Text;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Lowering;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class FlowM1RuntimeTests
{
    [Fact]
    public async Task Node_executes_a_flow_transition_with_the_same_observable_result()
    {
        const string source = """
            flow Door {
                board { attempts: number = 0; }
                event Open();
                event Reset();
                state Closed initial { on Open() -> Opened { board.attempts = board.attempts + 1; }; }
                state Opened { on Reset() -> Closed; }
            }
            """;

        var lowered = MirLowerer.Lower(SyntaxTree.Parse(source));
        Assert.Empty(lowered.Diagnostics);
        var compilation = JavaScriptBackend.Emit(lowered.Program!);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        const string suffix = """
            const session = Door.start();
            const transition = session.sendOpen();
            console.log(transition.kind);
            console.log(session.state);
            console.log(session.board.attempts);
            """;
        string path = Path.Combine(Path.GetTempPath(), "copeland-flow-" + Guid.NewGuid().ToString("N") + ".js");
        try
        {
            await File.WriteAllTextAsync(path, compilation.SourceText + suffix, new UTF8Encoding(false));
            using var process = Process.Start(new ProcessStartInfo("node", '"' + path + '"')
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.Equal(0, process.ExitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Equal("Transitioned\nOpened\n1\n", stdout);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
