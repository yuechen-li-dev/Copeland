namespace Aurelian.Graphics.Vulkan.Resources.Barriers;

public sealed record VulkanBarrierPlanResult(
    VulkanBarrierStatus Status,
    VulkanBarrierPlan? Plan,
    VulkanBarrierMapping? Mapping,
    IReadOnlyList<VulkanBarrierDiagnostic> Diagnostics)
{
    public bool Success => Status != VulkanBarrierStatus.Rejected
        && Diagnostics.All(x => x.Severity != VulkanBarrierDiagnosticSeverity.Error);

    public static VulkanBarrierPlanResult Planned(VulkanBarrierPlan plan, IReadOnlyList<VulkanBarrierDiagnostic>? diagnostics = null)
        => new(VulkanBarrierStatus.Planned, plan, null, diagnostics ?? []);

    public static VulkanBarrierPlanResult NoOp(IReadOnlyList<VulkanBarrierDiagnostic>? diagnostics = null)
        => new(VulkanBarrierStatus.NoOp, null, null, diagnostics ?? []);

    public static VulkanBarrierPlanResult Rejected(IReadOnlyList<VulkanBarrierDiagnostic> diagnostics)
        => new(VulkanBarrierStatus.Rejected, null, null, diagnostics);
}
