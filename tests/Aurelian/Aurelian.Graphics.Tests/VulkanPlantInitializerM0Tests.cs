using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Diagnostics;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class VulkanPlantInitializerM0Tests
{
    [Fact]
    public void VulkanPlantInitializer_CreatePlant_DoesNotThrow()
    {
        VulkanInitResult? result = null;
        Exception? exception = Record.Exception(() => result = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: false)));

        Assert.Null(exception);
        Assert.NotNull(result);
        result?.Plant?.Dispose();
    }

    [Fact]
    public void VulkanPlantInitializer_CreatePlant_ReturnsCreatedOrUnavailableOrRejected()
    {
        VulkanInitResult result = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: false));

        using (result.Plant)
        {
            Assert.Contains(result.Status, new[]
            {
                VulkanInitStatus.Created,
                VulkanInitStatus.Unavailable,
                VulkanInitStatus.Rejected,
            });
        }
    }

    [Fact]
    public void VulkanPlantInitializer_CreatePlant_WhenCreated_HasPlantZeroFacts()
    {
        VulkanInitResult result = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: false));

        using (result.Plant)
        {
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            Assert.NotNull(result.Facts);
            Assert.Equal(PlantId.Zero, result.Plant!.Context.Id);
            Assert.Equal(PlantId.Zero, result.Facts!.PlantId);
            Assert.Equal(PlantKind.Vulkan, result.Plant.Context.Kind);
            Assert.True(result.Facts.TimelineSemaphores);
            Assert.False(string.IsNullOrWhiteSpace(result.Facts.PhysicalDeviceName));
        }
    }

    [Fact]
    public void VulkanPlantInitializer_CreatePlant_WhenCreated_DisposesCleanly()
    {
        VulkanInitResult result = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: false));

        if (!result.Success)
        {
            Assert.NotEmpty(result.Diagnostics);
            return;
        }

        result.Plant!.Dispose();
        result.Plant.Dispose();
    }

    [Fact]
    public void VulkanPlantInitializer_CreatePlant_WhenCreated_SelectsNonHardcodedQueueFamilyFact()
    {
        VulkanInitResult result = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: false));

        using (result.Plant)
        {
            if (!result.Success)
            {
                Assert.NotEmpty(result.Diagnostics);
                return;
            }

            Assert.Equal(result.Plant!.QueueFamilyIndex, result.Facts!.QueueFamilyIndex);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == VulkanInitDiagnosticCodes.DeviceSelected);
        }
    }

    [Fact]
    public void VulkanPlantInitializer_CreatePlant_WhenUnavailable_HasDiagnostic()
    {
        VulkanInitResult result = VulkanPlantInitializer.CreatePlant(
            PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: false));

        using (result.Plant)
        {
            if (result.Status != VulkanInitStatus.Unavailable)
            {
                return;
            }

            Assert.NotEmpty(result.Diagnostics);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == VulkanInitDiagnosticSeverity.Error);
        }
    }
}
