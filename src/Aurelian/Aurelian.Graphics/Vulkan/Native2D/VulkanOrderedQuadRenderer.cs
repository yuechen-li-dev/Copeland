using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Draw;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.NativeForwardTextured;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.Graphics;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Resources.Uploads;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Rendering.Contracts.Shaders;
using Silk.NET.Vulkan;
using NativeBuffer = Silk.NET.Vulkan.Buffer;

namespace Aurelian.Graphics.Vulkan.Native2D;

public sealed unsafe class VulkanOrderedQuadRenderer : IDisposable
{
    public const int InitialQuadCapacity = 256;
    private const int VerticesPerQuad = 6;
    private const ulong FenceWaitTimeoutNanoseconds = 5_000_000_000;
    private const int MaximumBindingSets = 4096;
    private static long nextTextureId;

    private readonly AurelianVulkanPlant plant;
    private readonly CompiledGraphicsProgram program;
    private readonly Native2DPipelineOptions options;
    private readonly uint width;
    private readonly uint height;
    private readonly int vertexStride;
    private readonly CompiledVertexInput[] orderedVertexInputs;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commandPool;
    private readonly VulkanTextureUploader? textureUploader;
    private readonly VulkanCommandSubmitter submitter;
    private readonly AurelianVulkanRenderPass clearRenderPass;
    private readonly AurelianVulkanRenderPass? loadRenderPass;
    private readonly AurelianVulkanTexture renderTarget;
    private readonly AurelianVulkanFramebuffer clearFramebuffer;
    private readonly AurelianVulkanFramebuffer? loadFramebuffer;
    private readonly VulkanNativeFrameTarget? sharedTarget;
    private readonly DescriptorSetLayout descriptorSetLayout;
    private readonly DescriptorPool descriptorPool;
    private readonly Sampler sampler;
    private readonly bool usesTextureResources;
    private readonly AurelianVulkanGraphicsPipeline pipeline;
    private readonly Dictionary<ulong, TextureResource> textures = [];
    private readonly Dictionary<BindingKey, BindingResource> bindings = [];
    private readonly List<RenderSubmission> submissions = new(InitialQuadCapacity);

    private AurelianVulkanBuffer vertexBuffer;
    private byte[] vertexBytes;
    private BindingKey[] submissionKeys = new BindingKey[InitialQuadCapacity];
    private int vertexCapacityQuads = InitialQuadCapacity;
    private bool passActive;
    private bool disposed;

    public VulkanOrderedQuadRenderer(
        AurelianVulkanPlant plant,
        CompiledGraphicsProgram program,
        uint width = 256,
        uint height = 256,
        Native2DPipelineOptions? options = null)
        : this(plant, program, width, height, options, null)
    {
    }

    public VulkanOrderedQuadRenderer(
        AurelianVulkanPlant plant,
        CompiledGraphicsProgram program,
        VulkanNativeFrameTarget target,
        Native2DPipelineOptions? options = null)
        : this(
            plant,
            program,
            target?.Width ?? throw new ArgumentNullException(nameof(target)),
            target.Height,
            options,
            target)
    {
    }

    private VulkanOrderedQuadRenderer(
        AurelianVulkanPlant plant,
        CompiledGraphicsProgram program,
        uint width,
        uint height,
        Native2DPipelineOptions? options,
        VulkanNativeFrameTarget? sharedTarget)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(program);
        if (width == 0 || height == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The 2D target extent must be positive.");
        }

        options ??= Native2DPipelineOptions.Textured;
        ValidateProgram(program, options);
        this.plant = plant;
        this.program = program;
        this.options = options;
        this.sharedTarget = sharedTarget;
        this.width = width;
        this.height = height;
        if (sharedTarget is not null)
        {
            sharedTarget.ValidateCompatibility(plant, width, height);
        }
        orderedVertexInputs = program.VertexInputs
            .OrderBy(input => input.Order)
            .ToArray();
        vertexStride = orderedVertexInputs
            .Sum(input => VulkanForwardTexturedCanonicalFixture.PhysicalTypeSize(input.PhysicalType));
        usesTextureResources = program.Resources.Any(resource => resource.Kind == CompiledGraphicsResourceKind.Texture2D);

        allocator = new RawVulkanMemoryAllocator(plant);
        fences = VulkanFenceBundle.Create(plant);
        commandPool = VulkanCommandBufferPool.Create(plant);
        textureUploader = usesTextureResources
            ? new VulkanTextureUploader(plant, allocator, commandPool, fences)
            : null;
        submitter = new VulkanCommandSubmitter(plant, commandPool, fences);

