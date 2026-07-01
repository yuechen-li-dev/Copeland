namespace Aurelian.Core.Engine.Frames;

public static class AurelianFrameLoopDiagnosticCodes
{
    public const string FramePumpMissing = "ACFL1001";
    public const string InputProviderMissing = "ACFL1002";
    public const string InvalidMaxFrames = "ACFL1003";
    public const string FrameInputMissing = "ACFL1004";
    public const string FrameFailed = "ACFL1005";
    public const string PresentationFailed = "ACFL1006";
    public const string Cancelled = "ACFL1007";
    public const string RuntimeTickFailed = "ACFL1008";
    public const string RuntimeTickRejected = "ACFL1009";
    public const string RuntimeTickCancelled = "ACFL1010";
}

