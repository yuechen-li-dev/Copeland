namespace Aurelian.Graphics.Vulkan.Commanding.RenderPasses;

public sealed record VulkanRenderPassBeginResult(
    VulkanRenderPassCommandStatus Status,
    VulkanRenderPassScope? Scope,
    IReadOnlyList<VulkanRenderPassCommandDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanRenderPassCommandStatus.Recorded
        && Scope is not null
        && Diagnostics.All(static diagnostic => diagnostic.Severity != VulkanRenderPassCommandDiagnosticSeverity.Error);

    public static VulkanRenderPassBeginResult Recorded(
        VulkanRenderPassScope scope,
        IReadOnlyList<VulkanRenderPassCommandDiagnostic>? diagnostics = null)
        => new(VulkanRenderPassCommandStatus.Recorded, scope, diagnostics ?? []);
}
