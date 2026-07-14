using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class RecordFeatureTests
{
    private const string PointProgram = """
        record Point {
            x: number;
            y: number;
        }

        function move(point: Point): Point {
            const created: Point = { y: 2, x: 1 };
            return point with { y: created.x, x: point.y };
        }
        """;

    [Fact]
    public void Parser_preserves_record_declaration_member_access_and_with()
    {
        var tree = SyntaxTree.Parse(PointProgram);

        Assert.Empty(tree.Diagnostics);
        var declaration = Assert.IsType<RecordDeclarationSyntax>(tree.Root.Members[0]);
        Assert.Equal(["x", "y"], declaration.Fields.Select(field => field.Identifier.Text));
        var function = Assert.IsType<FunctionDeclarationSyntax>(tree.Root.Members[1]);
        var returned = Assert.IsType<ReturnStatementSyntax>(function.Body.Statements[1]);
        var withExpression = Assert.IsType<WithExpressionSyntax>(returned.Expression);
        Assert.Equal(["y", "x"], withExpression.Replacements.Properties.Select(property => property.NameToken.Text));
        Assert.IsType<MemberAccessExpressionSyntax>(withExpression.Replacements.Properties[0].ValueExpression);
    }

    [Fact]
    public void Binder_preserves_nominal_identity_and_authored_order()
    {
        var compilation = CopelandCompiler.CompileToMir(PointProgram);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        BoundProgram program = compilation.BoundCompilation!.Program;
        RecordTypeSymbol point = Assert.Single(program.Records).RecordType;
        Assert.Equal("r1", point.Id.ToString());
        Assert.Equal(["r1.f0", "r1.f1"], point.Fields.Select(field => field.Id.ToString()));

        var declaration = Assert.IsType<BoundVariableDeclaration>(program.Functions[0].Body.Statements[0]);
        var construction = Assert.IsType<BoundRecordConstructionExpression>(declaration.Initializer);
        Assert.Equal(["y", "x"], construction.Initializers.Select(item => item.Field.Name));

        var returned = Assert.IsType<BoundReturnStatement>(program.Functions[0].Body.Statements[1]);
        var update = Assert.IsType<BoundRecordWithExpression>(returned.Expression);
        Assert.Equal(["y", "x"], update.Replacements.Select(item => item.Field.Name));
    }

    [Fact]
    public void Mir_is_deterministic_and_displays_nominal_and_field_identities()
    {
        var first = CopelandCompiler.CompileToMir(PointProgram);
        var second = CopelandCompiler.CompileToMir(PointProgram);

        Assert.Equal(first.MirText, second.MirText);
        Assert.Contains("record Point [r1]", first.MirText, StringComparison.Ordinal);
        Assert.Contains("field x [r1.f0]: number", first.MirText, StringComparison.Ordinal);
        Assert.Contains("record-new [r1] { [r1.f1]: 2, [r1.f0]: 1 }", first.MirText, StringComparison.Ordinal);
        Assert.Contains("record-with [r1] point { [r1.f1]", first.MirText, StringComparison.Ordinal);
        Assert.Contains("record-get [r1] created.[r1.f0]", first.MirText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("const record Point { x: number; }", "COPE-REC-0001")]
    [InlineData("record Point { x(): number; }", "COPE-REC-0001")]
    [InlineData("record Point { x: Point; }", "COPE-REC-0004")]
    [InlineData("record Node { wrapper: Wrapper; } enum Wrapper { One(node: Node), }", "COPE-REC-0004")]
    [InlineData("function bad(): void { const value = { x: 0 }; }", "COPE-REC-0005")]
    [InlineData("record Point { x: number; } function bad(point: Point): void { point.x = 1; }", "COPE-REC-0011")]
    [InlineData("function bad(value: number): number { return value with { x: 1 }; }", "COPE-REC-0012")]
    [InlineData("record Point { x: number; } function bad(point: Point): Point { return point with {}; }", "COPE-REC-0013")]
    [InlineData("record A { x: number; } record B { x: number; } function bad(value: A): B { return value; }", "COPE-REC-0015")]
    [InlineData("record Point { x: number; } function bad(left: Point, right: Point): boolean { return left == right; }", "COPE-REC-0016")]
    public void Binder_reports_stable_record_diagnostic(string source, string diagnosticId)
    {
        var compilation = CopelandCompiler.CompileToMir(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
        Assert.DoesNotContain(compilation.Diagnostics, diagnostic => diagnostic.Id.StartsWith("COPE-PARSE", StringComparison.Ordinal));
    }

    [Fact]
    public void Mir_validator_rejects_incomplete_and_mismatched_record_construction()
    {
        var recordId = new MirRecordTypeId("r1");
        var xId = new MirRecordFieldId("r1.f0");
        var yId = new MirRecordFieldId("r1.f1");
        var number = new MirNamedType("number");
        var definition = new MirRecordDefinition(
            recordId,
            "Point",
            [new MirRecordFieldDefinition(xId, "x", number), new MirRecordFieldDefinition(yId, "y", number)]);
        var pointType = new MirRecordType(recordId, "Point");
        var construction = new MirRecordConstructionExpression(
            recordId,
            [new MirRecordFieldValue(xId, new MirLiteralExpression("wrong", new MirNamedType("string")))],
            pointType);
        var local = new MirLocal("point", pointType, true);
        var function = new MirFunction("main", [], pointType, [local], [new MirReturnStatement(construction)]);
        var program = new MirProgram([], [definition], [function]);

        var diagnostics = MirValidator.Validate(program);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("does not match field 'r1.f0'", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("missing field identity 'r1.f1'", StringComparison.Ordinal));
    }
}
