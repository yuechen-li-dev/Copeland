namespace Aurelian.Graphics.Vulkan.Commanding.Draw;

public sealed record VulkanDrawCommandResult(
    VulkanDrawCommandStatus Status,
    IReadOnlyList<VulkanDrawCommandDiagnostic> Diagnostics)
{
    public bool Success => Status == VulkanDrawCommandStatus.Recorded
        && Diagnostics.All(static diagnostic => diagnostic.Severity != VulkanDrawCommandDiagnosticSeverity.Error);

    public static VulkanDrawCommandResult Recorded(IReadOnlyList<VulkanDrawCommandDiagnostic>? diagnostics = null)
        => new(VulkanDrawCommandStatus.Recorded, diagnostics ?? []);
}
