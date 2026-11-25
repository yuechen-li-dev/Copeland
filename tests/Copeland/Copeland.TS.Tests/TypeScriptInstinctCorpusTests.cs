using Copeland.TS.Compiler;
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

    [Theory]
    [InlineData("const items = MutableList<int>(); items.append(\"seven\");", "COPE-TYPE-0005", "int")]
    [InlineData("const items: MutableArray<int> = MutableList<int>();", "COPE-TYPE-0001", "MutableArray<int>")]
    public void Mutable_list_requires_its_element_and_collection_types(string statements, string code, string expectedType)
    {
        var compilation = CopelandCompiler.CompileToMir($"function run(): void {{ {statements} }}");
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == code
            && diagnostic.Message.Contains($"expected '{expectedType}'", StringComparison.Ordinal));
    }

    [Fact]
    public void Filled_enum_array_rejects_mutable_nested_payloads()
    {
        var compilation = CopelandCompiler.CompileToMir("""
            record Storage { values: MutableList<int>; }
            enum Item { Empty, Present(value: Storage) }
            function run(): void {
                const items = MutableArray<Item>(2, Item.Empty);
            }
            """);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-ARRAY-0008");
    }
}
