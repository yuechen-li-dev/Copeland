using System.Numerics;
using System.Runtime.InteropServices;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Draw;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.Graphics;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Lighting;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>
/// Bounded opaque triangle renderer with a camera uniform and D32 depth buffer.
/// Uses the shared native color target, draw encoder, allocator, and submission path.
/// </summary>
public sealed unsafe class VulkanSolid3DRenderer : IDisposable
{
    private const int VertexStride = 40;
    private const int CameraBytes = 240;
    private const int MaximumVertices = 65_536;
    private readonly AurelianVulkanPlant plant;
    private readonly VulkanNativeFrameTarget target;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commandPool;
    private readonly VulkanCommandSubmitter submitter;
    private readonly AurelianVulkanTexture depth;
    private readonly AurelianVulkanRenderPass renderPass;
    private readonly AurelianVulkanFramebuffer framebuffer;
    private readonly AurelianVulkanGraphicsPipeline pipeline;
    private readonly AurelianVulkanBuffer vertices;
    private readonly AurelianVulkanBuffer camera;
    private readonly DescriptorSetLayout setLayout;
    private readonly DescriptorPool descriptorPool;
    private readonly DescriptorSet descriptorSet;
    private readonly VulkanModel3DBatches? modelRenderer;
    private readonly AurelianVulkanTexture? hdr;
    private readonly AurelianVulkanTexture shadowMap;
    private readonly Sampler shadowSampler;
    private readonly Vulkan3DPass? shadowPass;
    private readonly Vulkan3DPass? outputPass;
    private readonly AurelianVulkanBuffer? outputVertices;
    private readonly Vulkan3DGpuTimings? gpuTimings;
    private bool disposed;
    private readonly bool supportsDiffuseData;
    private AurelianVulkanTexture? diffuseData;
    private StaticDiffuseLighting? diffuseAsset;
    private bool diffusePublished;
    public float StaticEmissionIntensity { get; set; } = 1;
    public string? StaticDiffuseFallbackReason { get; private set; } = "NoPublishedLightingAsset";

    public Graphics3DSettings Settings { get; set; } = Graphics3DSettings.Default;

