using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class MalformedTsonEncodingPlanValidationTests
{
    public static IEnumerable<object[]> Cases()
    {
        MirProgram valid = ValidProgram();
        MirTsonEncodingPlan plan = Assert.Single(valid.TsonEncodingPlans);
        MirFunction function = Assert.Single(valid.Functions);

        yield return Case("duplicate plan identity", Program([plan, plan], function));
        yield return Case("missing referenced plan", Program([], function));
        yield return Case("malformed schema", Program([Plan(schema: "bad")], function));
        yield return Case("invalid limits", Program([Plan(limits: new MirTsonEncodingLimits(1, 1))], function));
        yield return Case(
            "wrong expression root",
            Program(
                [plan],
                Function(new MirTsonEncodeExpression(
                    new MirLiteralExpression("wrong", new MirNamedType("string")),
                    plan.Id,
                    ResultType()))));
        yield return Case(
            "wrong expression result",
            Program(
                [plan],
                Function(new MirTsonEncodeExpression(
                    new MirVariableExpression("value", RootType()),
                    plan.Id,
                    new MirResultType(new MirNamedType("number"), new MirNamedType("TsonEncodeError"))))));
        yield return Case(
            "missing reachable declaration",
            Program([Plan(definitions: [])], function));
        yield return Case(
            "extraneous declaration",
            Program(
                [Plan(definitions: [RecordPlan(), new MirTsonEnumPlan("Unused", Schema + "#Unused", [])])],
                function,
                extraEnums: [new MirEnum("Unused", [new MirEnumCase("Only", [])])]));
        yield return Case(
            "duplicate field identity",
            Program(
                [Plan(definitions: [new MirTsonRecordPlan(
                    RootId,
                    "Root",
                    Schema + "#Root",
                    [
                        new MirTsonRecordFieldPlan(FieldId, "text", Schema + "#Root", new MirTsonStringPlan()),
                    ])])],
                function));
        yield return Case(
            "unsupported child reference",
            Program(
                [Plan(definitions: [new MirTsonRecordPlan(
                    RootId,
                    "Root",
                    Schema + "#Root",
                    [
                        new MirTsonRecordFieldPlan(FieldId, "text", Schema + "#Root.text", new MirTsonEnumValuePlan("Missing")),
                    ])])],
                function));
        yield return Case(
            "noncanonical ordering",
            Program(
                [Plan(
                    rootValuePlan: new MirTsonEnumValuePlan("Zed"),
                    rootType: new MirNamedType("Zed"),
                    definitions:
                    [
                        new MirTsonEnumPlan("Zed", Schema + "#Zed", [new MirTsonEnumCasePlan("Only", Schema + "#Zed.Only", [])]),
                        RecordPlan(),
                    ])],
                Function(new MirTsonEncodeExpression(
                    new MirVariableExpression("value", new MirNamedType("Zed")),
                    new MirTsonEncodingPlanId("tson0"),
                    ResultType())),
                extraEnums: [new MirEnum("Zed", [new MirEnumCase("Only", [])])]));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Both_backends_reject_malformed_encoding_mir_without_artifact(string name, MirProgram program)
    {
        Assert.False(string.IsNullOrWhiteSpace(name));
        CSharpCompilation csharp = CSharpBackend.Emit(program);
        JavaScriptCompilation javaScript = JavaScriptBackend.Emit(program);

        Assert.NotEmpty(csharp.Diagnostics);
        Assert.Equal(string.Empty, csharp.SourceText);
        Assert.False(javaScript.Success);
        Assert.Null(javaScript.SourceText);
    }

    private const string Schema = "copeland://tests/malformed-plan";
    private static readonly MirRecordTypeId RootId = new("r1");
    private static readonly MirRecordFieldId FieldId = new("r1.f0");

    private static object[] Case(string name, MirProgram program) => [name, program];

    private static MirProgram ValidProgram()
    {
        MirTsonEncodingPlan plan = Plan();
        return Program(
            [plan],
            Function(new MirTsonEncodeExpression(
                new MirVariableExpression("value", RootType()),
                plan.Id,
                ResultType())));
    }

    private static MirProgram Program(
        IReadOnlyList<MirTsonEncodingPlan> plans,
        MirFunction function,
        IReadOnlyList<MirEnum>? extraEnums = null)
    {
        var enums = new List<MirEnum>
        {
            new("TsonEncodeError",
            [
                new MirEnumCase("InvalidUnicode", []),
                new MirEnumCase("OutputLimitExceeded", []),
            ]),
        };
        if (extraEnums is not null) enums.AddRange(extraEnums);
        return new MirProgram(
            enums,
            [new MirRecordDefinition(RootId, "Root", [new MirRecordFieldDefinition(FieldId, "text", new MirNamedType("string"))])],
            [],
            plans,
            [function]);
    }

    private static MirFunction Function(MirExpression expression)
        => new(
            "encode",
            [new MirParameter("value", expression is MirTsonEncodeExpression encode ? encode.Operand.Type : RootType())],
            expression.Type,
            [],
            [new MirReturnStatement(expression)]);

    private static MirTsonEncodingPlan Plan(
        string? schema = null,
        MirType? rootType = null,
        MirTsonValuePlan? rootValuePlan = null,
        IReadOnlyList<MirTsonNominalPlan>? definitions = null,
        MirTsonEncodingLimits? limits = null)
        => new(
            new MirTsonEncodingPlanId("tson0"),
            schema ?? Schema,
            rootType ?? RootType(),
            rootValuePlan ?? new MirTsonRecordValuePlan(RootId),
            definitions ?? [RecordPlan()],
            limits ?? new MirTsonEncodingLimits(1_048_576, 262_144));

    private static MirTsonRecordPlan RecordPlan()
        => new(
            RootId,
            "Root",
            Schema + "#Root",
            [new MirTsonRecordFieldPlan(FieldId, "text", Schema + "#Root.text", new MirTsonStringPlan())]);

    private static MirRecordType RootType() => new(RootId, "Root");

    private static MirResultType ResultType()
        => new(new MirNamedType("string"), new MirNamedType("TsonEncodeError"));
}
