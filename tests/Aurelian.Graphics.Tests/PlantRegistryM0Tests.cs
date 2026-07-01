using System.Globalization;
using Aurelian.Graphics.Plants;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class PlantRegistryM0Tests
{
    [Fact]
    public void PlantId_Zero_IsPlantZero()
    {
        Assert.Equal(0u, PlantId.Zero.Value);
    }

    [Fact]
    public void PlantId_ToString_UsesInvariantValue()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar-SA");

            Assert.Equal("4294967295", new PlantId(uint.MaxValue).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void PlantRegistry_SingleVulkanPlant_CreatesOnePresentationPlant()
    {
        var registry = PlantRegistry.SingleVulkanPlant();

        var plant = Assert.Single(registry.Plants);
        Assert.Equal(PlantId.Zero, plant.Id);
        Assert.Equal(PlantKind.Vulkan, plant.Kind);
        Assert.Equal(GpuCapabilityTier.VulkanM0, plant.Capability);
        Assert.True(plant.IsPresentationPlant);
        Assert.Equal(plant, registry.PresentationPlant);
    }

    [Fact]
    public void PlantRegistry_Create_SortsPlantsByPlantIdDeterministically()
    {
        PlantContext plantTwo = CreatePlant(2, isPresentationPlant: false);
        PlantContext plantZero = CreatePlant(0, isPresentationPlant: true);
        PlantContext plantOne = CreatePlant(1, isPresentationPlant: false);

        PlantRegistryResult result = PlantRegistry.Create([plantTwo, plantZero, plantOne]);

        Assert.True(result.Success);
        Assert.NotNull(result.Registry);
        Assert.Equal([0u, 1u, 2u], result.Registry.Plants.Select(plant => plant.Id.Value).ToArray());
    }

    [Fact]
    public void PlantRegistry_TryGet_ReturnsExpectedPlant()
    {
        PlantContext expected = CreatePlant(7, isPresentationPlant: true);
        var registry = new PlantRegistry([expected]);

        bool found = registry.TryGet(new PlantId(7), out PlantContext actual);

        Assert.True(found);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PlantRegistry_GetRequired_ThrowsForMissingPlant()
    {
        var registry = PlantRegistry.SingleVulkanPlant();

        Assert.Throws<KeyNotFoundException>(() => registry.GetRequired(new PlantId(99)));
    }

    [Fact]
    public void PlantRegistry_Create_RejectsEmptyPlantList()
    {
        PlantRegistryResult result = PlantRegistry.Create([]);

        Assert.False(result.Success);
        Assert.Null(result.Registry);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PlantRegistryDiagnosticCodes.NoPlants, diagnostic.Code);
        Assert.Equal(PlantRegistryDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void PlantRegistry_Create_RejectsDuplicatePlantIds()
    {
        PlantRegistryResult result = PlantRegistry.Create(
        [
            CreatePlant(0, isPresentationPlant: true),
            CreatePlant(0, isPresentationPlant: false),
        ]);

        Assert.False(result.Success);
        Assert.Null(result.Registry);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == PlantRegistryDiagnosticCodes.DuplicatePlantId);
    }

    [Fact]
    public void PlantRegistry_Create_RejectsMissingPresentationPlant()
    {
        PlantRegistryResult result = PlantRegistry.Create([CreatePlant(0, isPresentationPlant: false)]);

        Assert.False(result.Success);
        Assert.Null(result.Registry);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == PlantRegistryDiagnosticCodes.MissingPresentationPlant);
    }

    [Fact]
    public void PlantRegistry_Create_RejectsMultiplePresentationPlants()
    {
        PlantRegistryResult result = PlantRegistry.Create(
        [
            CreatePlant(0, isPresentationPlant: true),
            CreatePlant(1, isPresentationPlant: true),
        ]);

        Assert.False(result.Success);
        Assert.Null(result.Registry);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == PlantRegistryDiagnosticCodes.MultiplePresentationPlants);
    }

    [Fact]
    public void PlantRegistry_SelectDefault_ReturnsPresentationPlant()
    {
        var registry = new PlantRegistry(
        [
            CreatePlant(3, isPresentationPlant: false),
            CreatePlant(1, isPresentationPlant: true),
        ]);

        PlantSelection selection = registry.SelectDefault();

        Assert.Equal(new PlantId(1), selection.PlantId);
        Assert.Contains("M0 fixed", selection.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void PlantContext_ExposesOnlyManagedPlantData()
    {
        string[] forbiddenTypeNames =
        [
            "Si" + "lk",
            "V" + "k",
            "I" + "Window",
            "Window" + "Options",
            "Int" + "Ptr",
            "UInt" + "Ptr",
        ];
        string[] forbiddenPropertyNames =
        [
            "Han" + "dle",
            "Dev" + "ice",
            "Que" + "ue",
            "Sur" + "face",
            "Swap" + "chain",
            "Command" + "Buffer",
        ];

        var properties = typeof(PlantContext).GetProperties();

        foreach (var property in properties)
        {
            string typeName = property.PropertyType.FullName ?? property.PropertyType.Name;
            Assert.DoesNotContain(forbiddenTypeNames, forbidden => typeName.Contains(forbidden, StringComparison.Ordinal));
            Assert.DoesNotContain(forbiddenPropertyNames, forbidden => property.Name.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    private static PlantContext CreatePlant(uint id, bool isPresentationPlant)
        => new(
            new PlantId(id),
            PlantKind.Unknown,
            GpuCapabilityTier.Unknown,
            $"Plant {id.ToString(CultureInfo.InvariantCulture)}",
            isPresentationPlant);
}
