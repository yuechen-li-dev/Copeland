using System.Numerics;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Runtime.Lighting;
using Xunit;

namespace Aurelian.Runtime.Tests;

public sealed class LightingCompilationTests
{
    private static readonly LightingPlantAvailability Plant = new(0, true,
        new HashSet<LightingArtifactKind> { LightingArtifactKind.ShadowVisibility }, .0001);
    private static readonly LightingScheduleBudget Budget = new(1000, 10);

    [Fact]
    public void Explicit_invalidation_withdraws_immediately_and_requires_a_new_target_before_resubmission()
    {
        var submissions = new List<LightingBakeTicket>();
        var controller = Controller(submissions);
        controller.SetTarget(Plan());
        controller.Tick([Plant], [], Budget);
        var previous = Assert.Single(submissions);
        controller.Tick([Plant], [new(previous, true, true, 1)], Budget);
        controller.Invalidate("floor", "StaticMaterialChanged");
        Assert.Null(controller.Observe("floor").Published);
        for (int tick = 0; tick < 3; tick++)
            controller.Tick([Plant], [new(previous, true, true, 1)], Budget);
        Assert.Single(submissions);
        Assert.Equal("StaticMaterialChanged", controller.Observe("floor").Reason);
        controller.SetTarget(Plan());
        controller.Tick([Plant], [], Budget);
        Assert.Equal(2, submissions.Count);
        Assert.True(submissions[1].Generation > previous.Generation);
        Assert.Null(controller.Observe("floor").Published);
    }

    [Fact]
    public void Baked_dependencies_change_keys_and_runtime_coefficients_do_not()
    {
        LightingCompilation first = Plan();
        Assert.Equal(first.ContentKey, Plan(id: "another-slot").ContentKey);
        Assert.NotEqual(first.ContentKey, Plan(geometry: "moved-wall").ContentKey);
        Assert.NotEqual(first.ContentKey, Plan(materials: "new-albedo").ContentKey);
        Assert.NotEqual(first.ContentKey, Plan(domain: "another-eye").ContentKey);
        Vector3 response = LightingResponse.Apply(first, new(.1f, .2f, .3f), new(1, .5f, 0), 2);
        Assert.Equal(new Vector3(.2f, .2f, 0), response);
        Assert.Throws<ArgumentOutOfRangeException>(() => LightingResponse.Apply(first, Vector3.One, Vector3.One, float.NaN));
    }

    [Fact]
    public void Pending_results_are_not_published_and_unchanged_ready_results_do_no_work()
    {
        var submissions = new List<LightingBakeTicket>();
        var controller = Controller(submissions);
        controller.SetTarget(Plan());
        controller.Tick([Plant], [], Budget);
        Assert.Equal("Baking", controller.Observe("floor").Phase);
        Assert.Null(controller.Observe("floor").Published);
        var ticket = Assert.Single(submissions);
        controller.Tick([Plant], [new(ticket, true, true, 1)], Budget);
        Assert.Equal("Ready", controller.Observe("floor").Phase);
        Assert.Equal(ticket, controller.Observe("floor").Published);
        for (int index = 0; index < 20; index++)
        {
            controller.Tick([Plant], [], Budget);
        }
        Assert.Single(submissions);
        Assert.NotEmpty(controller.Inspector.Observe().Trace);
    }

    [Fact]
    public void Superseded_completion_and_other_plant_fences_cannot_publish()
    {
        var submissions = new List<LightingBakeTicket>();
        var controller = Controller(submissions);
        controller.SetTarget(Plan());
        controller.Tick([Plant], [], Budget);
        var old = Assert.Single(submissions);
        controller.SetTarget(Plan(geometry: "door-open"));
        controller.Tick([Plant], [new(old, true, true, 1)], Budget);
        controller.Tick([Plant], [], Budget);
        Assert.Equal(2, submissions.Count);
        var current = submissions[1];
        Assert.Null(controller.Observe("floor").Published);
        controller.Tick([Plant], [new(current with { PlantId = 1 }, true, true, 1)], Budget);
        Assert.Null(controller.Observe("floor").Published);
        controller.Tick([Plant], [new(current with { CompletionDomain = "another-timeline" }, true, true, 1)], Budget);
        Assert.Null(controller.Observe("floor").Published);
        controller.Tick([Plant], [new(current, true, true, 1)], Budget);
        Assert.Equal(current, controller.Observe("floor").Published);
    }

