using System.Collections.Immutable;

namespace Aurelian.Rendering.Contracts.Lighting;

public enum DiffuseRegionStrategy3D { Compiled, Residual, Runtime }

/// <summary>Disjoint integer partitions of the prepared probe grid, with one owner per probe.</summary>
public sealed record DiffuseRegionPartition3D(int X = 2, int Y = 1, int Z = 2)
{
    public int Count => checked(X * Y * Z);

    public void Validate(DiffuseProbeGrid grid)
    {
        grid.Validate();
        if (X < 1 || Y < 1 || Z < 1 || X > grid.X || Y > grid.Y || Z > grid.Z || Count > 32)
            throw new ArgumentException("AUR-GI-REGION-001: Regions must form at most 32 nonempty disjoint grid partitions.");
    }

    public int RegionForProbe(DiffuseProbeGrid grid, int probe)
    {
        if (probe < 0 || probe >= grid.Count) throw new ArgumentOutOfRangeException(nameof(probe));
        int x = probe % grid.X * X / grid.X;
        int y = probe / grid.X % grid.Y * Y / grid.Y;
        int z = probe / (grid.X * grid.Y) * Z / grid.Z;
        return x + y * X + z * X * Y;
    }

    public int[] ProbeCounts(DiffuseProbeGrid grid)
    {
        Validate(grid);
        int[] counts = new int[Count];
        for (int probe = 0; probe < grid.Count; probe++) counts[RegionForProbe(grid, probe)]++;
        return counts;
    }
}

/// <summary>Runtime refresh traces the full basis when admitted; zero explicitly defers work.</summary>
public sealed record DiffuseRegionCommand3D(DiffuseRegionStrategy3D Strategy, int RaysPerProbe, int Epoch);

public sealed class DiffuseRegionalPlan3D
{
    public Guid PolicyIdentity { get; }
    public DiffuseRegionPartition3D Partition { get; }
    public ImmutableArray<DiffuseRegionCommand3D> Regions { get; }

    public DiffuseRegionalPlan3D(DiffuseRegionPartition3D partition, IEnumerable<DiffuseRegionCommand3D> regions, Guid policyIdentity = default)
    {
        PolicyIdentity = policyIdentity;
        Partition = partition;
        Regions = regions.ToImmutableArray();
    }

    public void Validate(DiffuseProbeGrid grid)
    {
        Partition.Validate(grid);
        if (Regions.Length != Partition.Count) throw new ArgumentException("AUR-GI-REGION-002: One command is required per region.");
        foreach (var command in Regions)
        {
            if (command is null || !Enum.IsDefined(command.Strategy) || command.Epoch < 0 || command.RaysPerProbe < 0
                || command.RaysPerProbe > grid.RaysPerProbe
                || command.Strategy == DiffuseRegionStrategy3D.Compiled && command.RaysPerProbe != 0
                || command.Strategy == DiffuseRegionStrategy3D.Runtime && command.RaysPerProbe != 0 && command.RaysPerProbe != grid.RaysPerProbe)
                throw new ArgumentException("AUR-GI-REGION-003: Invalid region strategy, epoch or bounded directional budget.");
        }
    }
}

/// <summary>GPU change proxies, not reference-image error or an unbiased variance estimate.</summary>
public sealed record DiffuseRegionMeasurement3D(int Region, int Epoch, DiffuseRegionStrategy3D Strategy,
    int Probes, int UpdatedDirectionsPerProbe, double BaselineChange, double TemporalChange);

public sealed record DiffuseRegionalObservation3D(long Sequence, DiffuseProbeGrid Grid,
    DiffuseRegionPartition3D Partition, ImmutableArray<DiffuseRegionMeasurement3D> Regions, Guid PolicyIdentity = default);
