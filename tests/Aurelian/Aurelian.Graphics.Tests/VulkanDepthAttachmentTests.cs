using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanDepthAttachmentTests
{
    [Fact]
    public void DepthFormatRejectsColorUsageAndColorFormatRejectsDepthUsage()
    {
        WithPlant((plant, allocator) =>
        {
            var depthAsColor = VulkanTextureFactory.Create(plant, allocator,
                Plan(plant, VulkanTextureFormat.D32Float, VulkanTextureUsage.ColorAttachment));
            var colorAsDepth = VulkanTextureFactory.Create(plant, allocator,
                Plan(plant, VulkanTextureFormat.Rgba8Unorm, VulkanTextureUsage.DepthAttachment));
            Assert.False(depthAsColor.Success);
            Assert.False(colorAsDepth.Success);
            Assert.Contains(depthAsColor.Diagnostics, item => item.Code == VulkanTextureDiagnosticCodes.UnsupportedFormat);
            Assert.Contains(colorAsDepth.Diagnostics, item => item.Code == VulkanTextureDiagnosticCodes.UnsupportedFormat);
        });
    }

    [Fact]
    public void FramebufferRequiresMatchingDepthExtentAndPresence()
    {
        WithPlant((plant, allocator) =>
        {
            var colorResult = VulkanTextureFactory.Create(plant, allocator,
                Plan(plant, VulkanTextureFormat.Rgba8Unorm, VulkanTextureUsage.ColorAttachment));
            Assert.True(colorResult.Success);
            using var color = colorResult.Texture!;
            var depthResult = VulkanTextureFactory.Create(plant, allocator,
                Plan(plant, VulkanTextureFormat.D32Float, VulkanTextureUsage.DepthAttachment));
            Assert.True(depthResult.Success);
            using var depth = depthResult.Texture!;
            var smallResult = VulkanTextureFactory.Create(plant, allocator,
                Plan(plant, VulkanTextureFormat.D32Float, VulkanTextureUsage.DepthAttachment) with { Width = 16 });
            Assert.True(smallResult.Success);
            using var small = smallResult.Texture!;
            var passResult = VulkanRenderPassFactory.Create(plant, new VulkanRenderPassDescriptor(
                [new("color", VulkanTextureFormat.Rgba8Unorm, VulkanAttachmentLoadOp.Clear,
                    VulkanAttachmentStoreOp.Store, VulkanResourceLayout.Undefined, VulkanResourceLayout.ColorAttachment)],
                new("depth", VulkanTextureFormat.D32Float, VulkanAttachmentLoadOp.Clear,
                    VulkanAttachmentStoreOp.DontCare, VulkanResourceLayout.Undefined, VulkanResourceLayout.DepthStencilAttachment)));
            Assert.True(passResult.Success);
            using var pass = passResult.RenderPass!;
            var missing = VulkanFramebufferFactory.Create(plant, pass, new(32, 32, [color]));
            var wrongSize = VulkanFramebufferFactory.Create(plant, pass, new(32, 32, [color], small));
            Assert.False(missing.Success);
            Assert.False(wrongSize.Success);
            var valid = VulkanFramebufferFactory.Create(plant, pass, new(32, 32, [color], depth));
            Assert.True(valid.Success);
            valid.Framebuffer!.Dispose();
        });
    }

    [Fact]
    public void DepthLoadRejectsUndefinedInitialLayout()
    {
        WithPlant((plant, _) =>
        {
            var result = VulkanRenderPassFactory.Create(plant, new VulkanRenderPassDescriptor(
                [new("color", VulkanTextureFormat.Rgba8Unorm, VulkanAttachmentLoadOp.Clear,
                    VulkanAttachmentStoreOp.Store, VulkanResourceLayout.Undefined, VulkanResourceLayout.ColorAttachment)],
                new("depth", VulkanTextureFormat.D32Float, VulkanAttachmentLoadOp.Load,
                    VulkanAttachmentStoreOp.Store, VulkanResourceLayout.Undefined, VulkanResourceLayout.DepthStencilAttachment)));
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, item => item.Code == VulkanRenderPassDiagnosticCodes.UnsupportedAttachmentFormat);
        });
    }

    private static VulkanTextureCreatePlan Plan(AurelianVulkanPlant plant, VulkanTextureFormat format, VulkanTextureUsage usage)
    {
        return new VulkanTextureCreatePlan(plant.Context.Id, 32, 32, format, usage,
            VulkanMemoryUsage.GpuOnly, VulkanResourceLayout.Undefined);
    }

    private static void WithPlant(Action<AurelianVulkanPlant, RawVulkanMemoryAllocator> test)
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        if (!init.Success)
        {
            Assert.NotEmpty(init.Diagnostics);
            return;
        }
        using var plant = init.Plant!;
        using var allocator = new RawVulkanMemoryAllocator(plant);
        test(plant, allocator);
    }
}