    [Fact]
    public void Exhausted_work_is_failed_rather_than_published_as_a_miss()
    {
        var submissions = new List<LightingBakeTicket>();
        var controller = Controller(submissions);
        controller.SetTarget(Plan());
        controller.Tick([Plant], [], Budget);
        controller.Tick([Plant], [new(Assert.Single(submissions), true, false, 1, "UnresolvedRays")], Budget);
        Assert.Equal("Failed", controller.Observe("floor").Phase);
        Assert.Equal("UnresolvedRays", controller.Observe("floor").Reason);
        Assert.Null(controller.Observe("floor").Published);
    }

    [Fact]
    public void Budget_and_explicit_capabilities_control_delegation()
    {
        var submissions = new List<LightingBakeTicket>();
        var controller = Controller(submissions);
        controller.SetTarget(Plan());
        controller.Tick([Plant], [], new(0, 0));
        Assert.Empty(submissions);
        Assert.Equal("NoPlantWithinBudget", controller.Observe("floor").Reason);
        controller.Tick([Plant with { SupportedArtifacts = new HashSet<LightingArtifactKind>() }], [], Budget);
        Assert.Empty(submissions);
        controller.Tick([Plant, Plant with { PlantId = 2, EstimatedMillisecondsPerRay = .00001 }], [], Budget);
        Assert.Equal(2u, Assert.Single(submissions).PlantId);
    }

    [Fact]
    public void Submission_failures_are_inspectable_and_do_not_spin_retry()
    {
        int attempts = 0;
        var controller = new LightingCompilationController(_ =>
        {
            attempts++;
            throw new InvalidOperationException("DeviceUnavailable");
        });
        controller.SetTarget(Plan());
        controller.Tick([Plant], [], Budget);
        Assert.Equal("Failed", controller.Observe("floor").Phase);
        Assert.Contains("DeviceUnavailable", controller.Observe("floor").Reason);
        controller.Tick([Plant], [], Budget);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void Completion_cost_feedback_changes_subsequent_budget_admission()
    {
        var submissions = new List<LightingBakeTicket>();
        var controller = Controller(submissions);
        controller.SetTarget(Plan());
        controller.Tick([Plant], [], Budget);
        controller.Tick([Plant], [new(Assert.Single(submissions), true, true, 20)], Budget);
        controller.SetTarget(Plan(geometry: "changed"));
        controller.Tick([Plant], [], Budget);
        controller.Tick([Plant], [], Budget);
        Assert.Single(submissions);
        Assert.Equal("NoPlantWithinBudget", controller.Observe("floor").Reason);
        controller.Tick([Plant], [], new(1000, 25));
        Assert.Equal(2, submissions.Count);
    }

    private static LightingCompilationController Controller(List<LightingBakeTicket> submissions)
    {
        return new(request =>
        {
            var ticket = new LightingBakeTicket(request.PlantId, (ulong)submissions.Count + 1,
                request.Compilation.ContentKey, request.Generation);
            submissions.Add(ticket);
            return ticket;
        });
    }

    private static LightingCompilation Plan(string id = "floor", string geometry = "wall-v1",
        string materials = "material-v1", string domain = "receiver-v1")
        => new(id, LightingArtifactKind.ShadowVisibility,
            new(geometry, materials, "sun-v1", domain, "shader-v1"), 8, 8, 1, 256, linearLightResponse: true);
}
