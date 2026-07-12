using Aurelian.Shaders.Language.Ast;
using Aurelian.Shaders.Language.Diagnostics;
using Aurelian.Shaders.Language.Parsing;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class SdslvParserM0Tests
{
    [Fact]
    public void SdslvParser_ParseNamespaceUseAndRecord_ProducesModule()
    {
        var result = SdslvParser.ParseModule("""
            namespace Aurelian.Shaders.Demo;
            use Aurelian.Core;
            record Vertex {
                Position: float3;
                Color: float4;
            }
            """);

        Assert.True(result.Success);
        Assert.Equal("Aurelian.Shaders.Demo", result.Module?.Namespace?.ToString());
        Assert.Equal("Aurelian.Core", result.Module?.Uses[0].Path.ToString());
        var record = Assert.IsType<SdslvRecordDecl>(Assert.Single(result.Module!.Declarations));
        Assert.Equal("Vertex", record.Name);
        Assert.Equal("float4", record.Fields[1].TypeName.ToDisplayString());
    }

    [Fact]
    public void SdslvParser_ParseStream_ProducesStreamDecl()
    {
        var result = SdslvParser.ParseModule("stream SpriteVertex { Position: float3; TexCoord: float2; }");

        Assert.True(result.Success);
        var stream = Assert.IsType<SdslvStreamDecl>(Assert.Single(result.Module!.Declarations));
        Assert.Equal("SpriteVertex", stream.Name);
        Assert.Equal("TexCoord", stream.Fields[1].Name);
    }

    [Fact]
    public void SdslvParser_ParseEnum_ProducesEnumDecl()
    {
        var result = SdslvParser.ParseModule("enum LightKind { Directional; Point, Spot; }");

        Assert.True(result.Success);
        var enumDecl = Assert.IsType<SdslvEnumDecl>(Assert.Single(result.Module!.Declarations));
        Assert.Equal(["Directional", "Point", "Spot"], enumDecl.Variants.Select(variant => variant.Name).ToArray());
    }

    [Fact]
    public void SdslvParser_ParseShaderShell_ProducesShaderDecl()
    {
        var result = SdslvParser.ParseModule("shader Basic<T> implements IVertexShader { material Color: float4; }");

        Assert.True(result.Success);
        var shader = Assert.IsType<SdslvShaderDecl>(Assert.Single(result.Module!.Declarations));
        Assert.Equal("Basic", shader.Name);
        Assert.Equal("T", Assert.Single(shader.GenericParameters));
        Assert.Equal("IVertexShader", Assert.Single(shader.Implements).ToString());
        Assert.Equal("Color", Assert.Single(shader.MaterialFields).Name);
    }

    [Fact]
    public void SdslvParser_ParseNamedAndArrayTypeRefs_ProducesTypeRefs()
    {
        var result = SdslvParser.ParseModule("""
            type Color = Aurelian.Rendering.Color;
            record Palette { Colors: array<float4, 4>; }
            """);

        Assert.True(result.Success);
        var alias = Assert.IsType<SdslvTypeAliasDecl>(result.Module!.Declarations[0]);
        Assert.Equal("Aurelian.Rendering.Color", alias.TargetType.ToDisplayString());
        var record = Assert.IsType<SdslvRecordDecl>(result.Module.Declarations[1]);
        var array = Assert.IsType<SdslvArrayTypeRef>(record.Fields[0].TypeName);
        Assert.Equal("array<float4, 4>", array.ToDisplayString());
    }

    [Fact]
    public void SdslvParser_InvalidSyntax_ReturnsDiagnosticsWithoutThrowing()
    {
        var exception = Record.Exception(() => SdslvParser.ParseModule("record { Field float4 }") );
        var result = SdslvParser.ParseModule("record { Field float4 }");

        Assert.Null(exception);
        Assert.NotNull(result.Module);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == SdslvDiagnosticSeverity.Error);
    }

    [Fact]
    public void SdslvParser_DoesNotAcceptUnsupportedKeywordAsNativeConstruct()
    {
        var result = SdslvParser.ParseModule("mixin LegacyThing;");

        Assert.False(result.Success);
        Assert.Empty(result.Module!.Declarations);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Phase == SdslvDiagnosticPhase.Parsing);
    }

    [Fact]
    public void SdslvParser_ParseStageFunctionShell_ProducesFunctionDecl()
    {
        var result = SdslvParser.ParseModule("""
            shader Basic {
                stage VertexMain(input: Vertex): VertexOut {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        var shader = Assert.IsType<SdslvShaderDecl>(Assert.Single(result.Module!.Declarations));
        var stage = Assert.Single(shader.StageMethods);
        Assert.Equal("VertexMain", stage.Name);
        Assert.Equal("VertexOut", stage.ReturnType.ToDisplayString());
        Assert.IsType<SdslvReturnStatement>(Assert.Single(stage.Body!.Statements));
    }

    [Fact]
    public void SdslvParser_ParseSimpleBinaryExpression_ProducesBinaryExpression()
    {
        var result = SdslvParser.ParseModule("""
            shader Basic {
                fn Compute(value: float): float {
                    return value + 1.0;
                }
            }
            """);

        Assert.True(result.Success);
        var shader = Assert.IsType<SdslvShaderDecl>(Assert.Single(result.Module!.Declarations));
        var statement = Assert.IsType<SdslvReturnStatement>(Assert.Single(Assert.Single(shader.Methods).Body!.Statements));
        var binary = Assert.IsType<SdslvBinaryExpression>(statement.Value);
        Assert.Equal(SdslvBinaryOperator.Add, binary.Operator);
    }
}
