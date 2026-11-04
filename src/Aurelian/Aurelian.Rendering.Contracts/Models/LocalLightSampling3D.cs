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
    public uint Seed { get; init; } = 719;

    public void Validate(int pixels)
    {
        if (!Enum.IsDefined(Mode) || ExactLights is < 0 or > 3 || Candidates is < 1 or > 32
            || HistoryLimit is < 1 or > 1024 || Mode != LocalLightSamplingMode3D.Exact
            && (FrameRayBudget < pixels || (long)FrameRayBudget > (long)pixels * 4))
        {
            throw new ArgumentOutOfRangeException(nameof(LocalLightSampling3D),
                "AUR-LIGHT-SAMPLING-001: Sampled lighting requires 1..4 rays per allocated pixel, 0..3 exact lights and 1..32 candidates.");
        }
    }
}
