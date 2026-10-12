using System.Collections.Immutable;
using System.Numerics;

namespace Aurelian.Rendering.Contracts.Models;

public enum WaterDebugView3D
{
    Shaded,
    Depth,
    Normals,
    Foam,
    RefractionHits,
    PlanarReflection,
}

/// <summary>World-space wind wave. Steepness is dimensionless and bounded
/// collectively so the analytic surface cannot fold over itself.</summary>
public sealed record WaterWave3D(Vector2 Direction, float Amplitude, float Wavelength,
    float Speed, float Steepness = .15f);

public sealed record WaterImpulse3D(Vector2 Center, float StartedAt, float Amplitude = .035f,
    float Wavelength = .45f, float Speed = 1.3f, float Lifetime = 3);

/// <summary>Explicit presentation time and up to eight immutable impact events.
/// No wall clock, GPU readback or hidden event eviction.</summary>
public sealed record WaterFrame3D(float Time = 0)
{
    public ImmutableArray<WaterImpulse3D> Impulses { get; init; } = [];

    public void Validate()
    {
        if (!float.IsFinite(Time) || Time < 0 || Time > 65536 || Impulses.IsDefault || Impulses.Length > 8)
        {
            throw new InvalidDataException("AUR-WATER-002: Water requires time in 0..65536 seconds and at most eight impulses.");
        }
        foreach (var impulse in Impulses)
        {
            if (impulse is null)
            {
                throw new InvalidDataException("AUR-WATER-003: A ripple event cannot be null.");
            }
            float[] values = [impulse.Center.X, impulse.Center.Y, impulse.StartedAt,
                impulse.Amplitude, impulse.Wavelength, impulse.Speed, impulse.Lifetime];
            if (values.Any(value => !float.IsFinite(value)) || impulse.StartedAt < 0
                || Math.Abs(impulse.Amplitude) > .5f || impulse.Wavelength is < .05f or > 10
                || impulse.Speed is < .01f or > 20 || impulse.Lifetime is < .1f or > 30)
            {
                throw new InvalidDataException("AUR-WATER-003: Invalid world-space ripple event.");
            }
        }
    }
}

public sealed record PlanarWaterReflection3D(float Height)
{
    public int ResolutionDivisor { get; init; } = 2;

    public void Validate()
    {
        if (!float.IsFinite(Height) || ResolutionDivisor is not (2 or 4))
        {
            throw new InvalidDataException("AUR-WATER-004: Planar water reflection requires a finite height and half/quarter resolution.");
        }
    }
}

/// <summary>A pond/pool surface profile in metres. Geometry supplies a horizontal
/// tessellated base surface; the GPU applies wind waves and interaction ripples.</summary>
public sealed record WaterSurface3D
{
    public WaterDebugView3D DebugView { get; init; }
    public ImmutableArray<WaterWave3D> Waves { get; init; } =
    [
        new(new(1, .25f), .012f, 2.4f, .65f, .08f),
        new(new(-.4f, 1), .007f, 1.3f, .42f, .06f),
        new(new(.6f, -.8f), .004f, .7f, .3f, .04f),
    ];
    public float DetailAmplitude { get; init; } = .0006f;
    public float DetailWavelength { get; init; } = .16f;
    public float FoamWidth { get; init; } = .12f;
    public float FoamStrength { get; init; } = .65f;
    public float MaximumDepth { get; init; } = 8;
    public Vector3 DeepColor { get; init; } = new(.012f, .10f, .12f);

    public ModelMaterial Material(string slot = "water") => new(slot)
    {
        Metallic = 0,
        Roughness = .12f,
        Transmission = 1,
        IndexOfRefraction = 1.333f,
        AttenuationColor = new(.38f, .78f, .84f),
        AttenuationDistance = 2,
        Water = this,
    };

    public void Validate()
    {
        float[] values = [DetailAmplitude, DetailWavelength, FoamWidth, FoamStrength, MaximumDepth,
            DeepColor.X, DeepColor.Y, DeepColor.Z];
        if (!Enum.IsDefined(DebugView) || Waves.IsDefault || Waves.Length > 4 || values.Any(value => !float.IsFinite(value))
            || DetailAmplitude is < 0 or > .02f || DetailWavelength is < .05f or > 2
            || FoamWidth is < .001f or > 2 || FoamStrength is < 0 or > 1
            || MaximumDepth is < .05f or > 100 || DeepColor.X < 0 || DeepColor.Y < 0 || DeepColor.Z < 0
            || DeepColor.X > 1 || DeepColor.Y > 1 || DeepColor.Z > 1)
        {
            throw new InvalidDataException("AUR-WATER-001: Invalid bounded water surface profile.");
        }
        float steepness = 0;
        foreach (var wave in Waves)
        {
            if (wave is null)
            {
                throw new InvalidDataException("AUR-WATER-001: A wind wave cannot be null.");
            }
            float[] parameters = [wave.Direction.X, wave.Direction.Y, wave.Amplitude,
                wave.Wavelength, wave.Speed, wave.Steepness];
            if (parameters.Any(value => !float.IsFinite(value)) || !float.IsFinite(wave.Direction.LengthSquared())
                || wave.Direction.LengthSquared() < .0001f
                || wave.Amplitude is < 0 or > 2 || wave.Wavelength is < .1f or > 100
                || wave.Speed is < 0 or > 50 || wave.Steepness is < 0 or > .95f)
            {
                throw new InvalidDataException("AUR-WATER-001: Invalid analytic wind wave.");
            }
            if (wave.Amplitude > 0)
            {
                steepness += wave.Steepness;
            }
        }
        if (steepness > .95f)
        {
            throw new InvalidDataException("AUR-WATER-001: Combined wave steepness must be <= .95 to prevent surface folding.");
        }
    }
}
