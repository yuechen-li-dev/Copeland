namespace Aurelian.Rendering.Contracts.Models;

/// <summary>RGB approximation of rod–cone vision for a scene with authored luminance units.</summary>
public sealed record LowLightVision3D
{
    public float Strength { get; init; } = .65f;

    /// <summary>Scene-linear luminance at which vision is fully cone-dominated.</summary>
    public float PhotopicLuminance { get; init; } = .08f;

    internal void Validate()
    {
        if (!float.IsFinite(Strength) || Strength is < 0 or > 1
            || !float.IsFinite(PhotopicLuminance) || PhotopicLuminance is < .0001f or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(LowLightVision3D),
                "Strength must be in [0, 1] and photopic scene luminance must be in [0.0001, 10000].");
        }
    }
}