        renderTarget = sharedTarget?.Texture ?? VulkanNativeForwardTexturedRenderer.CreateTexture(
            plant,
            allocator,
            width,
            height,
            VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.TransferSource,
            VulkanMemoryUsage.GpuOnly,
            "native-2d.target");
        clearRenderPass = CreateRenderPass(VulkanAttachmentLoadOp.Clear, VulkanResourceLayout.Undefined);
        clearFramebuffer = CreateFramebuffer(clearRenderPass);
        if (sharedTarget is not null)
        {
            loadRenderPass = CreateRenderPass(VulkanAttachmentLoadOp.Load, VulkanResourceLayout.TransferSource);
            loadFramebuffer = CreateFramebuffer(loadRenderPass);
        }
        descriptorSetLayout = VulkanNativeForwardTexturedRenderer.CreateDescriptorSetLayout(plant, program);
        pipeline = VulkanNativeForwardTexturedRenderer.CreatePipeline(
            plant,
            clearRenderPass,
            program,
            vertexStride,
            descriptorSetLayout,
            options.StraightAlphaBlend);
        sampler = usesTextureResources
            ? VulkanNativeForwardTexturedRenderer.CreateSampler(plant, options.LinearFiltering)
            : default;
        descriptorPool = CreateDescriptorPool();
        vertexBuffer = CreateVertexBuffer(vertexCapacityQuads);
        vertexBytes = new byte[checked(vertexCapacityQuads * VerticesPerQuad * vertexStride)];
    }

    public uint Width => width;

    public uint Height => height;

    public int TextureCount => textures.Count;

    public int VertexCapacityQuads => vertexCapacityQuads;

    public Native2DPipelineKind PipelineKind => options.Kind;

    public bool LinearFiltering => options.LinearFiltering;

    public bool StraightAlphaBlend => options.StraightAlphaBlend;

    public Native2DTextureHandle CreateTexture(uint textureWidth, uint textureHeight, ReadOnlySpan<byte> rgba8)
    {
        return CreateTextureCore(textureWidth, textureHeight, rgba8.ToArray());
    }

    public Native2DTextureHandle CreateTexture(uint textureWidth, uint textureHeight, byte[] rgba8)
    {
        ArgumentNullException.ThrowIfNull(rgba8);
        return CreateTextureCore(textureWidth, textureHeight, rgba8);
    }

    private Native2DTextureHandle CreateTextureCore(uint textureWidth, uint textureHeight, byte[] rgba8)
    {
        ThrowIfDisposed();
        if (passActive)
        {
            throw new InvalidOperationException("Textures cannot be created during an active 2D pass.");
        }
        if (textureUploader is null)
        {
            throw new InvalidOperationException("The textureless analytic pipeline cannot create textures.");
        }
        if (textureWidth == 0 || textureHeight == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textureWidth), "Texture extent must be positive.");
        }
        ulong requiredBytes = checked((ulong)textureWidth * textureHeight * 4);
        if ((ulong)rgba8.Length != requiredBytes)
        {
            throw new ArgumentException($"RGBA8 payload is {rgba8.Length} bytes; extent requires {requiredBytes} bytes.", nameof(rgba8));
        }

        AurelianVulkanTexture texture = VulkanNativeForwardTexturedRenderer.CreateTexture(
            plant,
            allocator,
            textureWidth,
            textureHeight,
            VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination,
            VulkanMemoryUsage.GpuOnly,
            "native-2d.texture");
        VulkanTextureUploadResult upload = textureUploader.Upload(new VulkanTextureUploadRequest(
            texture,
            rgba8,
            "native-2d.texture-upload"));
        if (!upload.Success)
        {
            texture.Dispose();
            throw new InvalidOperationException("Texture upload failed: " + string.Join("; ", upload.Diagnostics.Select(item => item.Message)));
        }

        long textureId = Interlocked.Increment(ref nextTextureId);
        if (textureId <= 0)
        {
            texture.Dispose();
            throw new InvalidOperationException("Native 2D texture handle space was exhausted.");
        }
        var handle = new Native2DTextureHandle((ulong)textureId);
        textures.Add(handle.Value, new TextureResource(texture));
        return handle;
    }

    public void DisposeTexture(Native2DTextureHandle handle)
    {
        ThrowIfDisposed();
        if (passActive)
        {
            throw new InvalidOperationException("Textures cannot be disposed during an active 2D pass.");
        }
        if (!textures.Remove(handle.Value, out TextureResource? texture))
        {
            throw UnknownTexture(handle);
        }

        _ = plant.Vk.DeviceWaitIdle(plant.Device);
        BindingKey[] staleKeys = bindings.Keys.Where(key => key.TextureId == handle.Value).ToArray();
        foreach (BindingKey key in staleKeys)
        {
            BindingResource binding = bindings[key];
            DescriptorSet set = binding.DescriptorSet;
            _ = plant.Vk.FreeDescriptorSets(plant.Device, descriptorPool, 1, &set);
            binding.MaterialBuffer.Dispose();
            bindings.Remove(key);
        }
        texture.Texture.Dispose();
    }

    public void UpdateTexture(Native2DTextureHandle handle, uint width, uint height, byte[] rgba8)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rgba8);
        if (passActive)
        {
            throw new InvalidOperationException("Textures cannot be updated during an active 2D pass.");
        }
        if (textureUploader is null)
        {
            throw new InvalidOperationException("The textureless analytic pipeline cannot update textures.");
        }
        if (!textures.TryGetValue(handle.Value, out TextureResource? resource))
        {
            throw UnknownTexture(handle);
        }
        if (resource.Texture.Width != width || resource.Texture.Height != height)
        {
            throw new ArgumentException("Updated texture extent must match the existing texture extent.", nameof(width));
        }
        if ((ulong)rgba8.Length != checked((ulong)width * height * 4))
        {
            throw new ArgumentException("Updated RGBA8 payload length does not match the texture extent.", nameof(rgba8));
        }

        VulkanTextureUploadResult upload = textureUploader.Upload(new VulkanTextureUploadRequest(
            resource.Texture,
            rgba8,
            "native-2d.texture-update"));
        if (!upload.Success)
        {
            throw new InvalidOperationException("Texture update failed: " + string.Join("; ", upload.Diagnostics.Select(item => item.Message)));
        }
    }

    public void Begin2D()
    {
        ThrowIfDisposed();
        if (passActive)
        {
            throw new InvalidOperationException("Begin2D cannot be nested.");
        }
        submissions.Clear();
        passActive = true;
    }

    public void SubmitQuad(NativeQuadSubmission submission)
    {
        ThrowIfDisposed();
        if (!passActive)
        {
            throw new InvalidOperationException("SubmitQuad requires an active 2D pass.");
        }
        if (options.Kind != Native2DPipelineKind.Textured)
        {
            throw new InvalidOperationException("SubmitQuad requires the textured 2D pipeline.");
        }
        ValidateSubmission(submission.Texture, submission);
        submissions.Add(new RenderSubmission(
            submission.Destination,
            submission.Uv,
            submission.Texture,
            submission.Tint,
            default,
            default,
            default));
    }

    public void SubmitMsdfQuad(NativeMsdfQuadSubmission submission)
    {
        ThrowIfDisposed();
        if (!passActive)
        {
            throw new InvalidOperationException("SubmitMsdfQuad requires an active 2D pass.");
        }
        if (options.Kind != Native2DPipelineKind.MsdfText)
        {
            throw new InvalidOperationException("SubmitMsdfQuad requires the MSDF text pipeline.");
        }
        Native2DSubmissionValidator.ValidateValues(submission);
        if (!textures.ContainsKey(submission.AtlasTexture.Value))
        {
            throw UnknownTexture(submission.AtlasTexture);
        }
        submissions.Add(new RenderSubmission(
            submission.Destination,
            submission.Uv,
            submission.AtlasTexture,
            submission.Color,
            submission.Msdf,
            default,
            default));
    }

    public void SubmitAnalyticShape(NativeAnalyticShapeSubmission submission)
    {
        ThrowIfDisposed();
        if (!passActive)
        {
            throw new InvalidOperationException("SubmitAnalyticShape requires an active 2D pass.");
        }
        if (options.Kind != Native2DPipelineKind.AnalyticShape2D)
        {
            throw new InvalidOperationException("SubmitAnalyticShape requires the analytic shape 2D pipeline.");
        }
        Native2DSubmissionValidator.ValidateValues(submission);
        float radius = submission.Kind == NativeAnalyticShapeKind.Pill
            ? MathF.Min(submission.ShapeSize.Width, submission.ShapeSize.Height) / 2
            : submission.Radius;
        submissions.Add(new RenderSubmission(
            submission.Destination,
            submission.LocalCoordinates,
            default,
            submission.FillColor,
            default,
            new AnalyticParameters(
                submission.Kind,
                submission.ShapeSize,
                radius,
                submission.BorderColor,
                submission.BorderWidth),
            default));
    }

    public void SubmitSoftShockwave(NativeSoftShockwaveSubmission submission)
    {
        ThrowIfDisposed();
        if (!passActive)
        {
            throw new InvalidOperationException("SubmitSoftShockwave requires an active 2D pass.");
        }
        if (options.Kind != Native2DPipelineKind.SoftShockwave)
        {
            throw new InvalidOperationException("SubmitSoftShockwave requires the soft shockwave pipeline.");
        }
        Native2DSubmissionValidator.ValidateValues(submission);
        submissions.Add(new RenderSubmission(
            submission.Destination,
            submission.LocalCoordinates,
            default,
            submission.Color,
            default,
            default,
            new SoftShockwaveParameters(
                submission.Age,
                submission.Lifetime,
                submission.Radius,
                submission.Thickness,
                submission.Intensity,
                submission.Seed)));
    }

    public Native2DPassResult End2D(bool captureReadback = false)
    {
        if (sharedTarget is not null)
        {
            Cancel2D();
            throw new InvalidOperationException(
                "A renderer bound to a shared native frame target must be presented through VulkanNativeFrameSession.");
        }
        return End2DCore(captureReadback, clear: true, default);
    }

    internal Native2DPassResult EndShared2D(bool clear, VulkanColorClearValue clearColor)
    {
        if (sharedTarget is null)
        {
            throw new InvalidOperationException("The renderer is not bound to a shared native frame target.");
        }
        sharedTarget.ThrowIfDisposed();
        return End2DCore(captureReadback: false, clear, clearColor);
    }

    internal bool Targets(VulkanNativeFrameTarget target)
    {
        return ReferenceEquals(sharedTarget, target);
    }

    internal void Cancel2D()
    {
        submissions.Clear();
        passActive = false;
    }

    private Native2DPassResult End2DCore(
        bool captureReadback,
        bool clear,
        VulkanColorClearValue frameClearColor)
    {
        ThrowIfDisposed();
        if (!passActive)
        {
            throw new InvalidOperationException("End2D requires an active 2D pass.");
        }

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int descriptorAllocations = 0;
        int descriptorWrites = 0;
        AurelianVulkanBuffer? readbackBuffer = null;
        try
        {
            EnsureVertexCapacity(submissions.Count);
            Stopwatch uploadWatch = Stopwatch.StartNew();
            int vertexByteCount = BuildVertices();
            Require(vertexBuffer.Write(vertexBytes.AsSpan(0, vertexByteCount)).Success, "Vertex upload failed.");
            uploadWatch.Stop();

            for (int index = 0; index < submissions.Count; index++)
            {
                submissionKeys[index] = BindingKey.From(submissions[index]);
            }
            MakeBindingRoom(submissionKeys, submissions.Count);
            for (int index = 0; index < submissions.Count; index++)
            {
                BindingKey key = submissionKeys[index];
                if (!bindings.ContainsKey(key))
                {
                    bindings.Add(key, CreateBinding(key, submissions[index]));
                    descriptorAllocations++;
                    descriptorWrites += program.Resources.Count;
                }
            }

            if (captureReadback)
            {
                readbackBuffer = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(
                    plant,
                    allocator,
                    checked((ulong)width * height * 4),
                    VulkanBufferUsage.TransferDestination,
                    VulkanMemoryUsage.GpuToCpu,
                    "native-2d.readback");
            }

            Stopwatch recordWatch = Stopwatch.StartNew();
            VulkanCommandBufferLease commandBuffer = commandPool.Rent(fences.CommandListFence.LastKnownCompletedValue);
            Require(commandBuffer.Begin().Success, "Command buffer begin failed.");
            var renderPassEncoder = new VulkanRenderPassCommandEncoder();
            AurelianVulkanRenderPass activeRenderPass = clear
                ? clearRenderPass
                : loadRenderPass ?? throw new InvalidOperationException("The renderer has no shared-target load pass.");
            AurelianVulkanFramebuffer activeFramebuffer = clear
                ? clearFramebuffer
                : loadFramebuffer ?? throw new InvalidOperationException("The renderer has no shared-target load framebuffer.");
            VulkanColorClearValue clearValue = sharedTarget is null
                ? options.TransparentClear
                    ? new VulkanColorClearValue(0, 0, 0, 0)
                    : new VulkanColorClearValue(16f / 255f, 32f / 255f, 64f / 255f, 1)
                : frameClearColor;
            VulkanRenderPassBeginResult begin = renderPassEncoder.Begin(
                plant,
                commandBuffer,
                new VulkanRenderPassBeginRequest(
                    activeRenderPass,
                    activeFramebuffer,
                    clearValue));
            Require(begin.Success, "Render pass begin failed.");

            int drawCalls = RecordOrderedDraws(commandBuffer, begin.Scope!.Value, submissionKeys, submissions.Count);
            Require(renderPassEncoder.End(plant, commandBuffer, begin.Scope.Value).Success, "Render pass end failed.");
            if (readbackBuffer is not null)
            {
                RecordReadback(commandBuffer.CommandBuffer, readbackBuffer);
            }
            Require(commandBuffer.End().Success, "Command buffer end failed.");
            recordWatch.Stop();

            Stopwatch submitWatch = Stopwatch.StartNew();
            VulkanCommandSubmitResult submit = submitter.Submit(new VulkanCommandSubmitRequest(
                commandBuffer,
                WaitForCompletion: true,
                TimeoutNanoseconds: FenceWaitTimeoutNanoseconds,
                DebugName: "native-2d.pass"));
            Require(submit.Success, "2D pass submission failed.");
            submitWatch.Stop();

            Stopwatch readbackWatch = Stopwatch.StartNew();
            byte[]? pixels = readbackBuffer?.ReadBytes(checked((int)(width * height * 4)));
            string? hash = pixels is null
                ? null
                : Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
            readbackWatch.Stop();

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var metrics = new Native2DPassMetrics(
                submissions.Count,
                drawCalls,
                CommandBuffers: 1,
                QueueSubmissions: 1,
                BufferUploads: 1,
                DescriptorSetAllocations: descriptorAllocations,
                DescriptorWrites: descriptorWrites,
                VertexCapacityQuads: vertexCapacityQuads,
                Math.Round(uploadWatch.Elapsed.TotalMilliseconds, 3),
                Math.Round(recordWatch.Elapsed.TotalMilliseconds, 3),
                Math.Round(submitWatch.Elapsed.TotalMilliseconds, 3),
                Math.Round(readbackWatch.Elapsed.TotalMilliseconds, 3),
                allocatedBytes);
            return new Native2DPassResult(metrics, pixels, hash);
        }
        finally
        {
            readbackBuffer?.Dispose();
            submissions.Clear();
            passActive = false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        passActive = false;
        _ = plant.Vk.DeviceWaitIdle(plant.Device);
        foreach (BindingResource binding in bindings.Values)
        {
            binding.MaterialBuffer.Dispose();
        }
        bindings.Clear();
        foreach (TextureResource texture in textures.Values)
        {
            texture.Texture.Dispose();
        }
        textures.Clear();
        vertexBuffer.Dispose();
        plant.Vk.DestroyDescriptorPool(plant.Device, descriptorPool, null);
        if (usesTextureResources)
        {
            plant.Vk.DestroySampler(plant.Device, sampler, null);
        }
        pipeline.Dispose();
        loadFramebuffer?.Dispose();
        clearFramebuffer.Dispose();
        loadRenderPass?.Dispose();
        clearRenderPass.Dispose();
        plant.Vk.DestroyDescriptorSetLayout(plant.Device, descriptorSetLayout, null);
        if (sharedTarget is null)
        {
            renderTarget.Dispose();
        }
        submitter.Dispose();
        textureUploader?.Dispose();
        commandPool.Dispose();
        fences.Dispose();
        allocator.Dispose();
    }

    private static void ValidateProgram(CompiledGraphicsProgram program, Native2DPipelineOptions options)
    {
        if (program.FormatVersion != CompiledGraphicsProgram.CurrentFormatVersion)
        {
            throw new ArgumentException("Compiled graphics program format is unsupported.", nameof(program));
        }
        CompiledVertexInput[] inputs = program.VertexInputs.OrderBy(input => input.Order).ToArray();
        int expectedInputCount = options.Kind == Native2DPipelineKind.MsdfText ? 3 : 2;
        bool inputMismatch = inputs.Length != expectedInputCount;
        if (!inputMismatch)
        {
            inputMismatch = inputMismatch
                || inputs[0].Name != "position"
                || inputs[0].PhysicalType != "float3"
                || inputs[1].Name != (options.Kind is Native2DPipelineKind.AnalyticShape2D or Native2DPipelineKind.SoftShockwave ? "local" : "uv")
                || inputs[1].PhysicalType != "float2";
        }
        if (!inputMismatch && options.Kind == Native2DPipelineKind.MsdfText)
        {
            inputMismatch = inputs[2].Name != "fieldScale"
                || inputs[2].PhysicalType != "f32";
        }
        if (inputMismatch)
        {
            throw new ArgumentException("Native 2D requires position: float3 followed by uv: float2.", nameof(program));
        }
        bool resourcesValid = options.Kind is Native2DPipelineKind.AnalyticShape2D or Native2DPipelineKind.SoftShockwave
            ? program.Resources.Count == 1 && program.Resources.Single().Kind == CompiledGraphicsResourceKind.UniformBuffer
            : program.Resources.Count == 3
                && program.Resources.Count(resource => resource.Kind == CompiledGraphicsResourceKind.Texture2D) == 1
                && program.Resources.Count(resource => resource.Kind == CompiledGraphicsResourceKind.Sampler) == 1
                && program.Resources.Count(resource => resource.Kind == CompiledGraphicsResourceKind.UniformBuffer) == 1;
        if (!resourcesValid)
        {
            throw new ArgumentException("Native 2D resources do not match the selected compiler-described pipeline variant.", nameof(program));
        }
        CompiledMaterialLayout material = program.Material
            ?? throw new ArgumentException("Native 2D requires compiler-described material metadata.", nameof(program));
        string[] expectedFields = options.Kind switch
        {
            Native2DPipelineKind.MsdfText => ["tint", "pixelRange", "threshold"],
            Native2DPipelineKind.AnalyticShape2D => ["fillColor", "borderColor", "halfSize", "radius", "borderWidth", "shapeKind"],
            Native2DPipelineKind.SoftShockwave => ["color", "age", "lifetime", "radius", "thickness", "intensity", "seed"],
            _ => ["tint", "roughness"],
        };
        if (!material.Fields.OrderBy(field => field.Order).Select(field => field.Name).SequenceEqual(expectedFields, StringComparer.Ordinal))
        {
            throw new ArgumentException($"The {options.Kind} pipeline material shape is incompatible with native 2D.", nameof(program));
        }

        int stride = inputs.Sum(input => VulkanForwardTexturedCanonicalFixture.PhysicalTypeSize(input.PhysicalType));
        VulkanForwardTexturedFixture reflectionFixture = options.Kind == Native2DPipelineKind.Textured
            ? VulkanForwardTexturedCanonicalFixture.Create(program)
            : new VulkanForwardTexturedFixture(
                new byte[checked(stride * VerticesPerQuad)],
                stride,
                VerticesPerQuad,
                [255, 255, 255, 255],
                1,
                1,
                new byte[material.Size],
                program.Resources.Select(resource => resource.Binding).ToHashSet());
        VulkanForwardTexturedValidation reflectionValidation = VulkanNativeForwardTexturedRenderer.Validate(
            program,
            reflectionFixture,
            requireTexturedResourceShape: options.Kind is not (Native2DPipelineKind.AnalyticShape2D or Native2DPipelineKind.SoftShockwave));
        if (!reflectionValidation.Success)
        {
            throw new ArgumentException(
                "Compiled graphics program failed the native 2D SPIR-V cross-check: " + string.Join("; ", reflectionValidation.Errors),
                nameof(program));
        }
    }

    private void ValidateSubmission(Native2DTextureHandle texture, NativeQuadSubmission submission)
    {
        Native2DSubmissionValidator.ValidateValues(submission);
        if (!textures.ContainsKey(texture.Value))
        {
            throw UnknownTexture(texture);
        }
    }

    private int BuildVertices()
    {
        Span<Vertex> vertices = stackalloc Vertex[VerticesPerQuad];
        for (int quadIndex = 0; quadIndex < submissions.Count; quadIndex++)
        {
            RenderSubmission submission = submissions[quadIndex];
            float left = ToNdcX(submission.Destination.X);
            float right = ToNdcX(submission.Destination.X + submission.Destination.Width);
            float top = ToNdcY(submission.Destination.Y);
            float bottom = ToNdcY(submission.Destination.Y + submission.Destination.Height);
            // Vulkan evaluates front-face winding after the positive-height viewport transform.
            // This order therefore reaches the shader as counter-clockwise in framebuffer space.
            vertices[0] = new Vertex(left, bottom, 0, submission.Uv.U0, submission.Uv.V1, submission.Msdf.FieldScale);
            vertices[1] = new Vertex(right, bottom, 0, submission.Uv.U1, submission.Uv.V1, submission.Msdf.FieldScale);
            vertices[2] = new Vertex(right, top, 0, submission.Uv.U1, submission.Uv.V0, submission.Msdf.FieldScale);
            vertices[3] = new Vertex(left, bottom, 0, submission.Uv.U0, submission.Uv.V1, submission.Msdf.FieldScale);
            vertices[4] = new Vertex(right, top, 0, submission.Uv.U1, submission.Uv.V0, submission.Msdf.FieldScale);
            vertices[5] = new Vertex(left, top, 0, submission.Uv.U0, submission.Uv.V0, submission.Msdf.FieldScale);
            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                WriteVertex(vertexBytes, (quadIndex * VerticesPerQuad + vertexIndex) * vertexStride, vertices[vertexIndex]);
            }
        }
        return checked(submissions.Count * VerticesPerQuad * vertexStride);
    }

    private void WriteVertex(byte[] bytes, int baseOffset, Vertex vertex)
    {
        int offset = baseOffset;
        foreach (CompiledVertexInput input in orderedVertexInputs)
        {
            if (input.Name == "position" && input.PhysicalType == "float3")
            {
                WriteFloat(bytes, offset, vertex.X);
                WriteFloat(bytes, offset + 4, vertex.Y);
                WriteFloat(bytes, offset + 8, vertex.Z);
            }
            else if ((input.Name == "uv" || input.Name == "local") && input.PhysicalType == "float2")
            {
                WriteFloat(bytes, offset, vertex.U);
                WriteFloat(bytes, offset + 4, vertex.V);
            }
            else if (input.Name == "fieldScale" && input.PhysicalType == "f32")
            {
                WriteFloat(bytes, offset, vertex.FieldScale);
            }
            else
            {
                throw new InvalidOperationException($"Native 2D cannot populate vertex input '{input.Name}' of type '{input.PhysicalType}'.");
            }
            offset += VulkanForwardTexturedCanonicalFixture.PhysicalTypeSize(input.PhysicalType);
        }
    }

    private int RecordOrderedDraws(
        VulkanCommandBufferLease commandBuffer,
        VulkanRenderPassScope scope,
        BindingKey[] keys,
        int keyCount)
    {
        if (keyCount == 0)
        {
            return 0;
        }
        Viewport viewport = new()
        {
            X = 0,
            Y = 0,
            Width = clearFramebuffer.Width,
            Height = clearFramebuffer.Height,
            MinDepth = 0,
            MaxDepth = 1,
        };
        Rect2D scissor = new(new Offset2D(0, 0), new Extent2D(clearFramebuffer.Width, clearFramebuffer.Height));
        NativeBuffer nativeVertexBuffer = vertexBuffer.NativeBuffer;
        ulong vertexOffset = 0;
        plant.Vk.CmdSetViewport(commandBuffer.CommandBuffer, 0, 1, &viewport);
        plant.Vk.CmdSetScissor(commandBuffer.CommandBuffer, 0, 1, &scissor);
        plant.Vk.CmdBindPipeline(commandBuffer.CommandBuffer, PipelineBindPoint.Graphics, pipeline.NativePipeline);
        plant.Vk.CmdBindVertexBuffers(commandBuffer.CommandBuffer, 0, 1, &nativeVertexBuffer, &vertexOffset);

        int drawCalls = 0;
        int startQuad = 0;
        while (startQuad < keyCount)
        {
            BindingKey key = keys[startQuad];
            int endQuad = startQuad + 1;
            while (endQuad < keyCount && keys[endQuad] == key)
            {
                endQuad++;
            }

            DescriptorSet descriptorSet = bindings[key].DescriptorSet;
            plant.Vk.CmdBindDescriptorSets(
                commandBuffer.CommandBuffer,
                PipelineBindPoint.Graphics,
                pipeline.NativePipelineLayout,
                0,
                1,
                &descriptorSet,
                0,
                null);
            plant.Vk.CmdDraw(
                commandBuffer.CommandBuffer,
                checked((uint)((endQuad - startQuad) * VerticesPerQuad)),
                1,
                checked((uint)(startQuad * VerticesPerQuad)),
                0);
            drawCalls++;
            startQuad = endQuad;
        }
        return drawCalls;
    }

    private void MakeBindingRoom(BindingKey[] currentSubmissionKeys, int keyCount)
    {
        if (bindings.Count + keyCount <= MaximumBindingSets)
        {
            return;
        }
        var currentKeys = new HashSet<BindingKey>();
        for (int index = 0; index < keyCount; index++)
        {
            currentKeys.Add(currentSubmissionKeys[index]);
        }
        if (currentKeys.Count > MaximumBindingSets)
        {
            throw new InvalidOperationException($"Native 2D pass exceeds {MaximumBindingSets} simultaneously required material bindings.");
        }
        int missing = currentKeys.Count(key => !bindings.ContainsKey(key));
        if (bindings.Count + missing <= MaximumBindingSets)
        {
            return;
        }

        // Every preceding End2DCore submission waits for completion. Only bindings absent
        // from this pass may be released; current draws retain their original painter order.
        foreach (BindingKey key in bindings.Keys.Where(key => !currentKeys.Contains(key)).ToArray())
        {
            BindingResource binding = bindings[key];
            DescriptorSet set = binding.DescriptorSet;
            Require(plant.Vk.FreeDescriptorSets(plant.Device, descriptorPool, 1, &set) == Result.Success,
                "Stale native material descriptor release failed.");
            binding.MaterialBuffer.Dispose();
            bindings.Remove(key);
        }
    }

    private BindingResource CreateBinding(BindingKey key, RenderSubmission submission)
    {
        if (bindings.Count >= MaximumBindingSets)
        {
            throw new InvalidOperationException($"Native 2D binding capacity of {MaximumBindingSets} unique texture/tint pairs was exceeded.");
        }

        CompiledMaterialLayout material = program.Material!;
        byte[] materialBytes = new byte[material.Size];
        if (options.Kind is Native2DPipelineKind.Textured or Native2DPipelineKind.MsdfText)
        {
            WriteMaterialColor(materialBytes, material, "tint", submission.Tint);
        }
        if (options.Kind == Native2DPipelineKind.Textured)
        {
            CompiledMaterialField roughness = material.Fields.Single(field => field.Name == "roughness");
            WriteFloat(materialBytes, roughness.Offset, 1);
        }
        else if (options.Kind == Native2DPipelineKind.MsdfText)
        {
            WriteMaterialFloat(materialBytes, material, "pixelRange", submission.Msdf.PixelRange);
            WriteMaterialFloat(materialBytes, material, "threshold", submission.Msdf.Threshold);
        }
        else if (options.Kind == Native2DPipelineKind.AnalyticShape2D)
        {
            WriteMaterialColor(materialBytes, material, "fillColor", submission.Tint);
            WriteMaterialColor(materialBytes, material, "borderColor", submission.Analytic.BorderColor);
            CompiledMaterialField halfSize = material.Fields.Single(field => field.Name == "halfSize");
            WriteFloat(materialBytes, halfSize.Offset, submission.Analytic.ShapeSize.Width / 2);
            WriteFloat(materialBytes, halfSize.Offset + 4, submission.Analytic.ShapeSize.Height / 2);
            WriteMaterialFloat(materialBytes, material, "radius", submission.Analytic.Radius);
            WriteMaterialFloat(materialBytes, material, "borderWidth", submission.Analytic.BorderWidth);
            CompiledMaterialField kind = material.Fields.Single(field => field.Name == "shapeKind");
            BinaryPrimitives.WriteUInt32LittleEndian(materialBytes.AsSpan(kind.Offset, 4), (uint)(submission.Analytic.Kind == NativeAnalyticShapeKind.Circle ? 1 : 0));
        }
        else
        {
            WriteMaterialColor(materialBytes, material, "color", submission.Tint);
            WriteMaterialFloat(materialBytes, material, "age", submission.Shockwave.Age);
            WriteMaterialFloat(materialBytes, material, "lifetime", submission.Shockwave.Lifetime);
            WriteMaterialFloat(materialBytes, material, "radius", submission.Shockwave.Radius);
            WriteMaterialFloat(materialBytes, material, "thickness", submission.Shockwave.Thickness);
            WriteMaterialFloat(materialBytes, material, "intensity", submission.Shockwave.Intensity);
            WriteMaterialFloat(materialBytes, material, "seed", submission.Shockwave.Seed);
        }
        AurelianVulkanBuffer materialBuffer = VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(
            plant,
            allocator,
            (ulong)materialBytes.Length,
            VulkanBufferUsage.Uniform,
            VulkanMemoryUsage.CpuToGpu,
            "native-2d.material");
        Require(materialBuffer.Write(materialBytes).Success, "Material upload failed.");

        DescriptorSetLayout setLayout = descriptorSetLayout;
        DescriptorSetAllocateInfo allocateInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout,
        };
        Result allocateResult = plant.Vk.AllocateDescriptorSets(plant.Device, &allocateInfo, out DescriptorSet descriptorSet);
        if (allocateResult != Result.Success)
        {
            materialBuffer.Dispose();
            throw new InvalidOperationException($"Descriptor set allocation failed with {allocateResult}.");
        }

        DescriptorImageInfo textureInfo = default;
        if (program.Resources.Any(resource => resource.Kind == CompiledGraphicsResourceKind.Texture2D))
        {
            TextureResource texture = textures[key.TextureId];
            textureInfo = new(default, texture.Texture.NativeImageView!.Value, ImageLayout.ShaderReadOnlyOptimal);
        }
        DescriptorImageInfo samplerInfo = new(sampler, default, ImageLayout.Undefined);
        DescriptorBufferInfo materialInfo = new(materialBuffer.NativeBuffer, 0, materialBuffer.SizeBytes);
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[program.Resources.Count];
        int writeIndex = 0;
        foreach (CompiledGraphicsResource resource in program.Resources.OrderBy(resource => resource.Order))
        {
            WriteDescriptorSet write = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = (uint)resource.Binding,
                DescriptorCount = 1,
                DescriptorType = VulkanNativeForwardTexturedRenderer.MapDescriptorType(resource.Kind),
            };
            if (resource.Kind == CompiledGraphicsResourceKind.Texture2D)
            {
                write.PImageInfo = &textureInfo;
            }
            else if (resource.Kind == CompiledGraphicsResourceKind.Sampler)
            {
                write.PImageInfo = &samplerInfo;
            }
            else
            {
                write.PBufferInfo = &materialInfo;
            }
            writes[writeIndex++] = write;
        }
        plant.Vk.UpdateDescriptorSets(plant.Device, (uint)program.Resources.Count, writes, 0, null);
        return new BindingResource(descriptorSet, materialBuffer);
    }

    private void EnsureVertexCapacity(int requiredQuads)
    {
        if (requiredQuads <= vertexCapacityQuads)
        {
            return;
        }
        int newCapacity = vertexCapacityQuads;
        while (newCapacity < requiredQuads)
        {
            newCapacity = checked(newCapacity * 2);
        }
        vertexBuffer.Dispose();
        vertexCapacityQuads = newCapacity;
        vertexBuffer = CreateVertexBuffer(newCapacity);
        vertexBytes = new byte[checked(newCapacity * VerticesPerQuad * vertexStride)];
        submissionKeys = new BindingKey[newCapacity];
    }

    private AurelianVulkanBuffer CreateVertexBuffer(int capacityQuads)
        => VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(
            plant,
            allocator,
            checked((ulong)(capacityQuads * VerticesPerQuad * vertexStride)),
            VulkanBufferUsage.Vertex,
            VulkanMemoryUsage.CpuToGpu,
            "native-2d.vertices");

    private AurelianVulkanRenderPass CreateRenderPass(
        VulkanAttachmentLoadOp loadOperation,
        VulkanResourceLayout initialLayout)
    {
        VulkanRenderPassCreateResult result = VulkanRenderPassFactory.Create(
            plant,
            new VulkanRenderPassDescriptor([
                new VulkanRenderPassAttachmentDescriptor(
                    "Color0",
                    renderTarget.Format,
                    loadOperation,
                    VulkanAttachmentStoreOp.Store,
                    initialLayout,
                    VulkanResourceLayout.TransferSource),
            ]));
        Require(result.Success, "Native 2D render pass creation failed.");
        return result.RenderPass!;
    }

    private AurelianVulkanFramebuffer CreateFramebuffer(AurelianVulkanRenderPass pass)
    {
        VulkanFramebufferCreateResult result = VulkanFramebufferFactory.Create(
            plant,
            pass,
            new VulkanFramebufferDescriptor(width, height, [renderTarget]));
        Require(result.Success, "Native 2D framebuffer creation failed.");
        return result.Framebuffer!;
    }

    private DescriptorPool CreateDescriptorPool()
    {
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[3];
        uint poolSizeCount = 0;
        foreach (DescriptorType descriptorType in program.Resources.Select(resource => VulkanNativeForwardTexturedRenderer.MapDescriptorType(resource.Kind)).Distinct())
        {
            sizes[poolSizeCount++] = new DescriptorPoolSize(descriptorType, MaximumBindingSets);
        }
        DescriptorPoolCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = MaximumBindingSets,
            PoolSizeCount = poolSizeCount,
            PPoolSizes = sizes,
        };
        Result result = plant.Vk.CreateDescriptorPool(plant.Device, &createInfo, null, out DescriptorPool pool);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Native 2D descriptor pool creation failed with {result}.");
        }
        return pool;
    }

    private void RecordReadback(CommandBuffer commandBuffer, AurelianVulkanBuffer readback)
    {
        BufferImageCopy region = new()
        {
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new Extent3D(width, height, 1),
        };
        plant.Vk.CmdCopyImageToBuffer(commandBuffer, renderTarget.NativeImage, ImageLayout.TransferSrcOptimal, readback.NativeBuffer, 1, &region);
        BufferMemoryBarrier barrier = new()
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.HostReadBit,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Buffer = readback.NativeBuffer,
            Size = readback.SizeBytes,
        };
        plant.Vk.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.HostBit,
            0,
            0,
            null,
            1,
            &barrier,
            0,
            null);
    }

    private float ToNdcX(float pixelX)
        => pixelX / width * 2 - 1;

    private float ToNdcY(float pixelY)
        => pixelY / height * 2 - 1;

    private static void WriteFloat(byte[] destination, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));

    private static void WriteMaterialFloat(byte[] destination, CompiledMaterialLayout material, string name, float value)
    {
        CompiledMaterialField field = material.Fields.Single(candidate => candidate.Name == name);
        WriteFloat(destination, field.Offset, value);
    }

    private static void WriteMaterialColor(byte[] destination, CompiledMaterialLayout material, string name, Native2DTint value)
    {
        CompiledMaterialField field = material.Fields.Single(candidate => candidate.Name == name);
        WriteFloat(destination, field.Offset, value.Red);
        WriteFloat(destination, field.Offset + 4, value.Green);
        WriteFloat(destination, field.Offset + 8, value.Blue);
        WriteFloat(destination, field.Offset + 12, value.Alpha);
    }

    private static InvalidOperationException UnknownTexture(Native2DTextureHandle handle)
        => new($"Texture handle {handle.Value} is unknown or disposed.");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(disposed, this);

    private sealed record TextureResource(AurelianVulkanTexture Texture);

    private sealed record BindingResource(DescriptorSet DescriptorSet, AurelianVulkanBuffer MaterialBuffer);

    private readonly record struct BindingKey(
        ulong TextureId,
        int Red,
        int Green,
        int Blue,
        int Alpha,
        int PixelRange,
        int Threshold,
        int HalfWidth,
        int HalfHeight,
        int Radius,
        int BorderRed,
        int BorderGreen,
        int BorderBlue,
        int BorderAlpha,
        int BorderWidth,
        uint ShapeKind,
        int ShockwaveAge,
        int ShockwaveLifetime,
        int ShockwaveRadius,
        int ShockwaveThickness,
        int ShockwaveIntensity,
        int ShockwaveSeed)
    {
        public static BindingKey From(RenderSubmission submission)
            => new(
                submission.Texture.Value,
                BitConverter.SingleToInt32Bits(submission.Tint.Red),
                BitConverter.SingleToInt32Bits(submission.Tint.Green),
                BitConverter.SingleToInt32Bits(submission.Tint.Blue),
                BitConverter.SingleToInt32Bits(submission.Tint.Alpha),
                BitConverter.SingleToInt32Bits(submission.Msdf.PixelRange),
                BitConverter.SingleToInt32Bits(submission.Msdf.Threshold),
                BitConverter.SingleToInt32Bits(submission.Analytic.ShapeSize.Width / 2),
                BitConverter.SingleToInt32Bits(submission.Analytic.ShapeSize.Height / 2),
                BitConverter.SingleToInt32Bits(submission.Analytic.Radius),
                BitConverter.SingleToInt32Bits(submission.Analytic.BorderColor.Red),
                BitConverter.SingleToInt32Bits(submission.Analytic.BorderColor.Green),
                BitConverter.SingleToInt32Bits(submission.Analytic.BorderColor.Blue),
                BitConverter.SingleToInt32Bits(submission.Analytic.BorderColor.Alpha),
                BitConverter.SingleToInt32Bits(submission.Analytic.BorderWidth),
                (uint)submission.Analytic.Kind,
                BitConverter.SingleToInt32Bits(submission.Shockwave.Age),
                BitConverter.SingleToInt32Bits(submission.Shockwave.Lifetime),
                BitConverter.SingleToInt32Bits(submission.Shockwave.Radius),
                BitConverter.SingleToInt32Bits(submission.Shockwave.Thickness),
                BitConverter.SingleToInt32Bits(submission.Shockwave.Intensity),
                BitConverter.SingleToInt32Bits(submission.Shockwave.Seed));
    }

    private readonly record struct RenderSubmission(
        Native2DRect Destination,
        Native2DUvRect Uv,
        Native2DTextureHandle Texture,
        Native2DTint Tint,
        NativeMsdfParameters Msdf,
        AnalyticParameters Analytic,
        SoftShockwaveParameters Shockwave);

    private readonly record struct AnalyticParameters(
        NativeAnalyticShapeKind Kind,
        Native2DSize ShapeSize,
        float Radius,
        Native2DTint BorderColor,
        float BorderWidth);

    private readonly record struct SoftShockwaveParameters(
        float Age,
        float Lifetime,
        float Radius,
        float Thickness,
        float Intensity,
        float Seed);

    private readonly record struct Vertex(float X, float Y, float Z, float U, float V, float FieldScale);
}
