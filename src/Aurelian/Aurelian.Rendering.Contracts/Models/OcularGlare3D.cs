namespace Aurelian.Rendering.Contracts.Models;

/// <summary>Photopic forward scatter. Strength blends the optical result with the direct image.</summary>
public sealed record OcularGlare3D
{
    public float Strength { get; init; } = 1;
    public float OrthographicFieldOfViewDegrees { get; init; } = 50;

    internal void Validate()
    {
        if (!float.IsFinite(Strength) || Strength is < 0 or > 1
            || !float.IsFinite(OrthographicFieldOfViewDegrees) || OrthographicFieldOfViewDegrees is < 5 or > 150)
        {
            throw new ArgumentOutOfRangeException(nameof(OcularGlare3D), "Glare strength must be in [0,1] and the orthographic angular field in [5,150] degrees.");
        }
    }
}
