using Copeland.TS.InstinctCorpus;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TypeScriptInstinctCorpusTests
{
    public static IEnumerable<object[]> Cases()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TypeScriptInstinctCorpus", "instinct-corpus.json");
        return CorpusRunner.ReadCases(path).Select(fixture => new object[] { fixture });
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Authored_instinct_executes_on_both_backends_or_has_one_primary_repair(InstinctCase fixture)
    {
        return CorpusRunner.Verify(fixture);
    }
}
