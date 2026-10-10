using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Runtime.Dominatus.Inspection;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;

namespace Aurelian.Runtime.Lighting;

public sealed record LightingCompilationStatus(string Id, string Phase, long Generation,
    string ContentKey, string Reason, LightingBakeTicket? Published);

/// <summary>
/// Host-ticked policy agents. Submission delegates return immediately with completion identities;
/// observations arrive on later ticks. Native resources and retirement remain owned by the host.
/// </summary>
public sealed class LightingCompilationController
{
    private static readonly BbKey<string> ReasonKey = new("lighting.reason");
    private static readonly BbKey<string> ContentKey = new("lighting.content");
    private static readonly BbKey<long> GenerationKey = new("lighting.generation");
    private static readonly BbKey<long> PlantKey = new("lighting.plant");
    private static readonly BbKey<double> CostKey = new("lighting.estimatedMilliseconds");
    private static readonly string[] Phases = ["Dirty", "Baking", "Ready", "Failed"];
    private readonly Dictionary<string, Job> jobs = new(StringComparer.Ordinal);
    private readonly AurelianAgentRuntime runtime;
    private readonly Func<LightingBakeRequest, LightingBakeTicket> submit;
    private IReadOnlyList<LightingPlantAvailability> plants = [];
    private IReadOnlyList<LightingBakeCompletion> completions = [];
    private long raysRemaining;
    private double millisecondsRemaining;
    private long tick;
    private readonly HashSet<uint> dispatchedPlants = [];
    private readonly Dictionary<(uint Plant, LightingArtifactKind Kind), double> measuredCosts = [];

    public LightingCompilationController(Func<LightingBakeRequest, LightingBakeTicket> submit, int traceCapacity = 256)
    {
        ArgumentNullException.ThrowIfNull(submit);
        this.submit = submit;
        runtime = new(traceCapacity);
    }

    public DominatusInspector Inspector => runtime.Inspector;

