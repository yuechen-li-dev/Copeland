using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Silk.NET.Vulkan;

namespace Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;

public static unsafe class VulkanRenderPassFactory
{
    public static VulkanRenderPassCreateResult Create(
        AurelianVulkanPlant plant,
        VulkanRenderPassDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(plant);
        ArgumentNullException.ThrowIfNull(descriptor);

        PlantId plantId = plant.Context.Id;
        List<VulkanRenderPassDiagnostic> diagnostics = [];
        Validate(plantId, descriptor, diagnostics);
        if (descriptor.ColorAttachments is null)
        {
            return new VulkanRenderPassCreateResult(VulkanRenderPassStatus.Rejected, null, diagnostics);
        }
        if (plant.Device.Handle != 0)
        {
            plant.Vk.GetPhysicalDeviceProperties(plant.PhysicalDevice, out PhysicalDeviceProperties properties);
            if (descriptor.ColorAttachments.Count > properties.Limits.MaxColorAttachments)
            {
                diagnostics.Add(new VulkanRenderPassDiagnostic(
                    VulkanRenderPassDiagnosticCodes.MultipleColorAttachmentsUnsupported,
                    VulkanRenderPassDiagnosticSeverity.Error,
                    $"Device supports at most {properties.Limits.MaxColorAttachments} color attachments.", plantId));
            }
        }
        if (plant.Device.Handle == 0)
        {
            diagnostics.Add(new VulkanRenderPassDiagnostic(
                VulkanRenderPassDiagnosticCodes.RenderPassDisposed,
                VulkanRenderPassDiagnosticSeverity.Error,
                "Cannot create a render pass from a disposed Vulkan plant/device.",
                plantId));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == VulkanRenderPassDiagnosticSeverity.Error))
        {
            return new VulkanRenderPassCreateResult(VulkanRenderPassStatus.Rejected, null, diagnostics);
        }

        VulkanRenderPassAttachmentDescriptor attachment = descriptor.ColorAttachments[0];
        Vk vk = plant.Vk;
        Silk.NET.Vulkan.Device device = plant.Device;
        RenderPass renderPass = default;

        try
        {
            int colorCount = descriptor.ColorAttachments.Count;
            AttachmentDescription* attachments = stackalloc AttachmentDescription[colorCount + 1];
            AttachmentReference* colorReferences = stackalloc AttachmentReference[colorCount];
            for (int index = 0; index < colorCount; index++)
            {
                VulkanRenderPassAttachmentDescriptor color = descriptor.ColorAttachments[index];
                attachments[index] = new AttachmentDescription
                {
                    Format = MapFormat(color.Format),
                    Samples = SampleCountFlags.Count1Bit,
                    LoadOp = MapLoadOp(color.LoadOp),
                    StoreOp = MapStoreOp(color.StoreOp),
                    StencilLoadOp = AttachmentLoadOp.DontCare,
                    StencilStoreOp = AttachmentStoreOp.DontCare,
                    InitialLayout = MapRenderPassLayout(color.InitialLayout),
                    FinalLayout = MapRenderPassLayout(color.FinalLayout),
                };
                colorReferences[index] = new((uint)index, ImageLayout.ColorAttachmentOptimal);
            }
            bool hasDepth = descriptor.DepthAttachment is not null;
            if (descriptor.DepthAttachment is { } depth)
            {
                attachments[colorCount] = new AttachmentDescription
                {
                    Format = Format.D32Sfloat,
                    Samples = SampleCountFlags.Count1Bit,
                    LoadOp = MapLoadOp(depth.LoadOp),
                    StoreOp = MapStoreOp(depth.StoreOp),
                    StencilLoadOp = AttachmentLoadOp.DontCare,
                    StencilStoreOp = AttachmentStoreOp.DontCare,
                    InitialLayout = MapRenderPassLayout(depth.InitialLayout),
                    FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
                };
            }
            AttachmentReference depthReference = new()
            {
                Attachment = (uint)colorCount,
                Layout = ImageLayout.DepthStencilAttachmentOptimal,
            };

            SubpassDescription subpassDescription = new()
            {
                PipelineBindPoint = PipelineBindPoint.Graphics,
                ColorAttachmentCount = (uint)colorCount,
                PColorAttachments = colorReferences,
                PDepthStencilAttachment = hasDepth ? &depthReference : null,
            };

            SubpassDependency dependencyIn = new()
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = AccessFlags.None,
                DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
                DependencyFlags = DependencyFlags.ByRegionBit,
            };

