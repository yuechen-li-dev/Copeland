using Aurelian.Rendering.Contracts;

namespace Aurelian.Runtime.Rendering;

public sealed record WorldRenderSnapshotOptions(
    RenderFrameId FrameId,
    string DefaultCameraId = "MainCamera",
    double DefaultCameraWidth = 1280,
    double DefaultCameraHeight = 720);
