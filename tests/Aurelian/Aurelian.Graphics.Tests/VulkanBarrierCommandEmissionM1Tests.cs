using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Buffers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanBarrierCommandEmissionM1Tests
{
    [Fact]
    public void VulkanBarrierCommandEmitter_EmptyTextureBarrierList_ReturnsNoOp()
    {
        VulkanBarrierEmissionResult result = VulkanBarrierCommandEmitter.EmitTextureBarriers(null!, null!, []);

        Assert.True(result.Success);
        Assert.Equal(VulkanBarrierEmissionStatus.NoOp, result.Status);
        Assert.Equal(0, result.ImageBarrierCount);
        Assert.Equal(0, result.BufferBarrierCount);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanBarrierEmissionDiagnosticCodes.EmptyBatch);
    }

    [Fact]
    public void VulkanBarrierCommandEmitter_EmptyBufferBarrierList_ReturnsNoOp()
    {
        VulkanBarrierEmissionResult result = VulkanBarrierCommandEmitter.EmitBufferBarriers(null!, null!, []);

        Assert.True(result.Success);
        Assert.Equal(VulkanBarrierEmissionStatus.NoOp, result.Status);
        Assert.Equal(0, result.ImageBarrierCount);
        Assert.Equal(0, result.BufferBarrierCount);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanBarrierEmissionDiagnosticCodes.EmptyBatch);
    }

    [Fact]
    public void VulkanBarrierCommandEmitter_EmitTextureBarrier_WhenPlantCreated_RecordsPipelineBarrier()
        => WithPlantAllocatorAndCommandBuffer(PlantId.Zero, (plant, allocator, lease) =>
        {
            VulkanTextureCreateResult textureResult = CreateSmallRgbaTexture(plant, allocator);
            if (!textureResult.Success)
            {
                Assert.NotEmpty(textureResult.Diagnostics);
                return;
            }

            using AurelianVulkanTexture texture = textureResult.Texture!;
            VulkanBarrierPlanResult planResult = texture.LayoutTracker.Transition("test.texture", 0, 0, VulkanResourceLayout.TransferDestination);
            Assert.True(planResult.Success, FormatDiagnostics(planResult.Diagnostics));
            Assert.NotNull(planResult.Plan);

            VulkanBarrierEmissionResult emitResult = VulkanBarrierCommandEmitter.EmitTextureBarriers(
                plant,
                lease,
                [new VulkanTextureBarrierEmission(texture, planResult.Plan!)]);

            Assert.True(emitResult.Success, FormatDiagnostics(emitResult.Diagnostics));
            Assert.Equal(VulkanBarrierEmissionStatus.Emitted, emitResult.Status);
            Assert.Equal(1, emitResult.ImageBarrierCount);
            Assert.Equal(0, emitResult.BufferBarrierCount);
        });

    [Fact]
    public void VulkanBarrierCommandEmitter_EmitBufferBarrier_WhenPlantCreated_RecordsPipelineBarrier()
        => WithPlantAllocatorAndCommandBuffer(PlantId.Zero, (plant, allocator, lease) =>
        {
            VulkanBufferCreateResult bufferResult = CreateSmallVertexBuffer(plant, allocator);
            if (!bufferResult.Success)
            {
                Assert.NotEmpty(bufferResult.Diagnostics);
                return;
            }

            using AurelianVulkanBuffer buffer = bufferResult.Buffer!;
            VulkanBufferTransitionPlan plan = VulkanBufferTransitionPlanner.HostWriteToTransferRead("test.buffer", buffer.SizeBytes);

            VulkanBarrierEmissionResult emitResult = VulkanBarrierCommandEmitter.EmitBufferBarriers(
                plant,
                lease,
                [new VulkanBufferBarrierEmission(buffer, plan)]);

            Assert.True(emitResult.Success, FormatDiagnostics(emitResult.Diagnostics));
            Assert.Equal(VulkanBarrierEmissionStatus.Emitted, emitResult.Status);
            Assert.Equal(0, emitResult.ImageBarrierCount);
            Assert.Equal(1, emitResult.BufferBarrierCount);
        });

    [Fact]
    public void VulkanBarrierCommandEmitter_EmitCombinedBarriers_WhenPlantCreated_RecordsSingleCallOrSuccess()
        => WithPlantAllocatorAndCommandBuffer(PlantId.Zero, (plant, allocator, lease) =>
        {
            VulkanTextureCreateResult textureResult = CreateSmallRgbaTexture(plant, allocator);
            VulkanBufferCreateResult bufferResult = CreateSmallVertexBuffer(plant, allocator);
            if (!textureResult.Success || !bufferResult.Success)
            {
                Assert.True(textureResult.Diagnostics.Count > 0 || bufferResult.Diagnostics.Count > 0);
                return;
            }

            using AurelianVulkanTexture texture = textureResult.Texture!;
            using AurelianVulkanBuffer buffer = bufferResult.Buffer!;
            VulkanBarrierPlanResult texturePlan = texture.LayoutTracker.Transition("test.texture", 0, 0, VulkanResourceLayout.TransferDestination);
            Assert.True(texturePlan.Success, FormatDiagnostics(texturePlan.Diagnostics));
            VulkanBufferTransitionPlan bufferPlan = VulkanBufferTransitionPlanner.HostWriteToTransferRead("test.buffer", buffer.SizeBytes);

            VulkanBarrierEmissionResult emitResult = VulkanBarrierCommandEmitter.Emit(
                plant,
                lease,
                [new VulkanBufferBarrierEmission(buffer, bufferPlan)],
                [new VulkanTextureBarrierEmission(texture, texturePlan.Plan!)]);

            Assert.True(emitResult.Success, FormatDiagnostics(emitResult.Diagnostics));
            Assert.Equal(VulkanBarrierEmissionStatus.Emitted, emitResult.Status);
            Assert.Equal(1, emitResult.ImageBarrierCount);
            Assert.Equal(1, emitResult.BufferBarrierCount);
        });

    [Fact]
    public void VulkanBarrierCommandEmitter_RejectsBarrierForWrongPlantTexture()
    {
        var firstInit = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (firstInit.Plant)
        {
            if (!firstInit.Success)
            {
                Assert.NotEmpty(firstInit.Diagnostics);
                return;
            }

            var secondInit = VulkanPlantInitializer.CreatePlant(new PlantId(1), new VulkanPlantOptions(EnableValidation: false));
            using (secondInit.Plant)
            {
                if (!secondInit.Success)
                {
                    Assert.NotEmpty(secondInit.Diagnostics);
                    return;
                }

                using var firstAllocator = new RawVulkanMemoryAllocator(firstInit.Plant!);
                VulkanTextureCreateResult textureResult = CreateSmallRgbaTexture(firstInit.Plant!, firstAllocator);
                if (!textureResult.Success)
                {
                    Assert.NotEmpty(textureResult.Diagnostics);
                    return;
                }

                using AurelianVulkanTexture texture = textureResult.Texture!;
                VulkanBarrierPlanResult planResult = texture.LayoutTracker.Transition("wrong.texture", 0, 0, VulkanResourceLayout.TransferDestination);
                Assert.True(planResult.Success, FormatDiagnostics(planResult.Diagnostics));

                using var secondPool = VulkanCommandBufferPool.Create(secondInit.Plant!);
                VulkanCommandBufferLease secondLease = secondPool.Rent(completedFenceValue: 0);
                VulkanCommandBufferOperationResult beginResult = secondLease.Begin();
                Assert.True(beginResult.Success, FormatDiagnostics(beginResult.Diagnostics));

                VulkanBarrierEmissionResult emitResult = VulkanBarrierCommandEmitter.EmitTextureBarriers(
                    secondInit.Plant!,
                    secondLease,
                    [new VulkanTextureBarrierEmission(texture, planResult.Plan!)]);

                VulkanCommandBufferOperationResult endResult = secondLease.End();
                Assert.True(endResult.Success, FormatDiagnostics(endResult.Diagnostics));
                Assert.False(emitResult.Success);
                Assert.Equal(VulkanBarrierEmissionStatus.Rejected, emitResult.Status);
                Assert.Contains(emitResult.Diagnostics, diagnostic => diagnostic.Code == VulkanBarrierEmissionDiagnosticCodes.UnsupportedBarrierPlan);
            }
        }
    }

    [Fact]
    public void VulkanBarrierCommandEmitter_RejectsBarrierForWrongPlantBuffer()
    {
        var firstInit = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (firstInit.Plant)
        {
            if (!firstInit.Success)
            {
                Assert.NotEmpty(firstInit.Diagnostics);
                return;
            }

            var secondInit = VulkanPlantInitializer.CreatePlant(new PlantId(1), new VulkanPlantOptions(EnableValidation: false));
            using (secondInit.Plant)
            {
                if (!secondInit.Success)
                {
                    Assert.NotEmpty(secondInit.Diagnostics);
                    return;
                }

                using var firstAllocator = new RawVulkanMemoryAllocator(firstInit.Plant!);
                VulkanBufferCreateResult bufferResult = CreateSmallVertexBuffer(firstInit.Plant!, firstAllocator);
                if (!bufferResult.Success)
                {
                    Assert.NotEmpty(bufferResult.Diagnostics);
                    return;
                }

                using AurelianVulkanBuffer buffer = bufferResult.Buffer!;
                VulkanBufferTransitionPlan plan = VulkanBufferTransitionPlanner.HostWriteToTransferRead("wrong.buffer", buffer.SizeBytes);

                using var secondPool = VulkanCommandBufferPool.Create(secondInit.Plant!);
                VulkanCommandBufferLease secondLease = secondPool.Rent(completedFenceValue: 0);
                VulkanCommandBufferOperationResult beginResult = secondLease.Begin();
                Assert.True(beginResult.Success, FormatDiagnostics(beginResult.Diagnostics));

                VulkanBarrierEmissionResult emitResult = VulkanBarrierCommandEmitter.EmitBufferBarriers(
                    secondInit.Plant!,
                    secondLease,
                    [new VulkanBufferBarrierEmission(buffer, plan)]);

                VulkanCommandBufferOperationResult endResult = secondLease.End();
                Assert.True(endResult.Success, FormatDiagnostics(endResult.Diagnostics));
                Assert.False(emitResult.Success);
                Assert.Equal(VulkanBarrierEmissionStatus.Rejected, emitResult.Status);
                Assert.Contains(emitResult.Diagnostics, diagnostic => diagnostic.Code == VulkanBarrierEmissionDiagnosticCodes.UnsupportedBarrierPlan);
            }
        }
    }

    private static void WithPlantAllocatorAndCommandBuffer(
        PlantId plantId,
        Action<AurelianVulkanPlant, RawVulkanMemoryAllocator, VulkanCommandBufferLease> action)
    {
        var init = VulkanPlantInitializer.CreatePlant(plantId, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var allocator = new RawVulkanMemoryAllocator(init.Plant!);
            using var pool = VulkanCommandBufferPool.Create(init.Plant!);
            VulkanCommandBufferLease lease = pool.Rent(completedFenceValue: 0);
            VulkanCommandBufferOperationResult beginResult = lease.Begin();
            Assert.True(beginResult.Success, FormatDiagnostics(beginResult.Diagnostics));

            action(init.Plant!, allocator, lease);

            VulkanCommandBufferOperationResult endResult = lease.End();
            Assert.True(endResult.Success, FormatDiagnostics(endResult.Diagnostics));
        }
    }

    private static VulkanTextureCreateResult CreateSmallRgbaTexture(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator)
        => VulkanTextureFactory.Create(
            plant,
            allocator,
            new VulkanTextureCreatePlan(
                plant.Context.Id,
                4,
                4,
                VulkanTextureFormat.Rgba8Unorm,
                VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination,
                VulkanMemoryUsage.GpuOnly,
                VulkanResourceLayout.Undefined,
                MipLevels: 1,
                ArrayLayers: 1,
                DebugName: "test.texture"));

    private static VulkanBufferCreateResult CreateSmallVertexBuffer(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator)
        => VulkanBufferFactory.Create(
            plant,
            allocator,
            new VulkanBufferCreatePlan(
                plant.Context.Id,
                4096,
                VulkanBufferUsage.Vertex | VulkanBufferUsage.TransferDestination,
                VulkanMemoryUsage.CpuToGpu,
                "test.vertex"));

    private static string FormatDiagnostics(IEnumerable<VulkanBarrierDiagnostic> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.Message));

    private static string FormatDiagnostics(IEnumerable<VulkanCommandBufferDiagnostic> diagnostics)
        => string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.Message));
}
