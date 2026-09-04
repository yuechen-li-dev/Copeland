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
    private readonly uint width;
    private readonly uint height;
    private readonly int vertexStride;
    private readonly CompiledVertexInput[] orderedVertexInputs;
    private readonly RawVulkanMemoryAllocator allocator;
    private readonly VulkanFenceBundle fences;
    private readonly VulkanCommandBufferPool commandPool;
    private readonly VulkanTextureUploader textureUploader;
    private readonly VulkanCommandSubmitter submitter;
    private readonly AurelianVulkanRenderPass renderPass;
    private readonly AurelianVulkanTexture renderTarget;
    private readonly AurelianVulkanFramebuffer framebuffer;
    private readonly DescriptorSetLayout descriptorSetLayout;
    private readonly DescriptorPool descriptorPool;
    private readonly Sampler sampler;
    private readonly AurelianVulkanGraphicsPipeline pipeline;
    private readonly Dictionary<ulong, TextureResource> textures = [];
    private readonly Dictionary<BindingKey, BindingResource> bindings = [];
    private readonly List<NativeQuadSubmission> submissions = new(InitialQuadCapacity);

    private AurelianVulkanBuffer vertexBuffer;
    private int vertexCapacityQuads = InitialQuadCapacity;
    private bool passActive;
    private bool disposed;

    public VulkanOrderedQuadRenderer(
        AurelianVulkanPlant plant,
        CompiledGraphicsProgram program,
        uint width = 256,
        uint height = 256)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(program);
        if (width == 0 || height == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The 2D target extent must be positive.");
        }

        ValidateProgram(program);
        this.plant = plant;
        this.program = program;
        this.width = width;
        this.height = height;
        orderedVertexInputs = program.VertexInputs
            .OrderBy(input => input.Order)
            .ToArray();
        vertexStride = orderedVertexInputs
            .Sum(input => VulkanForwardTexturedCanonicalFixture.PhysicalTypeSize(input.PhysicalType));

        allocator = new RawVulkanMemoryAllocator(plant);
        fences = VulkanFenceBundle.Create(plant);
        commandPool = VulkanCommandBufferPool.Create(plant);
        textureUploader = new VulkanTextureUploader(plant, allocator, commandPool, fences);
        submitter = new VulkanCommandSubmitter(plant, commandPool, fences);

        renderTarget = VulkanNativeForwardTexturedRenderer.CreateTexture(
            plant,
            allocator,
            width,
            height,
            VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.TransferSource,
            VulkanMemoryUsage.GpuOnly,
            "native-2d.target");
        renderPass = CreateRenderPass();
        framebuffer = CreateFramebuffer();
        descriptorSetLayout = VulkanNativeForwardTexturedRenderer.CreateDescriptorSetLayout(plant, program);
        pipeline = VulkanNativeForwardTexturedRenderer.CreatePipeline(plant, renderPass, program, vertexStride, descriptorSetLayout);
        sampler = VulkanNativeForwardTexturedRenderer.CreateSampler(plant);
        descriptorPool = CreateDescriptorPool();
        vertexBuffer = CreateVertexBuffer(vertexCapacityQuads);
    }

    public uint Width => width;

    public uint Height => height;

    public int TextureCount => textures.Count;

    public int VertexCapacityQuads => vertexCapacityQuads;

    public Native2DTextureHandle CreateTexture(uint textureWidth, uint textureHeight, ReadOnlySpan<byte> rgba8)
    {
        ThrowIfDisposed();
        if (passActive)
        {
            throw new InvalidOperationException("Textures cannot be created during an active 2D pass.");
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
            rgba8.ToArray(),
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
        ValidateSubmission(submission);
        submissions.Add(submission);
    }

    public Native2DPassResult End2D(bool captureReadback = false)
    {
        ThrowIfDisposed();
        if (!passActive)
        {
            throw new InvalidOperationException("End2D requires an active 2D pass.");
        }

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int allocationsBefore = bindings.Count;
        int descriptorWrites = 0;
        AurelianVulkanBuffer? readbackBuffer = null;
        try
        {
            EnsureVertexCapacity(submissions.Count);
            Stopwatch uploadWatch = Stopwatch.StartNew();
            byte[] vertexBytes = BuildVertices();
            Require(vertexBuffer.Write(vertexBytes).Success, "Vertex upload failed.");
            uploadWatch.Stop();

            BindingKey[] submissionKeys = new BindingKey[submissions.Count];
            for (int index = 0; index < submissions.Count; index++)
            {
                BindingKey key = BindingKey.From(submissions[index]);
                submissionKeys[index] = key;
                if (!bindings.ContainsKey(key))
                {
                    bindings.Add(key, CreateBinding(key, submissions[index]));
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
            VulkanRenderPassBeginResult begin = renderPassEncoder.Begin(
                plant,
                commandBuffer,
                new VulkanRenderPassBeginRequest(
                    renderPass,
                    framebuffer,
                    new VulkanColorClearValue(16f / 255f, 32f / 255f, 64f / 255f, 1)));
            Require(begin.Success, "Render pass begin failed.");

            int drawCalls = RecordOrderedDraws(commandBuffer, begin.Scope!.Value, submissionKeys);
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
                DescriptorSetAllocations: bindings.Count - allocationsBefore,
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
        plant.Vk.DestroySampler(plant.Device, sampler, null);
        pipeline.Dispose();
        framebuffer.Dispose();
        renderPass.Dispose();
        plant.Vk.DestroyDescriptorSetLayout(plant.Device, descriptorSetLayout, null);
        renderTarget.Dispose();
        submitter.Dispose();
        textureUploader.Dispose();
        commandPool.Dispose();
        fences.Dispose();
        allocator.Dispose();
    }

    private static void ValidateProgram(CompiledGraphicsProgram program)
    {
        VulkanForwardTexturedFixture fixture = VulkanForwardTexturedCanonicalFixture.Create(program);
        VulkanForwardTexturedValidation validation = VulkanNativeForwardTexturedRenderer.Validate(program, fixture);
        if (!validation.Success)
        {
            throw new ArgumentException("Compiled graphics program is not compatible with the native 2D path: " + string.Join("; ", validation.Errors), nameof(program));
        }
    }

    private void ValidateSubmission(NativeQuadSubmission submission)
    {
        Native2DSubmissionValidator.ValidateValues(submission);
        if (!textures.ContainsKey(submission.Texture.Value))
        {
            throw UnknownTexture(submission.Texture);
        }
    }

    private byte[] BuildVertices()
    {
        byte[] bytes = new byte[checked(submissions.Count * VerticesPerQuad * vertexStride)];
        Span<Vertex> vertices = stackalloc Vertex[VerticesPerQuad];
        for (int quadIndex = 0; quadIndex < submissions.Count; quadIndex++)
        {
            NativeQuadSubmission submission = submissions[quadIndex];
            float left = ToNdcX(submission.Destination.X);
            float right = ToNdcX(submission.Destination.X + submission.Destination.Width);
            float top = ToNdcY(submission.Destination.Y);
            float bottom = ToNdcY(submission.Destination.Y + submission.Destination.Height);
            vertices[0] = new Vertex(left, bottom, 0, submission.Uv.U0, submission.Uv.V1);
            vertices[1] = new Vertex(right, top, 0, submission.Uv.U1, submission.Uv.V0);
            vertices[2] = new Vertex(right, bottom, 0, submission.Uv.U1, submission.Uv.V1);
            vertices[3] = new Vertex(left, bottom, 0, submission.Uv.U0, submission.Uv.V1);
            vertices[4] = new Vertex(left, top, 0, submission.Uv.U0, submission.Uv.V0);
            vertices[5] = new Vertex(right, top, 0, submission.Uv.U1, submission.Uv.V0);
            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                WriteVertex(bytes, (quadIndex * VerticesPerQuad + vertexIndex) * vertexStride, vertices[vertexIndex]);
            }
        }
        return bytes;
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
            else if (input.Name == "uv" && input.PhysicalType == "float2")
            {
                WriteFloat(bytes, offset, vertex.U);
                WriteFloat(bytes, offset + 4, vertex.V);
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
        IReadOnlyList<BindingKey> keys)
    {
        if (keys.Count == 0)
        {
            return 0;
        }
        var drawEncoder = new VulkanDrawCommandEncoder();
        int drawCalls = 0;
        int startQuad = 0;
        while (startQuad < keys.Count)
        {
            BindingKey key = keys[startQuad];
            int endQuad = startQuad + 1;
            while (endQuad < keys.Count && keys[endQuad] == key)
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
            VulkanDrawCommandResult draw = drawEncoder.DrawVertices(
                plant,
                commandBuffer,
                scope,
                new VulkanDrawVerticesRequest(
                    pipeline,
                    vertexBuffer,
                    checked((uint)((endQuad - startQuad) * VerticesPerQuad)),
                    checked((uint)(startQuad * VerticesPerQuad)),
                    VulkanViewportScissor.FromFramebuffer(framebuffer)));
            Require(draw.Success, "Quad draw recording failed.");
            drawCalls++;
            startQuad = endQuad;
        }
        return drawCalls;
    }

    private BindingResource CreateBinding(BindingKey key, NativeQuadSubmission submission)
    {
        if (bindings.Count >= MaximumBindingSets)
        {
            throw new InvalidOperationException($"Native 2D binding capacity of {MaximumBindingSets} unique texture/tint pairs was exceeded.");
        }

        CompiledMaterialLayout material = program.Material!;
        byte[] materialBytes = new byte[material.Size];
        CompiledMaterialField tint = material.Fields.Single(field => field.Name == "tint");
        CompiledMaterialField roughness = material.Fields.Single(field => field.Name == "roughness");
        WriteFloat(materialBytes, tint.Offset, submission.Tint.Red);
        WriteFloat(materialBytes, tint.Offset + 4, submission.Tint.Green);
        WriteFloat(materialBytes, tint.Offset + 8, submission.Tint.Blue);
        WriteFloat(materialBytes, tint.Offset + 12, submission.Tint.Alpha);
        WriteFloat(materialBytes, roughness.Offset, 1);
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

        TextureResource texture = textures[key.TextureId];
        DescriptorImageInfo textureInfo = new(default, texture.Texture.NativeImageView!.Value, ImageLayout.ShaderReadOnlyOptimal);
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
    }

    private AurelianVulkanBuffer CreateVertexBuffer(int capacityQuads)
        => VulkanNativeForwardTexturedRenderer.CreateMappedBuffer(
            plant,
            allocator,
            checked((ulong)(capacityQuads * VerticesPerQuad * vertexStride)),
            VulkanBufferUsage.Vertex,
            VulkanMemoryUsage.CpuToGpu,
            "native-2d.vertices");

    private AurelianVulkanRenderPass CreateRenderPass()
    {
        VulkanRenderPassCreateResult result = VulkanRenderPassFactory.Create(
            plant,
            new VulkanRenderPassDescriptor([
                new VulkanRenderPassAttachmentDescriptor(
                    "Color0",
                    VulkanTextureFormat.Rgba8Unorm,
                    VulkanAttachmentLoadOp.Clear,
                    VulkanAttachmentStoreOp.Store,
                    VulkanResourceLayout.Undefined,
                    VulkanResourceLayout.TransferSource),
            ]));
        Require(result.Success, "Native 2D render pass creation failed.");
        return result.RenderPass!;
    }

    private AurelianVulkanFramebuffer CreateFramebuffer()
    {
        VulkanFramebufferCreateResult result = VulkanFramebufferFactory.Create(
            plant,
            renderPass,
            new VulkanFramebufferDescriptor(width, height, [renderTarget]));
        Require(result.Success, "Native 2D framebuffer creation failed.");
        return result.Framebuffer!;
    }

    private DescriptorPool CreateDescriptorPool()
    {
        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[3]
        {
            new(DescriptorType.SampledImage, MaximumBindingSets),
            new(DescriptorType.Sampler, MaximumBindingSets),
            new(DescriptorType.UniformBuffer, MaximumBindingSets),
        };
        DescriptorPoolCreateInfo createInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = MaximumBindingSets,
            PoolSizeCount = 3,
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

    private readonly record struct BindingKey(ulong TextureId, int Red, int Green, int Blue, int Alpha)
    {
        public static BindingKey From(NativeQuadSubmission submission)
            => new(
                submission.Texture.Value,
                BitConverter.SingleToInt32Bits(submission.Tint.Red),
                BitConverter.SingleToInt32Bits(submission.Tint.Green),
                BitConverter.SingleToInt32Bits(submission.Tint.Blue),
                BitConverter.SingleToInt32Bits(submission.Tint.Alpha));
    }

    private readonly record struct Vertex(float X, float Y, float Z, float U, float V);
}
