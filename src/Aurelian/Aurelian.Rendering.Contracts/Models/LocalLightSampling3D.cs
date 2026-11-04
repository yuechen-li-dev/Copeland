namespace Aurelian.Rendering.Contracts.Models;

public enum LocalLightSamplingMode3D
{
    Exact,
    Uniform,
    Importance,
    Hybrid,
    Reservoir,
    TemporalReservoir,
    UniformEligible,
    ReprojectedReservoir,
    SpatiotemporalReservoir,
    SpatialReservoir,
}

/// <summary>Research controls. FrameRayBudget is an upper bound over the entire image,
/// including background pixels. The exact path remains the default.</summary>
public sealed record LocalLightSampling3D
{
    public LocalLightSamplingMode3D Mode { get; init; }
    public int FrameRayBudget { get; init; }
    public int ExactLights { get; init; } = 1;
    public int Candidates { get; init; } = 16;
    public int HistoryLimit { get; init; } = 256;
    public bool SpatialTargetMis { get; init; } = true;
    public bool ImportanceProposals { get; init; }
    public int SpatialNeighbors { get; init; } = 4;
    public float SpatialRadius { get; init; } = 8;
    public float ReusePlaneTolerance { get; init; } = .01f;
    public float ReuseNormalCosine { get; init; } = .9f;
    public bool ReconstructTail { get; init; }
    public int TailHistoryLimit { get; init; } = 8;
    public int TailSpatialPasses { get; init; } = 1;
    public uint Seed { get; init; } = 719;

    public void Validate(int pixels)
    {
        if (TailHistoryLimit is < 1 or > 16 || TailSpatialPasses is < 0 or > 3 || !Enum.IsDefined(Mode) || ExactLights is < 0 or > 3 || Candidates is < 1 or > 32
            || HistoryLimit is < 1 or > 1024 || SpatialNeighbors is < 0 or > 8
            || !float.IsFinite(SpatialRadius) || SpatialRadius < 1 || SpatialRadius > 32
            || !float.IsFinite(ReusePlaneTolerance) || ReusePlaneTolerance < .0001f || ReusePlaneTolerance > .1f
            || !float.IsFinite(ReuseNormalCosine) || ReuseNormalCosine < .5f || ReuseNormalCosine > 1 || Mode != LocalLightSamplingMode3D.Exact
            && (FrameRayBudget < pixels || (long)FrameRayBudget > (long)pixels * 4))
        {
            throw new ArgumentOutOfRangeException(nameof(LocalLightSampling3D),
                "AUR-LIGHT-SAMPLING-001: Sampled lighting requires 1..4 rays per allocated pixel, 0..3 exact lights, 1..32 candidates, 1..1024 history mass, 0..8 neighbors, 1..32 pixel radius, .0001.. .1 plane tolerance and .5..1 normal cosine; floating controls must be finite.");
        }
    }
}
