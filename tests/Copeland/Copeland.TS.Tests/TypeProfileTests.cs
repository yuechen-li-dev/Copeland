using System.Text.Json;
using Copeland.TS.Compiler;
using Copeland.TS.Tson;
using Copeland.TS.Workspace;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class TypeProfileTests
{
    private const string Source = """
        type CopelandProfile = "type";
        type Time = string;
        type Range = { start: Time; end: Time; };
        type Trim = { operation: "trim"; range: Range; note?: string; };
        type Delete = { operation: "delete"; clipId: string; };
        type Plan = { operations: (Trim | Delete)[]; options?: { enabled: boolean; }; };
        """;

    [Fact]
    public void ProfileProducesDeterministicStructuralTsonCatalogAndValidatesTypedData()
    {
        Assert.Equal("type", SourceFileProfiles.Read(Source).Name);
        var result = TypeProfileCompiler.Compile(Source, "plan.obj.ts", "copeland://tests/types");
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        var trim = Assert.IsType<TsonTypeAliasDefinition>(result.Catalog!.Definitions.Single(d => d.Name == "Trim"));
        var delete = Assert.IsType<TsonTypeAliasDefinition>(result.Catalog.Definitions.Single(d => d.Name == "Delete"));
        Assert.NotEqual(trim.Shape.Fields.Single(f => f.Name == "operation").Identity,
            delete.Shape.Fields.Single(f => f.Name == "operation").Identity);
        string artifact = TypeProfileCompiler.CanonicalArtifact(result.Catalog!);
        Assert.Equal(artifact, TypeProfileCompiler.CanonicalArtifact(TypeProfileCompiler.Compile(Source.Replace(";", ";\n"), "other.obj.ts", "copeland://tests/types").Catalog!));
        var value = TypeProfileCompiler.ReadTypedValue("""
            const $value: Plan = { operations: [{ operation: "trim", range: { start: "9007199254740993", end: "9007199254740994" } }, { operation: "delete", clipId: "x" }] };
            """, "value.obj.ts", result.Catalog!, "Plan");
        Assert.Empty(TypeProfileCompiler.Validate(result.Catalog!, "Plan", value));
        Assert.Contains("9007199254740993", value.GetRawText());
        using var invalid = JsonDocument.Parse("""{"operations":[{"operation":"delete","clipId":1}]}""");
        Assert.NotEmpty(TypeProfileCompiler.Validate(result.Catalog!, "Plan", invalid.RootElement));
    }

    [Theory]
    [InlineData("type A = Missing;")]
    [InlineData("type A = { x: A; };")]
    [InlineData("type A = { x: string; x: boolean; };")]
    [InlineData("function run(): number { return 1; }")]
    public void InvalidSchemasHaveLocatedDiagnostics(string body)
    {
        var result = TypeProfileCompiler.Compile(body, "invalid.obj.ts", "copeland://tests/types", "type");
        Assert.False(result.Success);
        Assert.All(result.Diagnostics, d => Assert.Equal("invalid.obj.ts", d.SourcePath));
    }

    [Fact]
    public void ProfileSelectionIsExplicitAndOldSchemaAndExecutableAliasesStayCompatible()
    {
        Assert.False(TypeProfileCompiler.Compile("type A = string;", "plain.obj.ts", "copeland://tests/types").Success);
        Assert.True(CopelandCompiler.CompileToMir("type A = string; function f(x: A): A { return x; }").Success);
        var legacy = TsonDocumentReader.ReadSelfDescribed("""
            const $schema: string = "copeland://tests/legacy";
            record Item { text: string; }
            const $value: Item = { text: "old" };
            """, TsonDocumentProfile.ObjectTypeScript);
        Assert.True(legacy.Success, string.Join("\n", legacy.Diagnostics));
        Assert.Contains(CopelandCompiler.CompileToMir(Source).Diagnostics, d => d.Id == "COPE-TYPE-0001");
    }
}
