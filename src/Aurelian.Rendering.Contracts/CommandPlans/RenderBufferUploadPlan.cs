namespace Aurelian.Rendering.Contracts.CommandPlans;

public sealed record RenderBufferUploadPlan(
    string Name,
    int ByteCount,
    int ItemCount);
