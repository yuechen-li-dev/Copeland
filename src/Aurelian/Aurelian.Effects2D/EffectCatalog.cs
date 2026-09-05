namespace Aurelian.Effects2D;

public sealed class EffectCatalog
{
    private readonly IReadOnlyDictionary<VisualEffectId, EffectDefinition> definitions;

    public EffectCatalog(IEnumerable<EffectDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        EffectDefinition[] materialized = definitions.ToArray();
        foreach (EffectDefinition definition in materialized)
        {
            definition.Validate();
            if (definition.MaterialId != EffectMaterialIds.AnalyticParticle
                && definition.MaterialId != EffectMaterialIds.SoftShockwave
                && definition.MaterialId != EffectMaterialIds.ScreenFlash)
            {
                throw new ArgumentException(
                    $"Unknown effect material '{definition.MaterialId}'.",
                    nameof(definitions));
            }
        }
        if (materialized.Select(item => item.Id).Distinct().Count() != materialized.Length)
        {
            throw new ArgumentException("Effect IDs must be unique.", nameof(definitions));
        }
        this.definitions = materialized.ToDictionary(item => item.Id);
    }

    public IReadOnlyCollection<EffectDefinition> Definitions => definitions.Values.ToArray();

    public bool TryGet(VisualEffectId id, out EffectDefinition? definition)
        => definitions.TryGetValue(id, out definition);

    public EffectDefinition Get(VisualEffectId id)
        => definitions.TryGetValue(id, out EffectDefinition? definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown visual effect '{id}'.");

    public static EffectCatalog CreateSmallGameDefaults() => new([
        new EffectDefinition(
            VisualEffectIds.SwordHit,
            EffectEmitterKind.Burst,
            TimeSpan.FromSeconds(0.42),
            14,
            24,
            64,
            320,
            720,
            0,
            EffectPainterLayer.FrontOfActors,
            EffectBlendMode.StraightAlpha,
            100,
            EffectMaterialIds.SoftShockwave,
            ShaderQuad: true),
        new EffectDefinition(
            VisualEffectIds.HarvestPuff,
            EffectEmitterKind.Burst,
            TimeSpan.FromSeconds(0.65),
            10,
            36,
            88,
            160,
            420,
            0,
            EffectPainterLayer.FrontOfActors,
            EffectBlendMode.StraightAlpha,
            60,
            EffectMaterialIds.AnalyticParticle),
        new EffectDefinition(
            VisualEffectIds.PickupSparkle,
            EffectEmitterKind.Burst,
            TimeSpan.FromSeconds(0.75),
            8,
            18,
            40,
            180,
            520,
            0,
            EffectPainterLayer.FrontOfActors,
            EffectBlendMode.StraightAlpha,
            70,
            EffectMaterialIds.AnalyticParticle),
        new EffectDefinition(
            VisualEffectIds.FootstepDust,
            EffectEmitterKind.Trail,
            TimeSpan.FromSeconds(0.5),
            1,
            24,
            56,
            40,
            100,
            12,
            EffectPainterLayer.BehindActors,
            EffectBlendMode.StraightAlpha,
            20,
            EffectMaterialIds.AnalyticParticle),
        new EffectDefinition(
            VisualEffectIds.AmbientMotes,
            EffectEmitterKind.Ambient,
            TimeSpan.FromSeconds(30),
            0,
            18,
            32,
            20,
            60,
            8,
            EffectPainterLayer.BehindActors,
            EffectBlendMode.StraightAlpha,
            10,
            EffectMaterialIds.AnalyticParticle),
        new EffectDefinition(
            VisualEffectIds.ScreenFlash,
            EffectEmitterKind.ScreenFlash,
            TimeSpan.FromSeconds(0.18),
            0,
            1,
            1,
            0,
            0,
            0,
            EffectPainterLayer.ScreenFlash,
            EffectBlendMode.StraightAlpha,
            90,
            EffectMaterialIds.ScreenFlash),
    ]);
}
