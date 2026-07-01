namespace Aurelian.Runtime.Compositor;

public static class CompositorPolicyDiagnosticCodes
{
    public const string MissingFrameFacts = "ARCOMP1001";
    public const string MissingRequiredOutputs = "ARCOMP1002";
    public const string RequiredOutputsNotReady = "ARCOMP1003";
    public const string UnsupportedPolicy = "ARCOMP1004";
    public const string DispatchActFailed = "ARCOMP1005";
    public const string DispatchResultFailed = "ARCOMP1006";
}
