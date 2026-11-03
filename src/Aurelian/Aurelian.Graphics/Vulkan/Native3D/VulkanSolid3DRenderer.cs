using System.Diagnostics;
using System.Numerics;
using System.Collections.Immutable;
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
/// Bounded triangle renderer with opaque depth, shared surface lighting and optional presentation passes.
/// Uses the shared native color target, draw encoder, allocator, and submission path.
/// </summary>
public sealed unsafe class VulkanSolid3DRenderer : IDisposable
{
    private const int VertexStride = 40;
    private readonly int cameraBytes;
    private readonly bool motionOutput;
    private readonly bool surfaceOnly;
    private readonly VulkanSurfaceLighting3D? surfaceLighting;
    private readonly VulkanTransparency3D? transparency;
    private VulkanRefraction3D? refraction;
    private readonly Func<VulkanRefraction3D>? refractionFactory;
    private readonly Func<PlanarWaterReflection3D, VulkanPlanarWaterReflection>? waterReflectionFactory;
    private VulkanPlanarWaterReflection? waterReflection;
    private float previousWaterTime;
    private bool previousWaterReflection;
    private ImmutableArray<WaterImpulse3D> previousWaterImpulses = [];
    internal AurelianVulkanTexture UnfoggedReflectionOutput => surfaceLighting!.UnfoggedOutput!;
    private const int MaximumVertices = 65_536;
    private readonly AurelianVulkanPlant plant;
    private readonly VulkanNativeFrameTarget target;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commandPool;
    private readonly VulkanCommandSubmitter submitter;
    private readonly VulkanFrameInFlightSet frames;
    private readonly AurelianVulkanTexture depth;
    private readonly AurelianVulkanRenderPass renderPass;
    private readonly AurelianVulkanFramebuffer framebuffer;
    private readonly AurelianVulkanGraphicsPipeline pipeline;
    private readonly AurelianVulkanBuffer[] vertexFrames = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private readonly AurelianVulkanBuffer[] previousVertexFrames = new AurelianVulkanBuffer[VulkanFrameInFlightSet.Count];
    private AurelianVulkanBuffer vertices => vertexFrames[frames.Slot];
    private AurelianVulkanBuffer? previousVertices => previousVertexFrames[frames.Slot];
    private AurelianVulkanBuffer camera => cameraBindings.Uniform;
    private readonly Vulkan3DFrameBindings cameraBindings;
    private readonly DescriptorSetLayout setLayout;
    private DescriptorSet descriptorSet => cameraBindings.Set;
    private readonly VulkanModel3DBatches? modelRenderer;
    private readonly VulkanRetainedModels3D? retainedModels;
    private (string Identity, NativeMesh3D Mesh, ModelMaterial Material)[] previousInstances = [];
    private readonly AurelianVulkanTexture? hdr;
    private readonly AurelianVulkanTexture? motion;
    private readonly VulkanTemporal3D? temporal;
    private readonly VulkanBloom3D? bloom;
    private AurelianVulkanBuffer? previousGpu;
    private AurelianVulkanBuffer? lastGpuSource;
    private Native3DVertex[] previousGeometry = [];
    private Matrix4x4 previousClip;
    private Graphics3DSettings? previousSettings;
    private string? previousLightingRevision;
    private float previousStaticEmissionIntensity;
    private string? previousRevision;
    private bool resetHistory = true;
    private (string? Identity, int Count, ModelMaterial Material)[] previousModels = [];
    private readonly bool outputHasBloom;
    private readonly AurelianVulkanTexture shadowMap;
    private readonly Sampler shadowSampler;
    private readonly Vulkan3DPass? shadowPass;
    private readonly Vulkan3DPass? outputPass;
    private readonly AurelianVulkanBuffer? outputVertices;
    private readonly Vulkan3DGpuTimings? gpuTimings;
    private bool disposed;
    private long frameSequence;
    private readonly bool supportsDiffuseData;
    private AurelianVulkanTexture? diffuseData;
    private StaticDiffuseLighting? diffuseAsset;
    private bool diffusePublished;
    public float StaticEmissionIntensity { get; set; } = 1;
    public string? StaticDiffuseFallbackReason { get; private set; } = "NoPublishedLightingAsset";

    public EnvironmentLighting? Environment { get; set; }
    public ReflectionProbe3D? ReflectionProbe { get; set; }
    public IReadOnlyList<LocalLight3D> LocalLights { get; set; } = [];

    /// <summary>Caller-owned pool; its lifetime must enclose renderer use.</summary>
    public VulkanParticles3D? ParticleSystem { get; set; }
    public IReadOnlyList<ParticleEmitter3D> ParticleEmitters { get; set; } = [];

    public Native3DFrameMode FrameMode { get; set; } = Native3DFrameMode.Capture;

    public Graphics3DSettings Settings { get; set; } = Graphics3DSettings.Default;

