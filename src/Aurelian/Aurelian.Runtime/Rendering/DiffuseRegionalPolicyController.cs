using System.Collections.Immutable;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Runtime.Dominatus.Inspection;
using Dominatus.Core;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;

namespace Aurelian.Runtime.Rendering;

public sealed record DiffuseRegionalPolicySettings
{
    public float Hysteresis { get; init; } = .08f;
    public float MinCommitSeconds { get; init; } = .2f;
    public double AuditSeconds { get; init; } = .75;
    public double CompiledTolerance { get; init; } = .025;
    public double TemporalThreshold { get; init; } = .15;
    public int ResidualDirections { get; init; } = 16;
    public int MaximumRayUpdates { get; init; } = 8192;
}

public sealed record DiffuseRegionPolicyState(int Region, int Epoch, DiffuseRegionStrategy3D Selected,
    DiffuseRegionStrategy3D Applied, int RaysPerProbe, bool CompleteCoverage,
    double BaselineChange, double TemporalChange);

/// <summary>Dominatus owns strategy selection; a hard work cap admits whole refreshes fairly.
/// Delayed GPU change proxies never masquerade as reference-image error.</summary>
public sealed class DiffuseRegionalPolicyController
{
    private readonly Guid policyIdentity = Guid.NewGuid();
    private readonly AurelianAgentRuntime runtime;
    private readonly DiffuseProbeGrid grid;
    private readonly DiffuseRegionPartition3D partition;
    private readonly DiffuseRegionalPolicySettings settings;
    private readonly Region[] regions;
    private readonly DecisionPolicy decisionPolicy;
    private long tick;
    private long lastObservation = -1;
    private double time;

    public DiffuseRegionalPolicyController(DiffuseProbeGrid grid, DiffuseRegionPartition3D partition,
        DiffuseRegionalPolicySettings? settings = null, int traceCapacity = 2048)
    {
        this.grid = grid;
        this.partition = partition;
        this.settings = settings ?? new();
        int[] counts = partition.ProbeCounts(grid);
        if (!float.IsFinite(this.settings.Hysteresis) || this.settings.Hysteresis < 0 || this.settings.Hysteresis > .5
            || !float.IsFinite(this.settings.MinCommitSeconds) || this.settings.MinCommitSeconds < 0 || this.settings.MinCommitSeconds > 60
            || !double.IsFinite(this.settings.AuditSeconds) || this.settings.AuditSeconds <= 0
            || !double.IsFinite(this.settings.CompiledTolerance) || this.settings.CompiledTolerance <= 0
            || !double.IsFinite(this.settings.TemporalThreshold) || this.settings.TemporalThreshold <= 0
            || this.settings.ResidualDirections < 1 || this.settings.ResidualDirections > grid.RaysPerProbe
            || this.settings.MaximumRayUpdates < counts.Max() * grid.RaysPerProbe)
            throw new ArgumentException("AUR-GI-POLICY-001: Invalid stability thresholds or budget too small for one complete region refresh.");
        decisionPolicy = new(this.settings.Hysteresis, this.settings.MinCommitSeconds);
        runtime = new(traceCapacity);
        regions = counts.Select(count => new Region(count)).ToArray();
        for (int index = 0; index < regions.Length; index++)
        {
            int region = index;
            var graph = new HfsmGraph { Root = State(region, DiffuseRegionStrategy3D.Runtime) };
            foreach (var strategy in Enum.GetValues<DiffuseRegionStrategy3D>())
            {
                var captured = strategy;
                graph.Add(State(region, captured), _ => Run(region, captured));
            }
            regions[index].Options = Enum.GetValues<DiffuseRegionStrategy3D>().Select(strategy =>
                new UtilityOption(strategy.ToString(), State(region, strategy), new((_, _) => Score(region, strategy)))).ToArray();
            runtime.Add("rendering.gi.region." + index, new(graph, new HfsmOptions { KeepRootFrame = false }));
        }
    }

    public DominatusInspector Inspector => runtime.Inspector;
    public int LastScheduledRayUpdates { get; private set; }
    public int RejectedObservations { get; private set; }
    public double ObservedFrameGpuMilliseconds { get; private set; }
    public ImmutableArray<DiffuseRegionPolicyState> States { get; private set; } = [];

    public void ResetObservationStream()
    {
        lastObservation = -1;
        Invalidate();
    }

    public void Invalidate(IEnumerable<int>? affectedRegions = null)
    {
        int[] affected = affectedRegions?.Distinct().ToArray() ?? Enumerable.Range(0, regions.Length).ToArray();
        if (affected.Any(index => index < 0 || index >= regions.Length)) throw new ArgumentOutOfRangeException(nameof(affectedRegions));
        foreach (int index in affected)
        {
            var region = regions[index];
            region.Epoch = checked(region.Epoch + 1);
            region.Complete = false;
            region.Coverage = 0;
            region.BaselineError = 0;
            region.Change = 1;
            region.LastComplete = double.NegativeInfinity;
        }
    }

