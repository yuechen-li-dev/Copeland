using System.Numerics;

namespace Aurelian.Graphics.Vulkan.Native3D;

/// <summary>A world-space elliptical projector with local +Z aligned to the receiver's outward normal.</summary>
public sealed record SurfaceDecal3D(string Receiver, Vector3 Center, Quaternion Orientation, Vector3 HalfExtent,
    Vector3 ColorMultiplier, float Opacity = 1, float? Roughness = null, float NormalFlattening = 0)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Receiver) || !Finite(Center) || !Finite(HalfExtent)
            || MathF.Min(HalfExtent.X, MathF.Min(HalfExtent.Y, HalfExtent.Z)) < .001f
            || !float.IsFinite(Orientation.LengthSquared()) || MathF.Abs(Orientation.LengthSquared() - 1) > .001f
            || !Finite(ColorMultiplier) || Vector3.Clamp(ColorMultiplier, Vector3.Zero, Vector3.One) != ColorMultiplier
            || !float.IsFinite(Opacity) || Opacity is < 0 or > 1
            || Roughness is float roughness && (!float.IsFinite(roughness) || roughness is < .045f or > 1)
            || !float.IsFinite(NormalFlattening) || NormalFlattening is < 0 or > 1)
        {
            throw new InvalidDataException("AUR-DECAL-001: A projector requires a receiver, finite positive extents, a unit orientation and bounded material factors.");
        }
    }

    private static bool Finite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
