using Aurelian.Shaders.Language.Ast;
using Aurelian.Shaders.Language.Diagnostics;
using Aurelian.Shaders.Language.Parsing;
using Aurelian.Shaders.Language.Validation;
using Aurelian.Shaders.Language.VdMir;
using Aurelian.Shaders.Language.VdMir.Lowering;
using Xunit;

namespace Aurelian.Shaders.Tests;

public sealed class VdMirM0LowererTests
{
    [Fact]
    public void VdMirM0Lowerer_CanLowerSmokeTriangle()
    {
        var module = LowerSmokeTriangle();

        Assert.True(module.Success, FormatDiagnostics(module.Diagnostics));
        Assert.Equal(2, module.Structs.Count);
        Assert.Equal(2, module.EntryPoints.Count);
    }

    [Fact]
    public void VdMirM0Lowerer_PreservesEntryPoints()
    {
        var module = LowerSmokeTriangle();

        Assert.Collection(
            module.EntryPoints,
            vertex => Assert.Equal("VSMain", vertex.Name),
            pixel => Assert.Equal("PSMain", pixel.Name));
    }

    [Fact]
    public void VdMirM0Lowerer_PreservesStageKinds()
    {
        var module = LowerSmokeTriangle();

        Assert.Collection(
            module.EntryPoints,
            vertex => Assert.Equal(VdMirStageKind.Vertex, vertex.Stage),
            pixel => Assert.Equal(VdMirStageKind.Pixel, pixel.Stage));
    }

    [Fact]
    public void VdMirM0Lowerer_PreservesSourceProvenanceWhereAvailable()
    {
        var module = LowerSmokeTriangle();

        Assert.All(module.EntryPoints, entryPoint => Assert.NotEqual(SdslvSpan.Unknown, entryPoint.Span));
    }

    [Fact]
    public void VdMirM0Lowerer_UnsupportedShapeProducesDiagnostic()
    {
        var module = new SdslvModule(
            null,
            [],
            [
                new SdslvShaderDecl(
                    "UnsupportedShader",
                    [],
                    [],
                    [],
                    [],
                    [],
                    [
                        new SdslvFunctionDecl(
                            false,
                            "VS",
                            "VSMain",
                            [new SdslvFunctionParameter("input", new SdslvNamedTypeRef(new SdslvPath("VertexInput")))],
                            new SdslvNamedTypeRef(new SdslvPath("VertexOutput")),
                            null,
                            new SdslvBody(
                                [
                                    new SdslvIfStatement(
                                        new SdslvBoolLiteralExpression(true),
                                        [new SdslvReturnStatement(new SdslvIdentifierExpression("input"))],
                                        null,
                                        new SdslvSpan(1, 2, 1, 1)),
                                ],
                                new SdslvSpan(1, 2, 1, 1))),
                    ]),
            ]);

        var lowered = VdMirM0Lowerer.LowerModule(module);

        Assert.False(lowered.Success);
        Assert.Contains(lowered.Diagnostics, diagnostic =>
            diagnostic.Code == VdMirDiagnosticCodes.UnsupportedStatement &&
            diagnostic.Severity == SdslvDiagnosticSeverity.Error);
    }

    private static VdMirModule LowerSmokeTriangle()
    {
        var source = ReadFixture("smoke_triangle.sdslv");
        var parse = SdslvParser.ParseModule(source);
        Assert.True(parse.Success, FormatSourceDiagnostics(parse.Diagnostics));

        var validation = SdslvValidator.ValidateModule(parse.Module!);
        Assert.True(validation.Success, FormatSourceDiagnostics(validation.Diagnostics));

        return VdMirM0Lowerer.LowerModule(parse.Module!);
    }

    private static string ReadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sdslv", name);
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "Sdslv", name));
        return File.ReadAllText(path);
    }

    private static string FormatDiagnostics(IEnumerable<VdMirDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatSourceDiagnostics(IEnumerable<SdslvDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
