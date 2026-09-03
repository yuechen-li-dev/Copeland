using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class GpuGraphicsBinderM3Tests
{
    [Fact]
    public void ForwardTextured_Binds_Canonical_Spaces_Resources_Sample_And_Material_Layout()
    {
        VdMirGraphicsModule module = Compile(Source);

        Assert.True(module.Success, Diagnostics(module));
        Assert.Equal("graphics.m3", module.FeatureLevel);
        Assert.Equal(["clip.position", "object.position", "world.position"], module.SemanticSpaces.Select(space => space.Name));
        Assert.Equal(
            [VdMirGraphicsResourceKind.Texture2D, VdMirGraphicsResourceKind.Sampler, VdMirGraphicsResourceKind.Material],
            module.GraphicsProgram!.Resources.Select(resource => resource.Kind));
        Assert.All(module.GraphicsProgram.Resources, resource => Assert.Equal([VdMirGraphicsStage.Pixel], resource.Visibility));
        VdMirMaterial material = Assert.Single(module.Materials);
        Assert.Equal(32, material.Size);
        Assert.Collection(
            material.Fields,
            tint =>
            {
                Assert.Equal("tint", tint.Name);
                Assert.Equal((0, 16, 16), (tint.Offset, tint.Size, tint.Alignment));
            },
            roughness =>
            {
                Assert.Equal("roughness", roughness.Name);
                Assert.Equal((16, 4, 4), (roughness.Offset, roughness.Size, roughness.Alignment));
            });
        VdMirExpression intrinsic = module.Functions
            .SelectMany(function => function.Statements)
            .Where(statement => statement.Expression is not null)
            .SelectMany(statement => Descendants(statement.Expression!))
            .Single(expression => expression.Kind == "intrinsic" && expression.Value == "Sample2D");
        Assert.Equal("Sample2D", intrinsic.Value);
        Assert.Equal("float4", intrinsic.Type);
    }

    [Fact]
    public void Semantic_Space_Assignment_And_Linkage_Are_Nominal()
    {
        string assignment = Source.Replace("return float3(value.x, value.y, value.z);", "return value;", StringComparison.Ordinal);
        string erasure = Source.Replace("function EstablishWorld(value: ObjectPosition3): WorldPosition3", "function EstablishWorld(value: ObjectPosition3): float3", StringComparison.Ordinal);
        string pixelInput = """
            stream PixelInput {
                @builtin(position)
                position: ClipPosition4;
                @location(0)
                uv: float2;
                @location(1)
                worldPosition: ObjectPosition3;
            }

            """;
        string linkage = Source
            .Replace("stream PixelBuiltins", pixelInput + "stream PixelBuiltins", StringComparison.Ordinal)
            .Replace("function PixelMain(input: ForwardVaryings", "function PixelMain(input: PixelInput", StringComparison.Ordinal);

        Assert.Contains(Compile(assignment).Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V1503");
        Assert.Contains(Compile(erasure).Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V1503");
        VdMirDiagnostic diagnostic = Assert.Single(Compile(linkage).Diagnostics, item => item.CanonicalCode == "SDSL-V4111");
        Assert.Single(diagnostic.RelatedSpans);
    }

    [Fact]
    public void Sampling_Bindings_Resource_Roles_And_Material_Are_Validated()
    {
        string wrongCoordinate = Source.Replace("input.uv);", "input.position);", StringComparison.Ordinal);
        string wrongSampler = Source.Replace("resources.linearSampler, input.uv", "resources.albedo, input.uv", StringComparison.Ordinal);
        string duplicateBinding = Source.Replace("@binding(1)\n    linearSampler", "@binding(0)\n    linearSampler", StringComparison.Ordinal);
        string roleConflict = Source.Replace("@binding(0)\n    albedo", "@location(0)\n    @binding(0)\n    albedo", StringComparison.Ordinal);
        string badMaterial = Source.Replace("roughness: f32;", "roughness: bool;", StringComparison.Ordinal);
        string materialMutation = Source.Replace("const texel: float4 = Sample", "resources.material.tint = float4(1.0, 1.0, 1.0, 1.0);\n    const texel: float4 = Sample", StringComparison.Ordinal);

        Assert.Contains(Compile(wrongCoordinate).Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V4119");
        Assert.Contains(Compile(wrongSampler).Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V4118");
        Assert.Contains(Compile(duplicateBinding).Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V4112" && diagnostic.RelatedSpans.Count == 1);
        Assert.Contains(Compile(roleConflict).Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V4102");
        Assert.Contains(Compile(badMaterial).Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V4114");
        Assert.Contains(Compile(materialMutation).Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V3701" && diagnostic.RelatedSpans.Count == 1);
    }

    [Fact]
    public void Ts_And_Vts_Are_Equivalent_And_M3_Json_Is_Deterministic()
    {
        VdMirGraphicsModule first = Compile(Source, "forward-textured.v.ts");
        VdMirGraphicsModule second = Compile(Source, "forward-textured.v.ts");
        VdMirGraphicsModule ts = Compile(Source, "forward-textured.ts");

        Assert.Equal(Hash(VdMirJson.Serialize(first)), Hash(VdMirJson.Serialize(second)));
        Assert.Equal(
            VdMirJson.Serialize(first).Replace("forward-textured.v.ts", "forward-textured.ts", StringComparison.Ordinal),
            VdMirJson.Serialize(ts));
    }

    internal static string Source => File.ReadAllText(Path.Combine(RepositoryRoot(), "samples", "Aurelian", "ForwardTexturedM3.v.ts")).Replace("\r\n", "\n", StringComparison.Ordinal);

    internal static VdMirGraphicsModule Compile(string source, string path = "forward-textured.v.ts")
        => GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(path, source)]));

    private static IEnumerable<VdMirExpression> Descendants(VdMirExpression expression)
    {
        yield return expression;
        foreach (VdMirExpression operand in expression.Operands ?? [])
        {
            foreach (VdMirExpression descendant in Descendants(operand))
            {
                yield return descendant;
            }
        }
    }

    private static string RepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string Diagnostics(VdMirGraphicsModule module)
        => string.Join(Environment.NewLine, module.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string Hash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
