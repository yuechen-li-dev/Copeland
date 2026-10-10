using System.Numerics;

namespace Aurelian.Rendering.Contracts.Models;

/// <summary>Optional box projection for a precompiled static environment captured at Position.</summary>
public sealed record ReflectionProbe3D(Vector3 Position, Vector3 HalfSize)
{
    public void Validate()
    {
        if (!Finite(Position) || !Finite(HalfSize) || HalfSize.X <= 0 || HalfSize.Y <= 0 || HalfSize.Z <= 0
            || HalfSize.X > 1000 || HalfSize.Y > 1000 || HalfSize.Z > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(ReflectionProbe3D), "Reflection probe bounds must be finite positive scene metres.");
        }
    }

    private static bool Finite(Vector3 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}
