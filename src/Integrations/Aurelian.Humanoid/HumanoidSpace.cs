using System.Numerics;

namespace Aurelian.Humanoid;

/// <summary>Aetheris canonical millimetres/+Z up/+Y forward to Aurelian metres/+Y up/-Z forward.</summary>
public static class HumanoidSpace
{
    public static readonly Matrix4x4 SourceToWorld = new(
        .001f, 0, 0, 0,
        0, 0, -.001f, 0,
        0, .001f, 0, 0,
        0, 0, 0, 1);
}
