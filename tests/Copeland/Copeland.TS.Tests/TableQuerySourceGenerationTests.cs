using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TableQuerySourceGenerationTests
{
    [Fact]
    public void Compiler_query_api_binds_lowers_generates_and_executes_a_typed_result()
    {
        const string source = """
            record table Products {
                id: int = [1, 2, 3];
                name: string = ["Beans", "Kettle", "Filter"];
                retail: number = [18.5, 42.0, 16.25];
            }
            """;

        var compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));

        var request = new TableQueryRequest(
            "Products",
            "retail > 16.0",
            [new TableQueryProjectionRequest("name"), new TableQueryProjectionRequest("retail")],
            [],
            [],
            [new TableQueryOrderingRequest("retail", "descending")],
            0,
            2,
            "query-test");
        BoundTableQueryPlan plan = TableQueryBinder.Bind(compilation.BoundCompilation!, compilation.MirCompilation!.Program!, request);
        var artifact = TableQueryBinder.Lower(plan);

        Assert.Equal(plan.StableId, artifact.StableId);
        Assert.Equal(["name", "retail"], artifact.ResultColumns.Select(column => column.Name));

        ITypedQueryResult result = CSharpTableQueryMaterializer.Execute(compilation.MirCompilation.Program!.WithExecutableArtifact(artifact), artifact);

        Assert.Equal(2, result.RowCount);
        Assert.Equal("Kettle", result.GetValue(0, 0));
        Assert.Equal(42.0, result.GetValue(0, 1));
        Assert.Equal("Beans", result.GetValue(1, 0));
    }
}
