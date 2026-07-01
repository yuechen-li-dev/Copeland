using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanRenderPassM0Tests
{
    [Fact]
    public void VulkanRenderPassFactory_Create_WhenVulkanUnavailable_SkipsCleanly()
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            VulkanRenderPassCreateResult result = VulkanRenderPassFactory.Create(init.Plant!, SingleColorDescriptor());
            AssertSuccessOrCleanFailure(result);
            result.RenderPass?.Dispose();
        }
    }

    [Fact]
    public void VulkanRenderPassFactory_CreateRejectsNoColorAttachments()
        => WithPlant(plant =>
        {
            VulkanRenderPassCreateResult result = VulkanRenderPassFactory.Create(
                plant,
                new VulkanRenderPassDescriptor([]));

            Assert.False(result.Success);
            Assert.Equal(VulkanRenderPassStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanRenderPassDiagnosticCodes.NoColorAttachments);
        });

    [Fact]
    public void VulkanRenderPassFactory_CreateRejectsMultipleColorAttachments()
        => WithPlant(plant =>
        {
            VulkanRenderPassCreateResult result = VulkanRenderPassFactory.Create(
                plant,
                new VulkanRenderPassDescriptor([
                    ColorAttachment("Color0"),
                    ColorAttachment("Color1"),
                ]));

            Assert.False(result.Success);
            Assert.Equal(VulkanRenderPassStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanRenderPassDiagnosticCodes.MultipleColorAttachmentsUnsupported);
        });

    [Fact]
    public void VulkanRenderPassFactory_CreateRejectsUnsupportedInitialLayout()
        => WithPlant(plant =>
        {
            VulkanRenderPassCreateResult result = VulkanRenderPassFactory.Create(
                plant,
                new VulkanRenderPassDescriptor([
                    ColorAttachment("Color0") with { InitialLayout = VulkanResourceLayout.TransferDestination },
                ]));

            Assert.False(result.Success);
            Assert.Equal(VulkanRenderPassStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanRenderPassDiagnosticCodes.UnsupportedInitialLayout);
        });

    [Fact]
    public void VulkanRenderPassFactory_CreateRejectsUnsupportedFinalLayout()
        => WithPlant(plant =>
        {
            VulkanRenderPassCreateResult result = VulkanRenderPassFactory.Create(
                plant,
                new VulkanRenderPassDescriptor([
                    ColorAttachment("Color0") with { FinalLayout = VulkanResourceLayout.TransferDestination },
                ]));

            Assert.False(result.Success);
            Assert.Equal(VulkanRenderPassStatus.Rejected, result.Status);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanRenderPassDiagnosticCodes.UnsupportedFinalLayout);
        });

    [Fact]
    public void VulkanRenderPassFactory_CreateSingleColorRenderPass_WhenPlantCreated_SucceedsOrReportsCleanFailure()
        => WithPlant(plant =>
        {
            VulkanRenderPassCreateResult result = VulkanRenderPassFactory.Create(plant, SingleColorDescriptor());
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            using AurelianVulkanRenderPass renderPass = result.RenderPass!;
            Assert.Equal(PlantId.Zero, renderPass.PlantId);
            Assert.Equal(VulkanAttachmentLoadOp.Clear, renderPass.Descriptor.ColorAttachments[0].LoadOp);
            Assert.False(renderPass.IsDisposed);
        });

    [Fact]
    public void AurelianVulkanRenderPass_Dispose_IsIdempotent()
        => WithPlant(plant =>
        {
            VulkanRenderPassCreateResult result = VulkanRenderPassFactory.Create(plant, SingleColorDescriptor());
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            AurelianVulkanRenderPass renderPass = result.RenderPass!;
            renderPass.Dispose();
            renderPass.Dispose();

            Assert.True(renderPass.IsDisposed);
        });

    [Fact]
    public void VulkanRenderPassFactory_DoesNotCreateDeferredNativeObjects()
    {
        string renderPassRoot = Path.Combine(FindRepositoryRoot(), "src", "Aurelian.Graphics", "Vulkan", "Pipelines", "RenderPasses");
        string[] sourceFiles = Directory.GetFiles(renderPassRoot, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(sourceFiles);

        foreach (string file in sourceFiles)
        {
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("Create" + "Framebuffer", text, StringComparison.Ordinal);
            Assert.DoesNotContain("vkCreate" + "Framebuffer", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CreateGraphics" + "Pipelines", text, StringComparison.Ordinal);
            Assert.DoesNotContain("vkCreateGraphics" + "Pipelines", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Cmd" + "Draw", text, StringComparison.Ordinal);
            Assert.DoesNotContain("vkCmd" + "Draw", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CmdBegin" + "RenderPass", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CmdEnd" + "RenderPass", text, StringComparison.Ordinal);
        }
    }

    private static void WithPlant(Action<AurelianVulkanPlant> action)
    {
        var init = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            action(init.Plant!);
        }
    }

    private static VulkanRenderPassDescriptor SingleColorDescriptor()
        => new([
            ColorAttachment("Color0"),
        ]);

    private static VulkanRenderPassAttachmentDescriptor ColorAttachment(string name)
        => new(
            name,
            VulkanTextureFormat.Rgba8Unorm,
            VulkanAttachmentLoadOp.Clear,
            VulkanAttachmentStoreOp.Store,
            VulkanResourceLayout.Undefined,
            VulkanResourceLayout.ColorAttachment);

    private static void AssertSuccessOrCleanFailure(VulkanRenderPassCreateResult result)
    {
        if (result.Success)
        {
            Assert.Equal(VulkanRenderPassStatus.Created, result.Status);
            Assert.NotNull(result.RenderPass);
            return;
        }

        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Status, new[] { VulkanRenderPassStatus.Rejected, VulkanRenderPassStatus.Failed });
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aurelian.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to find repository root.");
    }
}
