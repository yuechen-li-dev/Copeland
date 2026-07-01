using Aurelian.Core.Compositor;
using Aurelian.Core.Graphics.Vulkan.Compositor;
using Aurelian.Graphics.Vulkan.Commanding;
using Aurelian.Graphics.Vulkan.Commanding.Submit;
using Aurelian.Graphics.Vulkan.Compositor;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Presentation;
using Aurelian.Graphics.Vulkan.Resources.Allocation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Graphics.Vulkan.Sync;
using Aurelian.Integration.Tests.Support;
using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Runtime.Compositor;
using Dominatus.Core.Runtime;
using Xunit;

namespace Aurelian.Integration.Tests.Compositor;

public sealed class RuntimeGraphicsCompositorBridgeM0Tests
{
    [Fact]
    public async Task RuntimeGraphicsCompositorBridge_FakeActuator_DispatchesNeutralRequestThroughDominatus()
    {
        PlantOutputRef output = new(0, 1, "integration-final");
        PresentationTargetRef target = new(0, 0, 1);
        CompositorPolicyFacts facts = Facts(1, output, target, PlantOutputReadinessStatus.Ready);
        var handler = new CapturingCompositorDispatchHandler(cmd => new CompositorDispatchResult(
            CompositorDispatchStatus.Dispatched,
            cmd.Request.FrameId,
            cmd.Request.Policy,
            cmd.Request.Target,
            CompositorDiagnostics.Empty,
            []));
        var actuatorHost = new ActuatorHost();
        actuatorHost.Register(handler);

        CompositorPolicyResult result = await CompositorPolicySession.RunOnceAsync(facts, actuatorHost);

        Assert.True(result.Success, FormatDiagnostics(result));
        Assert.Equal(CompositorPolicyStatus.Dispatched, result.Status);
        CompositorDispatchAct act = Assert.Single(handler.Acts);
        Assert.Equal(1UL, act.Request.FrameId);
        Assert.Equal(CompositorPolicyKind.Passthrough, act.Request.Policy);
        Assert.Equal(output, Assert.Single(act.Request.Inputs));
        Assert.Equal(target, act.Request.Target);
        Assert.NotNull(result.DispatchResult);
        Assert.Equal(CompositorDispatchStatus.Dispatched, result.DispatchResult.Status);
    }

    [Fact]
    public async Task RuntimeGraphicsCompositorBridge_RealVulkanPassthrough_WhenAvailable_DispatchesThroughGraphicsMechanism()
    {
        VulkanInitResult init = VulkanVisibleTriangleIntegrationFixture.CreatePresentationPlant();
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }

