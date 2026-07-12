using Aurelian.Graphics.Plants;

namespace Aurelian.Graphics.Vulkan.Commanding.RenderPasses;

/// <summary>
/// Typed token proving that a render pass was successfully begun on a command buffer lease.
/// The identifiers are process-local diagnostics and validation state, not global device state.
/// </summary>
public readonly record struct VulkanRenderPassScope(
    PlantId PlantId,
    ulong CommandBufferLeaseId,
    ulong ScopeId);
