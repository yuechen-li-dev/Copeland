using Aurelian.Shaders.Language.Diagnostics;
using Aurelian.Shaders.Language.Parsing;
using Aurelian.Shaders.Language.Validation;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class SdslvValidationM0Tests
{
    [Fact]
    public void SdslvValidator_ValidRecordStreamEnumShaderModule_Succeeds()
    {
        var validation = ParseAndValidate("""
            namespace Demo.Shaders;
            use Demo.Core;

            record VertexIn {
                Position: float3;
                Color: float4;
            }

            stream PixelOut {
                Color: float4;
            }

            enum LightKind {
                Directional;
                Point;
            }

            type Color = float4;

            shader Basic<T> {
                material {
                    Tint: Color;
                    Weights: array<float, 4>;
                }

                fn Compute(input: VertexIn) -> PixelOut {
                    let local: Color = Tint;
                    return input;
                }

                stage pixel fn Main(input: VertexIn) -> PixelOut {
                    return input;
                }
            }
            """);

        Assert.True(validation.Success, FormatDiagnostics(validation.Diagnostics));
    }

    [Fact]
    public void SdslvValidator_DuplicateTopLevelDeclaration_ReportsDiagnostic()
    {
        var validation = ParseAndValidate("""
            record Surface { Color: float4; }
            stream Surface { Color: float4; }
            """);

        AssertDiagnostic(validation, SdslvValidator.DuplicateDeclarationCode);
    }

    [Fact]
    public void SdslvValidator_DuplicateRecordField_ReportsDiagnostic()
    {
        var validation = ParseAndValidate("record Surface { Color: float4; Color: float3; }");

        AssertDiagnostic(validation, SdslvValidator.DuplicateFieldCode);
    }

    [Fact]
    public void SdslvValidator_DuplicateStreamField_ReportsDiagnostic()
    {
        var validation = ParseAndValidate("stream Surface { Color: float4; Color: float3; }");

        AssertDiagnostic(validation, SdslvValidator.DuplicateFieldCode);
    }

    [Fact]
    public void SdslvValidator_DuplicateEnumVariant_ReportsDiagnostic()
    {
        var validation = ParseAndValidate("enum LightKind { Point; Point; }");

        AssertDiagnostic(validation, SdslvValidator.DuplicateEnumVariantCode);
    }

    [Fact]
    public void SdslvValidator_DuplicateShaderGeneric_ReportsDiagnostic()
    {
        var validation = ParseAndValidate("shader Basic<T, T> { fn Compute(value: T) -> T { return value; } }");

        AssertDiagnostic(validation, SdslvValidator.DuplicateGenericParameterCode);
    }

    [Fact]
    public void SdslvValidator_DuplicateShaderMaterialField_ReportsDiagnostic()
    {
        var validation = ParseAndValidate("""
            shader Basic {
                material { Color: float4; Color: float3; }
            }
            """);

        AssertDiagnostic(validation, SdslvValidator.DuplicateFieldCode);
    }

    [Fact]
    public void SdslvValidator_DuplicateShaderMethod_ReportsDiagnostic()
    {
        var validation = ParseAndValidate("""
            shader Basic {
                fn Compute(value: int) -> int { return value; }
                fn Compute(value: int) -> int { return value; }
            }
            """);

        AssertDiagnostic(validation, SdslvValidator.DuplicateShaderMemberCode);
    }

    [Fact]
    public void SdslvValidator_UnknownTypeReference_ReportsDiagnostic()
    {
        var validation = ParseAndValidate("record Surface { Missing: UnknownType; }");

        AssertDiagnostic(validation, SdslvValidator.UnknownTypeCode);
    }

    [Fact]
    public void SdslvValidator_InvalidArrayLength_ReportsDiagnostic()
    {
        var validation = ParseAndValidate("record Surface { Values: array<float, 0>; }");

        AssertDiagnostic(validation, SdslvValidator.InvalidArrayLengthCode);
    }

    [Fact]
    public void SdslvValidator_DuplicateLocalInSameFunctionScope_ReportsDiagnostic()
    {
        var validation = ParseAndValidate("""
            shader Basic {
                fn Compute(value: int) -> int {
                    let local: int = 1;
                    let local: int = 2;
                    return value;
                }
            }
            """);

        AssertDiagnostic(validation, SdslvValidator.DuplicateLocalCode);
    }

    private static Aurelian.Shaders.Language.Validation.SdslvValidationResult ParseAndValidate(string source)
    {
        var parse = SdslvParser.ParseModule(source);
        Assert.True(parse.Success, FormatDiagnostics(parse.Diagnostics));
        return SdslvValidator.ValidateModule(parse.Module!);
    }

    private static void AssertDiagnostic(Aurelian.Shaders.Language.Validation.SdslvValidationResult validation, string code)
    {
        Assert.False(validation.Success);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Code == code &&
            diagnostic.Phase == SdslvDiagnosticPhase.Validation &&
            diagnostic.Severity == SdslvDiagnosticSeverity.Error);
    }

    private static string FormatDiagnostics(IEnumerable<SdslvDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
