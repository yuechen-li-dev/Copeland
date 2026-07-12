using Aurelian.Shaders.Language.Ast;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class SdslvAstContractTests
{
    private static SdslvNamedTypeRef Type(string name) => new(new SdslvPath(name));

    [Fact]
    public void SdslvPath_ToString_JoinsSegments()
    {
        var path = new SdslvPath("Aurelian", "Materials", "Flat");

        Assert.Equal("Aurelian.Materials.Flat", path.ToString());
    }

    [Fact]
    public void SdslvTypeRef_NamedType_DisplaysPath()
    {
        SdslvTypeRef type = new SdslvNamedTypeRef(new SdslvPath("math", "float4"));

        Assert.Equal("math.float4", type.ToDisplayString());
    }

    [Fact]
    public void SdslvTypeRef_ArrayType_DisplaysArray()
    {
        SdslvTypeRef type = new SdslvArrayTypeRef(Type("float4"), 3);

        Assert.Equal("array<float4, 3>", type.ToDisplayString());
    }

    [Fact]
    public void SdslvModule_CanHoldNamespaceUsesAndDeclarations()
    {
        var declaration = new SdslvTypeAliasDecl("Color", Type("float4"));
        var module = new SdslvModule(
            new SdslvPath("Aurelian", "ShaderFixtures"),
            [new SdslvUseDecl(new SdslvPath("Aurelian", "Math"))],
            [declaration]);

        Assert.Equal("Aurelian.ShaderFixtures", module.Namespace?.ToString());
        Assert.Equal("Aurelian.Math", module.Uses[0].Path.ToString());
        Assert.Same(declaration, module.Declarations[0]);
    }

    [Fact]
    public void SdslvRecordDecl_CanHoldFields()
    {
        var record = new SdslvRecordDecl(
            "Vertex",
            [
                new SdslvFieldDecl("Position", Type("float3")),
                new SdslvFieldDecl("Color", Type("float4")),
            ]);

        Assert.Equal("Vertex", record.Name);
        Assert.Equal(2, record.Fields.Count);
        Assert.Equal("float4", record.Fields[1].TypeName.ToDisplayString());
    }

    [Fact]
    public void SdslvStreamDecl_CanHoldFields()
    {
        var stream = new SdslvStreamDecl(
            "VertexStream",
            [new SdslvFieldDecl("Position", Type("float3"))]);

        Assert.Equal("VertexStream", stream.Name);
        Assert.Equal("Position", stream.Fields[0].Name);
    }

    [Fact]
    public void SdslvEnumDecl_CanHoldVariants()
    {
        var declaration = new SdslvEnumDecl(
            "LightMode",
            [new SdslvEnumVariant("Unlit"), new SdslvEnumVariant("Lit")]);

        Assert.Equal("LightMode", declaration.Name);
        Assert.Equal("Lit", declaration.Variants[1].Name);
    }

    [Fact]
    public void SdslvShaderDecl_CanHoldGenericsInterfacesConstraintsMaterialAndStageMethods()
    {
        var materialFields = new[] { new SdslvFieldDecl("BaseColor", Type("float4")) };
        var stageMethod = new SdslvFunctionDecl(
            IsOverride: true,
            Stage: "fragment",
            Name: "FragmentMain",
            Parameters: [new SdslvFunctionParameter("input", Type("VertexOut"))],
            ReturnType: Type("float4"),
            ErrorType: null,
            Body: new SdslvBody([new SdslvReturnStatement(new SdslvIdentifierExpression("BaseColor"))]));
        var shader = new SdslvShaderDecl(
            "ForwardPass",
            ["TMaterial"],
            [new SdslvPath("IRenderable")],
            [new SdslvWhereConstraint("TMaterial", [new SdslvPath("IBaseColor")])],
            materialFields,
            [],
            [stageMethod]);

        Assert.Equal("ForwardPass", shader.Name);
        Assert.Equal("TMaterial", shader.GenericParameters[0]);
        Assert.Equal("IRenderable", shader.Implements[0].ToString());
        Assert.Equal("IBaseColor", shader.Constraints[0].Bounds[0].ToString());
        Assert.Same(materialFields[0], shader.MaterialFields[0]);
        Assert.Equal("fragment", shader.StageMethods[0].Stage);
    }

    [Fact]
    public void SdslvExpression_SwitchMatchUtilityAndFallibilityShapesAreRepresentable()
    {
        var zero = new SdslvIntegerLiteralExpression("0");
        var one = new SdslvIntegerLiteralExpression("1");
        var subject = new SdslvIdentifierExpression("mode");
        var switchExpression = new SdslvSwitchExpression(
            subject,
            [new SdslvSwitchCase(new SdslvBoolLiteralExpression(true), one)],
            zero);
        var matchExpression = new SdslvMatchExpression(
            new SdslvIdentifierExpression("result"),
            [
                new SdslvMatchArm(new SdslvFallibleOkMatchArmKind("value"), new SdslvIdentifierExpression("value")),
                new SdslvMatchArm(new SdslvFallibleErrMatchArmKind("error"), zero),
            ]);
        var utilityExpression = new SdslvWhenUtilityExpression(
            new SdslvUtilityOptions(new SdslvFloatLiteralExpression("0.25"), new SdslvIntegerLiteralExpression("2")),
            [new SdslvUtilityCase(new SdslvStringLiteralExpression("attack"), new SdslvBoolLiteralExpression(true), one)],
            new SdslvStringLiteralExpression("idle"));
        var tryExpression = new SdslvTryPropagateExpression(new SdslvCallExpression(new SdslvIdentifierExpression("Compute"), []));
        var unwrapExpression = new SdslvUnwrapExpression(new SdslvIdentifierExpression("optionalValue"));

        Assert.Same(subject, switchExpression.Subject);
        Assert.IsType<SdslvFallibleOkMatchArmKind>(matchExpression.Arms[0].Kind);
        Assert.NotNull(utilityExpression.Options?.Hysteresis);
        Assert.IsType<SdslvCallExpression>(tryExpression.Expression);
        Assert.IsType<SdslvIdentifierExpression>(unwrapExpression.Expression);
    }

    [Fact]
    public void SdslvFlowDecl_CanRepresentBoardStatesAndTransitions()
    {
        var flow = new SdslvFlowDecl(
            "ChooseAction",
            [new SdslvFunctionParameter("threat", Type("float"))],
            Type("Action"),
            new SdslvFlowBoard([new SdslvFlowBoardField("lastAction", Type("Action"))]),
            [
                new SdslvFlowState(
                    "Evaluate",
                    [
                        new SdslvFlowBoardAssignStatement("lastAction", new SdslvIdentifierExpression("Attack")),
                        new SdslvFlowWhenStatement(
                            new SdslvFlowWhen(
                                [new SdslvFlowCase(new SdslvBoolLiteralExpression(true), new SdslvFlowGotoAction(new SdslvPath("Commit")))],
                                new SdslvFlowReturnAction(new SdslvIdentifierExpression("Idle")))),
                    ]),
                new SdslvFlowState("Commit", [new SdslvFlowReturnStatement(new SdslvIdentifierExpression("lastAction"))]),
            ]);

        Assert.Equal("ChooseAction", flow.Name);
        Assert.Equal("lastAction", flow.Board?.Fields[0].Name);
        Assert.Equal(2, flow.States.Count);
        Assert.IsType<SdslvFlowWhenStatement>(flow.States[0].Statements[1]);
    }
}
