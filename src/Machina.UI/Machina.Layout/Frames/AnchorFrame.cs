using Machina.Layout.Geometry;

namespace Machina.Layout.Frames;

public sealed record AnchorFrame(
    UiLength? Left = null,
    UiLength? Right = null,
    UiLength? Top = null,
    UiLength? Bottom = null,
    UiLength? Width = null,
    UiLength? Height = null) : FrameSpec;
