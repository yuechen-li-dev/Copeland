using System.Numerics;

namespace Aurelian.Rendering.Contracts.Models;

public enum ParticleEffectKind3D
{
    Fire,
    Smoke,
    Sparks,
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
            || !Range(Color.W, 0, 1) || !Range(SoftIntersectionDistance, .001f, 10))
        {
            throw new ArgumentException("AUR-VFX-001: Emitter identity, finite parameters and bounded lifetime are required.");
        }
    }

    public static ParticleEmitter3D Fire(string id, Vector3 position, uint seed = 1) => new(id, ParticleEffectKind3D.Fire)
    {
        Position = position,
        Seed = seed,
        Color = new(12, 2.4f, .25f, .65f),
        Rate = 140,
        Lifetime = 1.1f,
        Speed = 1.2f,
        Size = .22f,
        Radius = .18f,
    };

    public static ParticleEmitter3D Smoke(string id, Vector3 position, uint seed = 1) => new(id, ParticleEffectKind3D.Smoke)
    {
        Position = position,
        Seed = seed,
        Color = new(.16f, .18f, .21f, .25f),
        Rate = 35,
        Lifetime = 3.5f,
        Speed = .7f,
        Size = .35f,
        Radius = .18f,
    };

    public static ParticleEmitter3D Sparks(string id, Vector3 position, uint seed = 1) => new(id, ParticleEffectKind3D.Sparks)
    {
        Position = position,
        Seed = seed,
        Color = new(20, 7, .7f, .9f),
        Rate = 35,
        Lifetime = 1.5f,
        Speed = 3.5f,
        Size = .025f,
        Radius = .1f,
    };

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Range(float value, float low, float high) => float.IsFinite(value) && value >= low && value <= high;
}
