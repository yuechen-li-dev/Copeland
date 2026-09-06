using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Draw;
using Aurelian.Graphics.Vulkan.Commanding.RenderPasses;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Device;
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

namespace Aurelian.Graphics.Vulkan.NativeForwardTextured;

public sealed record VulkanForwardTexturedFixture(
    byte[] VertexBytes,
    int VertexStride,
    int VertexCount,
    byte[] TextureRgba,
    uint TextureWidth,
    uint TextureHeight,
    byte[] MaterialBytes,
    IReadOnlySet<int> BoundBindings);

public sealed record VulkanForwardTexturedValidation(
    bool Success,
    IReadOnlyList<string> Errors,
    IReadOnlyList<VulkanReflectedGraphicsResource> ReflectedResources,
    IReadOnlyList<int> ReflectedMaterialOffsets);

public sealed record VulkanReflectedGraphicsResource(
    int Set,
    int Binding,
    CompiledGraphicsResourceKind Kind,
    IReadOnlyList<CompiledGraphicsStage> Visibility);

public sealed record VulkanForwardTexturedPixelFacts(
    int ClearPixelCount,
    int DrawnPixelCount,
    int DistinctDrawnColors,
    bool TextureContributed,
    bool TintContributed);

public sealed record VulkanForwardTexturedTimings(
    double ResourceUploadMilliseconds,
    double PipelineMilliseconds,
    double RecordSubmitMilliseconds,
    double ReadbackMilliseconds,
    double TotalMilliseconds);

public sealed record VulkanForwardTexturedRenderResult(
    bool Success,
    string? PixelSha256,
    byte[] Pixels,
    VulkanForwardTexturedPixelFacts? PixelFacts,
    VulkanForwardTexturedValidation ContractValidation,
    VulkanForwardTexturedTimings? Timings,
    IReadOnlyList<string> Diagnostics);

public static class VulkanForwardTexturedCanonicalFixture
{
    public static VulkanForwardTexturedFixture Create(CompiledGraphicsProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);

        IReadOnlyList<CompiledVertexInput> inputs = program.VertexInputs.OrderBy(input => input.Order).ToArray();
        int stride = inputs.Sum(input => PhysicalTypeSize(input.PhysicalType));
        var vertices = new (float X, float Y, float Z, float U, float V)[]
        {
            (-0.75f, -0.75f, 0, 0, 1),
            ( 0.75f,  0.75f, 0, 1, 0),
            ( 0.75f, -0.75f, 0, 1, 1),
            (-0.75f, -0.75f, 0, 0, 1),
            (-0.75f,  0.75f, 0, 0, 0),
            ( 0.75f,  0.75f, 0, 1, 0),
        };

        byte[] vertexBytes = new byte[stride * vertices.Length];
        for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
        {
            int offset = vertexIndex * stride;
            foreach (CompiledVertexInput input in inputs)
            {
                if (input.PhysicalType == "float3" && input.Name == "position")
                {
                    WriteFloat(vertexBytes, offset, vertices[vertexIndex].X);
                    WriteFloat(vertexBytes, offset + 4, vertices[vertexIndex].Y);
                    WriteFloat(vertexBytes, offset + 8, vertices[vertexIndex].Z);
                }
                else if (input.PhysicalType == "float2" && input.Name == "uv")
                {
                    WriteFloat(vertexBytes, offset, vertices[vertexIndex].U);
                    WriteFloat(vertexBytes, offset + 4, vertices[vertexIndex].V);
                }
                else
                {
                    throw new InvalidOperationException($"Canonical fixture cannot populate vertex input '{input.Name}' of type '{input.PhysicalType}'.");
                }

                offset += PhysicalTypeSize(input.PhysicalType);
            }
        }

        CompiledMaterialLayout material = program.Material
            ?? throw new InvalidOperationException("ForwardTextured requires material metadata.");
        byte[] materialBytes = new byte[material.Size];
        CompiledMaterialField tint = material.Fields.Single(field => field.Name == "tint");
        CompiledMaterialField roughness = material.Fields.Single(field => field.Name == "roughness");
        WriteFloat(materialBytes, tint.Offset, 0.5f);
        WriteFloat(materialBytes, tint.Offset + 4, 1.0f);
        WriteFloat(materialBytes, tint.Offset + 8, 0.75f);
        WriteFloat(materialBytes, tint.Offset + 12, 1.0f);
        WriteFloat(materialBytes, roughness.Offset, 0.375f);

