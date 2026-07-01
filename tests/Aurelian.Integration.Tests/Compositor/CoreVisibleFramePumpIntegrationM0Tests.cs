using Aurelian.Core.Engine.Frames;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Presentation;
using Aurelian.Graphics.Vulkan.Resources.Barriers;
using Aurelian.Integration.Tests.Support;
using Aurelian.Rendering.Contracts.Compositor;
using Aurelian.Runtime.Compositor;
using Xunit;

namespace Aurelian.Integration.Tests.Compositor;

public sealed class CoreVisibleFramePumpIntegrationM0Tests
{
    [Fact]
    public async Task AurelianFramePump_RunOneVisibleFrame_WhenAvailable_DispatchesCompositorAndPresents()
    {
        if (!VulkanVisibleFrameTestFixture.TryCreate(60, out VulkanVisibleFrameTestFixture? fixture))
        {
            Assert.Null(fixture);
            return;
        }

        Assert.NotNull(fixture);
        using (fixture)
        {
            VulkanVisibleFrameTestFixture visibleFrame = fixture;
            AurelianFrameResult result = await visibleFrame.FramePump.RunOneFrameAsync(visibleFrame.Input);

            Assert.True(result.Success, FormatDiagnostics(result));
            Assert.Equal(AurelianFrameStatus.Completed, result.Status);
            Assert.NotNull(result.CompositorResult);
            Assert.Equal(CompositorPolicyStatus.Dispatched, result.CompositorResult.Status);
            Assert.NotNull(result.CompositorResult.DispatchResult);
            Assert.True(result.CompositorResult.DispatchResult.Success, FormatDiagnostics(result.CompositorResult.DispatchResult));
            Assert.Equal(CompositorDispatchStatus.Dispatched, result.CompositorResult.DispatchResult.Status);
            Assert.Equal(visibleFrame.Input.CompositorFacts.Target, result.CompositorResult.DispatchResult.Target);
            Assert.Equal(VulkanResourceLayout.Present, visibleFrame.PresentationTargets.Images[(int)visibleFrame.AcquiredImageIndex].LayoutTracker.Get(0, 0));

            VulkanSwapchainPresentResult present = visibleFrame.Present();
            Assert.Contains(present.Status, new[]
            {
                VulkanSwapchainPresentStatus.Presented,
                VulkanSwapchainPresentStatus.Suboptimal,
                VulkanSwapchainPresentStatus.OutOfDate,
            });
        }
    }

    [Fact]
    public void AurelianFramePump_DoesNotCreateVulkanResources_ForVisibleFramePumpIntegration()
    {
        string framesRoot = ProjectPath("src/Aurelian.Core/Engine/Frames");
        string source = string.Join('\n', Directory.GetFiles(framesRoot, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        string[] forbidden =
        [
            "Create" + "Vul" + "kan" + "Surface",
            "Window" + ".Create",
            "Vk" + ".GetApi",
            "vk" + "Create",
            "vk" + "Cmd",
            "vk" + "Queue",
            "Swap" + "chain",
            "Surface",
            "Sil" + "k",
            "Vul" + "kan",
        ];

        foreach (string term in forbidden)
        {
            Assert.DoesNotContain(term, source, StringComparison.Ordinal);
        }
    }

    private static string FormatDiagnostics(AurelianFrameResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private static string FormatDiagnostics(CompositorDispatchResult result) =>
        string.Join(Environment.NewLine, result.DispatchDiagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

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
}
