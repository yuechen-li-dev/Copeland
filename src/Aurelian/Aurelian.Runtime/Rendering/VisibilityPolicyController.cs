using Aurelian.Runtime.Dominatus.Inspection;
using Dominatus.Core;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;

namespace Aurelian.Runtime.Rendering;

[Flags]
public enum VisibilityPolicyMode { Baseline = 0, Lod = 1, Hzb = 2, Both = Lod | Hzb }

public sealed record VisibilityPolicyCost(VisibilityPolicyMode Mode, int Samples, double GpuMilliseconds);

/// <summary>Managed, host-ticked Dominatus utility decisions over measured complete GPU frames.</summary>
public sealed class VisibilityPolicyController
{
    private static readonly VisibilityPolicyMode[] Modes =
        [VisibilityPolicyMode.Baseline, VisibilityPolicyMode.Lod, VisibilityPolicyMode.Hzb, VisibilityPolicyMode.Both];
    private readonly AurelianAgentRuntime runtime;
    private readonly UtilityOption[] options;
    private readonly DecisionPolicy policy;
    private readonly Dictionary<long, Submission> pending = [];
    private readonly int[] counts = new int[4];
    private readonly double[] costs = new double[4];
    private readonly double[] sampledAt = new double[4];
    private VisibilityPolicyMode selected;
    private VisibilityPolicyMode available;
    private VisibilityPolicyMode submittedMode;
    private int consecutiveSubmissions;
    private long tick;
    private double time;
    private int epoch;
    private int probe = -1;
    private int probeSamples;

    public VisibilityPolicyController(float hysteresis = .05f, float minCommitSeconds = .75f, int traceCapacity = 256)
    {
        if (!float.IsFinite(hysteresis) || hysteresis < 0 || hysteresis > .15f
            || !float.IsFinite(minCommitSeconds) || minCommitSeconds < 0 || minCommitSeconds > 60)
            throw new ArgumentOutOfRangeException(nameof(hysteresis));
        policy = new(hysteresis, minCommitSeconds);
        runtime = new(traceCapacity);
        options = Modes.Select(mode => new UtilityOption(mode.ToString(), State(mode), new((_, _) => Score(mode)))).ToArray();
        var graph = new HfsmGraph { Root = State(VisibilityPolicyMode.Baseline) };
        foreach (var mode in Modes)
        {
            var captured = mode;
            graph.Add(State(mode), _ => Run(captured));
        }
        runtime.Add("rendering.visibility", new(graph, new HfsmOptions { KeepRootFrame = false }));
    }

    public DominatusInspector Inspector => runtime.Inspector;
    public VisibilityPolicyMode Current => selected & available;
    public IReadOnlyList<VisibilityPolicyCost> Costs => Modes.Select(mode =>
        new VisibilityPolicyCost(mode, counts[(int)mode], costs[(int)mode])).ToArray();

    public VisibilityPolicyMode Tick(TimeSpan elapsed, bool hzbAvailable, bool lodAvailable)
    {
        if (elapsed < TimeSpan.Zero || elapsed.TotalSeconds > 60) throw new ArgumentOutOfRangeException(nameof(elapsed));
        var nextAvailable = VisibilityPolicyMode.Baseline;
        if (hzbAvailable) nextAvailable |= VisibilityPolicyMode.Hzb;
        if (lodAvailable) nextAvailable |= VisibilityPolicyMode.Lod;
        if (available != nextAvailable) InvalidateMeasurements();
        available = nextAvailable;
        time += elapsed.TotalSeconds;
        tick++;
        if (probe < 0 && Modes.Where(Allowed).All(mode => counts[(int)mode] >= 8))
        {
            var oldest = Modes.Where(Allowed).OrderBy(mode => sampledAt[(int)mode]).First();
            if (time - sampledAt[(int)oldest] >= 15)
            {
                probe = (int)oldest;
                probeSamples = 0;
            }
        }
        runtime.Tick(elapsed);
        return Current;
    }

    public void InvalidateMeasurements()
    {
        Array.Clear(counts);
        Array.Clear(costs);
        pending.Clear();
        consecutiveSubmissions = 0;
        probe = -1;
        epoch++;
    }

    public void RecordSubmission(long sequence, VisibilityPolicyMode actualMode)
    {
        if ((actualMode & ~VisibilityPolicyMode.Both) != 0) throw new ArgumentOutOfRangeException(nameof(actualMode));
        if (actualMode != submittedMode) consecutiveSubmissions = 0;
        submittedMode = actualMode;
        pending[sequence] = new(actualMode, epoch, ++consecutiveSubmissions > 8);
        if (pending.Count > 64) pending.Remove(pending.Keys.Min());
    }

    public bool Complete(long sequence, double gpuMilliseconds)
    {
        if (!pending.Remove(sequence, out var submission) || submission.Epoch != epoch || !submission.Settled
            || !double.IsFinite(gpuMilliseconds) || gpuMilliseconds <= 0) return false;
        int index = (int)submission.Mode;
        costs[index] = counts[index] == 0 ? gpuMilliseconds : costs[index] * .875 + gpuMilliseconds * .125;
        counts[index]++;
        sampledAt[index] = time;
        if (probe == index && ++probeSamples >= 8) probe = -1;
        return true;
    }

    private bool Allowed(VisibilityPolicyMode mode) => (mode & available) == mode;

    private float Score(VisibilityPolicyMode mode)
    {
        if (!Allowed(mode)) return 0;
        int index = (int)mode;
        if (counts[index] < 8 || probe == index) return 1;
        double best = Modes.Where(candidate => Allowed(candidate) && counts[(int)candidate] > 0)
            .Min(candidate => costs[(int)candidate]);
        return (float)(.8 * best / costs[index]);
    }

    private IEnumerator<AiStep> Run(VisibilityPolicyMode mode)
    {
        while (true)
        {
            selected = mode;
            yield return new Decide("rendering.visibility.mode", options, policy);
            long observed = tick;
            yield return new WaitUntil(_ => observed != tick);
        }
    }

    private static StateId State(VisibilityPolicyMode mode) => StateId.Of("rendering.visibility." + mode);
    private sealed record Submission(VisibilityPolicyMode Mode, int Epoch, bool Settled);
}