        byte[] texture =
        [
            255, 0, 0, 255,
            0, 255, 0, 255,
            0, 0, 255, 255,
            255, 255, 255, 255,
        ];

        return new VulkanForwardTexturedFixture(
            vertexBytes,
            stride,
            vertices.Length,
            texture,
            TextureWidth: 2,
            TextureHeight: 2,
            materialBytes,
            program.Resources.Select(resource => resource.Binding).ToHashSet());
    }

    internal static int PhysicalTypeSize(string physicalType)
        => physicalType switch
        {
            "float" or "f32" or "uint" or "u32" => 4,
            "float2" => 8,
            "float3" => 12,
            "float4" => 16,
            _ => throw new InvalidOperationException($"Unsupported M0 physical type '{physicalType}'."),
        };

    private static void WriteFloat(byte[] destination, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));
}

public static unsafe class VulkanNativeForwardTexturedRenderer
{
    public const uint TargetWidth = 64;
    public const uint TargetHeight = 64;
    private const ulong FenceWaitTimeoutNanoseconds = 5_000_000_000;
    private static readonly byte[] ClearRgba = [16, 32, 64, 255];

    public static VulkanForwardTexturedValidation Validate(
        CompiledGraphicsProgram program,
        VulkanForwardTexturedFixture fixture,
        bool requireTexturedResourceShape = true)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(fixture);

        List<string> errors = [];
        if (program.FormatVersion != CompiledGraphicsProgram.CurrentFormatVersion)
        {
            errors.Add($"Unsupported compiled graphics program format '{program.FormatVersion}'.");
        }

        CompiledMaterialLayout? material = program.Material;
        if (material is null)
        {
            errors.Add("Material metadata is missing.");
        }
        else if (fixture.MaterialBytes.Length != material.Size)
        {
            errors.Add($"Material payload is {fixture.MaterialBytes.Length} bytes; compiler metadata requires {material.Size} bytes.");
        }

        int expectedStride = 0;
        try
        {
            expectedStride = program.VertexInputs.OrderBy(input => input.Order).Sum(input => VulkanForwardTexturedCanonicalFixture.PhysicalTypeSize(input.PhysicalType));
        }
        catch (InvalidOperationException exception)
        {
            errors.Add(exception.Message);
        }

        if (expectedStride != 0 && fixture.VertexStride != expectedStride)
        {
            errors.Add($"Vertex stride is {fixture.VertexStride} bytes; compiler metadata requires {expectedStride} bytes.");
        }

        if (fixture.VertexCount <= 0 || fixture.VertexBytes.Length != fixture.VertexStride * fixture.VertexCount)
        {
            errors.Add("Vertex payload length does not match stride and vertex count.");
        }

        ulong expectedTextureBytes = (ulong)fixture.TextureWidth * fixture.TextureHeight * 4;
        if ((ulong)fixture.TextureRgba.Length != expectedTextureBytes)
        {
            errors.Add($"Texture payload is {fixture.TextureRgba.Length} bytes; extent requires {expectedTextureBytes} RGBA bytes.");
        }

        foreach (CompiledGraphicsResource resource in program.Resources)
        {
            if (resource.Set != 0)
            {
                errors.Add($"M0 supports descriptor set 0 only; resource '{resource.Name}' uses set {resource.Set}.");
            }
        }

        if (program.Resources.Select(resource => (resource.Set, resource.Binding)).Distinct().Count() != program.Resources.Count)
        {
            errors.Add("Compiler resource metadata contains duplicate descriptor bindings.");
        }

        int[] missingBindings = program.Resources
            .Select(resource => resource.Binding)
            .Where(binding => !fixture.BoundBindings.Contains(binding))
            .Order()
            .ToArray();
        if (missingBindings.Length > 0)
        {
            errors.Add($"Fixture is missing required descriptor binding(s): {string.Join(", ", missingBindings)}.");
        }

        if (requireTexturedResourceShape
            && (program.Resources.Count(resource => resource.Kind == CompiledGraphicsResourceKind.Texture2D) != 1
            || program.Resources.Count(resource => resource.Kind == CompiledGraphicsResourceKind.Sampler) != 1
            || program.Resources.Count(resource => resource.Kind == CompiledGraphicsResourceKind.UniformBuffer) != 1))
        {
            errors.Add("ForwardTextured M0 requires exactly one texture, one sampler, and one uniform buffer resource.");
        }

