using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Aurelian.Graphics.Vulkan.Native2D;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanNativeFrameTargetM0Tests
{
    [Fact]
    public void BeginFrameRejectsInvalidClearColor()
    {
        WithPlant(plant =>
        {
            using var target = new VulkanNativeFrameTarget(plant, 32, 32);

            Assert.Throws<ArgumentOutOfRangeException>(() => target.BeginFrame(
                new NativeFrameClearColor(float.NaN, 0, 0, 1)));
        });
    }

    [Fact]
    public void FrameWithoutPresentedPassCannotComplete()
    {
        WithPlant(plant =>
        {
            using var target = new VulkanNativeFrameTarget(plant, 32, 32);
            using VulkanNativeFrameSession frame = target.BeginFrame(NativeFrameClearColor.Transparent);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => frame.EndFrame(captureReadback: false));

            Assert.Contains("at least one presented pass", error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DisposedTargetRejectsPresentationLifecycle()
    {
        WithPlant(plant =>
        {
            var target = new VulkanNativeFrameTarget(plant, 32, 32);
            target.Dispose();

            Assert.Throws<ObjectDisposedException>(() => target.BeginFrame(NativeFrameClearColor.Transparent));
        });
    }

    private static void WithPlant(Action<AurelianVulkanPlant> action)
    {
        VulkanInitResult init = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: false));
        using (init.Plant)
        {
            if (!init.Success)
            {
                Assert.NotEmpty(init.Diagnostics);
                return;
            }
            action(init.Plant!);
        }
    }
}