            SubpassDependency dependencyOut = new()
            {
                SrcSubpass = 0,
                DstSubpass = Vk.SubpassExternal,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                DstStageMask = PipelineStageFlags.BottomOfPipeBit,
                SrcAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
                DstAccessMask = AccessFlags.MemoryReadBit,
                DependencyFlags = DependencyFlags.ByRegionBit,
            };
            if (descriptor.ColorAttachments.Any(color => color.FinalLayout == VulkanResourceLayout.ShaderResourceFragment))
            {
                dependencyOut.DstStageMask = PipelineStageFlags.FragmentShaderBit;
                dependencyOut.DstAccessMask = AccessFlags.ShaderReadBit;
            }

            if (hasDepth)
            {
                PipelineStageFlags depthStages = PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit;
                dependencyIn.SrcStageMask |= depthStages;
                dependencyIn.DstStageMask |= depthStages;
                dependencyIn.SrcAccessMask |= AccessFlags.DepthStencilAttachmentWriteBit;
                dependencyIn.DstAccessMask |= AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit;
                dependencyOut.SrcStageMask |= depthStages;
                dependencyOut.SrcAccessMask |= AccessFlags.DepthStencilAttachmentWriteBit;
            }

            SubpassDependency* dependencies = stackalloc SubpassDependency[2];
            dependencies[0] = dependencyIn;
            dependencies[1] = dependencyOut;

