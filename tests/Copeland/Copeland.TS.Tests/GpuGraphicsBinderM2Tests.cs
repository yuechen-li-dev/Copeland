using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class GpuGraphicsBinderM2Tests
{
    private const string GraphicsSource = """
        stream VertexInput {
            @location(0)
            position: float3;
            @location(1)
            uv: float2;
        }

        stream VertexOutput {
            @builtin(position)
            position: float4;
            @location(0)
            uv: float2;
        }

        stream PixelInput {
            @location(0)
            uv: float2;
        }

        stream PixelOutput {
            @target(0)
            color: float4;
        }

        function PassUv(value: float2): float2 {
            return value;
        }

        @vertex
        function VertexMain(input: VertexInput): VertexOutput {
            return {
                position: float4(input.position, 1.0),
                uv: PassUv(input.uv),
            };
        }

        @pixel
        function PixelMain(input: PixelInput): PixelOutput {
            return {
                color: float4(PassUv(input.uv), 0.0, 1.0),
            };
        }
        """;

    [Fact]
    public void Parser_Distinguishes_Shader_Stream_From_Layout_Stream_And_Retains_Provenance()
    {
        SyntaxTree shaderTree = SyntaxTree.Parse(GraphicsSource, "graphics.v.ts");
        SyntaxTree layoutTree = SyntaxTree.Parse("stream Page<0px, 0px> { width: 10px; }", "layout.ts");

        Assert.Empty(shaderTree.Diagnostics);
        ShaderStreamDeclarationSyntax stream = Assert.IsType<ShaderStreamDeclarationSyntax>(shaderTree.Root.Members[0]);
        Assert.Equal("location", Assert.Single(stream.Fields[0].Annotations!).NameToken.Text);
        Assert.IsType<StreamDeclarationSyntax>(layoutTree.Root.Members[0]);
    }

    [Fact]
    public void Graphics_Profile_Binds_Streams_Stages_Vectors_Linkage_And_Helper_Closure()
    {
        VdMirGraphicsModule module = Compile(GraphicsSource);

        Assert.True(module.Success, Diagnostics(module));
        Assert.Equal("vdmir.semantic.v1", module.Schema);
        Assert.Equal("graphics.m2", module.FeatureLevel);
        Assert.Equal([VdMirGraphicsStage.Vertex, VdMirGraphicsStage.Pixel], module.EntryPoints.Select(entry => entry.Stage));
        Assert.Contains(module.Types, type => type == "float2");
        Assert.Contains(module.Types, type => type == "float3");
        Assert.Contains(module.Types, type => type == "float4");
        VdMirStream output = module.Streams.Single(stream => stream.Name == "VertexOutput");
        Assert.Equal("position", output.Members[0].Builtin);
        Assert.Null(output.Members[0].Location);
        Assert.Equal(0, output.Members[1].Location);
        Assert.NotNull(output.Members[1].MetadataSource);
        Assert.Single(module.GraphicsProgram!.Varyings);
        Assert.Contains(module.Functions, function => function.Name == "PassUv");
    }

    [Fact]
    public void Ts_And_Vts_Graphics_Are_Equivalent_And_Json_Is_Deterministic()
    {
        VdMirGraphicsModule first = Compile(GraphicsSource, "graphics.v.ts");
        VdMirGraphicsModule second = Compile(GraphicsSource, "graphics.v.ts");
        VdMirGraphicsModule ts = Compile(GraphicsSource, "graphics.ts");

        Assert.Equal(Hash(VdMirJson.Serialize(first)), Hash(VdMirJson.Serialize(second)));
        Assert.Equal(
            VdMirJson.Serialize(first).Replace("graphics.v.ts", "graphics.ts", StringComparison.Ordinal),
            VdMirJson.Serialize(ts));
    }

    [Fact]
    public void Mixed_Role_Duplicate_Location_And_Duplicate_Target_Are_Rejected()
    {
        string mixed = GraphicsSource.Replace("@location(1)\n    uv: float2;", "@binding(1)\n    uv: float2;", StringComparison.Ordinal);
        string duplicateLocation = GraphicsSource.Replace("@location(1)\n    uv: float2;", "@location(0)\n    uv: float2;", StringComparison.Ordinal);
        string duplicateTarget = GraphicsSource.Replace("color: float4;", "color: float4;\n    @target(0) other: float4;", StringComparison.Ordinal);

        Assert.Contains(Compile(mixed).Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V4102");
        Assert.Contains(Compile(duplicateLocation).Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V4105" && diagnostic.RelatedSpans.Count == 1);
        Assert.Contains(Compile(duplicateTarget).Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V4108" && diagnostic.RelatedSpans.Count == 1);
    }

    [Fact]
    public void Missing_Clip_Position_And_Invalid_Stage_Builtin_Are_Rejected()
    {
        string missing = GraphicsSource.Replace("@builtin(position)\n    position: float4;", "@location(2)\n    position: float4;", StringComparison.Ordinal);
        string invalidBuiltin = GraphicsSource.Replace("@location(1)\n    uv: float2;\n}\n\nstream VertexOutput", "@builtin(front_face)\n    uv: float2;\n}\n\nstream VertexOutput", StringComparison.Ordinal);

        Assert.Contains(Compile(missing).Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V4106");
        Assert.Contains(Compile(invalidBuiltin).Diagnostics, diagnostic => diagnostic.CanonicalCode == "SDSL-V4109");
    }

    [Fact]
    public void Missing_And_Mismatched_Varyings_Are_Rejected_With_Source_Evidence()
    {
        string missing = GraphicsSource.Replace("@location(0)\n    uv: float2;\n}\n\nstream PixelOutput", "@location(2)\n    uv: float2;\n}\n\nstream PixelOutput", StringComparison.Ordinal);
        string mismatch = GraphicsSource.Replace("stream PixelInput {\n    @location(0)\n    uv: float2;", "stream PixelInput {\n    @location(0)\n    uv: float3;", StringComparison.Ordinal);

        Assert.Contains(Compile(missing).Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-LINK-0001");
        VdMirDiagnostic diagnostic = Assert.Single(Compile(mismatch).Diagnostics, item => item.Code == "COPE-GPU-LINK-0002");
        Assert.Single(diagnostic.RelatedSpans);
    }

    [Fact]
    public void Imported_Shared_Helper_Is_Reachable_From_Both_Stages()
    {
        const string shared = "export function PassUv(value: float2): float2 { return value; }";
        string entries = GraphicsSource.Replace("function PassUv(value: float2): float2 {\n    return value;\n}\n\n", "import { PassUv } from \"./shared\";\n\n", StringComparison.Ordinal);

        VdMirGraphicsModule module = GpuGraphicsBinder.Compile(new GpuCompilationRequest([
            new GpuSourceFile("shared.ts", shared),
            new GpuSourceFile("graphics.v.ts", entries),
        ]));

        Assert.True(module.Success, Diagnostics(module));
        Assert.Single(module.Functions, function => function.Name == "PassUv");
    }

    [Fact]
    public void Location_Inference_And_Interpolation_Metadata_Follow_Canonical_Order()
    {
        string inferred = GraphicsSource.Replace("@location(1)\n    uv: float2;", "uv: float2;", StringComparison.Ordinal);
        string flat = GraphicsSource
            .Replace("@location(0)\n    uv: float2;\n}\n\nstream PixelInput", "@location(0)\n    @interpolation(flat)\n    uv: float2;\n}\n\nstream PixelInput", StringComparison.Ordinal)
            .Replace("stream PixelInput {\n    @location(0)\n    uv: float2;", "stream PixelInput {\n    @location(0)\n    @interpolation(flat)\n    uv: float2;", StringComparison.Ordinal);
        string mismatch = flat.Replace("stream PixelInput {\n    @location(0)\n    @interpolation(flat)", "stream PixelInput {\n    @location(0)\n    @interpolation(linear)", StringComparison.Ordinal);

        VdMirGraphicsModule inferredModule = Compile(inferred);
        VdMirGraphicsModule flatModule = Compile(flat);

        Assert.True(inferredModule.Success, Diagnostics(inferredModule));
        Assert.Equal(1, inferredModule.Streams.Single(stream => stream.Name == "VertexInput").Members.Single(member => member.Name == "uv").Location);
        Assert.True(flatModule.Success, Diagnostics(flatModule));
        Assert.Equal("flat", Assert.Single(flatModule.GraphicsProgram!.Varyings).Interpolation);
        Assert.Contains(Compile(mismatch).Diagnostics, diagnostic => diagnostic.Code == "COPE-GPU-LINK-0002");
    }

    internal static VdMirGraphicsModule Compile(string source, string path = "graphics.v.ts")
        => GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile(path, source)]));

    internal static string Source => GraphicsSource;

    private static string Diagnostics(VdMirGraphicsModule module)
        => string.Join(Environment.NewLine, module.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string Hash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
