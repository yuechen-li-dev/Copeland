using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Textures;

namespace Aurelian.Graphics.Vulkan.Pipelines.RenderPasses;

public sealed record VulkanRenderPassAttachmentDescriptor(
    string Name,
    VulkanTextureFormat Format,
    VulkanAttachmentLoadOp LoadOp,
    VulkanAttachmentStoreOp StoreOp,
    VulkanResourceLayout InitialLayout,
    VulkanResourceLayout FinalLayout);