    public VulkanSolid3DRenderer(
        AurelianVulkanPlant plant,
        CompiledGraphicsProgram program,
        VulkanNativeFrameTarget target,
        bool enableDepth = true,
        CompiledGraphicsProgram? modelProgram = null,
        CompiledGraphicsProgram? shadowProgram = null,
        CompiledGraphicsProgram? outputProgram = null)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(target);
        target.ValidateExternalPass(plant);
        ValidateProgram(program);
        supportsDiffuseData = program.Resources.Count == 5;
        if ((shadowProgram is null) != (outputProgram is null))
        {
            throw new ArgumentException("The modern 3D path requires both shadow and output shaders.");
        }
        this.plant = plant;
        this.target = target;
        allocator = new RawVulkanMemoryAllocator(plant);
        fences = VulkanFenceBundle.Create(plant);
        commandPool = VulkanCommandBufferPool.Create(plant);
        submitter = new VulkanCommandSubmitter(plant, commandPool, fences);
        try
        {
            if (outputProgram is not null)
            {
                ValidateModernFormats();
                gpuTimings = new(plant);
                hdr = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, target.Width, target.Height,
                    VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferSource,
                    VulkanMemoryUsage.GpuOnly, "solid3d.hdr", VulkanTextureFormat.Rgba16Float);
                shadowMap = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, Lighting3DUniforms.ShadowSize, Lighting3DUniforms.ShadowSize,
                    VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferSource,
                    VulkanMemoryUsage.GpuOnly, "solid3d.shadow", VulkanTextureFormat.R32Float);
                shadowPass = new(plant, allocator, shadowProgram!, shadowMap, Lighting3DUniforms.ShadowSize, Lighting3DUniforms.ShadowSize, true);
                outputPass = new(plant, allocator, outputProgram, target.Texture, target.Width, target.Height, false, hdr);
                outputVertices = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator, 24,
                    VulkanBufferUsage.Vertex, VulkanMemoryUsage.CpuToGpu, "solid3d.output-triangle");
                float[] triangle = [-1, -1, 3, -1, -1, 3];
                Require(outputVertices.Write(MemoryMarshal.AsBytes(triangle.AsSpan())).Success, "Output triangle upload failed.");
            }
            else
            {
                Settings = Graphics3DSettings.Basic;
                shadowMap = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, 1, 1,
                    VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination, VulkanMemoryUsage.GpuOnly,
                    "solid3d.no-shadow");
                using var uploader = new Aurelian.Graphics.Vulkan.Resources.Uploads.VulkanTextureUploader(plant, allocator, commandPool, fences);
                Require(uploader.Upload(new(shadowMap, new byte[] { 255, 255, 255, 255 }, "solid3d.no-shadow")).Success,
                    "Fallback shadow texture upload failed.");
            }
            shadowSampler = Vulkan3DPass.CreateSampler(plant, Filter.Nearest);
            depth = VulkanNativeForwardTexturedRenderer.CreateTexture(
                plant, allocator, target.Width, target.Height,
                VulkanTextureUsage.DepthAttachment, VulkanMemoryUsage.GpuOnly, "solid3d.depth", VulkanTextureFormat.D32Float);
            var passResult = VulkanRenderPassFactory.Create(plant, new VulkanRenderPassDescriptor(
                [new VulkanRenderPassAttachmentDescriptor(
                    "solid3d.color", hdr?.Format ?? target.TextureFormat, VulkanAttachmentLoadOp.Clear,
                    VulkanAttachmentStoreOp.Store, VulkanResourceLayout.Undefined,
                    hdr is null ? VulkanResourceLayout.TransferSource : VulkanResourceLayout.ShaderResourceFragment)],
                new VulkanRenderPassAttachmentDescriptor(
                    "solid3d.depth", VulkanTextureFormat.D32Float, VulkanAttachmentLoadOp.Clear,
                    VulkanAttachmentStoreOp.DontCare, VulkanResourceLayout.Undefined, VulkanResourceLayout.DepthStencilAttachment)));
            Require(passResult.Success, string.Join("; ", passResult.Diagnostics.Select(item => item.Message)));
            renderPass = passResult.RenderPass!;
            var framebufferResult = VulkanFramebufferFactory.Create(plant, renderPass,
                new VulkanFramebufferDescriptor(target.Width, target.Height, [hdr ?? target.Texture], depth));
            Require(framebufferResult.Success, string.Join("; ", framebufferResult.Diagnostics.Select(item => item.Message)));
            framebuffer = framebufferResult.Framebuffer!;
            setLayout = VulkanNativeForwardTexturedRenderer.CreateDescriptorSetLayout(plant, program);
            var descriptorResult = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(
                program.Shaders,
                [new VulkanVertexBufferLayoutDescriptor(0, VertexStride)],
                [
                    new VulkanVertexAttributeDescriptor(0, 0, VulkanVertexAttributeFormat.Float3, 0),
                    new VulkanVertexAttributeDescriptor(1, 0, VulkanVertexAttributeFormat.Float3, 12),
                    new VulkanVertexAttributeDescriptor(2, 0, VulkanVertexAttributeFormat.Float4, 24),
                ],
                enableDepthTest: enableDepth,
                enableDepthWrite: enableDepth);
            Require(descriptorResult.Success, string.Join("; ", descriptorResult.Diagnostics.Select(item => item.Message)));
            var pipelineResult = VulkanGraphicsPipelineFactory.Create(plant, renderPass, descriptorResult.Descriptor!, [setLayout]);
            Require(pipelineResult.Success, string.Join("; ", pipelineResult.Diagnostics.Select(item => item.Message)));
            pipeline = pipelineResult.Pipeline!;
            vertices = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(
                plant, allocator, MaximumVertices * VertexStride, VulkanBufferUsage.Vertex,
                VulkanMemoryUsage.CpuToGpu, "solid3d.vertices");
            camera = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(
                plant, allocator, CameraBytes, VulkanBufferUsage.Uniform, VulkanMemoryUsage.CpuToGpu, "solid3d.camera");
            (descriptorPool, descriptorSet) = CreateCameraDescriptor();
            if (modelProgram is not null)
                modelRenderer = new(plant, allocator, commandPool, fences, renderPass, modelProgram, shadowMap, shadowSampler);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Prepare(StaticModel model)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (modelRenderer is null) throw new InvalidOperationException("StaticModel3D shader was not configured.");
        modelRenderer.Prepare(model);
    }

    public Native3DFrameResult Render(Native3DScene scene, Matrix4x4 worldToClip, Vector3 eye,
        NativeFrameClearColor clearColor, bool capture = false, NativeGpuGeometry3D? gpuGeometry = null)
    {
        return Render(scene.Geometry, worldToClip, clearColor, capture, scene.Models, eye, gpuGeometry);
    }

    public Native3DFrameResult Render(
        ReadOnlySpan<Native3DVertex> geometry,
        Matrix4x4 worldToClip,
        NativeFrameClearColor clearColor,
        bool capture = false,
        IReadOnlyList<NativeModel3DBatch>? models = null,
        Vector3 eye = default,
        NativeGpuGeometry3D? gpuGeometry = null,
        Vector3? lightDirection = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        target.ValidateExternalPass(plant);
        models ??= [];
        if ((geometry.IsEmpty && models.Count == 0 && gpuGeometry is null) || geometry.Length % 3 != 0 || geometry.Length > MaximumVertices)
        {
            throw new ArgumentException("Geometry must contain 1..65536 vertices in complete triangles.", nameof(geometry));
        }
        gpuGeometry?.Validate(plant);
        Graphics3DSettings settings = Settings ?? throw new InvalidOperationException("3D settings are required.");
        if (lightDirection is not null)
        {
            settings = settings with { SunDirection = lightDirection.Value };
        }
        settings.Validate();
        if (!float.IsFinite(StaticEmissionIntensity) || StaticEmissionIntensity < 0)
            throw new ArgumentOutOfRangeException(nameof(StaticEmissionIntensity));
        bool useDiffuse = diffusePublished && diffuseAsset is not null && settings.SolidPbr
            && settings.SolidMetallic == 0
            && Vector3.Distance(Vector3.Normalize(settings.SunDirection), diffuseAsset.SunDirection) < .00001f;
        if (useDiffuse)
        {
            StaticDiffuseFallbackReason = null;
        }
        else if (diffusePublished)
        {
            StaticDiffuseFallbackReason = "UnsupportedMaterialOrChangedSunDirection";
        }
        else
        {
            StaticDiffuseFallbackReason = "NoPublishedLightingAsset";
        }
        if (outputPass is null && (settings.Shadows || settings.ToneMapping || settings.Exposure != 1))
        {
            throw new InvalidOperationException("Shadows, tone mapping and exposure require the Shadow3D and ToneMap3D shaders at renderer creation.");
        }
        if (!Finite(eye))
        {
            throw new ArgumentException("Camera eye must be finite.", nameof(eye));
        }
        Vector3 light = Vector3.Normalize(settings.SunDirection);
        if (!Finite(light) || light.LengthSquared() < .000001f || light.LengthSquared() > 1.0001f)
        {
            throw new ArgumentException("Light direction must be finite, nonzero and have length at most one.", nameof(lightDirection));
        }
        foreach (Native3DVertex vertex in geometry)
        {
            if (!Finite(vertex.Position) || !Finite(vertex.Normal) || !Finite(vertex.Color))
            {
                throw new ArgumentException("3D vertices must be finite.", nameof(geometry));
            }
        }
        Matrix4x4 shadowCamera = Lighting3DUniforms.ShadowCamera(settings, eye);
        float[] lighting = Lighting3DUniforms.Scene(settings, shadowCamera, shadowPass is not null);
        float[] cameraRows =
        [
            .. Lighting3DUniforms.Rows(worldToClip),
            .. lighting[..4],
            eye.X, eye.Y, eye.Z, 1,
            .. lighting[4..16],
            settings.SolidRoughness, settings.SolidMetallic, settings.SolidPbr ? 1 : 0, useDiffuse ? 1 : 0,
            .. lighting[16..],
        ];
        cameraRows[31] = StaticEmissionIntensity;
        if (cameraRows.Any(value => !float.IsFinite(value)))
        {
            throw new ArgumentException("Camera transform must be finite.", nameof(worldToClip));
        }
        float[] channels = [clearColor.Red, clearColor.Green, clearColor.Blue, clearColor.Alpha];
        if (channels.Any(value => !float.IsFinite(value) || value < 0 || value > 1))
        {
            throw new ArgumentOutOfRangeException(nameof(clearColor));
        }
        if (!geometry.IsEmpty) Require(vertices.Write(MemoryMarshal.AsBytes(geometry)).Success, "Vertex upload failed.");
        Require(camera.Write(MemoryMarshal.AsBytes(cameraRows.AsSpan())).Success, "Camera upload failed.");
        if (models.Count > 0)
        {
            if (modelRenderer is null) throw new InvalidOperationException("StaticModel3D shader was not configured.");
        }
        modelRenderer?.Upload(models, worldToClip, eye, lighting);
        VulkanCommandBufferLease command = commandPool.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "3D command begin failed.");
        gpuTimings?.Reset(command);
        gpuTimings?.Mark(command, 0);
        var encoder = new VulkanRenderPassCommandEncoder();
        if (shadowPass is not null)
        {
            shadowPass.Upload(Lighting3DUniforms.Rows(shadowCamera));
            var shadowBegin = encoder.Begin(plant, command, new(shadowPass.Pass, shadowPass.Framebuffer, new(1, 1, 1, 1)));
            Require(shadowBegin.Success, "Shadow pass begin failed.");
            if (settings.Shadows)
            {
                if (!geometry.IsEmpty)
                {
                    shadowPass.Draw(command, shadowBegin.Scope!.Value, vertices, (uint)geometry.Length);
                }
                if (gpuGeometry is not null)
                {
                    shadowPass.Draw(command, shadowBegin.Scope!.Value, gpuGeometry.Buffer, gpuGeometry.VertexCount);
                }
                modelRenderer?.DrawShadows(command, shadowBegin.Scope!.Value, shadowPass, models);
            }
            Require(encoder.End(plant, command, shadowBegin.Scope!.Value).Success, "Shadow pass end failed.");
            Vulkan3DPass.SampleAfterRendering(plant, command, shadowMap);
        }
        gpuTimings?.Mark(command, 1);
        gpuTimings?.Mark(command, 2);
        var begin = encoder.Begin(plant, command, new VulkanRenderPassBeginRequest(
            renderPass, framebuffer, hdr is null ? target.PrepareClearColor(clearColor)
                : new VulkanColorClearValue(NativeSrgbTransfer.Decode(clearColor.Red), NativeSrgbTransfer.Decode(clearColor.Green),
                    NativeSrgbTransfer.Decode(clearColor.Blue), clearColor.Alpha)));
        Require(begin.Success, "3D render pass begin failed.");
        DescriptorSet set = descriptorSet;
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Graphics,
            pipeline.NativePipelineLayout, 0, 1, &set, 0, null);
        if (!geometry.IsEmpty)
        {
            var draw = new VulkanDrawCommandEncoder().DrawVertices(plant, command, begin.Scope!.Value,
                new VulkanDrawVerticesRequest(pipeline, vertices, (uint)geometry.Length, 0,
                    VulkanViewportScissor.FromFramebuffer(framebuffer)));
            Require(draw.Success, string.Join("; ", draw.Diagnostics.Select(item => item.Message)));
        }
        if (models.Count > 0) modelRenderer!.Draw(command, begin.Scope!.Value, framebuffer, models);
        if (gpuGeometry is not null)
        {
            DescriptorSet cameraSet = descriptorSet;
            plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Graphics,
                pipeline.NativePipelineLayout, 0, 1, &cameraSet, 0, null);
            var draw = new VulkanDrawCommandEncoder().DrawVertices(plant, command, begin.Scope!.Value,
                new VulkanDrawVerticesRequest(pipeline, gpuGeometry.Buffer, gpuGeometry.VertexCount, 0,
                    VulkanViewportScissor.FromFramebuffer(framebuffer)));
            Require(draw.Success, string.Join("; ", draw.Diagnostics.Select(item => item.Message)));
        }
        Require(encoder.End(plant, command, begin.Scope!.Value).Success, "3D render pass end failed.");
        gpuTimings?.Mark(command, 3);
        gpuTimings?.Mark(command, 4);
        if (outputPass is not null)
        {
            Vulkan3DPass.SampleAfterRendering(plant, command, hdr!);
            bool encodeSrgb = target.TextureFormat is not (VulkanTextureFormat.Rgba8Srgb or VulkanTextureFormat.Bgra8Srgb);
            outputPass.Upload([settings.Exposure, settings.ToneMapping ? 1 : 0, encodeSrgb ? 1 : 0, 0]);
            var outputBegin = encoder.Begin(plant, command, new(outputPass.Pass, outputPass.Framebuffer, new(0, 0, 0, 1)));
            Require(outputBegin.Success, "Tone mapping pass begin failed.");
            outputPass.Draw(command, outputBegin.Scope!.Value, outputVertices!, 3);
            Require(encoder.End(plant, command, outputBegin.Scope!.Value).Success, "Tone mapping pass end failed.");
        }
        gpuTimings?.Mark(command, 5);
        Require(command.End().Success, "3D command end failed.");
        var submit = submitter.Submit(new VulkanCommandSubmitRequest(command,
            WaitForCompletion: true, TimeoutNanoseconds: 5_000_000_000, DebugName: "solid3d.draw"));
        Require(submit.Success, string.Join("; ", submit.Diagnostics.Select(item => item.Message)));
        IReadOnlyList<Native3DGpuPassTime> times = gpuTimings?.Read() ?? [];
        int triangleCount = (geometry.Length + models.Sum(item => item.Vertices.Length) + (int)(gpuGeometry?.VertexCount ?? 0)) / 3;
        if (capture)
        {
            var readback = target.Capture();
            return new Native3DFrameResult(triangleCount, readback.Pixels, readback.Hash) { GpuPassTimes = times };
        }
        return new Native3DFrameResult(triangleCount, null, null) { GpuPassTimes = times };
    }

    private (DescriptorPool Pool, DescriptorSet Set) CreateCameraDescriptor()
    {
        var descriptor = Vulkan3DPass.AllocateSet(plant, setLayout, camera, supportsDiffuseData ? 2u : 1u);
        Vulkan3DPass.WriteImage(plant, descriptor.Set, 1, shadowMap, shadowSampler);
        if (supportsDiffuseData) Vulkan3DPass.WriteImage(plant, descriptor.Set, 3, shadowMap, shadowSampler);
        return descriptor;
    }

    /// <summary>Upload completes before returning. Publication remains the controller's decision.</summary>
    public ulong UploadStaticDiffuseLighting(StaticDiffuseLighting asset)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!supportsDiffuseData) throw new NotSupportedException("Solid shader has no compiled diffuse resource bindings.");
        plant.Vk.GetPhysicalDeviceFormatProperties(plant.PhysicalDevice, Format.R32G32B32A32Sfloat, out var properties);
        if ((properties.OptimalTilingFeatures & FormatFeatureFlags.SampledImageBit) == 0)
            throw new NotSupportedException("Device cannot sample RGBA32F lighting data.");
        float[] data = asset.TextureData();
        var replacement = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, 32, (uint)(data.Length / 128),
            VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination, VulkanMemoryUsage.GpuOnly,
            "solid3d.compiled-diffuse", VulkanTextureFormat.Rgba32Float);
        try
        {
            using var uploader = new Aurelian.Graphics.Vulkan.Resources.Uploads.VulkanTextureUploader(plant, allocator, commandPool, fences);
            var uploaded = uploader.Upload(new(replacement, MemoryMarshal.AsBytes(data.AsSpan()).ToArray(), "compiled-diffuse"));
            Require(uploaded.Success, string.Join("; ", uploaded.Diagnostics.Select(item => item.Message)));
            // Render and uploader wait for their submissions. No previous draw can retain this descriptor/image.
            Vulkan3DPass.WriteImage(plant, descriptorSet, 3, replacement, shadowSampler);
            diffuseData?.Dispose();
            diffuseData = replacement;
            diffuseAsset = asset;
            diffusePublished = false;
            return uploaded.SignalFenceValue!.Value;
        }
        catch
        {
            replacement.Dispose();
            throw;
        }
    }

    public void PublishStaticDiffuseLighting(bool published) => diffusePublished = published && diffuseAsset is not null;

    private void ValidateModernFormats()
    {
        var required = new (Format Format, FormatFeatureFlags Features)[]
        {
            (Format.R16G16B16A16Sfloat, FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.SampledImageBit | FormatFeatureFlags.SampledImageFilterLinearBit),
            (Format.R32Sfloat, FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.SampledImageBit),
        };
        foreach (var format in required)
        {
            plant.Vk.GetPhysicalDeviceFormatProperties(plant.PhysicalDevice, format.Format, out FormatProperties properties);
            if ((properties.OptimalTilingFeatures & format.Features) != format.Features)
            {
                throw new NotSupportedException($"Modern 3D rendering requires {format.Features} for {format.Format} on {plant.Facts.PhysicalDeviceName}.");
            }
        }
    }

    private static void ValidateProgram(CompiledGraphicsProgram program)
    {
        CompiledVertexInput[] inputs = program.VertexInputs.OrderBy(input => input.Order).ToArray();
        string[] expectedFields = ["clipX", "clipY", "clipZ", "clipW", "light", "eye", "sun", "sky", "ground", "surface",
            "shadowX", "shadowY", "shadowZ", "shadowW", "shadowParameters"];
        bool valid = inputs.Length == 3
            && inputs[0].Location == 0 && inputs[0].PhysicalType == "float3"
            && inputs[1].Location == 1 && inputs[1].PhysicalType == "float3"
            && inputs[2].Location == 2 && inputs[2].PhysicalType == "float4"
            && program.Resources.Count is 3 or 5
            && program.Resources[0].Set == 0 && program.Resources[0].Binding == 0
            && program.Resources[0].Kind == CompiledGraphicsResourceKind.UniformBuffer
            && program.Resources[0].Visibility.Order().SequenceEqual(new[] { CompiledGraphicsStage.Vertex, CompiledGraphicsStage.Fragment }.Order())
            && program.Material is { Size: CameraBytes } material
            && material.Fields.Count == 15
            && material.Fields.Select(field => field.Name).SequenceEqual(expectedFields)
            && material.Fields.All(field => field.PhysicalType == "float4")
            && material.Fields.Select(field => field.Offset).SequenceEqual(Enumerable.Range(0, 15).Select(index => index * 16));
        if (!valid)
        {
            throw new ArgumentException("Solid3D requires position/normal/color, the 240-byte scene uniform and shadow texture/sampler.", nameof(program));
        }
        CompiledGraphicsResource[] sampled = program.Resources.Where(item => item.Binding != 0).OrderBy(item => item.Binding).ToArray();
        if (sampled[0].Binding != 1 || sampled[0].Set != 0 || sampled[0].Kind != CompiledGraphicsResourceKind.Texture2D
            || sampled[1].Binding != 2 || sampled[1].Set != 0 || sampled[1].Kind != CompiledGraphicsResourceKind.Sampler
            || sampled.Any(item => !item.Visibility.SequenceEqual([CompiledGraphicsStage.Fragment])))
        {
            throw new ArgumentException("Solid3D shadow texture/sampler must be fragment-visible bindings 1 and 2.", nameof(program));
        }
        if (sampled.Length == 4 && (sampled[2].Binding != 3 || sampled[2].Set != 0 || sampled[2].Kind != CompiledGraphicsResourceKind.Texture2D
            || sampled[3].Binding != 4 || sampled[3].Set != 0 || sampled[3].Kind != CompiledGraphicsResourceKind.Sampler))
            throw new ArgumentException("Compiled diffuse texture/sampler require bindings 3 and 4.", nameof(program));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        _ = plant.Vk.DeviceWaitIdle(plant.Device);
        gpuTimings?.Dispose();
        modelRenderer?.Dispose();
        outputPass?.Dispose();
        shadowPass?.Dispose();
        outputVertices?.Dispose();
        pipeline?.Dispose();
        if (descriptorPool.Handle != 0)
        {
            plant.Vk.DestroyDescriptorPool(plant.Device, descriptorPool, null);
        }
        if (setLayout.Handle != 0)
        {
            plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null);
        }
        camera?.Dispose();
        vertices?.Dispose();
        framebuffer?.Dispose();
        renderPass?.Dispose();
        depth?.Dispose();
        hdr?.Dispose();
        diffuseData?.Dispose();
        shadowMap?.Dispose();
        if (shadowSampler.Handle != 0)
        {
            plant.Vk.DestroySampler(plant.Device, shadowSampler, null);
        }
        submitter.Dispose();
        commandPool.Dispose();
        fences.Dispose();
        allocator.Dispose();
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool Finite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
