using System.Text.Json;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.InstinctCorpus;
using Xunit;

namespace Copeland.TS.Tests;

internal static class TableJavaScriptParity
{
    public static void AssertRun(CopelandCompilation compilation, int[] expected)
    {
        foreach (JavaScriptEmissionProfile profile in Enum.GetValues<JavaScriptEmissionProfile>())
        {
            var emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!, new JavaScriptEmissionOptions { Profile = profile });
            Assert.Empty(emitted.Diagnostics);
            string output = CorpusRunner.RunNode(emitted.SourceText + "\nconsole.log(JSON.stringify(run()));").GetAwaiter().GetResult();
            Assert.Equal(expected, JsonSerializer.Deserialize<int[]>(output.Trim()));
        }
    }
}