            RenderPassCreateInfo createInfo = new()
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = (uint)(colorCount + (hasDepth ? 1 : 0)),
                PAttachments = attachments,
                SubpassCount = 1,
                PSubpasses = &subpassDescription,
                DependencyCount = 2,
                PDependencies = dependencies,
            };

            Result createResult = vk.CreateRenderPass(device, &createInfo, (AllocationCallbacks*)null, out renderPass);
            if (createResult != Result.Success)
            {
                diagnostics.Add(new VulkanRenderPassDiagnostic(
                    VulkanRenderPassDiagnosticCodes.RenderPassCreationFailed,
                    VulkanRenderPassDiagnosticSeverity.Error,
                    $"vkCreateRenderPass failed with result {createResult}.",
                    plantId,
                    attachment.Name));
                return new VulkanRenderPassCreateResult(VulkanRenderPassStatus.Failed, null, diagnostics);
            }

            AurelianVulkanRenderPass owner = new(vk, device, renderPass, plantId, descriptor);
            renderPass = default;
            return new VulkanRenderPassCreateResult(VulkanRenderPassStatus.Created, owner, diagnostics);
        }
        catch (Exception ex)
        {
            if (renderPass.Handle != 0 && device.Handle != 0)
            {
                vk.DestroyRenderPass(device, renderPass, (AllocationCallbacks*)null);
            }

            diagnostics.Add(new VulkanRenderPassDiagnostic(
                VulkanRenderPassDiagnosticCodes.RenderPassCreationFailed,
                VulkanRenderPassDiagnosticSeverity.Error,
                $"Unexpected Vulkan render pass creation failure: {ex.Message}",
                plantId,
                attachment.Name));
            return new VulkanRenderPassCreateResult(VulkanRenderPassStatus.Failed, null, diagnostics);
        }
    }

    private static void Validate(
        PlantId plantId,
        VulkanRenderPassDescriptor descriptor,
        List<VulkanRenderPassDiagnostic> diagnostics)
    {
        if (descriptor.ColorAttachments is null || descriptor.ColorAttachments.Count == 0)
        {
            diagnostics.Add(new VulkanRenderPassDiagnostic(
                VulkanRenderPassDiagnosticCodes.NoColorAttachments,
                VulkanRenderPassDiagnosticSeverity.Error,
                "Render pass requires at least one color attachment.",
                plantId));
            return;
        }

        if (descriptor.ColorAttachments.Count > 8)
        {
            diagnostics.Add(new VulkanRenderPassDiagnostic(
                VulkanRenderPassDiagnosticCodes.MultipleColorAttachmentsUnsupported,
                VulkanRenderPassDiagnosticSeverity.Error,
                "Render pass supports up to eight color attachments.",
                plantId));
        }

        foreach (VulkanRenderPassAttachmentDescriptor attachment in descriptor.ColorAttachments)
        {
            if (!Enum.IsDefined(attachment.Format) || attachment.Format == VulkanTextureFormat.D32Float)
            {
                diagnostics.Add(new VulkanRenderPassDiagnostic(
                    VulkanRenderPassDiagnosticCodes.UnsupportedAttachmentFormat,
                    VulkanRenderPassDiagnosticSeverity.Error,
                    $"Unsupported color attachment format '{attachment.Format}'.",
                    plantId,
                    attachment.Name));
            }

            if (!IsSupportedInitialLayout(attachment.InitialLayout))
            {
                diagnostics.Add(new VulkanRenderPassDiagnostic(
                    VulkanRenderPassDiagnosticCodes.UnsupportedInitialLayout,
                    VulkanRenderPassDiagnosticSeverity.Error,
                    $"Unsupported color attachment initial layout '{attachment.InitialLayout}' for render pass M0.",
                    plantId,
                    attachment.Name));
            }

            if (!IsSupportedFinalLayout(attachment.FinalLayout))
            {
                diagnostics.Add(new VulkanRenderPassDiagnostic(
                    VulkanRenderPassDiagnosticCodes.UnsupportedFinalLayout,
                    VulkanRenderPassDiagnosticSeverity.Error,
                    $"Unsupported color attachment final layout '{attachment.FinalLayout}' for render pass M0.",
                    plantId,
                    attachment.Name));
            }
        }

        if (descriptor.DepthAttachment is { } depth)
        {
            if (depth.Format != VulkanTextureFormat.D32Float
                || depth.InitialLayout is not (VulkanResourceLayout.Undefined or VulkanResourceLayout.DepthStencilAttachment)
                || depth.FinalLayout != VulkanResourceLayout.DepthStencilAttachment
                || (depth.LoadOp == VulkanAttachmentLoadOp.Load && depth.InitialLayout == VulkanResourceLayout.Undefined))
            {
                diagnostics.Add(new VulkanRenderPassDiagnostic(
                    VulkanRenderPassDiagnosticCodes.UnsupportedAttachmentFormat,
                    VulkanRenderPassDiagnosticSeverity.Error,
                    "Depth attachment requires D32Float, Undefined or DepthStencilAttachment initial layout, and DepthStencilAttachment final layout; Load requires preserved depth.",
                    plantId,
                    depth.Name));
            }
        }
    }

    private static bool IsSupportedInitialLayout(VulkanResourceLayout layout)
        => layout is VulkanResourceLayout.Undefined
            or VulkanResourceLayout.ColorAttachment
            or VulkanResourceLayout.Present
            or VulkanResourceLayout.TransferSource;

    private static bool IsSupportedFinalLayout(VulkanResourceLayout layout)
        => layout is VulkanResourceLayout.ColorAttachment
            or VulkanResourceLayout.ShaderResourceFragment
            or VulkanResourceLayout.Present
            or VulkanResourceLayout.TransferSource;

    private static ImageLayout MapRenderPassLayout(VulkanResourceLayout layout)
    {
        VulkanBarrierPlanResult mapping = VulkanBarrierMappings.Map(layout);
        if (!mapping.Success || mapping.Mapping is null)
        {
            throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unsupported Vulkan render pass layout.");
        }

        return mapping.Mapping.ImageLayout;
    }

    private static AttachmentLoadOp MapLoadOp(VulkanAttachmentLoadOp loadOp)
        => loadOp switch
        {
            VulkanAttachmentLoadOp.Load => AttachmentLoadOp.Load,
            VulkanAttachmentLoadOp.Clear => AttachmentLoadOp.Clear,
            VulkanAttachmentLoadOp.DontCare => AttachmentLoadOp.DontCare,
            _ => throw new ArgumentOutOfRangeException(nameof(loadOp), loadOp, "Unsupported Vulkan attachment load op."),
        };

    private static AttachmentStoreOp MapStoreOp(VulkanAttachmentStoreOp storeOp)
        => storeOp switch
        {
            VulkanAttachmentStoreOp.Store => AttachmentStoreOp.Store,
            VulkanAttachmentStoreOp.DontCare => AttachmentStoreOp.DontCare,
            _ => throw new ArgumentOutOfRangeException(nameof(storeOp), storeOp, "Unsupported Vulkan attachment store op."),
        };

    private static Format MapFormat(VulkanTextureFormat format)
        => format switch
        {
            VulkanTextureFormat.Rgba8Unorm => Format.R8G8B8A8Unorm,
            VulkanTextureFormat.Bgra8Unorm => Format.B8G8R8A8Unorm,
            VulkanTextureFormat.Rgba8Srgb => Format.R8G8B8A8Srgb,
            VulkanTextureFormat.Bgra8Srgb => Format.B8G8R8A8Srgb,
            VulkanTextureFormat.Rgba16Float => Format.R16G16B16A16Sfloat,
            VulkanTextureFormat.Rgba32Float => Format.R32G32B32A32Sfloat,
            VulkanTextureFormat.R32Float => Format.R32Sfloat,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported Vulkan texture format."),
        };
}
