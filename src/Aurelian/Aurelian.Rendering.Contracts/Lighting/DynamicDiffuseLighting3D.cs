namespace Aurelian.Rendering.Contracts.Lighting;

public enum DynamicDiffuseMode3D
{
    Off,
    Runtime,
    CompiledResidual,
    CompiledOnly,
}

/// <summary>Bounded world-space single-bounce irradiance-field research controls.</summary>
public sealed record DynamicDiffuseLighting3D
{
    public DynamicDiffuseMode3D Mode { get; init; }
    public DiffuseProbeGrid? Grid { get; init; }
    public int RaysPerProbePerFrame { get; init; } = 16;
    public bool ConnectivityVisibility { get; init; } = true;
    public float Intensity { get; init; } = 1;

    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || RaysPerProbePerFrame is < 1 or > 128
            || !float.IsFinite(Intensity) || Intensity < 0 || Intensity > 10)
            throw new ArgumentOutOfRangeException(nameof(DynamicDiffuseLighting3D));
        if (Mode == DynamicDiffuseMode3D.Off) return;
        if (Grid is null) throw new ArgumentException("AUR-GI-002: Dynamic diffuse lighting requires a declared world-space grid.");
        Grid.Validate();
        if (Grid.SurfaceResolution != 8)
            throw new ArgumentException("AUR-GI-002: Dynamic diffuse currently uses an eight by eight octahedral convolution tile.");
        if (RaysPerProbePerFrame > Grid.RaysPerProbe)
            throw new ArgumentException("AUR-GI-002: Per-frame samples exceed the compiled directional basis.");
    }
}

/// <summary>A GPU-acquired static control variate. The ray samples and convolved coefficients use the same finite directional basis.</summary>
public sealed record CompiledDiffuseProbeField3D(int Version, DiffuseProbeGrid Grid, float[] Data)
{
    public void Validate()
    {
        Grid.Validate();
        if (Version != 1 || Grid.SurfaceResolution != 8 || Data is null || Data.Length != Grid.Count * (Grid.RaysPerProbe + 64) * 4
            || Data.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("AUR-GI-003: Invalid compiled probe-field version, dimensions or nonfinite data.");
    }
}
