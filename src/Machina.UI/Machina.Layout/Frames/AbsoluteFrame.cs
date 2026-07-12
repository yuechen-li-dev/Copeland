namespace Machina.Layout.Frames;

public sealed record AbsoluteFrame(
    double X,
    double Y,
    double Width,
    double Height) : FrameSpec;
