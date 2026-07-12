using System.Collections.Generic;

namespace Aurelian.Rendering.Contracts.Compositor;

public sealed record CompositorFrameFacts(
    ulong FrameId,
    IReadOnlyList<PlantOutputReadiness> Outputs,
    CompositorDiagnostics PreviousDiagnostics,
    double? ShadowCalibrationConfidence = null);
