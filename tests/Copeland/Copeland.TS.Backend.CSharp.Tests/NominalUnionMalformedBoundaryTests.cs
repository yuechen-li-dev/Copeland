using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Backend.CSharp.Tests;

public sealed class NominalUnionMalformedBoundaryTests
{
    [Fact]
    public void Union_authored_canonical_enum_and_tson_states_are_rejected_before_either_backend_emits()
    {
        MirProgram baseline = CompileUnionProgram();
        MirEnum shape = baseline.Enums.Single(@enum => @enum.Name == "Shape");
        MirRecordDefinition circle = baseline.Records.Single(record => record.Name == "Circle");
        MirTsonEncodingPlan plan = Assert.Single(baseline.TsonEncodingPlans);
        MirTsonEnumPlan enumPlan = Assert.IsType<MirTsonEnumPlan>(plan.Definitions.Single(definition => definition is MirTsonEnumPlan));
        MirFunction make = baseline.Functions.Single(function => function.Name == "make");
        MirFunction area = baseline.Functions.Single(function => function.Name == "area");

        MirType shapeType = new MirNamedType("Shape");
        MirType numberType = new MirNamedType("number");
        MirType missingCircleType = new MirRecordType(new MirRecordTypeId("missing"), "Circle");

        var cases = new (string Expected, MirProgram Program)[]
        {
            (
                "duplicate case name 'Circle'",
                ReplaceShapeEnum(
                    baseline,
                    new MirEnum(
                        "Shape",
                        [
                            new MirEnumCase("Circle", [new MirEnumPayloadField("value", new MirRecordType(circle.Id, "Circle"))]),
                            new MirEnumCase("Circle", [new MirEnumPayloadField("value", new MirRecordType(circle.Id, "Circle"))]),
                        ]))),
            (
                "enum 'Shape' payload",
                ReplaceShapeEnum(
                    baseline,
                    new MirEnum(
                        "Shape",
                        [
                            new MirEnumCase("Circle", [new MirEnumPayloadField("value", missingCircleType)]),
                            shape.Cases[1],
                        ]))),
            (
                "has 0 payloads but case declares 1",
                ReplaceFunction(
                    baseline,
                    new MirFunction(
                        make.Name,
                        make.Parameters,
                        make.ReturnType,
                        make.Locals,
                        [
                            new MirReturnStatement(new MirEnumValueExpression("Shape", "Circle", [], shapeType)),
                        ]))),
            (
                "has 0 bindings but case declares 1 payloads",
                ReplaceFunction(
                    baseline,
                    new MirFunction(
                        area.Name,
                        area.Parameters,
                        area.ReturnType,
                        area.Locals,
                        [
                            new MirReturnStatement(
                                new MirMatchExpression(
                                    new MirVariableExpression("shape", shapeType),
                                    [
                                        new MirMatchArm("Circle", [], new MirLiteralExpression(1, numberType)),
                                        new MirMatchArm("Rectangle", [new MirMatchPayloadBinding("value", new MirRecordType(baseline.Records.Single(record => record.Name == "Rectangle").Id, "Rectangle"))], new MirLiteralExpression(2, numberType)),
                                    ],
                                    numberType)),
                        ]))),
            (
                "non-exhaustive match for enum 'Shape'",
                ReplaceFunction(
                    baseline,
                    new MirFunction(
                        area.Name,
                        area.Parameters,
                        area.ReturnType,
                        area.Locals,
                        [
                            new MirReturnStatement(
                                new MirMatchExpression(
                                    new MirVariableExpression("shape", shapeType),
                                    [
                                        new MirMatchArm("Circle", [new MirMatchPayloadBinding("value", new MirRecordType(circle.Id, "Circle"))], new MirLiteralExpression(1, numberType)),
                                    ],
                                    numberType)),
                        ]))),
            (
                "does not match declaration order, identity, or type",
                ReplacePlan(
                    baseline,
                    new MirTsonEncodingPlan(
                        plan.Id,
                        plan.SchemaIdentity,
                        plan.RootType,
                        plan.RootValuePlan,
                        plan.Definitions.Select<MirTsonNominalPlan, MirTsonNominalPlan>(definition =>
                            definition is MirTsonEnumPlan
                                ? new MirTsonEnumPlan(
                                    enumPlan.Name,
                                    enumPlan.StableIdentity,
                                    [
                                        new MirTsonEnumCasePlan("Circle", enumPlan.Cases[0].StableIdentity, [
                                            new MirTsonEnumPayloadPlan("payload", enumPlan.Cases[0].Payloads[0].StableIdentity, new MirTsonRecordValuePlan(circle.Id)),
                                        ]),
                                        enumPlan.Cases[1],
                                    ])
                                : definition).ToArray(),
                        plan.Limits))),
        };

        foreach ((string expected, MirProgram program) in cases)
        {
            CSharpCompilation csharp = CSharpBackend.Emit(program);
            JavaScriptCompilation javaScript = JavaScriptBackend.Emit(program);

            Assert.Equal(string.Empty, csharp.SourceText);
            Assert.False(javaScript.Success);
            Assert.Null(javaScript.SourceText);
            Assert.Contains(csharp.Diagnostics, diagnostic => diagnostic.Id == "COPE-CS-0002" && diagnostic.Message.Contains(expected, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(javaScript.Diagnostics, diagnostic => diagnostic.Id == "COPE-JS-0002" && diagnostic.Message.Contains(expected, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static MirProgram CompileUnionProgram()
    {
        const string source = """
const $schema: string = "copeland://tests/union-boundary";
record Circle { radius: number; }
record Rectangle { width: number; height: number; }
type Shape = Circle | Rectangle;
function make(): Shape {
    const circle: Circle = { radius: 4 };
    return circle;
}
function area(shape: Shape): number {
    return match shape {
        Circle(value) => value.radius,
        Rectangle(value) => value.width * value.height,
    };
}
function encode(shape: Shape): string ! TsonEncodeError { return tsonEncode(shape); }
""";

        CopelandCompilation compilation = CopelandCompiler.CompileToMir(source);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        return compilation.MirCompilation!.Program!;
    }

    private static MirProgram ReplaceShapeEnum(MirProgram baseline, MirEnum replacement)
    {
        return new MirProgram(
            baseline.Enums.Select(@enum => @enum.Name == "Shape" ? replacement : @enum).ToArray(),
            baseline.Records,
            baseline.Tables,
            baseline.TsonEncodingPlans,
            baseline.Functions);
    }

    private static MirProgram ReplaceFunction(MirProgram baseline, MirFunction replacement)
    {
        return new MirProgram(
            baseline.Enums,
            baseline.Records,
            baseline.Tables,
            baseline.TsonEncodingPlans,
            baseline.Functions.Select(function => function.Name == replacement.Name ? replacement : function).ToArray());
    }

    private static MirProgram ReplacePlan(MirProgram baseline, MirTsonEncodingPlan replacement)
    {
        return new MirProgram(
            baseline.Enums,
            baseline.Records,
            baseline.Tables,
            baseline.TsonEncodingPlans.Select(plan => plan.Id == replacement.Id ? replacement : plan).ToArray(),
            baseline.Functions);
    }
}
