namespace Aurelian.Rendering.Contracts.Compositor;

public static class CompositorDispatchDiagnosticCodes
{
    public const string MissingInputs = "ACOMP1001";
    public const string MissingTarget = "ACOMP1002";
    public const string RequiredOutputsNotReady = "ACOMP1003";
    public const string UnsupportedPolicy = "ACOMP1004";
    public const string MechanismUnavailable = "ACOMP1005";
    public const string DispatchFailed = "ACOMP1006";
    public const string DiagnosticsInvalid = "ACOMP1007";
}
