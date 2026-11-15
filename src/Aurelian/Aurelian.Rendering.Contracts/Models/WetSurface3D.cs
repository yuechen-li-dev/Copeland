using System.Numerics;

namespace Aurelian.Rendering.Contracts.Models;

/// <summary>Authored porous-surface response. Apply to the dry material for each presentation snapshot.</summary>
public sealed record WetSurface3D
{
    public float Darkening { get; init; } = .35f;
    public float RoughnessMultiplier { get; init; } = .3f;
    public float NormalMultiplier { get; init; } = .55f;

    public ModelMaterial Apply(ModelMaterial dry, float wetness, float exposure = 1)
    {
        ArgumentNullException.ThrowIfNull(dry);
        Unit(Darkening, nameof(Darkening));
        Unit(RoughnessMultiplier, nameof(RoughnessMultiplier));
        Unit(NormalMultiplier, nameof(NormalMultiplier));
        Unit(wetness, nameof(wetness));
        Unit(exposure, nameof(exposure));
        dry.Validate();
        if (dry.Transmission > 0 || dry.Unlit || dry.AlphaBlend)
        {
            throw new ArgumentException("Wet substrate response requires a lit opaque material.", nameof(dry));
        }
        float amount = wetness * exposure;
        if (amount == 0)
        {
            return dry;
        }
        float colorScale = 1 - Darkening * amount;
        return dry with
        {
            BaseColor = new(dry.BaseColor.X * colorScale, dry.BaseColor.Y * colorScale,
                dry.BaseColor.Z * colorScale, dry.BaseColor.W),
            Roughness = dry.Roughness * (1 - (1 - RoughnessMultiplier) * amount),
            NormalScale = dry.NormalScale * (1 - (1 - NormalMultiplier) * amount),
        };
    }

    /// <summary>Orientation exposure only; shelter must be supplied separately by the author.</summary>
    public static float RainExposure(Vector3 normal, float shelter = 0)
    {
        Unit(shelter, nameof(shelter));
        if (!float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z)
            || MathF.Abs(normal.LengthSquared() - 1) > .001f)
        {
            throw new ArgumentException("Exposure needs a finite unit world normal.", nameof(normal));
        }
        return Math.Clamp((normal.Y + .25f) / 1.25f, 0, 1) * (1 - shelter);
    }

    private static void Unit(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1)
        {
            throw new ArgumentOutOfRangeException(name, "Expected a finite value in 0..1.");
        }
    }
}
