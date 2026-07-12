namespace Machina.Layout.Frames;

public sealed record FillFrame(
    double Weight = 1,
    double? Cross = null,
    bool CrossFill = true) : FrameSpec;
