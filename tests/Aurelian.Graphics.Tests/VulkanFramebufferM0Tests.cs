using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanFramebufferM0Tests
{
    [Fact]
    public void VulkanFramebufferFactory_Create_WhenVulkanUnavailable_SkipsCleanly()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var allocator = new RawVulkanMemoryAllocator(init.Plant!);
            using AurelianVulkanTexture? texture = CreateColorTexture(init.Plant!, allocator).Texture;
            if (texture is null)
            {
                return;
            }

            using AurelianVulkanRenderPass? renderPass = CreateRenderPass(init.Plant!).RenderPass;
            if (renderPass is null)
            {
                return;
            }

            VulkanFramebufferCreateResult result = VulkanFramebufferFactory.Create(init.Plant!, renderPass, Descriptor(texture));
            AssertSuccessOrCleanFailure(result);
            result.Framebuffer?.Dispose();
        }
    }

    [Fact]
    public void VulkanFramebufferFactory_CreateRejectsInvalidDimensions()
        => WithPlantResources((plant, _, renderPass, texture) =>
        {
            VulkanFramebufferCreateResult result = VulkanFramebufferFactory.Create(plant, renderPass, Descriptor(texture) with { Width = 0 });

            Assert.False(result.Success);
            Assert.Equal(VulkanFramebufferStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanFramebufferDiagnosticCodes.InvalidDimensions);
        });

    [Fact]
    public void VulkanFramebufferFactory_CreateRejectsNoColorAttachments()
        => WithPlantResources((plant, _, renderPass, _) =>
        {
            VulkanFramebufferCreateResult result = VulkanFramebufferFactory.Create(
                plant,
                renderPass,
                new VulkanFramebufferDescriptor(4, 4, []));

            Assert.False(result.Success);
            Assert.Equal(VulkanFramebufferStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanFramebufferDiagnosticCodes.NoColorAttachments);
        });

    [Fact]
    public void VulkanFramebufferFactory_CreateRejectsMultipleColorAttachments()
        => WithPlantResources((plant, _, renderPass, texture) =>
        {
            VulkanFramebufferCreateResult result = VulkanFramebufferFactory.Create(
                plant,
                renderPass,
                new VulkanFramebufferDescriptor(4, 4, [texture, texture]));

            Assert.False(result.Success);
            Assert.Equal(VulkanFramebufferStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanFramebufferDiagnosticCodes.MultipleColorAttachmentsUnsupported);
        });

    [Fact]
    public void VulkanFramebufferFactory_CreateRejectsAttachmentSizeMismatch()
        => WithPlantResources((plant, _, renderPass, texture) =>
        {
            VulkanFramebufferCreateResult result = VulkanFramebufferFactory.Create(
                plant,
                renderPass,
                new VulkanFramebufferDescriptor(8, 4, [texture]));

            Assert.False(result.Success);
            Assert.Equal(VulkanFramebufferStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanFramebufferDiagnosticCodes.AttachmentSizeMismatch);
        });

    [Fact]
    public void VulkanFramebufferFactory_CreateRejectsAttachmentWithoutColorUsage()
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanTextureCreateResult textureResult = CreateShaderResourceTexture(plant, allocator);
            if (!textureResult.Success)
            {
                Assert.NotEmpty(textureResult.Diagnostics);
                return;
            }

            using AurelianVulkanTexture texture = textureResult.Texture!;
            using AurelianVulkanRenderPass? renderPass = CreateRenderPass(plant).RenderPass;
            if (renderPass is null)
            {
                return;
            }

            VulkanFramebufferCreateResult result = VulkanFramebufferFactory.Create(plant, renderPass, Descriptor(texture));

            Assert.False(result.Success);
            Assert.Equal(VulkanFramebufferStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanFramebufferDiagnosticCodes.AttachmentMissingColorUsage);
        });

    [Fact]
    public void VulkanFramebufferFactory_CreateRejectsRenderPassFormatMismatch()
        => WithPlantResources((plant, _, _, texture) =>
        {
            VulkanRenderPassCreateResult renderPassResult = CreateRenderPass(plant, VulkanTextureFormat.Bgra8Unorm);
            if (!renderPassResult.Success)
            {
                Assert.NotEmpty(renderPassResult.Diagnostics);
                return;
            }

            using AurelianVulkanRenderPass renderPass = renderPassResult.RenderPass!;
            VulkanFramebufferCreateResult result = VulkanFramebufferFactory.Create(plant, renderPass, Descriptor(texture));

            Assert.False(result.Success);
            Assert.Equal(VulkanFramebufferStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanFramebufferDiagnosticCodes.RenderPassAttachmentMismatch);
        });

    [Fact]
    public void VulkanFramebufferFactory_CreateOneColorFramebuffer_WhenPlantCreated_SucceedsOrReportsCleanFailure()
        => WithPlantResources((plant, _, renderPass, texture) =>
        {
            VulkanFramebufferCreateResult result = VulkanFramebufferFactory.Create(plant, renderPass, Descriptor(texture));
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            using AurelianVulkanFramebuffer framebuffer = result.Framebuffer!;
            Assert.Equal(plant.Context.Id, framebuffer.PlantId);
            Assert.Equal(4U, framebuffer.Width);
            Assert.Equal(4U, framebuffer.Height);
            Assert.Same(renderPass, framebuffer.RenderPass);
            Assert.False(framebuffer.IsDisposed);
        });

    [Fact]
    public void AurelianVulkanFramebuffer_Dispose_IsIdempotent()
        => WithPlantResources((plant, _, renderPass, texture) =>
        {
            VulkanFramebufferCreateResult result = VulkanFramebufferFactory.Create(plant, renderPass, Descriptor(texture));
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            AurelianVulkanFramebuffer framebuffer = result.Framebuffer!;
            framebuffer.Dispose();
            framebuffer.Dispose();

            Assert.True(framebuffer.IsDisposed);
        });

    [Fact]
    public void VulkanFramebufferFactory_DoesNotDisposeRenderPassOrTexture()
        => WithPlantResources((plant, _, renderPass, texture) =>
        {
            VulkanFramebufferCreateResult result = VulkanFramebufferFactory.Create(plant, renderPass, Descriptor(texture));
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            result.Framebuffer!.Dispose();

            Assert.False(renderPass.IsDisposed);
            Assert.False(texture.IsDisposed);
            renderPass.Dispose();
            texture.Dispose();
            Assert.True(renderPass.IsDisposed);
            Assert.True(texture.IsDisposed);
        });

    private static VulkanFramebufferDescriptor Descriptor(AurelianVulkanTexture texture)
        => new(4, 4, [texture]);

    private static VulkanTextureCreateResult CreateColorTexture(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator)
        => VulkanTextureFactory.Create(plant, allocator, TexturePlan(
            plant,
            VulkanTextureFormat.Rgba8Unorm,
            VulkanTextureUsage.ColorAttachment | VulkanTextureUsage.TransferDestination | VulkanTextureUsage.ShaderResource));

    private static VulkanTextureCreateResult CreateShaderResourceTexture(AurelianVulkanPlant plant, RawVulkanMemoryAllocator allocator)
        => VulkanTextureFactory.Create(plant, allocator, TexturePlan(
            plant,
            VulkanTextureFormat.Rgba8Unorm,
            VulkanTextureUsage.ShaderResource | VulkanTextureUsage.TransferDestination));

    private static VulkanTextureCreatePlan TexturePlan(
        AurelianVulkanPlant plant,
        VulkanTextureFormat format,
        VulkanTextureUsage usage)
        => new(
            plant.Context.Id,
            4,
            4,
            format,
            usage,
            VulkanMemoryUsage.GpuOnly,
            VulkanResourceLayout.Undefined,
            MipLevels: 1,
            ArrayLayers: 1,
            DebugName: "test.framebuffer.texture");

    private static VulkanRenderPassCreateResult CreateRenderPass(
        AurelianVulkanPlant plant,
        VulkanTextureFormat format = VulkanTextureFormat.Rgba8Unorm)
        => VulkanRenderPassFactory.Create(
            plant,
            new VulkanRenderPassDescriptor([
                new VulkanRenderPassAttachmentDescriptor(
                    "Color0",
                    format,
                    VulkanAttachmentLoadOp.Clear,
                    VulkanAttachmentStoreOp.Store,
                    VulkanResourceLayout.Undefined,
                    VulkanResourceLayout.ColorAttachment),
            ]));

    private static void WithPlantResources(
        Action<AurelianVulkanPlant, RawVulkanMemoryAllocator, AurelianVulkanRenderPass, AurelianVulkanTexture> action)
        => WithPlantAndAllocator((plant, allocator) =>
        {
            VulkanTextureCreateResult textureResult = CreateColorTexture(plant, allocator);
            if (!textureResult.Success)
            {
                Assert.NotEmpty(textureResult.Diagnostics);
                return;
            }

            VulkanRenderPassCreateResult renderPassResult = CreateRenderPass(plant);
            if (!renderPassResult.Success)
            {
                textureResult.Texture!.Dispose();
                Assert.NotEmpty(renderPassResult.Diagnostics);
                return;
            }

            using AurelianVulkanTexture texture = textureResult.Texture!;
            using AurelianVulkanRenderPass renderPass = renderPassResult.RenderPass!;
            action(plant, allocator, renderPass, texture);
        });

    private static void WithPlantAndAllocator(Action<AurelianVulkanPlant, RawVulkanMemoryAllocator> action)
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            using var allocator = new RawVulkanMemoryAllocator(init.Plant!);
            action(init.Plant!, allocator);
        }
    }

    private static void AssertSuccessOrCleanFailure(VulkanFramebufferCreateResult result)
    {
        if (result.Success)
        {
            Assert.Equal(VulkanFramebufferStatus.Created, result.Status);
            Assert.NotNull(result.Framebuffer);
            return;
        }

        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Status, new[] { VulkanFramebufferStatus.Rejected, VulkanFramebufferStatus.Failed });
    }
}
