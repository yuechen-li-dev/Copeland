using System.Security.Cryptography;
using System.Text;
using Aurelian.Rendering.Contracts.Shaders;
using Copeland.TS.Gpu.VdMir;

namespace Aurelian.Shaders.Graphics;

public static class CompiledGraphicsProgramExporter
{
    public static CompiledGraphicsProgram Export(
        VdMirGraphicsModule module,
        VdMirGraphicsBackendResult backend)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(backend);

        if (!module.Success || module.GraphicsProgram is null)
        {
            throw new ArgumentException("A successful VD-MIR graphics module is required.", nameof(module));
        }

        if (!backend.Vertex.SpirvValidated || !backend.Pixel.SpirvValidated)
        {
            throw new ArgumentException("Both graphics stages must contain validated SPIR-V.", nameof(backend));
        }

        VdMirGraphicsProgram graphics = module.GraphicsProgram;
        var shaders = new CompiledShaderProgram(
            CompiledShaderProgram.CurrentFormatVersion,
            [
                ExportStage(backend.Vertex, CompiledShaderStageKind.Vertex),
                ExportStage(backend.Pixel, CompiledShaderStageKind.Fragment),
            ]);

        IReadOnlyList<CompiledVertexInput> vertexInputs = graphics.VertexInputs
            .OrderBy(input => input.Order)
            .Select(input => new CompiledVertexInput(
                input.Order,
                input.Name,
                input.Type,
                input.PhysicalType ?? input.Type,
                input.Location ?? throw new InvalidOperationException($"Vertex input '{input.Name}' is missing a location."),
                input.SemanticSpace))
            .ToArray();

        IReadOnlyList<CompiledPixelTarget> pixelTargets = graphics.PixelTargets
            .OrderBy(target => target.Order)
            .Select(target => new CompiledPixelTarget(
                target.Order,
                target.Name,
                target.PhysicalType ?? target.Type,
                target.Target ?? throw new InvalidOperationException($"Pixel target '{target.Name}' is missing a target index.")))
            .ToArray();

        IReadOnlyList<CompiledGraphicsResource> resources = graphics.Resources
            .OrderBy(resource => resource.Order)
            .Select(resource => new CompiledGraphicsResource(
                resource.Order,
                resource.Name,
                MapResourceKind(resource.Kind),
                resource.Type,
                resource.Set,
                resource.Binding,
                resource.Visibility.Select(MapStage).ToArray()))
            .ToArray();

        CompiledMaterialLayout? material = graphics.Material is null
            ? null
            : new CompiledMaterialLayout(
                graphics.Material.Name,
                graphics.Material.Size,
                graphics.Material.Set,
                graphics.Material.Binding,
                graphics.Material.Visibility.Select(MapStage).ToArray(),
                graphics.Material.Fields.OrderBy(field => field.Order).Select(field => new CompiledMaterialField(
                    field.Order,
                    field.Name,
                    field.PhysicalType,
                    field.Offset,
                    field.Size,
                    field.Alignment)).ToArray());

        string vdMir = VdMirJson.Serialize(module);
        return new CompiledGraphicsProgram(
            CompiledGraphicsProgram.CurrentFormatVersion,
            graphics.Name,
            module.FeatureLevel,
            "dxc-vulkan1.3",
            Hash(Encoding.UTF8.GetBytes(vdMir)),
            shaders,
            vertexInputs,
            pixelTargets,
            resources,
            material);
    }

    private static CompiledShaderStage ExportStage(VdMirGraphicsStageResult stage, CompiledShaderStageKind kind)
        => new(
            kind,
            stage.EntryPoint,
            stage.Profile,
            stage.Spirv,
            stage.SpirvSha256 ?? throw new InvalidOperationException($"{stage.Stage} SPIR-V hash is missing."),
            $"{stage.EntryPoint}.hlsl");

    private static CompiledGraphicsResourceKind MapResourceKind(VdMirGraphicsResourceKind kind)
        => kind switch
        {
            VdMirGraphicsResourceKind.Texture2D => CompiledGraphicsResourceKind.Texture2D,
            VdMirGraphicsResourceKind.Sampler => CompiledGraphicsResourceKind.Sampler,
            VdMirGraphicsResourceKind.Material => CompiledGraphicsResourceKind.UniformBuffer,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    private static CompiledGraphicsStage MapStage(VdMirGraphicsStage stage)
        => stage switch
        {
            VdMirGraphicsStage.Vertex => CompiledGraphicsStage.Vertex,
            VdMirGraphicsStage.Pixel => CompiledGraphicsStage.Fragment,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
        };

    private static string Hash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
