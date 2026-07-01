namespace Aurelian.Rendering.Contracts.CommandPlans;

public static class RenderCommandPlanDiagnosticCodes
{
    public const string EmptySnapshot = "ACP1001";
    public const string MissingCamera = "ACP1002";
    public const string MissingDrawItems = "ACP1003";
    public const string InvalidDrawItem = "ACP1004";
    public const string MissingPipeline = "ACP1005";
    public const string MissingShader = "ACP1006";
    public const string UnsupportedFeature = "ACP1007";
}
