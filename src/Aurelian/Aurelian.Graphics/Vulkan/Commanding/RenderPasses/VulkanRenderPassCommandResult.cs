namespace Aurelian.Graphics.Vulkan.Commanding.RenderPasses;

public sealed record VulkanRenderPassCommandResult(
    VulkanRenderPassCommandStatus Status,
    IReadOnlyList<VulkanRenderPassCommandDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanRenderPassCommandStatus.Recorded
        && Diagnostics.All(static diagnostic => diagnostic.Severity != VulkanRenderPassCommandDiagnosticSeverity.Error);

    public static VulkanRenderPassCommandResult Recorded(IReadOnlyList<VulkanRenderPassCommandDiagnostic>? diagnostics = null)
        => new(VulkanRenderPassCommandStatus.Recorded, diagnostics ?? []);
}
