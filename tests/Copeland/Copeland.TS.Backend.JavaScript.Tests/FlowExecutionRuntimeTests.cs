using System.Diagnostics;
using System.Text;
using Copeland.TS.Lowering;
using Copeland.TS.Syntax;
using Copeland.TS.Tests.TestSupport;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class FlowExecutionRuntimeTests
{
    [Theory]
    [MemberData(nameof(FlowExecutionCases.All), MemberType = typeof(FlowExecutionCases))]
    public async Task Typed_steps_run_in_node(string source, int expected)
    {
        var lowered = MirLowerer.Lower(SyntaxTree.Parse(source));
        Assert.Empty(lowered.Diagnostics);
        var emitted = JavaScriptBackend.Emit(lowered.Program!);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        string path = Path.Combine(Path.GetTempPath(), "copeland-execution-" + Guid.NewGuid().ToString("N") + ".js");
        try
        {
            await File.WriteAllTextAsync(path, emitted.SourceText + "\nconsole.log(main());\n", new UTF8Encoding(false));
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
            Assert.Equal(expected.ToString() + "\n", stdout.Replace("\r\n", "\n", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
