using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Pipelines.Framebuffers;

public static unsafe class VulkanFramebufferFactory
{
    public static VulkanFramebufferCreateResult Create(
        AurelianVulkanPlant plant,
        AurelianVulkanRenderPass renderPass,
        VulkanFramebufferDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(renderPass);
        ArgumentNullException.ThrowIfNull(descriptor);

        PlantId plantId = plant.Context.Id;
        List<VulkanFramebufferDiagnostic> diagnostics = [];
        Validate(plantId, plant, renderPass, descriptor, diagnostics);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == VulkanFramebufferDiagnosticSeverity.Error))
        {
            return new VulkanFramebufferCreateResult(VulkanFramebufferStatus.Rejected, null, diagnostics);
        }

        AurelianVulkanTexture attachment = descriptor.ColorAttachments[0];
        ImageView imageView = attachment.NativeImageView!.Value;
        Framebuffer framebuffer = default;
        Vk vk = plant.Vk;
        Silk.NET.Vulkan.Device device = plant.Device;

        try
        {
            FramebufferCreateInfo createInfo = new()
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = renderPass.NativeRenderPass,
                AttachmentCount = 1,
                PAttachments = &imageView,
                Width = descriptor.Width,
                Height = descriptor.Height,
                Layers = 1,
            };

            Result createResult = vk.CreateFramebuffer(device, &createInfo, (AllocationCallbacks*)null, out framebuffer);
            if (createResult != Result.Success)
            {
                diagnostics.Add(new VulkanFramebufferDiagnostic(
                    VulkanFramebufferDiagnosticCodes.FramebufferCreationFailed,
                    VulkanFramebufferDiagnosticSeverity.Error,
                    $"vkCreateFramebuffer failed with result {createResult}.",
                    plantId));
                return new VulkanFramebufferCreateResult(VulkanFramebufferStatus.Failed, null, diagnostics);
            }

            AurelianVulkanFramebuffer owner = new(vk, device, framebuffer, plantId, descriptor.Width, descriptor.Height, descriptor, renderPass);
            framebuffer = default;
            return new VulkanFramebufferCreateResult(VulkanFramebufferStatus.Created, owner, diagnostics);
        }
        catch (Exception ex)
        {
            if (framebuffer.Handle != 0 && device.Handle != 0)
            {
                vk.DestroyFramebuffer(device, framebuffer, (AllocationCallbacks*)null);
            }

            diagnostics.Add(new VulkanFramebufferDiagnostic(
                VulkanFramebufferDiagnosticCodes.FramebufferCreationFailed,
                VulkanFramebufferDiagnosticSeverity.Error,
                $"Unexpected Vulkan framebuffer creation failure: {ex.GetType().Name}: {ex.Message}",
                plantId));
            return new VulkanFramebufferCreateResult(VulkanFramebufferStatus.Failed, null, diagnostics);
        }
    }

    private static void Validate(
        PlantId plantId,
        AurelianVulkanPlant plant,
        AurelianVulkanRenderPass renderPass,
        VulkanFramebufferDescriptor descriptor,
        List<VulkanFramebufferDiagnostic> diagnostics)
    {
        if (descriptor.Width == 0 || descriptor.Height == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanFramebufferDiagnosticCodes.InvalidDimensions,
                "Framebuffer width and height must be greater than zero.",
                plantId));
        }

        if (plant.Device.Handle == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanFramebufferDiagnosticCodes.FramebufferDisposed,
                "Cannot create a framebuffer from a disposed Vulkan plant/device.",
                plantId));
        }

        if (renderPass.IsDisposed || renderPass.NativeRenderPass.Handle == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanFramebufferDiagnosticCodes.FramebufferDisposed,
                "Cannot create a framebuffer for a disposed render pass.",
                plantId));
        }

        if (renderPass.PlantId != plantId)
        {
            diagnostics.Add(Diagnostic(
                VulkanFramebufferDiagnosticCodes.PlantMismatch,
                "Framebuffer render pass must belong to the target Vulkan plant.",
                plantId));
        }

        IReadOnlyList<AurelianVulkanTexture>? attachments = descriptor.ColorAttachments;
        if (attachments is null || attachments.Count == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanFramebufferDiagnosticCodes.NoColorAttachments,
                "Framebuffer M0 requires exactly one color attachment.",
                plantId));
            return;
        }

        if (attachments.Count > 1)
        {
            diagnostics.Add(Diagnostic(
                VulkanFramebufferDiagnosticCodes.MultipleColorAttachmentsUnsupported,
                "Framebuffer M0 supports only one color attachment.",
                plantId));
        }

        if (renderPass.Descriptor.ColorAttachments.Count != 1)
        {
            diagnostics.Add(Diagnostic(
                VulkanFramebufferDiagnosticCodes.RenderPassAttachmentMismatch,
                "Framebuffer M0 requires a render pass with exactly one color attachment.",
                plantId));
        }

        AurelianVulkanTexture? attachment = attachments[0];
        if (attachment is null)
        {
            diagnostics.Add(Diagnostic(
                VulkanFramebufferDiagnosticCodes.AttachmentMissing,
                "Framebuffer color attachment 0 must not be null.",
                plantId));
            return;
        }

        if (attachment.IsDisposed)
        {
            diagnostics.Add(Diagnostic(
                VulkanFramebufferDiagnosticCodes.AttachmentDisposed,
                "Framebuffer color attachment 0 is disposed.",
                plantId));
        }

        if (attachment.PlantId != plantId)
        {
            diagnostics.Add(Diagnostic(
                VulkanFramebufferDiagnosticCodes.PlantMismatch,
                "Framebuffer color attachment 0 must belong to the target Vulkan plant.",
                plantId));
        }

        if (attachment.Width != descriptor.Width || attachment.Height != descriptor.Height)
        {
            diagnostics.Add(Diagnostic(
                VulkanFramebufferDiagnosticCodes.AttachmentSizeMismatch,
                "Framebuffer color attachment 0 dimensions must match the framebuffer descriptor.",
                plantId));
        }

        if ((attachment.Usage & VulkanTextureUsage.ColorAttachment) == 0)
        {
            diagnostics.Add(Diagnostic(
                VulkanFramebufferDiagnosticCodes.AttachmentMissingColorUsage,
                "Framebuffer color attachment 0 must include ColorAttachment texture usage.",
                plantId));
        }

        if (attachment.NativeImageView is not { Handle: not 0 })
        {
            diagnostics.Add(Diagnostic(
                VulkanFramebufferDiagnosticCodes.AttachmentMissingImageView,
                "Framebuffer color attachment 0 must have a native image view.",
                plantId));
        }

        if (renderPass.Descriptor.ColorAttachments.Count == 1
            && renderPass.Descriptor.ColorAttachments[0].Format != attachment.Format)
        {
            diagnostics.Add(Diagnostic(
                VulkanFramebufferDiagnosticCodes.RenderPassAttachmentMismatch,
                "Framebuffer color attachment 0 format must match the render pass color attachment format.",
                plantId,
                renderPass.Descriptor.ColorAttachments[0].Name));
        }
    }

    private static VulkanFramebufferDiagnostic Diagnostic(
        string code,
        string message,
        PlantId plantId,
        string? attachmentName = null)
        => new(code, VulkanFramebufferDiagnosticSeverity.Error, message, plantId, attachmentName);
}
