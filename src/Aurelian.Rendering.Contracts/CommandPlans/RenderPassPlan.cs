using System.Collections.Generic;

namespace Aurelian.Rendering.Contracts.CommandPlans;

public sealed record RenderPassPlan(
    string Name,
    RenderTargetRef Target,
    RenderPipelineRef Pipeline,
    RenderShaderRef Shader,
    IReadOnlyList<DrawItem2D> DrawItems);