        SpirvReflectionFacts reflection = Reflect(program.Shaders.Stages);
        int[] expectedBindings = program.Resources.Select(resource => resource.Binding).Order().ToArray();
        int[] reflectedBindings = reflection.Resources.Select(resource => resource.Binding).Order().ToArray();
        if (!expectedBindings.SequenceEqual(reflectedBindings))
        {
            errors.Add($"SPIR-V bindings [{string.Join(",", reflectedBindings)}] disagree with compiler metadata [{string.Join(",", expectedBindings)}].");
        }

        foreach (CompiledGraphicsResource resource in program.Resources)
        {
            VulkanReflectedGraphicsResource? reflected = reflection.Resources.SingleOrDefault(item => item.Set == resource.Set && item.Binding == resource.Binding);
            if (reflected is null)
            {
                continue;
            }

            if (reflected.Kind != resource.Kind)
            {
                errors.Add($"SPIR-V binding {resource.Set}:{resource.Binding} kind {reflected.Kind} disagrees with compiler kind {resource.Kind}.");
            }

            CompiledGraphicsStage[] expectedVisibility = resource.Visibility.Order().ToArray();
            CompiledGraphicsStage[] reflectedVisibility = reflected.Visibility.Order().ToArray();
            if (!expectedVisibility.SequenceEqual(reflectedVisibility))
            {
                errors.Add($"SPIR-V binding {resource.Set}:{resource.Binding} visibility [{string.Join(",", reflectedVisibility)}] disagrees with compiler visibility [{string.Join(",", expectedVisibility)}].");
            }
        }

        if (material is not null)
        {
            int[] expectedOffsets = material.Fields.Select(field => field.Offset).Order().ToArray();
            int[] reflectedOffsets = reflection.MemberOffsets.Order().ToArray();
            if (!expectedOffsets.All(reflectedOffsets.Contains))
            {
                errors.Add($"SPIR-V material offsets [{string.Join(",", reflectedOffsets)}] do not contain compiler offsets [{string.Join(",", expectedOffsets)}].");
            }
        }

