using Aurelian.Rendering.Contracts.Models;
using Aurelian.Runtime.Dominatus.Inspection;
using Dominatus.Core;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;

namespace Aurelian.Runtime.Rendering;

/// <summary>Dominatus trades raster quality against completed GPU frame time.</summary>
public sealed class ResolutionPolicyController
{
    private const int MinimumSamples = 8;
    private const double UpgradeHeadroom = .88;
    private static readonly RenderScale3D[] Scales = [RenderScale3D.Native, RenderScale3D.TwoThirds, RenderScale3D.Half];
    private static readonly double[] PixelAreas = [1, 4d / 9, .25];
    private readonly AurelianAgentRuntime runtime;
    private readonly UtilityOption[] options;
    private readonly DecisionPolicy policy = new(Hysteresis: .045f, MinCommitSeconds: .40f);
    private readonly double[] upgradeLimits = new double[Scales.Length];
    private long tick;
    private int samples;
    private int blockedUpgrade = -1;
    private double previousGpuMilliseconds;
    private double olderGpuMilliseconds;

    public ResolutionPolicyController(double budgetMilliseconds, int traceCapacity = 256)
    {
        if (!double.IsFinite(budgetMilliseconds) || budgetMilliseconds is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds), "GPU frame budget must be between 1 and 100 ms.");
        }
        BudgetMilliseconds = budgetMilliseconds;
        runtime = new(traceCapacity);
        options = Scales.Select(scale => new UtilityOption(scale.ToString(), State(scale), new((_, _) => Score(scale)))).ToArray();
        var graph = new HfsmGraph { Root = State(RenderScale3D.Native) };
        foreach (RenderScale3D scale in Scales)
        {
            var captured = scale;
            graph.Add(State(captured), _ => Run(captured));
        }
        runtime.Add("rendering.resolution", new(graph, new HfsmOptions { KeepRootFrame = false }));
    }

    public double BudgetMilliseconds { get; }
    public double SmoothedGpuMilliseconds { get; private set; }
    public long AcceptedSamples { get; private set; }
    public RenderScale3D Current { get; private set; }
    public DominatusInspector Inspector => runtime.Inspector;

    public RenderScale3D Tick(TimeSpan elapsed)
    {
        tick++;
        runtime.Tick(elapsed);
        return Current;
    }

    public void Observe(double gpuMilliseconds)
    {
        if (!double.IsFinite(gpuMilliseconds) || gpuMilliseconds <= 0)
        {
            return;
        }
        double sustained = samples < 2 ? gpuMilliseconds : Math.Clamp(olderGpuMilliseconds,
            Math.Min(previousGpuMilliseconds, gpuMilliseconds), Math.Max(previousGpuMilliseconds, gpuMilliseconds));
        double average = samples == 0 ? sustained : SmoothedGpuMilliseconds * .8 + sustained * .2;
        // The median of the last three timings responds to sustained load without following isolated spikes.
        SmoothedGpuMilliseconds = Math.Max(average, sustained);
        olderGpuMilliseconds = previousGpuMilliseconds;
        previousGpuMilliseconds = gpuMilliseconds;
        AcceptedSamples++;
        samples++;
        if (samples == MinimumSamples && blockedUpgrade >= 0)
        {
            // A higher scale just exceeded budget. Require a lighter workload before retrying it.
            upgradeLimits[blockedUpgrade] = Predict((RenderScale3D)blockedUpgrade) * UpgradeHeadroom;
            blockedUpgrade = -1;
        }
    }

    public void InvalidateMeasurements()
    {
        ClearSamples();
        Array.Clear(upgradeLimits);
        blockedUpgrade = -1;
    }

    private void ClearSamples()
    {
        samples = 0;
        SmoothedGpuMilliseconds = 0;
        previousGpuMilliseconds = 0;
        olderGpuMilliseconds = 0;
    }

    private double Predict(RenderScale3D scale)
    {
        double areaRatio = PixelAreas[(int)scale] / PixelAreas[(int)Current];
        // Some work (e.g. shadows and probe updates) survives a raster size change.
        return SmoothedGpuMilliseconds * (.25 + .75 * areaRatio);
    }

    private float Score(RenderScale3D scale)
    {
        if (samples < MinimumSamples)
        {
            return scale == Current ? 1 : 0;
        }
        double predicted = Predict(scale);
        bool upgrade = scale < Current;
        double retryLimit = upgradeLimits[(int)scale];
        if (upgrade && retryLimit > 0 && predicted >= retryLimit)
        {
            return 0;
        }
        // If even half scale is over budget, preserve the utility difference between slower and faster choices.
        double achievableBudget = Math.Max(BudgetMilliseconds, Predict(RenderScale3D.Half));
        double limit = achievableBudget * (upgrade ? UpgradeHeadroom : 1);
        double pressure = Math.Max(0, predicted / limit - 1);
        double quality = .3 - .1 * (int)scale;
        return (float)((.7 + quality) / (1 + pressure * 8));
    }

    private IEnumerator<AiStep> Run(RenderScale3D scale)
    {
        while (true)
        {
            if (scale != Current)
            {
                if (scale > Current && SmoothedGpuMilliseconds > BudgetMilliseconds)
                {
                    blockedUpgrade = (int)Current;
                }
                Current = scale;
                ClearSamples();
            }
            yield return new Decide("rendering.resolution.scale", options, policy);
            long observed = tick;
            yield return new WaitUntil(_ => observed != tick);
        }
    }

    private static StateId State(RenderScale3D scale) => StateId.Of("rendering.resolution." + scale);
}
