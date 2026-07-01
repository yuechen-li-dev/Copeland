using Aurelian.Core.Compositor;
using Aurelian.Core.Graphics.Vulkan.Compositor;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Compositor;
using Aurelian.Rendering.Contracts.Compositor;
using Xunit;

namespace Aurelian.Core.Tests;

public sealed class VulkanCompositorMechanismAdapterM0Tests
{
    private static readonly PlantOutputRef Output = new(0, 1, "final-color");
    private static readonly PresentationTargetRef Target = new(0, 0, 1);

    [Fact]
    public void VulkanCompositorMechanismAdapter_ImplementsICompositorMechanism()
    {
        var adapter = Adapter(new FakeVulkanCompositorPassthroughMechanism(Result(CompositorDispatchStatus.Dispatched, 1)));

        Assert.IsAssignableFrom<ICompositorMechanism>(adapter);
    }

    [Fact]
    public async Task VulkanCompositorMechanismAdapter_ForwardsRequestToPassthroughMechanism()
    {
        var mechanism = new FakeVulkanCompositorPassthroughMechanism(Result(CompositorDispatchStatus.Dispatched, 2));
        var adapter = Adapter(mechanism);
        CompositorDispatchRequest request = Request(2);

        await adapter.DispatchAsync(request);

        Assert.Equal(request, mechanism.LastRequest);
        Assert.Same(mechanism.PlantOutputs, mechanism.LastPlantOutputs);
        Assert.Same(mechanism.PresentationTargets, mechanism.LastPresentationTargets);
    }

    [Fact]
    public async Task VulkanCompositorMechanismAdapter_ReturnsNeutralDispatchResult()
    {
        CompositorDispatchResult expected = Result(
            CompositorDispatchStatus.Rejected,
            3,
            [new CompositorDispatchDiagnostic("ACOMP-FAKE", CompositorDispatchDiagnosticSeverity.Error, "Fake Vulkan passthrough rejection.")]);
        var adapter = Adapter(new FakeVulkanCompositorPassthroughMechanism(expected));

        CompositorDispatchResult actual = await adapter.DispatchAsync(Request(3));

        Assert.Equal(expected, actual);
        Assert.Equal(CompositorDispatchStatus.Rejected, actual.Status);
        Assert.Equal("ACOMP-FAKE", Assert.Single(actual.DispatchDiagnostics).Code);
    }

    [Fact]
    public void VulkanCompositorMechanismAdapter_DoesNotRequireRuntimeOrDominatus()
    {
        string coreProject = File.ReadAllText(ProjectPath("src/Aurelian.Core/Aurelian.Core.csproj"));
        string graphicsProject = File.ReadAllText(ProjectPath("src/Aurelian.Graphics/Aurelian.Graphics.csproj"));
        string contractsProject = File.ReadAllText(ProjectPath("src/Aurelian.Rendering.Contracts/Aurelian.Rendering.Contracts.csproj"));
        string adapterSource = File.ReadAllText(ProjectPath("src/Aurelian.Core/Graphics/Vulkan/Compositor/VulkanCompositorMechanismAdapter.cs"));

        Assert.Contains("Aurelian.Graphics", coreProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian.Runtime", graphicsProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Dominatus", graphicsProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian.Runtime", contractsProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian.Graphics", contractsProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Dominatus", adapterSource, StringComparison.Ordinal);
    }

    private static VulkanCompositorMechanismAdapter Adapter(FakeVulkanCompositorPassthroughMechanism mechanism)
    {
        VulkanPlantOutputImageSet plantOutputs = new([]);
        VulkanPresentationTargetImageSet presentationTargets = new(PlantId.Zero, []);
        mechanism.PlantOutputs = plantOutputs;
        mechanism.PresentationTargets = presentationTargets;
        return new VulkanCompositorMechanismAdapter(mechanism, plantOutputs, presentationTargets);
    }

    private static CompositorDispatchRequest Request(ulong frameId) =>
        new(frameId, CompositorPolicyKind.Passthrough, [Output], Target);

    private static CompositorDispatchResult Result(
        CompositorDispatchStatus status,
        ulong frameId,
        IReadOnlyList<CompositorDispatchDiagnostic>? dispatchDiagnostics = null) =>
        new(status, frameId, CompositorPolicyKind.Passthrough, Target, CompositorDiagnostics.Empty, dispatchDiagnostics ?? []);

    private static string ProjectPath(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aurelian.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, relativePath);
    }

    private sealed class FakeVulkanCompositorPassthroughMechanism : IVulkanCompositorPassthroughMechanism
    {
        private readonly CompositorDispatchResult result;

        public FakeVulkanCompositorPassthroughMechanism(CompositorDispatchResult result)
        {
            this.result = result;
        }

        public CompositorDispatchRequest? LastRequest { get; private set; }
        public VulkanPlantOutputImageSet? LastPlantOutputs { get; private set; }
        public VulkanPresentationTargetImageSet? LastPresentationTargets { get; private set; }
        public VulkanPlantOutputImageSet? PlantOutputs { get; set; }
        public VulkanPresentationTargetImageSet? PresentationTargets { get; set; }

        public VulkanCompositorResult Dispatch(
            CompositorDispatchRequest request,
            VulkanPlantOutputImageSet plantOutputs,
            VulkanPresentationTargetImageSet presentationTargets)
        {
            LastRequest = request;
            LastPlantOutputs = plantOutputs;
            LastPresentationTargets = presentationTargets;
            return new VulkanCompositorResult(VulkanCompositorStatus.Dispatched, result, SignalFenceValue: 1, Diagnostics: []);
        }
    }
}
