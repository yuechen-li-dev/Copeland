using System.Numerics;

namespace Aurelian.Rendering.Contracts.Models;

public enum ParticleEffectKind3D
{
    Fire,
    Smoke,
    Sparks,

    /// <summary>Light glowing flakes carried up and around by the plume, cooling as they go.</summary>
    Embers,
}

/// <summary>Immutable visual command. An owning agent supplies time, seed and activation;
/// particle motion and billboard generation run on the GPU.</summary>
public sealed record ParticleEmitter3D(string Id, ParticleEffectKind3D Kind)
{
    public Vector3 Position { get; init; }
    public Vector3 Direction { get; init; } = Vector3.UnitY;
    public uint Seed { get; init; } = 1;
    public float ElapsedSeconds { get; init; }
    public float EmitUntilSeconds { get; init; } = 3600;
    public float Rate { get; init; } = 100;
    public float Lifetime { get; init; } = 1.5f;
    public float Speed { get; init; } = 1;
    public float Radius { get; init; } = .2f;
    public float Size { get; init; } = .15f;
    public Vector4 Color { get; init; } = Vector4.One;
    public float SoftIntersectionDistance { get; init; } = .15f;

    /// <summary>Peak speed of the swirling curl-noise flow, in metres per second. Zero gives smooth trajectories.</summary>
    public float Turbulence { get; init; }

    /// <summary>Birth temperature in kelvin for emitting kinds (fire, sparks, embers). Colour follows blackbody radiation:
    /// about 1000 K is dull red, 1500 K orange and 2000 K yellow-white. <see cref="Color"/> tints and scales the result.</summary>
    public float Temperature { get; init; } = 1800;

    /// <summary>Smoke only: linear radiance of fire light reaching the underside of the plume at the emitter.</summary>
    public Vector3 Glow { get; init; }

    /// <summary>Smoke only: height above the emitter, in metres, over which <see cref="Glow"/> fades.</summary>
    public float GlowHeight { get; init; } = 1;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || !Enum.IsDefined(Kind)
            || !Finite(Position) || !Finite(Direction) || !float.IsFinite(Direction.LengthSquared())
            || !Range(Position.X, -100000, 100000) || !Range(Position.Y, -100000, 100000) || !Range(Position.Z, -100000, 100000)
            || Direction.LengthSquared() < .000001f
            || !Range(ElapsedSeconds, 0, 3600) || !Range(EmitUntilSeconds, 0, 3600)
            || !Range(Rate, .01f, 100000) || !Range(Lifetime, .01f, 30)
            || !Range(Speed, 0, 100) || !Range(Radius, 0, 100) || !Range(Size, .001f, 100)
            || !Range(Color.X, 0, 100) || !Range(Color.Y, 0, 100) || !Range(Color.Z, 0, 100)
            || !Range(Color.W, 0, 1) || !Range(SoftIntersectionDistance, .001f, 10)
            || !Range(Turbulence, 0, 50) || !Range(Temperature, 500, 6000)
            || !Finite(Glow) || !Range(Glow.X, 0, 100) || !Range(Glow.Y, 0, 100) || !Range(Glow.Z, 0, 100)
            || !Range(GlowHeight, .01f, 100))
        {
            throw new ArgumentException("AUR-VFX-001: Emitter identity, finite parameters and bounded lifetime are required.");
        }
    }

    /// <summary>A campfire-sized flame: about a metre tall at <paramref name="scale"/> 1.</summary>
    public static ParticleEmitter3D Fire(string id, Vector3 position, uint seed = 1, float scale = 1) => new(id, ParticleEffectKind3D.Fire)
    {
        Position = position,
        Seed = seed,
        Color = new(1.8f, 1.8f, 1.8f, .9f),
        Temperature = 1900,
        Rate = 95 * MathF.Sqrt(scale),
        Lifetime = .9f * MathF.Sqrt(scale),
        Speed = 1.3f * MathF.Sqrt(scale),
        Size = .28f * scale,
        Radius = .24f * scale,
        Turbulence = .9f * MathF.Sqrt(scale),
        SoftIntersectionDistance = .08f * scale + .02f,
    };

    /// <summary>A plume of grey smoke rising from <paramref name="position"/>.</summary>
    public static ParticleEmitter3D Smoke(string id, Vector3 position, uint seed = 1, float scale = 1) => new(id, ParticleEffectKind3D.Smoke)
    {
        Position = position,
        Seed = seed,
        Color = new(.36f, .355f, .35f, .8f),
        Rate = 16 * MathF.Sqrt(scale),
        Lifetime = 6.5f * MathF.Sqrt(scale),
        Speed = .75f * MathF.Sqrt(scale),
        Size = .24f * scale,
        Radius = .18f * scale,
        Turbulence = .45f * MathF.Sqrt(scale),
        GlowHeight = 1.2f * scale,
    };

    /// <summary>Hot sparks thrown out ballistically, streaking as they fly.</summary>
    public static ParticleEmitter3D Sparks(string id, Vector3 position, uint seed = 1, float scale = 1) => new(id, ParticleEffectKind3D.Sparks)
    {
        Position = position,
        Seed = seed,
        Color = new(1.5f, 1.5f, 1.5f, 1),
        Temperature = 2100,
        Rate = 35 * scale,
        Lifetime = 1.2f,
        Speed = 3.5f * MathF.Sqrt(scale),
        Size = .012f,
        Radius = .1f * scale,
        Turbulence = .3f,
    };

    /// <summary>Glowing embers that drift up and swirl in the plume above a fire.</summary>
    public static ParticleEmitter3D Embers(string id, Vector3 position, uint seed = 1, float scale = 1) => new(id, ParticleEffectKind3D.Embers)
    {
        Position = position,
        Seed = seed,
        Color = new(2.5f, 2.5f, 2.5f, 1),
        Temperature = 1750,
        Rate = 24 * scale,
        Lifetime = 2.6f * MathF.Sqrt(scale),
        Speed = 1.1f * MathF.Sqrt(scale),
        Size = .011f,
        Radius = .3f * scale,
        Turbulence = 1.1f * MathF.Sqrt(scale),
    };

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Range(float value, float low, float high) => float.IsFinite(value) && value >= low && value <= high;
}
