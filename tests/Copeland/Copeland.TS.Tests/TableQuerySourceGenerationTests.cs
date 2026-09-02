using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Copeland.TS.Tson;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TableQuerySourceGenerationTests
{
    private const string PayloadEnumTable = """
        const $schema: string = "copeland://tests/table-query/payload-enum";
        enum ScheduleDay { Every, Day(value: int), }
        enum OtherDay { Day(value: int), }
        enum State { Ready, Done, }
        record table Schedules {
            actor: string = ["Mara", "Mara", "Elias"];
            day: ScheduleDay = [ScheduleDay.Every, ScheduleDay.Day(6), ScheduleDay.Day(6)];
            other: OtherDay = [OtherDay.Day(1), OtherDay.Day(2), OtherDay.Day(3)];
            state: State = [State.Ready, State.Done, State.Ready];
            enabled: boolean = [true, false, true];
            count: int = [1, 2, 3];
            score: number = [1.5, 2.5, 3.5];
        }
        const $value = Schedules;
        """;

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

    [Theory]
    [InlineData("day == Day(6)")]
    [InlineData("day == ScheduleDay.Day(6)")]
    public void Payload_enum_equality_uses_nominal_case_and_payload_values(string predicate)
    {
        ITypedQueryResult result = Execute(PayloadEnumTable, predicate, "actor");

        Assert.Equal(2, result.RowCount);
        Assert.Equal("Mara", result.GetValue(0, 0));
        Assert.Equal("Elias", result.GetValue(1, 0));
    }

    [Theory]
    [InlineData("day == OtherDay.Day(6)", "COPE-TABLE-QUERY-0030")]
    [InlineData("day == ScheduleDay.Missing(6)", "COPE-TABLE-QUERY-0031")]
    [InlineData("day == ScheduleDay.Day()", "COPE-TABLE-QUERY-0032")]
    [InlineData("day == ScheduleDay.Day(\"6\")", "COPE-TABLE-QUERY-0033")]
    [InlineData("day == ScheduleDay.Every(6)", "COPE-TABLE-QUERY-0032")]
    [InlineData("day == ScheduleDay.Day", "COPE-TABLE-QUERY-0032")]
    public void Payload_enum_query_diagnostics_are_specific(string predicate, string code)
    {
        TableQueryBindingException exception = Assert.Throws<TableQueryBindingException>(
            () => Bind(PayloadEnumTable, predicate, "actor"));

        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public void Simple_enums_and_primitive_comparisons_remain_supported()
    {
        Assert.Equal(2, Execute(PayloadEnumTable, "state == Ready", "actor").RowCount);
        Assert.Equal(2, Execute(PayloadEnumTable, "enabled == true", "actor").RowCount);
        Assert.Equal(2, Execute(PayloadEnumTable, "count > 1", "actor").RowCount);
        Assert.Equal(2, Execute(PayloadEnumTable, "score > 2.0", "actor").RowCount);
        Assert.Equal(1, Execute(PayloadEnumTable, "actor == \"Mara\" && day == Day(6)", "actor").RowCount);
    }

    [Fact]
    public void Payload_enum_query_is_stable_after_row_reorder_and_canonical_tson_roundtrip()
    {
        string reordered = PayloadEnumTable
            .Replace("[\"Mara\", \"Mara\", \"Elias\"]", "[\"Elias\", \"Mara\", \"Mara\"]", StringComparison.Ordinal)
            .Replace("[ScheduleDay.Every, ScheduleDay.Day(6), ScheduleDay.Day(6)]", "[ScheduleDay.Day(6), ScheduleDay.Day(6), ScheduleDay.Every]", StringComparison.Ordinal)
            .Replace("[State.Ready, State.Done, State.Ready]", "[State.Ready, State.Done, State.Ready]", StringComparison.Ordinal)
            .Replace("[true, false, true]", "[true, false, true]", StringComparison.Ordinal)
            .Replace("[1, 2, 3]", "[3, 2, 1]", StringComparison.Ordinal)
            .Replace("[1.5, 2.5, 3.5]", "[3.5, 2.5, 1.5]", StringComparison.Ordinal);
        string tsonSource = PayloadEnumTable
            .Replace("value: int", "value: number", StringComparison.Ordinal)
            .Replace("count: int", "count: number", StringComparison.Ordinal)
            .Replace("[1.5, 2.5, 3.5]", "[1, 2, 3]", StringComparison.Ordinal);
        TsonReadResult read = TsonDocumentReader.ReadSelfDescribed(
            tsonSource,
            TsonDocumentProfile.ObjectTypeScript);
        Assert.True(
            read.Success,
            string.Join(Environment.NewLine, read.Diagnostics.Select(diagnostic => diagnostic.Message)));
        string canonical = TsonCanonicalPrinter.Print(read.Document!);

        Assert.Equal(2, Execute(reordered, "day == Day(6)", "actor").RowCount);
        Assert.Equal(2, Execute(canonical, "day == Day(6)", "actor").RowCount);
    }

    private static BoundTableQueryPlan Bind(string source, string predicate, params string[] projection)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(
            compilation.Success,
            string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return TableQueryBinder.Bind(
            compilation.BoundCompilation!,
            compilation.MirCompilation!.Program!,
            new TableQueryRequest(
                "Schedules",
                predicate,
                projection.Select(column => new TableQueryProjectionRequest(column)).ToArray(),
                [],
                [],
                [],
                0,
                100));
    }

    private static ITypedQueryResult Execute(string source, string predicate, params string[] projection)
    {
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(
            compilation.Success,
            string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
        BoundTableQueryPlan plan = TableQueryBinder.Bind(
            compilation.BoundCompilation!,
            compilation.MirCompilation!.Program!,
            new TableQueryRequest(
                "Schedules",
                predicate,
                projection.Select(column => new TableQueryProjectionRequest(column)).ToArray(),
                [],
                [],
                [],
                0,
                100));
        MirTableQueryArtifact artifact = TableQueryBinder.Lower(plan);
        return CSharpTableQueryMaterializer.Execute(
            compilation.MirCompilation.Program!.WithExecutableArtifact(artifact),
            artifact);
    }
}