    public void SetTarget(LightingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (jobs.TryGetValue(compilation.Id, out Job? existing))
        {
            if (existing.Compilation.ContentKey != compilation.ContentKey)
            {
                existing.Compilation = compilation;
                existing.Generation++;
                existing.Published = null;
                existing.Agent!.Bb.Set(ContentKey, compilation.ContentKey);
                existing.Agent.Bb.Set(GenerationKey, existing.Generation);
                existing.Agent.Bb.Set(ReasonKey, "BakedInputsChanged");
            }
            return;
        }
        var job = new Job(compilation);
        var root = StateId.Of(StateName(job, "Dirty"));
        var graph = new HfsmGraph { Root = root };
        foreach (string phase in Phases)
        {
            string captured = phase;
            graph.Add(StateId.Of(StateName(job, phase)), ctx => Run(job, captured, ctx));
        }
        job.Agent = runtime.Add("lighting." + compilation.Id,
            new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = false }));
        jobs.Add(compilation.Id, job);
    }

    public void Tick(IReadOnlyList<LightingPlantAvailability> availablePlants,
        IReadOnlyList<LightingBakeCompletion> observedCompletions, LightingScheduleBudget budget)
    {
        ArgumentNullException.ThrowIfNull(availablePlants);
        ArgumentNullException.ThrowIfNull(observedCompletions);
        ArgumentNullException.ThrowIfNull(budget);
        if (budget.MaximumRays < 0 || !double.IsFinite(budget.MaximumEstimatedMilliseconds)
            || budget.MaximumEstimatedMilliseconds < 0 || availablePlants.Select(item => item.PlantId).Distinct().Count() != availablePlants.Count
            || availablePlants.Any(item => item.SupportedArtifacts is null
                || !double.IsFinite(item.EstimatedMillisecondsPerRay) || item.EstimatedMillisecondsPerRay < 0))
        {
            throw new ArgumentException("Scheduling requires unique plants and finite nonnegative budgets/costs.");
        }
        plants = availablePlants;
        completions = observedCompletions;
        raysRemaining = budget.MaximumRays;
        millisecondsRemaining = budget.MaximumEstimatedMilliseconds;
        dispatchedPlants.Clear();
        tick++;
        runtime.Tick(TimeSpan.FromSeconds(1.0 / 60));
        completions = [];
    }

    public LightingCompilationStatus Observe(string id)
    {
        Job job = jobs[id];
        var path = job.Agent!.Brain.GetActivePath();
        string phase = path.Count == 0 ? "Dirty" : path.Last().ToString().Split('.').Last();
        return new(id, phase, job.Generation, job.Compilation.ContentKey,
            job.Agent.Bb.GetOrDefault(ReasonKey, "NotTicked"), job.Published);
    }

    private IEnumerator<AiStep> Run(Job job, string phase, AiCtx context)
    {
        while (true)
        {
            context.Agent.Bb.Set(ContentKey, job.Compilation.ContentKey);
            context.Agent.Bb.Set(GenerationKey, job.Generation);
            string next = Update(job, phase, context);
            if (next != phase)
            {
                yield return new Goto(StateId.Of(StateName(job, next)), context.Agent.Bb.GetOrDefault(ReasonKey, ""));
                yield break;
            }
            long observedTick = tick;
            yield return new WaitUntil(_ => tick != observedTick);
        }
    }

    private string Update(Job job, string phase, AiCtx context)
    {
        if (phase != "Dirty" && job.SubmittedGeneration != job.Generation)
        {
            job.Pending = null;
            context.Agent.Bb.Set(ReasonKey, "BakedInputsChanged");
            return "Dirty";
        }
        if (phase == "Baking")
        {
            LightingBakeCompletion? complete = completions.FirstOrDefault(item => item.Ticket == job.Pending);
            if (complete is null)
            {
                context.Agent.Bb.Set(ReasonKey, "CompletionPending");
                return phase;
            }
            if (!complete.Success || !complete.FullyResolved || !double.IsFinite(complete.Milliseconds) || complete.Milliseconds < 0)
            {
                context.Agent.Bb.Set(ReasonKey, complete.Diagnostic ?? "UnqualifiedCompletion");
                return "Failed";
            }
            job.Published = complete.Ticket;
            if (complete.Milliseconds > 0)
            {
                measuredCosts[(complete.Ticket.PlantId, job.Compilation.Kind)]
                    = complete.Milliseconds / job.Compilation.EstimatedRayCount;
            }
            context.Agent.Bb.Set(ReasonKey, "PublishCompletedGeneration");
            return "Ready";
        }
        if (phase is "Ready" or "Failed")
        {
            return phase;
        }
        long rays = job.Compilation.EstimatedRayCount;
        LightingPlantAvailability? selected = plants
            .Where(item => item.Available && !dispatchedPlants.Contains(item.PlantId)
                && item.SupportedArtifacts.Contains(job.Compilation.Kind)
                && rays <= raysRemaining && Estimate(item) <= millisecondsRemaining)
            .OrderBy(Estimate).ThenBy(item => item.PlantId).FirstOrDefault();
        if (selected is null)
        {
            context.Agent.Bb.Set(ReasonKey, "NoPlantWithinBudget");
            return "Dirty";
        }
        job.SubmittedGeneration = job.Generation;
        try
        {
            LightingBakeTicket ticket = submit(new(job.Compilation, job.Generation, selected.PlantId));
            if (ticket.PlantId != selected.PlantId || ticket.CompletionValue == 0 || string.IsNullOrWhiteSpace(ticket.CompletionDomain)
                || ticket.ContentKey != job.Compilation.ContentKey || ticket.Generation != job.Generation)
            {
                throw new InvalidOperationException("Submission returned a mismatched completion identity.");
            }
            job.Pending = ticket;
            raysRemaining -= rays;
            double estimatedMilliseconds = Estimate(selected);
            millisecondsRemaining -= estimatedMilliseconds;
            dispatchedPlants.Add(selected.PlantId);
            context.Agent.Bb.Set(PlantKey, (long)selected.PlantId);
            context.Agent.Bb.Set(CostKey, estimatedMilliseconds);
            context.Agent.Bb.Set(ReasonKey, "DelegateCompilation");
            return "Baking";
        }
        catch (Exception exception)
        {
            context.Agent.Bb.Set(ReasonKey, "SubmissionFailed: " + exception.Message);
            return "Failed";
        }

        double Estimate(LightingPlantAvailability candidate)
        {
            double perRay = measuredCosts.GetValueOrDefault((candidate.PlantId, job.Compilation.Kind), candidate.EstimatedMillisecondsPerRay);
            return rays * perRay;
        }
    }

    private static string StateName(Job job, string phase) => "aurelian.lighting." + job.Compilation.Id + "." + phase;

    private sealed class Job(LightingCompilation compilation)
    {
        public LightingCompilation Compilation { get; set; } = compilation;
        public long Generation { get; set; } = 1;
        public long SubmittedGeneration { get; set; }
        public LightingBakeTicket? Pending { get; set; }
        public LightingBakeTicket? Published { get; set; }
        public global::Dominatus.Core.Runtime.AiAgent? Agent { get; set; }
    }
}
