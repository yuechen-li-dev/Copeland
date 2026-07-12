namespace Aurelian.Rendering.Contracts.CommandPlans;

public enum RenderCommandPlanReason
{
    Ready,
    EmptySnapshot,
    MissingCamera,
    MissingDrawItems,
    InvalidDrawItem,
    MissingPipeline,
    MissingShader,
    UnsupportedFeature
}