    public VulkanSolid3DRenderer(
        AurelianVulkanPlant plant,
        CompiledGraphicsProgram program,
        VulkanNativeFrameTarget target,
        bool enableDepth = true,
        CompiledGraphicsProgram? modelProgram = null,
        CompiledGraphicsProgram? shadowProgram = null,
        CompiledGraphicsProgram? outputProgram = null,
        CompiledGraphicsProgram? temporalProgram = null,
        CompiledGraphicsProgram? bloomProgram = null,
        SurfaceLightingPrograms? surfacePrograms = null, RetainedModel3DPrograms? retainedPrograms = null)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(target);
        target.ValidateExternalPass(plant);
        ValidateProgram(program);
        surfaceOnly = program.PixelTargets.Count == 5;
        if (surfaceOnly != (surfacePrograms is not null) || (surfaceOnly && (outputProgram is null || !enableDepth
            || modelProgram is not null && modelProgram.PixelTargets.Count != 5)))
        {
            throw new ArgumentException("Surface lighting requires matching five-target geometry, depth and HDR output programs.");
        }
        supportsDiffuseData = surfaceOnly || program.Resources.Count == 5;
        motionOutput = program.VertexInputs.Count == 4;
        cameraBytes = program.Material!.Size;
        outputHasBloom = outputProgram?.Resources.Count == 5;
        if (temporalProgram is not null && (!motionOutput || outputProgram is null || modelProgram?.VertexInputs.Count == 5))
            throw new ArgumentException("Temporal resolve requires HDR output and motion-capable geometry shaders.");
        Settings = Graphics3DSettings.Default with
        {
            AntiAliasing = temporalProgram is null ? AntiAliasing3D.None : AntiAliasing3D.Temporal,
            BloomIntensity = bloomProgram is null ? 0 : Graphics3DSettings.Default.BloomIntensity,
        };
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
        frames = new(fences.CommandListFence);
        try
        {
            if (outputProgram is not null)
            {
                ValidateModernFormats();
                gpuTimings = new(plant, ["directional-shadow", "linear-lighting", "temporal-resolve", "bloom", "tone-map-output",
                    .. surfaceOnly ? new[] { "ambient-occlusion", "local-light-culling", "surface-lighting", "local-shadows", "subsurface-diffusion", "atmosphere", "transparency", "refraction", "particles-update" } : []], frames);
                hdr = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, target.Width, target.Height,
                    VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferSource,
                    VulkanMemoryUsage.GpuOnly, "solid3d.hdr", VulkanTextureFormat.Rgba16Float);
                shadowMap = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, Lighting3DUniforms.ShadowSize, Lighting3DUniforms.ShadowSize,
                    VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferSource,
                    VulkanMemoryUsage.GpuOnly, "solid3d.shadow", VulkanTextureFormat.R32Float);
                shadowPass = new(plant, allocator, shadowProgram!, shadowMap, Lighting3DUniforms.ShadowSize, Lighting3DUniforms.ShadowSize, true, frames: frames);
                outputPass = new(plant, allocator, outputProgram, target.Texture, target.Width, target.Height, false, hdr, frames);
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
            if (motionOutput)
            {
                motion = VulkanNativeForwardTexturedRenderer.CreateTexture(plant, allocator, target.Width, target.Height,
                    VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.ShaderResource, VulkanMemoryUsage.GpuOnly,
                    "solid3d.motion", VulkanTextureFormat.Rgba32Float);
                for (int index = 0; index < VulkanFrameInFlightSet.Count; index++)
                {
                    previousVertexFrames[index] = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator,
                        MaximumVertices * VertexStride, VulkanBufferUsage.Vertex, VulkanMemoryUsage.CpuToGpu, "solid3d.previous");
                }
            }
            if (temporalProgram is not null)
                temporal = new(plant, allocator, commandPool, fences, temporalProgram, target.Width, target.Height, frames);
            if (bloomProgram is not null)
            {
                if (!outputHasBloom || hdr is null) throw new ArgumentException("Bloom requires the HDR bloom-aware output shader.");
                bloom = new(plant, allocator, bloomProgram, target.Width, target.Height, frames);
            }
            if (surfacePrograms is not null)
            {
                surfaceLighting = new(plant, allocator, commandPool, fences, surfacePrograms, shadowProgram!, target.Width, target.Height, frames);
            }
            shadowSampler = Vulkan3DPass.CreateSampler(plant, Filter.Nearest);
            depth = VulkanNativeForwardTexturedRenderer.CreateTexture(
                plant, allocator, target.Width, target.Height,
                VulkanTextureUsage.DepthAttachment, VulkanMemoryUsage.GpuOnly, "solid3d.depth", VulkanTextureFormat.D32Float);
            var passResult = VulkanRenderPassFactory.Create(plant, new VulkanRenderPassDescriptor(
                [new VulkanRenderPassAttachmentDescriptor(
                    "solid3d.color", hdr?.Format ?? target.TextureFormat, VulkanAttachmentLoadOp.Clear,
                    VulkanAttachmentStoreOp.Store, VulkanResourceLayout.Undefined,
                    hdr is null ? VulkanResourceLayout.TransferSource : VulkanResourceLayout.ShaderResourceFragment),
                    .. motion is null ? Array.Empty<VulkanRenderPassAttachmentDescriptor>() : [new VulkanRenderPassAttachmentDescriptor(
                        "solid3d.motion", motion.Format, VulkanAttachmentLoadOp.Clear, VulkanAttachmentStoreOp.Store,
                        VulkanResourceLayout.Undefined, VulkanResourceLayout.ShaderResourceFragment)],
                    .. surfaceLighting is null ? Array.Empty<VulkanRenderPassAttachmentDescriptor>() : new[] {
                        new VulkanRenderPassAttachmentDescriptor("surface.normal", surfaceLighting.Normal.Format,
                            VulkanAttachmentLoadOp.Clear, VulkanAttachmentStoreOp.Store,
                            VulkanResourceLayout.Undefined, VulkanResourceLayout.ShaderResourceFragment),
                        new VulkanRenderPassAttachmentDescriptor("surface.emission", surfaceLighting.Emission.Format,
                            VulkanAttachmentLoadOp.Clear, VulkanAttachmentStoreOp.Store,
                            VulkanResourceLayout.Undefined, VulkanResourceLayout.ShaderResourceFragment),
                        new VulkanRenderPassAttachmentDescriptor("surface.subsurface", surfaceLighting.Subsurface.Format,
                            VulkanAttachmentLoadOp.Clear, VulkanAttachmentStoreOp.Store,
                            VulkanResourceLayout.Undefined, VulkanResourceLayout.ShaderResourceFragment) }],
                new VulkanRenderPassAttachmentDescriptor(
                    "solid3d.depth", VulkanTextureFormat.D32Float, VulkanAttachmentLoadOp.Clear,
                    VulkanAttachmentStoreOp.DontCare, VulkanResourceLayout.Undefined, VulkanResourceLayout.DepthStencilAttachment)));
            Require(passResult.Success, string.Join("; ", passResult.Diagnostics.Select(item => item.Message)));
            renderPass = passResult.RenderPass!;
            var framebufferResult = VulkanFramebufferFactory.Create(plant, renderPass,
                new VulkanFramebufferDescriptor(target.Width, target.Height, [hdr ?? target.Texture, .. motion is null ? Array.Empty<AurelianVulkanTexture>() : [motion],
                    .. surfaceLighting is null ? Array.Empty<AurelianVulkanTexture>() : [surfaceLighting.Normal, surfaceLighting.Emission, surfaceLighting.Subsurface]], depth));
            Require(framebufferResult.Success, string.Join("; ", framebufferResult.Diagnostics.Select(item => item.Message)));
            framebuffer = framebufferResult.Framebuffer!;
            setLayout = VulkanNativeForwardTexturedRenderer.CreateDescriptorSetLayout(plant, program);
            var descriptorResult = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(
                program.Shaders,
                motionOutput ? [new(0, VertexStride), new(1, VertexStride)] : [new VulkanVertexBufferLayoutDescriptor(0, VertexStride)],
                [
                    new VulkanVertexAttributeDescriptor(0, 0, VulkanVertexAttributeFormat.Float3, 0),
                    new VulkanVertexAttributeDescriptor(1, 0, VulkanVertexAttributeFormat.Float3, 12),
                    new VulkanVertexAttributeDescriptor(2, 0, VulkanVertexAttributeFormat.Float4, 24),
                    .. motionOutput ? new VulkanVertexAttributeDescriptor[] { new(3, 1, VulkanVertexAttributeFormat.Float3, 0) } : [],
                ],
                enableDepthTest: enableDepth,
                enableDepthWrite: enableDepth);
            Require(descriptorResult.Success, string.Join("; ", descriptorResult.Diagnostics.Select(item => item.Message)));
            var pipelineResult = VulkanGraphicsPipelineFactory.Create(plant, renderPass, descriptorResult.Descriptor!, [setLayout]);
            Require(pipelineResult.Success, string.Join("; ", pipelineResult.Diagnostics.Select(item => item.Message)));
            pipeline = pipelineResult.Pipeline!;
            for (int index = 0; index < VulkanFrameInFlightSet.Count; index++)
            {
                vertexFrames[index] = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(plant, allocator,
                    MaximumVertices * VertexStride, VulkanBufferUsage.Vertex, VulkanMemoryUsage.CpuToGpu, "solid3d.vertices");
            }
            cameraBindings = new(plant, allocator, setLayout, cameraBytes,
                surfaceOnly ? 0u : supportsDiffuseData ? 2u : 1u, frames);
            InitializeCameraDescriptors();
            if (modelProgram is not null)
                modelRenderer = new(plant, allocator, commandPool, fences, renderPass, modelProgram, shadowMap, shadowSampler, frames: frames);
            if (retainedPrograms is not null)
            {
                if (modelRenderer is null || modelProgram is null)
                {
                    throw new ArgumentException("Retained geometry requires the model material program.");
                }
                retainedModels = new(plant, allocator, commandPool, fences, frames,
                    modelRenderer, renderPass, modelProgram, retainedPrograms);
            }
            if (surfacePrograms?.TransparentModel is not null && surfacePrograms.TransparencyResolve is not null)
                transparency = new(plant, allocator, commandPool, fences, surfacePrograms,
                    target.Width, target.Height, shadowMap, shadowSampler, frames);
            if (surfacePrograms?.RefractiveModel is not null && surfacePrograms.RefractionResolve is not null)
                refractionFactory = () => new(plant, allocator, commandPool, fences, surfacePrograms,
                    target.Width, target.Height, shadowMap, shadowSampler, frames);
            if (surfaceOnly && surfacePrograms is not null && modelProgram is not null)
            {
                waterReflectionFactory = configuration => new(plant, target.Width, target.Height,
                    configuration, program, modelProgram, shadowProgram, outputProgram, surfacePrograms);
            }
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
        if (model.Occurrences.Any(item => item.Primitive.Material.AlphaBlend) && transparency is null)
            throw new NotSupportedException("Blend materials require the transparent model and resolve programs.");
        if (model.Occurrences.Any(item => item.Primitive.Material.Transmission > 0))
            EnsureRefraction();
        surfaceLighting?.ValidatePresentation(Settings, model.Occurrences.Any(item => item.Primitive.Material.SubsurfaceStrength > 0));
        modelRenderer.Prepare(model);
        transparency?.Prepare(model);
        refraction?.Prepare(model);
    }

    public Native3DFrameResult Render(Native3DScene scene, Matrix4x4 worldToClip, Vector3 eye,
        NativeFrameClearColor clearColor, bool capture = false, NativeGpuGeometry3D? gpuGeometry = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.Geometry.Length % 3 != 0 || scene.Models.Any(batch => batch.Vertices.Length == 0 || batch.Vertices.Length % 3 != 0))
        {
            throw new ArgumentException("Scene batches must contain complete triangles.", nameof(scene));
        }
        if (scene.Instances.Count > 0 && retainedModels is null)
        {
            throw new InvalidOperationException("AUR-MESH-012: Retained instance shaders were not configured.");
        }
        if (scene.Instances.Any(instance => instance.Material.AlphaBlend || instance.Material.Transmission > 0))
        {
            throw new NotSupportedException("AUR-MESH-013: Blended and refractive instances use the existing material batch adapter.");
        }
        scene.WaterFrame.Validate();
        scene.WaterReflection?.Validate();
        var waterBatches = scene.Models.Where(batch => batch.Material.Water is not null).ToArray();
        ValidateWater(waterBatches, eye);
        if (scene.WaterReflection is { } configuration)
        {
            if (gpuGeometry is not null || waterBatches.Length == 0 || waterBatches.Any(batch =>
                Math.Abs(batch.Vertices[0].Position.Y - configuration.Height) > .0001f))
            {
                throw new NotSupportedException("AUR-WATER-010: One planar reflection supports coplanar water patches and CPU-authored scene batches.");
            }
            if (waterReflectionFactory is null)
            {
                throw new NotSupportedException("Planar water reflection requires the shared surface renderer.");
            }
            if (waterReflection?.Configuration != configuration)
            {
                frames.Drain();
                waterReflection?.Dispose();
                waterReflection = null;
                waterReflection = waterReflectionFactory(configuration);
                ResetTemporalHistory();
            }
            waterReflection.Render(scene.Instances.Count == 0 ? scene : scene with
            {
                Models = [.. scene.Models, .. scene.Instances.Select(instance => instance.Expand())],
                Instances = [],
            }, worldToClip, eye, clearColor, Settings, Environment, ReflectionProbe, LocalLights);
        }
        return RenderCore(scene.Geometry, worldToClip, clearColor, capture, scene.Models, eye, gpuGeometry,
            temporalRevision: scene.TemporalRevision, lightingRevision: scene.LightingRevision,
            waterFrame: scene.WaterFrame, useWaterReflection: scene.WaterReflection is not null, sourceInstances: scene.Instances);
    }

    private static void ValidateWater(IReadOnlyList<NativeModel3DBatch> batches, Vector3 eye)
    {
        foreach (var batch in batches)
        {
            if (batch.Material.Water is null)
            {
                continue;
            }
            batch.Material.Validate();
            if (batch.Vertices.Length == 0 || batch.Vertices.Length % 3 != 0)
            {
                throw new InvalidDataException("AUR-WATER-008: Water requires complete base triangles.");
            }
            if (batch.Vertices.Any(vertex => vertex.Normal.Y < .999f)
                || batch.Vertices.Any(vertex => Math.Abs(vertex.Position.Y - batch.Vertices[0].Position.Y) > .0001f))
            {
                throw new InvalidDataException("AUR-WATER-008: Water needs a horizontal tessellated base patch with upward normals.");
            }
            if (eye.Y <= batch.Vertices[0].Position.Y)
            {
                throw new NotSupportedException("AUR-WATER-009: This water profile requires the camera above water.");
            }
        }
    }

    public Native3DFrameResult Render(ReadOnlySpan<Native3DVertex> geometry, Matrix4x4 worldToClip,
        NativeFrameClearColor clearColor, bool capture = false, IReadOnlyList<NativeModel3DBatch>? models = null,
        Vector3 eye = default, NativeGpuGeometry3D? gpuGeometry = null, Vector3? lightDirection = null,
        string? temporalRevision = null, string? lightingRevision = null, WaterFrame3D? waterFrame = null)
    {
        return RenderCore(geometry, worldToClip, clearColor, capture, models, eye, gpuGeometry,
            lightDirection, temporalRevision, lightingRevision, waterFrame);
    }

    private Native3DFrameResult RenderCore(
        ReadOnlySpan<Native3DVertex> geometry,
        Matrix4x4 worldToClip,
        NativeFrameClearColor clearColor,
        bool capture = false,
        IReadOnlyList<NativeModel3DBatch>? models = null,
        Vector3 eye = default,
        NativeGpuGeometry3D? gpuGeometry = null,
        Vector3? lightDirection = null,
        string? temporalRevision = null,
        string? lightingRevision = null,
        WaterFrame3D? waterFrame = null,
        bool useWaterReflection = false,
        IReadOnlyList<SceneInstance3D>? sourceInstances = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        target.ValidateExternalPass(plant);
        long frameStarted = Stopwatch.GetTimestamp();
        int frameSlot = frames.Slot;
        frames.Begin();
        long frameReady = Stopwatch.GetTimestamp();
        Native3DCompletedGpuFrame? completedGpuFrame = gpuTimings?.ReadCompletedFrame();
        models ??= [];
        sourceInstances ??= [];
        waterFrame ??= new();
        waterFrame.Validate();
        ValidateWater(models, eye);
        bool changedPastImpulses = previousWaterImpulses.Any(impulse =>
            impulse.StartedAt < previousWaterTime && previousWaterTime < impulse.StartedAt + impulse.Lifetime
            && !waterFrame.Impulses.Contains(impulse))
            || waterFrame.Impulses.Any(impulse => impulse.StartedAt < previousWaterTime && !previousWaterImpulses.Contains(impulse));
        if (waterFrame.Time < previousWaterTime || useWaterReflection != previousWaterReflection || changedPastImpulses)
        {
            ResetTemporalHistory();
        }
        if ((geometry.IsEmpty && models.Count == 0 && sourceInstances.Count == 0 && gpuGeometry is null) || geometry.Length % 3 != 0 || geometry.Length > MaximumVertices)
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
        if (settings.SurfaceDebugView != SurfaceDebugView3D.Shaded)
        {
            // Display AO visibility itself, without exposure, bloom or retained
            // lighting history changing what the inspection view measures.
            settings = settings with
            {
                ToneMapping = false,
                Exposure = 1,
                BloomIntensity = 0,
                AntiAliasing = AntiAliasing3D.None,
            };
        }
        bool useTemporal = settings.AntiAliasing == AntiAliasing3D.Temporal;
        if (useTemporal && temporal is null) throw new InvalidOperationException("Temporal AA requires TemporalResolve3D at renderer creation.");
        if (useTemporal && gpuGeometry is not null && (gpuGeometry.Buffer.Usage & VulkanBufferUsage.TransferSource) == 0)
            throw new InvalidOperationException("Temporal GPU geometry requires TransferSource usage to retain previous positions without readback.");
        if (settings.BloomIntensity > 0 && bloom is null) throw new InvalidOperationException("Bloom requires Bloom3D at renderer creation.");
        var modelKeys = models.Select(model => (model.TemporalIdentity, model.Vertices.Length, model.Material)).ToArray();
        var instanceKeys = sourceInstances.Select(instance => (instance.Identity, instance.Mesh, instance.Material)).ToArray();
        bool correspondenceChanged = !instanceKeys.SequenceEqual(previousInstances) || !modelKeys.SequenceEqual(previousModels) || previousGeometry.Length != geometry.Length || previousRevision != temporalRevision
            || previousSettings != settings || previousStaticEmissionIntensity != StaticEmissionIntensity
            || lastGpuSource != gpuGeometry?.Buffer;
        if (settings.Volumetrics.Enabled && previousLightingRevision != lightingRevision)
            correspondenceChanged = true;
        if (!float.IsFinite(StaticEmissionIntensity) || StaticEmissionIntensity < 0)
            throw new ArgumentOutOfRangeException(nameof(StaticEmissionIntensity));
        bool useDiffuse = diffusePublished && diffuseAsset is not null && settings.SolidPbr
            && settings.SolidMetallic == 0
            && Vector3.Distance(Vector3.Normalize(settings.SunDirection), diffuseAsset.SunDirection) < .00001f;
        if (useDiffuse)
        {
            if (settings.DirectDiffuseModel != DirectDiffuseModel3D.Lambert)
            {
                throw new NotSupportedException("AUR-DIFFUSE-001: Published static diffuse lighting requires Lambert. Disable the compiled-lighting asset for direct-diffuse experiments.");
            }
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
        float[] channels = [clearColor.Red, clearColor.Green, clearColor.Blue, clearColor.Alpha];
        if (channels.Any(value => !float.IsFinite(value) || value < 0 || value > 1))
        {
            throw new ArgumentOutOfRangeException(nameof(clearColor));
        }
        if (Lighting3DUniforms.Rows(worldToClip).Any(value => !float.IsFinite(value)))
            throw new ArgumentException("Camera transform must be finite.", nameof(worldToClip));
        bool orthographic = worldToClip.M14 == 0 && worldToClip.M24 == 0 && worldToClip.M34 == 0;
        if (orthographic && (settings.Volumetrics.Enabled || models.Any(batch => batch.Material.Transmission > 0)))
            throw new NotSupportedException("AUR-TRANSPORT-002: Froxel lighting and refraction currently require a perspective camera.");
        if (models.Count > 0 && modelRenderer is null)
            throw new InvalidOperationException("StaticModel3D shader was not configured.");
        if (surfaceLighting is null && (Environment is not null || ReflectionProbe is not null || LocalLights.Count > 0
            || settings.AmbientOcclusionStrength > 0 || settings.SurfaceDebugView != SurfaceDebugView3D.Shaded))
        {
            throw new InvalidOperationException("Environment, local lights and AO require the surface lighting programs.");
        }
        if (surfaceLighting?.Prepare(Environment, ReflectionProbe, LocalLights, settings.LocalShadowBudget) == true) correspondenceChanged = true;
        if (correspondenceChanged) ResetTemporalHistory();
        Matrix4x4 logicalCamera = worldToClip;
        if (useTemporal) worldToClip = temporal!.Begin(logicalCamera);
        if (surfaceOnly && !Matrix4x4.Invert(worldToClip, out _))
        {
            throw new ArgumentException("Surface lighting requires an invertible camera.", nameof(worldToClip));
        }
        if (resetHistory) previousClip = worldToClip;
        DirectionalShadowCascades3D? cascades = surfaceOnly ? DirectionalShadowCascades3D.Create(settings, logicalCamera, eye) : null;
        Matrix4x4 shadowCamera = cascades?.Cameras[0] ?? Lighting3DUniforms.ShadowCamera(settings, eye);
        float[] lighting = Lighting3DUniforms.Scene(settings, shadowCamera, shadowPass is not null);
        float[] cameraRows =
        [
            .. Lighting3DUniforms.Rows(worldToClip),
            .. lighting[..4],
            eye.X, eye.Y, eye.Z, 1,
            .. lighting[4..16],
            settings.SolidRoughness, settings.SolidMetallic, settings.SolidPbr ? 1 : 0, useDiffuse ? 1 : 0,
            .. lighting[16..],
            .. motionOutput ? Lighting3DUniforms.Rows(previousClip) : [],
        ];
        cameraRows[31] = StaticEmissionIntensity;
        if (cameraRows.Any(value => !float.IsFinite(value)))
        {
            throw new ArgumentException("Camera transform must be finite.", nameof(worldToClip));
        }
        if (!geometry.IsEmpty)
        {
            Require(vertices.Write(MemoryMarshal.AsBytes(geometry)).Success, "Vertex upload failed.");
            if (motionOutput)
            {
                ReadOnlySpan<Native3DVertex> prior = resetHistory ? geometry : previousGeometry;
                Require(previousVertices!.Write(MemoryMarshal.AsBytes(prior)).Success, "Previous vertex upload failed.");
            }
        }
        if (useTemporal && gpuGeometry is not null)
        {
            ulong bytes = (ulong)gpuGeometry.VertexCount * VertexStride;
            if (previousGpu is null || previousGpu.SizeBytes != bytes)
            {
                frames.Drain();
                previousGpu?.Dispose();
                var created = VulkanBufferFactory.Create(plant, allocator, new(plant.Context.Id, bytes,
                    VulkanBufferUsage.Vertex | VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuOnly,
                    "solid3d.previous-gpu"));
                Require(created.Success, "Previous GPU geometry allocation failed.");
                previousGpu = created.Buffer!;
                ResetTemporalHistory();
            }
        }
        Require(camera.Write(MemoryMarshal.AsBytes(cameraRows.AsSpan())).Success, "Camera upload failed.");
        if (models.Count > 0)
        {
            if (modelRenderer is null) throw new InvalidOperationException("StaticModel3D shader was not configured.");
        }
        modelRenderer?.Upload(models, worldToClip, eye, lighting, previousClip, resetHistory,
            sourceInstances.Select(instance => instance.Material).ToArray());
        retainedModels?.Upload(sourceInstances, resetHistory);
        bool hasParticles = ParticleEmitters.Count > 0;
        if (hasParticles)
        {
            if (ParticleSystem is null || transparency is null || surfaceLighting is null)
            {
                throw new InvalidOperationException("AUR-VFX-008: Emitters require a particle pool and surface transparency programs.");
            }
        }
        if (ParticleSystem is not null)
        {
            ParticleSystem.ValidatePlant(plant);
            ParticleSystem.Configure(ParticleEmitters, worldToClip, frames.Slot);
        }
        bool hasTransparency = hasParticles || models.Any(batch => batch.Material.AlphaBlend);
        bool hasRefraction = models.Any(batch => batch.Material.Transmission > 0);
        if (hasRefraction)
            EnsureRefraction();
        bool hasSubsurface = models.Any(batch => batch.Material.SubsurfaceStrength > 0)
            || sourceInstances.Any(instance => instance.Material.SubsurfaceStrength > 0);
        if (surfaceLighting is null && (settings.Fog.Density > 0 || settings.Volumetrics.Enabled))
            throw new NotSupportedException("Atmosphere rendering requires the shared surface renderer.");
        surfaceLighting?.ValidatePresentation(settings, hasSubsurface);
        if (hasTransparency)
        {
            if (transparency is null || surfaceLighting is null)
                throw new InvalidOperationException("Blend materials require the transparent model and resolve programs.");
            transparency.Prepare(models, worldToClip, eye, lighting, previousClip,
                surfaceLighting.TransparentUniforms(cascades!, settings, target.Width, target.Height),
                surfaceLighting.TransparentInputs(shadowMap, motion!));
            transparency.PrepareParticles(hasParticles ? ParticleSystem : null, worldToClip, eye, settings,
                motion!, surfaceLighting.VolumeTexture, surfaceLighting.VolumeParameters(settings));
        }
        VulkanCommandBufferLease command = commandPool.Rent(fences.CommandListFence.LastKnownCompletedValue);
        Require(command.Begin().Success, "3D command begin failed.");
        // Images and GPU-generated history remain shared on one ordered graphics queue.
        // Finish all earlier frame reads/writes before reusing those attachments.
        MemoryBarrier frameBarrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
            DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
        };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, PipelineStageFlags.AllCommandsBit,
            PipelineStageFlags.AllCommandsBit, 0, 1, &frameBarrier, 0, null, 0, null);
        gpuTimings?.Reset(command);
        gpuTimings?.BeginFrame(command, frameSequence);
        surfaceLighting?.RecordUploads(command);
        if (surfaceOnly)
        {
            gpuTimings?.Mark(command, 26);
            if (hasParticles) { ParticleSystem!.Record(command); }
            gpuTimings?.Mark(command, 27);
        }
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
                retainedModels?.DrawShadows(command, shadowBegin.Scope!.Value, shadowPass);
            }
            Require(encoder.End(plant, command, shadowBegin.Scope!.Value).Success, "Shadow pass end failed.");
            Vulkan3DPass.SampleAfterRendering(plant, command, shadowMap);
        }
        if (surfaceLighting is not null)
        {
            for (int index = 0; index < 2; index++)
            {
                var pass = surfaceLighting.CascadePass(index);
                pass.Upload(Lighting3DUniforms.Rows(cascades!.Cameras[index + 1]));
                var cascadeBegin = encoder.Begin(plant, command, new(pass.Pass, pass.Framebuffer, new(1, 1, 1, 1)));
                Require(cascadeBegin.Success, "Directional cascade begin failed.");
                if (settings.Shadows)
                {
                    if (!geometry.IsEmpty) pass.Draw(command, cascadeBegin.Scope!.Value, vertices, (uint)geometry.Length);
                    if (gpuGeometry is not null) pass.Draw(command, cascadeBegin.Scope!.Value, gpuGeometry.Buffer, gpuGeometry.VertexCount);
                    modelRenderer?.DrawShadows(command, cascadeBegin.Scope!.Value, pass, models);
                    retainedModels?.DrawShadows(command, cascadeBegin.Scope!.Value, pass);
                }
                Require(encoder.End(plant, command, cascadeBegin.Scope!.Value).Success, "Directional cascade end failed.");
                surfaceLighting.FinishCascade(command, index);
            }
        }
        gpuTimings?.Mark(command, 1);
        if (surfaceLighting is not null)
        {
            gpuTimings?.Mark(command, 16);
            for (int index = 0; index < surfaceLighting.ShadowCount; index++)
            {
                var pass = surfaceLighting.ShadowPass(index);
                pass.Upload(Lighting3DUniforms.Rows(surfaceLighting.ShadowCamera(index)));
                var spotBegin = encoder.Begin(plant, command, new(pass.Pass, pass.Framebuffer, new(1, 1, 1, 1)));
                Require(spotBegin.Success, "Local shadow pass begin failed.");
                if (!geometry.IsEmpty) pass.Draw(command, spotBegin.Scope!.Value, vertices, (uint)geometry.Length);
                if (gpuGeometry is not null) pass.Draw(command, spotBegin.Scope!.Value, gpuGeometry.Buffer, gpuGeometry.VertexCount);
                if (models.Count > 0) modelRenderer!.DrawShadows(command, spotBegin.Scope!.Value, pass, models);
                retainedModels?.DrawShadows(command, spotBegin.Scope!.Value, pass);
                Require(encoder.End(plant, command, spotBegin.Scope!.Value).Success, "Local shadow pass end failed.");
                surfaceLighting.FinishShadow(command, index);
            }
            gpuTimings?.Mark(command, 17);
        }
        gpuTimings?.Mark(command, 2);
        var begin = encoder.Begin(plant, command, new VulkanRenderPassBeginRequest(
            renderPass, framebuffer, hdr is null ? target.PrepareClearColor(clearColor)
                : new VulkanColorClearValue(NativeSrgbTransfer.Decode(clearColor.Red), NativeSrgbTransfer.Decode(clearColor.Green),
                    NativeSrgbTransfer.Decode(clearColor.Blue), clearColor.Alpha))
        {
            AdditionalClearColors = motionOutput ? [new VulkanColorClearValue(0, 0, 1, 1),
            .. surfaceOnly ? new[] { new VulkanColorClearValue(0, 1, 0, 0), new VulkanColorClearValue(0, 0, 0, 1), new VulkanColorClearValue(0, 0, 0, 0) } : []] : []
        });
        Require(begin.Success, "3D render pass begin failed.");
        DescriptorSet set = descriptorSet;
        plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Graphics,
            pipeline.NativePipelineLayout, 0, 1, &set, 0, null);
        if (!geometry.IsEmpty)
        {
            var draw = new VulkanDrawCommandEncoder().DrawVertices(plant, command, begin.Scope!.Value,
                new VulkanDrawVerticesRequest(pipeline, vertices, (uint)geometry.Length, 0,
                    VulkanViewportScissor.FromFramebuffer(framebuffer))
                { PreviousVertexBuffer = previousVertices });
            Require(draw.Success, string.Join("; ", draw.Diagnostics.Select(item => item.Message)));
        }
        if (models.Count > 0) modelRenderer!.Draw(command, begin.Scope!.Value, framebuffer, models);
        retainedModels?.Draw(command, begin.Scope!.Value, framebuffer);
        if (gpuGeometry is not null)
        {
            DescriptorSet cameraSet = descriptorSet;
            plant.Vk.CmdBindDescriptorSets(command.CommandBuffer, PipelineBindPoint.Graphics,
                pipeline.NativePipelineLayout, 0, 1, &cameraSet, 0, null);
            AurelianVulkanBuffer? priorGpu = null;
            if (motionOutput) priorGpu = useTemporal && !resetHistory ? previousGpu : gpuGeometry.Buffer;
            var draw = new VulkanDrawCommandEncoder().DrawVertices(plant, command, begin.Scope!.Value,
                new VulkanDrawVerticesRequest(pipeline, gpuGeometry.Buffer, gpuGeometry.VertexCount, 0,
                    VulkanViewportScissor.FromFramebuffer(framebuffer))
                { PreviousVertexBuffer = priorGpu });
            Require(draw.Success, string.Join("; ", draw.Diagnostics.Select(item => item.Message)));
        }
        Require(encoder.End(plant, command, begin.Scope!.Value).Success, "3D render pass end failed.");
        gpuTimings?.Mark(command, 3);
        AurelianVulkanTexture? resolved = hdr;
        if (surfaceLighting is not null)
        {
            Matrix4x4.Invert(worldToClip, out var inverse);
            float[] surfaceUniforms = (float[])lighting.Clone();
            surfaceUniforms[11] = StaticEmissionIntensity;
            surfaceLighting.SetClear(new(NativeSrgbTransfer.Decode(clearColor.Red), NativeSrgbTransfer.Decode(clearColor.Green),
                NativeSrgbTransfer.Decode(clearColor.Blue), clearColor.Alpha));
            resolved = surfaceLighting.Record(command, outputVertices!, hdr!, motion!, shadowMap, diffuseData,
                worldToClip, inverse, eye, settings, surfaceUniforms, useDiffuse, gpuTimings, cascades!, hasSubsurface);
        }
        if (surfaceOnly) gpuTimings?.Mark(command, 24);
        if (hasRefraction && settings.SurfaceDebugView == SurfaceDebugView3D.Shaded)
        {
            resolved = refraction!.Record(command, outputVertices!, models, worldToClip, eye, lighting,
                surfaceLighting!.TransparentUniforms(cascades!, settings, target.Width, target.Height),
                surfaceLighting.TransparentInputs(shadowMap, motion!), surfaceLighting.UnfoggedOutput!, resolved!,
                waterFrame, resetHistory ? waterFrame.Time : previousWaterTime, previousClip, logicalCamera,
                resetHistory, useTemporal, settings.TemporalHistoryWeight,
                useWaterReflection ? waterReflection!.Color : null, waterReflection?.Clip ?? Matrix4x4.Identity);
        }
        if (surfaceOnly) gpuTimings?.Mark(command, 25);
        if (surfaceOnly) gpuTimings?.Mark(command, 22);
        if (hasTransparency && settings.SurfaceDebugView == SurfaceDebugView3D.Shaded)
        {

            resolved = transparency!.Record(command, outputVertices!, resolved!);

        }
        if (surfaceOnly) gpuTimings?.Mark(command, 23);
        gpuTimings?.Mark(command, 4);
        if (useTemporal)
            resolved = temporal!.Resolve(command, outputVertices!, resolved!, motion!, settings.TemporalHistoryWeight);
        gpuTimings?.Mark(command, 5);
        gpuTimings?.Mark(command, 6);
        AurelianVulkanTexture? glow = resolved;
        if (settings.BloomIntensity > 0)
            glow = bloom!.Record(command, outputVertices!, resolved!, settings);
        gpuTimings?.Mark(command, 7);
        gpuTimings?.Mark(command, 8);
        if (outputPass is not null)
        {
            Vulkan3DPass.SampleAfterRendering(plant, command, resolved!);
            if (outputHasBloom) outputPass.SetOutputTextures(resolved!, glow!);
            bool encodeSrgb = target.TextureFormat is not (VulkanTextureFormat.Rgba8Srgb or VulkanTextureFormat.Bgra8Srgb);
            float toneControl = settings.ToneMapping
                ? 1 + settings.ToneMapColorRetention + (settings.ToneMapGamutCompression ? 2 : 0)
                : 0;
            outputPass.Upload([settings.Exposure, toneControl,
                encodeSrgb ? 1 : 0, settings.BloomIntensity]);
            var outputBegin = encoder.Begin(plant, command, new(outputPass.Pass, outputPass.Framebuffer, new(0, 0, 0, 1)));
            Require(outputBegin.Success, "Tone mapping pass begin failed.");
            outputPass.Draw(command, outputBegin.Scope!.Value, outputVertices!, 3);
            Require(encoder.End(plant, command, outputBegin.Scope!.Value).Success, "Tone mapping pass end failed.");
        }
        gpuTimings?.Mark(command, 9);
        if (useTemporal && gpuGeometry is not null) CopyPreviousGpu(command, gpuGeometry);
        gpuTimings?.EndFrame(command);
        Require(command.End().Success, "3D command end failed.");
        long recordedAt = Stopwatch.GetTimestamp();
        var submit = submitter.Submit(new VulkanCommandSubmitRequest(command,
            WaitForCompletion: capture || FrameMode == Native3DFrameMode.Capture,
            TimeoutNanoseconds: 5_000_000_000, DebugName: "solid3d.draw"));
        Require(submit.Success, string.Join("; ", submit.Diagnostics.Select(item => item.Message)));
        long submittedAt = Stopwatch.GetTimestamp();
        var cpuTimings = new Native3DFrameCpuTimings(frameSequence++, frameSlot,
            Stopwatch.GetElapsedTime(frameStarted, frameReady).TotalMilliseconds,
            Stopwatch.GetElapsedTime(frameReady, recordedAt).TotalMilliseconds,
            Stopwatch.GetElapsedTime(recordedAt, submittedAt).TotalMilliseconds);
        if (capture || FrameMode == Native3DFrameMode.Capture)
        {
            completedGpuFrame = gpuTimings?.ReadCompletedFrame();
        }
        if (motionOutput)
        {
            if (previousGeometry.Length != geometry.Length) previousGeometry = new Native3DVertex[geometry.Length];
            geometry.CopyTo(previousGeometry);
        }
        previousModels = modelKeys;
        previousInstances = instanceKeys;
        previousClip = worldToClip;
        previousWaterTime = waterFrame.Time;
        previousWaterReflection = useWaterReflection;
        previousWaterImpulses = waterFrame.Impulses;
        previousSettings = settings;
        previousStaticEmissionIntensity = StaticEmissionIntensity;
        previousRevision = temporalRevision;
        previousLightingRevision = lightingRevision;
        lastGpuSource = gpuGeometry?.Buffer;
        resetHistory = false;
        IReadOnlyList<Native3DGpuPassTime> times = capture || FrameMode == Native3DFrameMode.Capture
            ? gpuTimings?.Read() ?? [] : [];
        frames.Submitted(submit.SignalFenceValue!.Value);
        if (useWaterReflection)
        {
            times = [.. times, new("water-reflection", waterReflection!.GpuMilliseconds)];
        }
        int triangleCount = (geometry.Length + models.Sum(item => item.Vertices.Length) + (int)(gpuGeometry?.VertexCount ?? 0)) / 3;
        triangleCount = checked(triangleCount + (retainedModels?.TriangleCount ?? 0));
        if (hasParticles)
        {
            triangleCount += (int)ParticleSystem!.VertexCount / 3;
        }
        if (capture)
        {
            var readback = target.Capture();
            return new Native3DFrameResult(triangleCount, readback.Pixels, readback.Hash) { GpuPassTimes = times, Retained = retainedModels?.Statistics,
                CpuTimings = cpuTimings, CompletedGpuFrame = completedGpuFrame };
        }
        return new Native3DFrameResult(triangleCount, null, null) { GpuPassTimes = times, Retained = retainedModels?.Statistics,
                CpuTimings = cpuTimings, CompletedGpuFrame = completedGpuFrame };
    }

    /// <summary>Call for camera cuts, teleports, or replacement of raw vertex correspondence.</summary>
    public void ResetTemporalHistory()
    {
        temporal?.Reset();
        resetHistory = true;
    }

    private void EnsureRefraction()
    {
        if (surfaceLighting is null || refractionFactory is null)
            throw new NotSupportedException("Refractive materials require the shared surface renderer and refractive model/resolve programs.");
        refraction ??= refractionFactory();
    }

    private void CopyPreviousGpu(VulkanCommandBufferLease command, NativeGpuGeometry3D geometry)
    {
        MemoryBarrier before = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.ShaderWriteBit | AccessFlags.VertexAttributeReadBit,
            DstAccessMask = AccessFlags.TransferReadBit | AccessFlags.TransferWriteBit,
        };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.VertexInputBit,
            PipelineStageFlags.TransferBit, 0, 1, &before, 0, null, 0, null);
        BufferCopy copy = new(0, 0, (ulong)geometry.VertexCount * VertexStride);
        plant.Vk.CmdCopyBuffer(command.CommandBuffer, geometry.Buffer.NativeBuffer, previousGpu!.NativeBuffer, 1, &copy);
        MemoryBarrier after = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.VertexAttributeReadBit,
        };
        plant.Vk.CmdPipelineBarrier(command.CommandBuffer, PipelineStageFlags.TransferBit, PipelineStageFlags.VertexInputBit,
            0, 1, &after, 0, null, 0, null);
    }

    public void WaitForPendingFrames() => frames.Drain();

    public void Prepare(NativeMesh3D mesh)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (retainedModels is null)
        {
            throw new InvalidOperationException("AUR-MESH-012: Retained instance shaders were not configured.");
        }
        retainedModels.Prepare(mesh);
    }

    private void InitializeCameraDescriptors()
    {
        if (surfaceOnly)
        {
            return;
        }
        foreach (DescriptorSet set in cameraBindings.Sets)
        {
            Vulkan3DPass.WriteImage(plant, set, 1, shadowMap, shadowSampler);
            if (supportsDiffuseData)
            {
                Vulkan3DPass.WriteImage(plant, set, 3, shadowMap, shadowSampler);
            }
        }
    }

    /// <summary>Upload completes before returning. Publication remains the controller's decision.</summary>
    public ulong UploadStaticDiffuseLighting(StaticDiffuseLighting asset)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        frames.Drain();
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
            if (!surfaceOnly)
            {
                foreach (DescriptorSet set in cameraBindings.Sets)
                {
                    Vulkan3DPass.WriteImage(plant, set, 3, replacement, shadowSampler);
                }
            }
            diffuseData?.Dispose();
            diffuseData = replacement;
            diffuseAsset = asset;
            diffusePublished = false;
            ResetTemporalHistory();
            return uploaded.SignalFenceValue!.Value;
        }
        catch
        {
            replacement.Dispose();
            throw;
        }
    }

    public void PublishStaticDiffuseLighting(bool published)
    {
        bool next = published && diffuseAsset is not null;
        if (next != diffusePublished) ResetTemporalHistory();
        diffusePublished = next;
    }

    private void ValidateModernFormats()
    {
        plant.Vk.GetPhysicalDeviceProperties(plant.PhysicalDevice, out PhysicalDeviceProperties properties);
        if (surfaceOnly && properties.Limits.MaxColorAttachments < 5)
            throw new NotSupportedException("The surface renderer requires five color attachments for explicit material profiles.");
        var required = new (Format Format, FormatFeatureFlags Features)[]
        {
            (Format.R16G16B16A16Sfloat, FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.SampledImageBit | FormatFeatureFlags.SampledImageFilterLinearBit),
            (Format.R32Sfloat, FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.SampledImageBit),
            (Format.R32G32B32A32Sfloat, FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.SampledImageBit
                | FormatFeatureFlags.SampledImageFilterLinearBit | (surfaceOnly ? FormatFeatureFlags.ColorAttachmentBlendBit : 0)),
        };
        foreach (var format in required)
        {
            plant.Vk.GetPhysicalDeviceFormatProperties(plant.PhysicalDevice, format.Format, out FormatProperties formatProperties);
            if ((formatProperties.OptimalTilingFeatures & format.Features) != format.Features)
            {
                throw new NotSupportedException($"Modern 3D rendering requires {format.Features} for {format.Format} on {plant.Facts.PhysicalDeviceName}.");
            }
        }
    }

    private static void ValidateProgram(CompiledGraphicsProgram program)
    {
        CompiledVertexInput[] inputs = program.VertexInputs.OrderBy(input => input.Location).ToArray();
        string[] expectedFields = ["clipX", "clipY", "clipZ", "clipW", "light", "eye", "sun", "sky", "ground", "surface",
            "shadowX", "shadowY", "shadowZ", "shadowW", "shadowParameters"];
        bool motionOutput = inputs.Length == 4;
        if (motionOutput) expectedFields = [.. expectedFields, "previousX", "previousY", "previousZ", "previousW"];
        bool valid = inputs.Length is 3 or 4
            && (!motionOutput || (inputs[3].Location == 3 && inputs[3].PhysicalType == "float3"))
            && inputs[0].Location == 0 && inputs[0].PhysicalType == "float3"
            && inputs[1].Location == 1 && inputs[1].PhysicalType == "float3"
            && inputs[2].Location == 2 && inputs[2].PhysicalType == "float4"
            && (program.Resources.Count is 3 or 5 || program.PixelTargets.Count == 5 && program.Resources.Count == 1)
            && program.Resources[0].Set == 0 && program.Resources[0].Binding == 0
            && program.Resources[0].Kind == CompiledGraphicsResourceKind.UniformBuffer
            && program.Resources[0].Visibility.Order().SequenceEqual(new[] { CompiledGraphicsStage.Vertex, CompiledGraphicsStage.Fragment }.Order())
            && program.Material is { } material && material.Size == expectedFields.Length * 16
            && material.Fields.Count == expectedFields.Length
            && material.Fields.Select(field => field.Name).SequenceEqual(expectedFields)
            && material.Fields.All(field => field.PhysicalType == "float4")
            && material.Fields.Select(field => field.Offset).SequenceEqual(Enumerable.Range(0, expectedFields.Length).Select(index => index * 16));
        if (!valid)
        {
            throw new ArgumentException("Solid3D requires position/normal/color and packed scene uniforms (240 bytes, or 304 with previous positions), with shadow texture/sampler.", nameof(program));
        }
        if (program.PixelTargets.Count == 5) return;
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
        frames.Drain();
        surfaceLighting?.Dispose();
        temporal?.Dispose();
        bloom?.Dispose();
        foreach (var buffer in previousVertexFrames) buffer?.Dispose();
        previousGpu?.Dispose();
        motion?.Dispose();
        gpuTimings?.Dispose();
        transparency?.Dispose();
        refraction?.Dispose();
        waterReflection?.Dispose();
        retainedModels?.Dispose();
        modelRenderer?.Dispose();
        outputPass?.Dispose();
        shadowPass?.Dispose();
        outputVertices?.Dispose();
        pipeline?.Dispose();
        cameraBindings?.Dispose();
        if (setLayout.Handle != 0)
        {
            plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null);
        }
        foreach (var buffer in vertexFrames) buffer?.Dispose();
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