    public bool Observe(DiffuseRegionalObservation3D observation)
    {
        if (observation.PolicyIdentity != policyIdentity || observation.Sequence < 0 || observation.Sequence <= lastObservation || observation.Grid != grid || observation.Partition != partition)
        {
            RejectedObservations++;
            return false;
        }
        if (observation.Regions.Length != regions.Length
            || observation.Regions.Select(value => value.Region).Distinct().Count() != regions.Length)
            throw new ArgumentException("AUR-GI-POLICY-002: One unique measurement is required per region.");
        foreach (var measured in observation.Regions)
        {
            if (measured.Region < 0 || measured.Region >= regions.Length || !Enum.IsDefined(measured.Strategy)
                || measured.Epoch < 0 || measured.Probes != regions[measured.Region].Probes
                || !double.IsFinite(measured.BaselineChange) || !double.IsFinite(measured.TemporalChange)
                || measured.BaselineChange < 0 || measured.TemporalChange < 0
                || measured.UpdatedDirectionsPerProbe < 0 || measured.UpdatedDirectionsPerProbe > grid.RaysPerProbe
                || measured.Strategy == DiffuseRegionStrategy3D.Compiled && measured.UpdatedDirectionsPerProbe != 0
                || measured.Strategy == DiffuseRegionStrategy3D.Runtime && measured.UpdatedDirectionsPerProbe != 0
                    && measured.UpdatedDirectionsPerProbe != grid.RaysPerProbe)
                throw new ArgumentException("AUR-GI-POLICY-002: Invalid GPU change observation.");
        }
        lastObservation = observation.Sequence;
        bool accepted = false;
        foreach (var measured in observation.Regions)
        {
            if (measured.Region < 0 || measured.Region >= regions.Length) throw new ArgumentException("Invalid observed region.");
            var region = regions[measured.Region];
            if (measured.Epoch != region.Epoch || measured.Strategy == DiffuseRegionStrategy3D.Compiled || measured.UpdatedDirectionsPerProbe == 0)
            {
                if (measured.Epoch != region.Epoch) RejectedObservations++;
                continue;
            }
            region.BaselineError = measured.BaselineChange;
            region.Change = measured.TemporalChange;
            region.Coverage += measured.UpdatedDirectionsPerProbe;
            if (region.Coverage >= grid.RaysPerProbe)
            {
                region.Complete = true;
                region.Coverage = 0;
                region.LastComplete = time;
            }
            accepted = true;
        }
        return accepted;
    }

    public void ObserveFrameCost(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds <= 0) return;
        ObservedFrameGpuMilliseconds = ObservedFrameGpuMilliseconds == 0 ? milliseconds
            : ObservedFrameGpuMilliseconds * .875 + milliseconds * .125;
    }

    public DiffuseRegionalPlan3D Tick(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero || elapsed.TotalSeconds > 60) throw new ArgumentOutOfRangeException(nameof(elapsed));
        time += elapsed.TotalSeconds;
        tick++;
        runtime.Tick(elapsed);
        int remaining = settings.MaximumRayUpdates;
        var commands = regions.Select(region => new DiffuseRegionCommand3D(
            region.Complete ? region.Selected : DiffuseRegionStrategy3D.Runtime, 0, region.Epoch)).ToArray();
        // Incomplete coverage is a correctness guard, not permission to hold a stale compiled result.
        // MinCommit still governs the Dominatus state; the applied override is explicitly reported.
        foreach (int index in Enumerable.Range(0, regions.Length)
            .OrderBy(index => regions[index].Complete ? 1 : 0)
            .ThenBy(index => regions[index].LastScheduled))
        {
            var region = regions[index];
            var strategy = commands[index].Strategy;
            if (strategy == DiffuseRegionStrategy3D.Compiled) continue;
            int directions = strategy == DiffuseRegionStrategy3D.Runtime ? grid.RaysPerProbe : settings.ResidualDirections;
            int cost = region.Probes * directions;
            if (cost > remaining) continue;
            commands[index] = commands[index] with { RaysPerProbe = directions };
            region.LastScheduled = tick;
            remaining -= cost;
        }
        LastScheduledRayUpdates = settings.MaximumRayUpdates - remaining;
        States = commands.Select((command, index) => new DiffuseRegionPolicyState(index, command.Epoch,
            regions[index].Selected, command.Strategy, command.RaysPerProbe, regions[index].Complete,
            regions[index].BaselineError, regions[index].Change)).ToImmutableArray();
        return new(partition, commands, policyIdentity);
    }

    private float Score(int index, DiffuseRegionStrategy3D strategy)
    {
        var region = regions[index];
        if (!region.Complete)
            return strategy switch { DiffuseRegionStrategy3D.Runtime => 1, DiffuseRegionStrategy3D.Residual => .3f, _ => 0 };
        if (time - region.LastComplete >= settings.AuditSeconds)
            return strategy switch { DiffuseRegionStrategy3D.Residual => .95f, DiffuseRegionStrategy3D.Runtime => .4f, _ => 0 };
        float activity = (float)Math.Clamp(region.Change / settings.TemporalThreshold, 0, 1);
        return strategy switch
        {
            DiffuseRegionStrategy3D.Compiled => region.BaselineError <= settings.CompiledTolerance ? .95f - activity * .8f : 0,
            DiffuseRegionStrategy3D.Residual => .8f - activity * .25f,
            DiffuseRegionStrategy3D.Runtime => .35f + activity * .65f,
            _ => 0,
        };
    }

    private IEnumerator<AiStep> Run(int index, DiffuseRegionStrategy3D strategy)
    {
        while (true)
        {
            regions[index].Selected = strategy;
            yield return new Decide("rendering.gi.region." + index, regions[index].Options, decisionPolicy);
            long observed = tick;
            yield return new WaitUntil(_ => observed != tick);
        }
    }

    private static StateId State(int region, DiffuseRegionStrategy3D strategy) => StateId.Of("rendering.gi.region." + region + "." + strategy);

    private sealed class Region(int probes)
    {
        public int Probes { get; } = probes;
        public UtilityOption[] Options = [];
        public int Epoch;
        public int Coverage;
        public bool Complete;
        public double BaselineError;
        public double Change = 1;
        public double LastComplete = double.NegativeInfinity;
        public long LastScheduled;
        public DiffuseRegionStrategy3D Selected = DiffuseRegionStrategy3D.Runtime;
    }
}
