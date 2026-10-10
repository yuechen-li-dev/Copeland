using System.Numerics;

namespace Aurelian.Rendering.Contracts.Models;

public enum LocalLightKind
{
    Point,
    Spot,
}

/// <summary>Linear radiance with a finite influence radius in scene metres.</summary>
public sealed record LocalLight3D(Vector3 Position, Vector3 Color, float Intensity, float Range)
{
    public LocalLightKind Kind { get; init; }
    public Vector3 Direction { get; init; } = -Vector3.UnitY;
    public float InnerAngle { get; init; } = .35f;
    public float OuterAngle { get; init; } = .6f;
    public bool CastShadows { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || !Finite(Position) || !Finite(Color)
            || Color.X < 0 || Color.Y < 0 || Color.Z < 0
            || !float.IsFinite(Intensity) || Intensity < 0 || Intensity > 100_000
            || !float.IsFinite(Range) || Range < .01f || Range > 1000
            || !Finite(Direction) || !float.IsFinite(Direction.LengthSquared()) || Direction.LengthSquared() < .000001f
            || !float.IsFinite(InnerAngle) || !float.IsFinite(OuterAngle)
            || InnerAngle < 0 || OuterAngle <= InnerAngle || OuterAngle >= 1.55f)
        {
            throw new ArgumentOutOfRangeException(nameof(LocalLight3D), "Local light radiance, radius and spot cone must be finite and bounded.");
        }
        if (CastShadows && (Kind != LocalLightKind.Spot || Range <= .1f))
        {
            throw new NotSupportedException("AUR-LIGHT-LOCAL-002: Local shadows currently require a spot light with range greater than 0.1 metres.");
        }
    }

    private static bool Finite(Vector3 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }
}
