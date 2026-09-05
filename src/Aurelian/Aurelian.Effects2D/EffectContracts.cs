using System.Numerics;

namespace Aurelian.Effects2D;

public readonly record struct VisualEffectId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct VisualEffectEventId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct EmitterInstanceId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct EffectMaterialId(string Value)
{
    public override string ToString() => Value;
}

public enum EffectCoordinateSpace
{
    World,
    Screen,
}

public enum EffectEmitterKind
{
    Burst,
    Ambient,
    Trail,
    ScreenFlash,
}

public enum EffectBlendMode
{
    StraightAlpha,
    Additive,
}

public enum EffectPainterLayer
{
    BehindActors = 150,
    FrontOfActors = 250,
    ScreenFlash = 500,
}

public sealed record VisualEffectEvent(
    VisualEffectId EffectId,
    VisualEffectEventId StableEventId,
    EffectCoordinateSpace Space,
    Vector2? Position = null,
    Vector2? Direction = null,
    float Scale = 1,
    float Intensity = 1,
    string? SourceId = null,
    string? TargetId = null,
    ulong Seed = 0,
    string? SemanticVariant = null);

public sealed record EffectDefinition(
    VisualEffectId Id,
    EffectEmitterKind EmitterKind,
    TimeSpan Lifetime,
    int SpawnCount,
    float MinimumSize,
    float MaximumSize,
    float MinimumSpeed,
    float MaximumSpeed,
    float SpawnsPerSecond,
    EffectPainterLayer PainterLayer,
    EffectBlendMode BlendMode,
    int Priority,
    EffectMaterialId MaterialId,
    bool ShaderQuad = false)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id.Value))
        {
            throw new ArgumentException("Effect ID cannot be empty.", nameof(Id));
        }
        if (Lifetime <= TimeSpan.Zero || !double.IsFinite(Lifetime.TotalSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(Lifetime), "Effect lifetime must be positive and finite.");
        }
        if (SpawnCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SpawnCount), "Effect spawn count cannot be negative.");
        }
        if (!float.IsFinite(MinimumSize) || !float.IsFinite(MaximumSize)
            || MinimumSize <= 0 || MaximumSize < MinimumSize)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumSize), "Effect sizes must be finite, positive, and ordered.");
        }
        if (!float.IsFinite(MinimumSpeed) || !float.IsFinite(MaximumSpeed)
            || MinimumSpeed < 0 || MaximumSpeed < MinimumSpeed)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumSpeed), "Effect speeds must be finite, non-negative, and ordered.");
        }
        if (!float.IsFinite(SpawnsPerSecond) || SpawnsPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SpawnsPerSecond), "Spawn rate must be finite and non-negative.");
        }
        if (!Enum.IsDefined(EmitterKind) || !Enum.IsDefined(PainterLayer) || !Enum.IsDefined(BlendMode))
        {
            throw new ArgumentException("Effect definition contains an unsupported enum value.");
        }
        if (string.IsNullOrWhiteSpace(MaterialId.Value))
        {
            throw new ArgumentException("Effect material ID cannot be empty.", nameof(MaterialId));
        }
    }
}

public static class VisualEffectIds
{
    public static VisualEffectId SwordHit { get; } = new("aurelian.effect.sword-hit");
    public static VisualEffectId HarvestPuff { get; } = new("aurelian.effect.harvest-puff");
    public static VisualEffectId PickupSparkle { get; } = new("aurelian.effect.pickup-sparkle");
    public static VisualEffectId FootstepDust { get; } = new("aurelian.effect.footstep-dust");
    public static VisualEffectId AmbientMotes { get; } = new("aurelian.effect.ambient-motes");
    public static VisualEffectId ScreenFlash { get; } = new("aurelian.effect.screen-flash");
}

public static class EffectMaterialIds
{
    public static EffectMaterialId AnalyticParticle { get; } = new("aurelian.material.analytic-particle");
    public static EffectMaterialId SoftShockwave { get; } = new("aurelian.material.soft-shockwave");
    public static EffectMaterialId ScreenFlash { get; } = new("aurelian.material.screen-flash");
}