            VulkanSwapchainCreateResult swapchainResult = VulkanSwapchainFactory.Create(init.Plant!);
            using (swapchainResult.Surface)
            using (swapchainResult.Swapchain)
            {
                if (!swapchainResult.Success)
                {
                    Assert.NotEmpty(swapchainResult.Diagnostics);
                    Assert.Contains(swapchainResult.Status, new[] { VulkanPresentationStatus.Unavailable, VulkanPresentationStatus.Rejected, VulkanPresentationStatus.Failed });
                    return;
                }

                AurelianVulkanSwapchain swapchain = swapchainResult.Swapchain!;
                VulkanSwapchainAcquireResult acquire = swapchain.AcquireNextImage();
                if (acquire.Status is not (VulkanSwapchainAcquireStatus.Acquired or VulkanSwapchainAcquireStatus.Suboptimal) || acquire.ImageIndex is null)
                {
                    Assert.NotEmpty(acquire.Diagnostics);
                    return;
                }

                if (!VulkanVisibleTriangleIntegrationFixture.TryMapTextureFormat(swapchain.Facts.SelectedFormat, out VulkanTextureFormat sourceFormat))
                {
                    return;
                }

                using var allocator = new RawVulkanMemoryAllocator(init.Plant!);
                VulkanTextureCreateResult textureResult = VulkanTextureFactory.Create(
                    init.Plant!,
                    allocator,
                    new VulkanTextureCreatePlan(
                        init.Plant!.Context.Id,
                        swapchain.Facts.Width,
                        swapchain.Facts.Height,
                        sourceFormat,
                        VulkanTextureUsage.TransferSource | VulkanTextureUsage.TransferDestination | VulkanTextureUsage.ColorAttachment,
                        VulkanMemoryUsage.GpuOnly,
                        VulkanResourceLayout.Undefined,
                        DebugName: "a56.integration.passthrough.source"));
                if (!textureResult.Success)
                {
                    Assert.NotEmpty(textureResult.Diagnostics);
                    return;
                }

                using AurelianVulkanTexture sourceTexture = textureResult.Texture!;
                using var fences = VulkanFenceBundle.Create(init.Plant!);
                using var commandPool = VulkanCommandBufferPool.Create(init.Plant!);
                using var submitter = new VulkanCommandSubmitter(init.Plant!, commandPool, fences);
                using var compositor = new VulkanCompositorPassthrough(init.Plant!, commandPool, submitter);

                PlantOutputRef output = new(init.Plant!.Context.Id.Value, 1, "integration-final");
                VulkanPlantOutputImageSet outputs = new([new VulkanPlantOutputImage(output, sourceTexture)]);
                VulkanPresentationTargetImageSet targets = swapchain.CreatePresentationTargetImageSet();
                PresentationTargetRef target = new(init.Plant!.Context.Id.Value, acquire.ImageIndex.Value, 1);
                CompositorPolicyFacts facts = Facts(1, output, target, PlantOutputReadinessStatus.Ready);
                var adapter = new VulkanCompositorMechanismAdapter(compositor, outputs, targets);
                var bridge = new CompositorActuationBridge(adapter);
                var handler = new CapturingCompositorDispatchHandler(cmd => bridge.HandleAsync(cmd).GetAwaiter().GetResult());
                var actuatorHost = new ActuatorHost();
                actuatorHost.Register(handler);

                CompositorPolicyResult result = await CompositorPolicySession.RunOnceAsync(facts, actuatorHost);

                Assert.True(result.Success, FormatDiagnostics(result));
                Assert.NotNull(result.DispatchResult);
                Assert.True(result.DispatchResult.Success, FormatDiagnostics(result.DispatchResult));
                Assert.Equal(VulkanResourceLayout.Present, targets.Images[(int)acquire.ImageIndex.Value].LayoutTracker.Get(0, 0));
                CompositorDispatchAct act = Assert.Single(handler.Acts);
                Assert.Equal(output, Assert.Single(act.Request.Inputs));
                Assert.Equal(target, act.Request.Target);

                VulkanSwapchainPresentResult present = swapchain.Present(acquire.ImageIndex.Value);
                Assert.Contains(present.Status, new[]
                {
                    VulkanSwapchainPresentStatus.Presented,
                    VulkanSwapchainPresentStatus.Suboptimal,
                    VulkanSwapchainPresentStatus.OutOfDate,
                });
            }
        }
    }

    [Fact]
    public void RuntimeGraphicsCompositorBridge_ProductionProjectsRemainDecoupled()
    {
        string repoRoot = GetRepoRoot();
        string runtimeProject = File.ReadAllText(Path.Combine(repoRoot, "src/Aurelian.Runtime/Aurelian.Runtime.csproj"));
        string graphicsProject = File.ReadAllText(Path.Combine(repoRoot, "src/Aurelian.Graphics/Aurelian.Graphics.csproj"));
        string contractsProject = File.ReadAllText(Path.Combine(repoRoot, "src/Aurelian.Rendering.Contracts/Aurelian.Rendering.Contracts.csproj"));

        Assert.DoesNotContain("Aurelian.Graphics", runtimeProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian.Runtime", graphicsProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Dominatus", graphicsProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian.Runtime", contractsProject, StringComparison.Ordinal);
        Assert.DoesNotContain("Aurelian.Graphics", contractsProject, StringComparison.Ordinal);
    }

    private static CompositorPolicyFacts Facts(
        ulong frameId,
        PlantOutputRef output,
        PresentationTargetRef target,
        PlantOutputReadinessStatus status)
    {
        var readiness = new PlantOutputReadiness(
            output,
            status,
            CompletedFenceValue: status is PlantOutputReadinessStatus.Ready or PlantOutputReadinessStatus.Reused ? frameId : null);
        var frameFacts = new CompositorFrameFacts(frameId, [readiness], CompositorDiagnostics.Empty);
        var required = new RequiredPlantOutputSet(frameId, CompositorPolicyKind.Passthrough, [output]);
        return new CompositorPolicyFacts(frameFacts, required, target, CompositorPolicyKind.Passthrough);
    }

    private static string FormatDiagnostics(CompositorPolicyResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(CompositorDispatchResult result)
        => string.Join(Environment.NewLine, result.DispatchDiagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string GetRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aurelian.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