        return new VulkanForwardTexturedValidation(errors.Count == 0, errors, reflection.Resources, reflection.MemberOffsets.Order().ToArray());
    }

    public static VulkanForwardTexturedRenderResult Render(
        AurelianVulkanPlant plant,
        CompiledGraphicsProgram program,
        VulkanForwardTexturedFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(plant);
        VulkanForwardTexturedValidation validation = Validate(program, fixture);
        if (!validation.Success)
        {
            return Failure(validation, validation.Errors);
        }

        Stopwatch total = Stopwatch.StartNew();
        List<string> diagnostics = [];
        using var allocator = new RawVulkanMemoryAllocator(plant);
        using var fences = VulkanFenceBundle.Create(plant);
        using var commandPool = VulkanCommandBufferPool.Create(plant);
        using var textureUploader = new VulkanTextureUploader(plant, allocator, commandPool, fences);
        using var submitter = new VulkanCommandSubmitter(plant, commandPool, fences);

        DescriptorSetLayout setLayout = default;
        DescriptorPool descriptorPool = default;
        Sampler sampler = default;
        AurelianVulkanTexture? sampledTexture = null;
        AurelianVulkanTexture? renderTarget = null;
        AurelianVulkanBuffer? vertexBuffer = null;
        AurelianVulkanBuffer? materialBuffer = null;
        AurelianVulkanBuffer? readbackBuffer = null;
        AurelianVulkanRenderPass? renderPass = null;
        AurelianVulkanFramebuffer? framebuffer = null;
        AurelianVulkanGraphicsPipeline? pipeline = null;

        double uploadMilliseconds = 0;
        double pipelineMilliseconds = 0;
        double submitMilliseconds = 0;
        double readbackMilliseconds = 0;

        try
        {
            Stopwatch upload = Stopwatch.StartNew();
            sampledTexture = CreateTexture(plant, allocator, fixture.TextureWidth, fixture.TextureHeight,
                VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination,
                VulkanMemoryUsage.GpuOnly,
                "native-forward-textured.texture");
            VulkanTextureUploadResult textureUpload = textureUploader.Upload(new VulkanTextureUploadRequest(
                sampledTexture,
                fixture.TextureRgba,
                "native-forward-textured.texture-upload"));
            Require(textureUpload.Success, "Texture upload failed", textureUpload.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));

            vertexBuffer = CreateMappedBuffer(plant, allocator, (ulong)fixture.VertexBytes.Length, VulkanBufferUsage.Vertex, VulkanMemoryUsage.CpuToGpu, "native-forward-textured.vertices");
            Require(vertexBuffer.Write(fixture.VertexBytes).Success, "Vertex upload failed", []);
            materialBuffer = CreateMappedBuffer(plant, allocator, (ulong)fixture.MaterialBytes.Length, VulkanBufferUsage.Uniform, VulkanMemoryUsage.CpuToGpu, "native-forward-textured.material");
            Require(materialBuffer.Write(fixture.MaterialBytes).Success, "Material upload failed", []);
            readbackBuffer = CreateMappedBuffer(plant, allocator, TargetWidth * TargetHeight * 4, VulkanBufferUsage.TransferDestination, VulkanMemoryUsage.GpuToCpu, "native-forward-textured.readback");
            renderTarget = CreateTexture(plant, allocator, TargetWidth, TargetHeight,
                VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.TransferSource,
                VulkanMemoryUsage.GpuOnly,
                "native-forward-textured.target");
            upload.Stop();
            uploadMilliseconds = upload.Elapsed.TotalMilliseconds;

            Stopwatch pipelineWatch = Stopwatch.StartNew();
            renderPass = CreateRenderPass(plant);
            framebuffer = CreateFramebuffer(plant, renderPass, renderTarget);
            setLayout = CreateDescriptorSetLayout(plant, program);
            pipeline = CreatePipeline(plant, renderPass, program, fixture.VertexStride, setLayout);
            sampler = CreateSampler(plant);
            DescriptorSet descriptorSet = CreateAndWriteDescriptorSet(
                plant,
                program,
                setLayout,
                sampledTexture,
                sampler,
                materialBuffer,
                out descriptorPool);
            pipelineWatch.Stop();
            pipelineMilliseconds = pipelineWatch.Elapsed.TotalMilliseconds;

            Stopwatch submitWatch = Stopwatch.StartNew();
            VulkanCommandBufferLease commandBuffer = commandPool.Rent(fences.CommandListFence.LastKnownCompletedValue);
            Require(commandBuffer.Begin().Success, "Command buffer begin failed", []);

            var renderPassEncoder = new VulkanRenderPassCommandEncoder();
            VulkanRenderPassBeginResult beginRenderPass = renderPassEncoder.Begin(
                plant,
                commandBuffer,
                new VulkanRenderPassBeginRequest(renderPass, framebuffer, new VulkanColorClearValue(16f / 255f, 32f / 255f, 64f / 255f, 1)));
            Require(beginRenderPass.Success, "Render pass begin failed", beginRenderPass.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));

            plant.Vk.CmdBindDescriptorSets(
                commandBuffer.CommandBuffer,
                PipelineBindPoint.Graphics,
                pipeline.NativePipelineLayout,
                0,
                1,
                &descriptorSet,
                0,
                null);

            var drawEncoder = new VulkanDrawCommandEncoder();
            VulkanDrawCommandResult draw = drawEncoder.DrawVertices(
                plant,
                commandBuffer,
                beginRenderPass.Scope!.Value,
                new VulkanDrawVerticesRequest(
                    pipeline,
                    vertexBuffer,
                    (uint)fixture.VertexCount,
                    FirstVertex: 0,
                    VulkanViewportScissor.FromFramebuffer(framebuffer)));
            Require(draw.Success, "Draw recording failed", draw.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
            Require(renderPassEncoder.End(plant, commandBuffer, beginRenderPass.Scope.Value).Success, "Render pass end failed", []);

            RecordReadback(plant, commandBuffer.CommandBuffer, renderTarget, readbackBuffer);
            Require(commandBuffer.End().Success, "Command buffer end failed", []);
            VulkanCommandSubmitResult submit = submitter.Submit(new VulkanCommandSubmitRequest(
                commandBuffer,
                WaitForCompletion: true,
                TimeoutNanoseconds: FenceWaitTimeoutNanoseconds,
                DebugName: "native-forward-textured.draw-readback"));
            Require(submit.Success, "Draw submission failed", submit.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
            submitWatch.Stop();
            submitMilliseconds = submitWatch.Elapsed.TotalMilliseconds;

            Stopwatch readbackWatch = Stopwatch.StartNew();
            byte[] pixels = readbackBuffer.ReadBytes(checked((int)(TargetWidth * TargetHeight * 4)));
            VulkanForwardTexturedPixelFacts pixelFacts = InspectPixels(pixels);
            Require(pixelFacts.ClearPixelCount > 0, "Clear background was not preserved outside the primitive.", []);
            Require(pixelFacts.DrawnPixelCount > 0, "No rendered pixels differ from the clear color.", []);
            Require(pixelFacts.TextureContributed, "Multiple texture colors did not survive sampling.", []);
            Require(pixelFacts.TintContributed, "The non-white material tint was not observed.", []);
            string hash = Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
            readbackWatch.Stop();
            readbackMilliseconds = readbackWatch.Elapsed.TotalMilliseconds;
            total.Stop();

            return new VulkanForwardTexturedRenderResult(
                true,
                hash,
                pixels,
                pixelFacts,
                validation,
                new VulkanForwardTexturedTimings(
                    Math.Round(uploadMilliseconds, 3),
                    Math.Round(pipelineMilliseconds, 3),
                    Math.Round(submitMilliseconds, 3),
                    Math.Round(readbackMilliseconds, 3),
                    Math.Round(total.Elapsed.TotalMilliseconds, 3)),
                diagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.Add(exception.Message);
            return Failure(validation, diagnostics);
        }
        finally
        {
            _ = plant.Vk.DeviceWaitIdle(plant.Device);
            if (descriptorPool.Handle != 0)
            {
                plant.Vk.DestroyDescriptorPool(plant.Device, descriptorPool, null);
            }
            if (sampler.Handle != 0)
            {
                plant.Vk.DestroySampler(plant.Device, sampler, null);
            }
            pipeline?.Dispose();
            framebuffer?.Dispose();
            renderPass?.Dispose();
            if (setLayout.Handle != 0)
            {
                plant.Vk.DestroyDescriptorSetLayout(plant.Device, setLayout, null);
            }
            readbackBuffer?.Dispose();
            materialBuffer?.Dispose();
            vertexBuffer?.Dispose();
            renderTarget?.Dispose();
            sampledTexture?.Dispose();
        }
    }

    internal static AurelianVulkanTexture CreateTexture(
        AurelianVulkanPlant plant,
        IVulkanMemoryAllocator allocator,
        uint width,
        uint height,
        VulkanTextureUsage usage,
        VulkanMemoryUsage memoryUsage,
        string debugName,
        VulkanTextureFormat format = VulkanTextureFormat.Rgba8Unorm)
    {
        VulkanTextureCreateResult result = VulkanTextureFactory.Create(
            plant,
            allocator,
            new VulkanTextureCreatePlan(
                plant.Context.Id,
                width,
                height,
                format,
                usage,
                memoryUsage,
                VulkanResourceLayout.Undefined,
                DebugName: debugName));
        Require(result.Success, $"Texture creation failed for {debugName}", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
        return result.Texture!;
    }

    internal static AurelianVulkanBuffer CreateMappedBuffer(
        AurelianVulkanPlant plant,
        IVulkanMemoryAllocator allocator,
        ulong size,
        VulkanBufferUsage usage,
        VulkanMemoryUsage memoryUsage,
        string debugName)
    {
        VulkanBufferCreateResult result = VulkanBufferFactory.Create(
            plant,
            allocator,
            new VulkanBufferCreatePlan(plant.Context.Id, size, usage, memoryUsage, debugName, MapOnCreate: true));
        Require(result.Success, $"Buffer creation failed for {debugName}", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
        return result.Buffer!;
    }

    private static AurelianVulkanRenderPass CreateRenderPass(AurelianVulkanPlant plant)
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
        Require(result.Success, "Render pass creation failed", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
        return result.RenderPass!;
    }

    private static AurelianVulkanFramebuffer CreateFramebuffer(
        AurelianVulkanPlant plant,
        AurelianVulkanRenderPass renderPass,
        AurelianVulkanTexture target)
    {
        VulkanFramebufferCreateResult result = VulkanFramebufferFactory.Create(
            plant,
            renderPass,
            new VulkanFramebufferDescriptor(TargetWidth, TargetHeight, [target]));
        Require(result.Success, "Framebuffer creation failed", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
        return result.Framebuffer!;
    }

    internal static DescriptorSetLayout CreateDescriptorSetLayout(AurelianVulkanPlant plant, CompiledGraphicsProgram program)
    {
        DescriptorSetLayoutBinding[] bindings = program.Resources
            .OrderBy(resource => resource.Binding)
            .Select(resource => new DescriptorSetLayoutBinding
            {
                Binding = (uint)resource.Binding,
                DescriptorType = MapDescriptorType(resource.Kind),
                DescriptorCount = 1,
                StageFlags = MapStageFlags(resource.Visibility),
            })
            .ToArray();

        fixed (DescriptorSetLayoutBinding* bindingsPointer = bindings)
        {
            DescriptorSetLayoutCreateInfo createInfo = new()
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = (uint)bindings.Length,
                PBindings = bindingsPointer,
            };
            Result result = plant.Vk.CreateDescriptorSetLayout(plant.Device, &createInfo, null, out DescriptorSetLayout layout);
            Require(result == Result.Success, $"Descriptor set layout creation failed with {result}.", []);
            return layout;
        }
    }

    internal static AurelianVulkanGraphicsPipeline CreatePipeline(
        AurelianVulkanPlant plant,
        AurelianVulkanRenderPass renderPass,
        CompiledGraphicsProgram program,
        int stride,
        DescriptorSetLayout setLayout,
        bool enableStraightAlphaBlend = false)
    {
        uint offset = 0;
        List<VulkanVertexAttributeDescriptor> attributes = [];
        foreach (CompiledVertexInput input in program.VertexInputs.OrderBy(input => input.Order))
        {
            attributes.Add(new VulkanVertexAttributeDescriptor(
                (uint)input.Location,
                Binding: 0,
                MapVertexFormat(input.PhysicalType),
                offset));
            offset += (uint)VulkanForwardTexturedCanonicalFixture.PhysicalTypeSize(input.PhysicalType);
        }

        VulkanCompiledGraphicsPipelineDescriptorResult descriptor = VulkanCompiledGraphicsPipelineDescriptorFactory.CreateDescriptor(
            program.Shaders,
            [new VulkanVertexBufferLayoutDescriptor(Binding: 0, (uint)stride)],
            attributes,
            enableStraightAlphaBlend: enableStraightAlphaBlend);
        Require(descriptor.Success, "Compiled pipeline descriptor creation failed", descriptor.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
        VulkanGraphicsPipelineCreateResult result = VulkanGraphicsPipelineFactory.Create(plant, renderPass, descriptor.Descriptor!, [setLayout]);
        Require(result.Success, "Native graphics pipeline creation failed", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
        return result.Pipeline!;
    }

    internal static Sampler CreateSampler(AurelianVulkanPlant plant, bool linearFiltering = false)
    {
        SamplerCreateInfo createInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = linearFiltering ? Filter.Linear : Filter.Nearest,
            MinFilter = linearFiltering ? Filter.Linear : Filter.Nearest,
            MipmapMode = linearFiltering ? SamplerMipmapMode.Linear : SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MinLod = 0,
            MaxLod = 0,
            MaxAnisotropy = 1,
        };
        Result result = plant.Vk.CreateSampler(plant.Device, &createInfo, null, out Sampler sampler);
        Require(result == Result.Success, $"Sampler creation failed with {result}.", []);
        return sampler;
    }

    private static DescriptorSet CreateAndWriteDescriptorSet(
        AurelianVulkanPlant plant,
        CompiledGraphicsProgram program,
        DescriptorSetLayout setLayout,
        AurelianVulkanTexture texture,
        Sampler sampler,
        AurelianVulkanBuffer material,
        out DescriptorPool pool)
    {
        DescriptorPoolSize* poolSizes = stackalloc DescriptorPoolSize[3]
        {
            new DescriptorPoolSize(DescriptorType.SampledImage, 1),
            new DescriptorPoolSize(DescriptorType.Sampler, 1),
            new DescriptorPoolSize(DescriptorType.UniformBuffer, 1),
        };
        DescriptorPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 3,
            PPoolSizes = poolSizes,
        };
        Result poolResult = plant.Vk.CreateDescriptorPool(plant.Device, &poolInfo, null, out pool);
        Require(poolResult == Result.Success, $"Descriptor pool creation failed with {poolResult}.", []);

        DescriptorSetAllocateInfo allocateInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = pool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout,
        };
        Result allocateResult = plant.Vk.AllocateDescriptorSets(plant.Device, &allocateInfo, out DescriptorSet descriptorSet);
        Require(allocateResult == Result.Success, $"Descriptor set allocation failed with {allocateResult}.", []);

        DescriptorImageInfo textureInfo = new(default, texture.NativeImageView!.Value, ImageLayout.ShaderReadOnlyOptimal);
        DescriptorImageInfo samplerInfo = new(sampler, default, ImageLayout.Undefined);
        DescriptorBufferInfo materialInfo = new(material.NativeBuffer, 0, material.SizeBytes);
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
                DescriptorType = MapDescriptorType(resource.Kind),
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
        return descriptorSet;
    }

    private static void RecordReadback(
        AurelianVulkanPlant plant,
        CommandBuffer commandBuffer,
        AurelianVulkanTexture target,
        AurelianVulkanBuffer readback)
    {
        BufferImageCopy region = new()
        {
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new Extent3D(TargetWidth, TargetHeight, 1),
        };
        plant.Vk.CmdCopyImageToBuffer(commandBuffer, target.NativeImage, ImageLayout.TransferSrcOptimal, readback.NativeBuffer, 1, &region);

        BufferMemoryBarrier barrier = new()
        {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.HostReadBit,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Buffer = readback.NativeBuffer,
            Offset = 0,
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

    private static VulkanForwardTexturedPixelFacts InspectPixels(byte[] pixels)
    {
        int clear = 0;
        var drawnColors = new HashSet<uint>();
        bool tint = false;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            ReadOnlySpan<byte> pixel = pixels.AsSpan(offset, 4);
            if (pixel.SequenceEqual(ClearRgba))
            {
                clear++;
                continue;
            }

            drawnColors.Add(BinaryPrimitives.ReadUInt32LittleEndian(pixel));
            if ((pixel[0] is >= 126 and <= 129 && pixel[1] < 5)
                || (pixel[2] is >= 189 and <= 193 && pixel[0] < 5))
            {
                tint = true;
            }
        }

        int drawn = pixels.Length / 4 - clear;
        return new VulkanForwardTexturedPixelFacts(clear, drawn, drawnColors.Count, drawnColors.Count >= 3, tint);
    }

    internal static DescriptorType MapDescriptorType(CompiledGraphicsResourceKind kind)
        => kind switch
        {
            CompiledGraphicsResourceKind.Texture2D => DescriptorType.SampledImage,
            CompiledGraphicsResourceKind.Sampler => DescriptorType.Sampler,
            CompiledGraphicsResourceKind.UniformBuffer => DescriptorType.UniformBuffer,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    internal static ShaderStageFlags MapStageFlags(IReadOnlyList<CompiledGraphicsStage> stages)
    {
        ShaderStageFlags flags = 0;
        foreach (CompiledGraphicsStage stage in stages)
        {
            flags |= stage switch
            {
                CompiledGraphicsStage.Vertex => ShaderStageFlags.VertexBit,
                CompiledGraphicsStage.Fragment => ShaderStageFlags.FragmentBit,
                _ => throw new ArgumentOutOfRangeException(nameof(stages), stage, null),
            };
        }
        return flags;
    }

    private static VulkanVertexAttributeFormat MapVertexFormat(string physicalType)
        => physicalType switch
        {
            "float" or "f32" => VulkanVertexAttributeFormat.Float,
            "float2" => VulkanVertexAttributeFormat.Float2,
            "float3" => VulkanVertexAttributeFormat.Float3,
            "float4" => VulkanVertexAttributeFormat.Float4,
            _ => throw new InvalidOperationException($"Unsupported vertex physical type '{physicalType}'."),
        };

    private static SpirvReflectionFacts Reflect(IReadOnlyList<CompiledShaderStage> stages)
    {
        Dictionary<(int Set, int Binding), ReflectedResourceBuilder> reflectedResources = [];
        HashSet<int> memberOffsets = [];
        foreach (CompiledShaderStage stage in stages)
        {
            ReadOnlySpan<byte> bytes = stage.SpirvBytes;
            if (bytes.Length < 20 || bytes.Length % 4 != 0)
            {
                continue;
            }
            uint[] words = new uint[bytes.Length / 4];
            for (int index = 0; index < words.Length; index++)
            {
                words[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(index * 4, 4));
            }
            Dictionary<uint, int> bindings = [];
            Dictionary<uint, int> descriptorSets = [];
            Dictionary<uint, uint> pointerPointees = [];
            Dictionary<uint, uint> variablePointerTypes = [];
            HashSet<uint> imageTypes = [];
            HashSet<uint> samplerTypes = [];
            HashSet<uint> structTypes = [];
            for (int index = 5; index < words.Length;)
            {
                uint instruction = words[index];
                int wordCount = (int)(instruction >> 16);
                int opcode = (int)(instruction & 0xffff);
                if (wordCount <= 0 || index + wordCount > words.Length)
                {
                    break;
                }
                if (opcode == 71 && wordCount >= 4)
                {
                    uint target = words[index + 1];
                    uint decoration = words[index + 2];
                    int value = (int)words[index + 3];
                    if (decoration == 33)
                    {
                        bindings[target] = value;
                    }
                    else if (decoration == 34)
                    {
                        descriptorSets[target] = value;
                    }
                }
                else if (opcode == 72 && wordCount >= 5 && words[index + 3] == 35)
                {
                    memberOffsets.Add((int)words[index + 4]);
                }
                else if (opcode == 25 && wordCount >= 2)
                {
                    imageTypes.Add(words[index + 1]);
                }
                else if (opcode == 26 && wordCount >= 2)
                {
                    samplerTypes.Add(words[index + 1]);
                }
                else if (opcode == 30 && wordCount >= 2)
                {
                    structTypes.Add(words[index + 1]);
                }
                else if (opcode == 32 && wordCount >= 4)
                {
                    pointerPointees[words[index + 1]] = words[index + 3];
                }
                else if (opcode == 59 && wordCount >= 4)
                {
                    variablePointerTypes[words[index + 2]] = words[index + 1];
                }
                index += wordCount;
            }

            foreach ((uint target, int binding) in bindings)
            {
                if (!descriptorSets.TryGetValue(target, out int set)
                    || !variablePointerTypes.TryGetValue(target, out uint pointerType)
                    || !pointerPointees.TryGetValue(pointerType, out uint pointeeType))
                {
                    continue;
                }

                CompiledGraphicsResourceKind? kind = null;
                if (imageTypes.Contains(pointeeType))
                {
                    kind = CompiledGraphicsResourceKind.Texture2D;
                }
                else if (samplerTypes.Contains(pointeeType))
                {
                    kind = CompiledGraphicsResourceKind.Sampler;
                }
                else if (structTypes.Contains(pointeeType))
                {
                    kind = CompiledGraphicsResourceKind.UniformBuffer;
                }

                if (kind is null)
                {
                    continue;
                }

                (int Set, int Binding) key = (set, binding);
                if (!reflectedResources.TryGetValue(key, out ReflectedResourceBuilder? builder))
                {
                    builder = new ReflectedResourceBuilder(set, binding, kind.Value);
                    reflectedResources.Add(key, builder);
                }
                builder.Visibility.Add(MapCompiledStage(stage.Stage));
            }
        }

        VulkanReflectedGraphicsResource[] resources = reflectedResources.Values
            .OrderBy(resource => resource.Set)
            .ThenBy(resource => resource.Binding)
            .Select(resource => new VulkanReflectedGraphicsResource(
                resource.Set,
                resource.Binding,
                resource.Kind,
                resource.Visibility.Order().ToArray()))
            .ToArray();
        return new SpirvReflectionFacts(resources, memberOffsets);
    }

    private static CompiledGraphicsStage MapCompiledStage(CompiledShaderStageKind stage)
        => stage switch
        {
            CompiledShaderStageKind.Vertex => CompiledGraphicsStage.Vertex,
            CompiledShaderStageKind.Fragment => CompiledGraphicsStage.Fragment,
            _ => throw new InvalidOperationException($"Unsupported reflected graphics stage '{stage}'."),
        };

    internal static void Require(bool condition, string message, IEnumerable<string> details)
    {
        if (!condition)
        {
            string suffix = string.Join("; ", details);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(suffix) ? message : $"{message}: {suffix}");
        }
    }

    private static VulkanForwardTexturedRenderResult Failure(
        VulkanForwardTexturedValidation validation,
        IReadOnlyList<string> diagnostics)
        => new(false, null, [], null, validation, null, diagnostics);

    private sealed record SpirvReflectionFacts(
        IReadOnlyList<VulkanReflectedGraphicsResource> Resources,
        IReadOnlySet<int> MemberOffsets);

    private sealed class ReflectedResourceBuilder
    {
        public ReflectedResourceBuilder(int set, int binding, CompiledGraphicsResourceKind kind)
        {
            Set = set;
            Binding = binding;
            Kind = kind;
        }

        public int Set { get; }

        public int Binding { get; }

        public CompiledGraphicsResourceKind Kind { get; }

        public HashSet<CompiledGraphicsStage> Visibility { get; } = [];
    }
}
