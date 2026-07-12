namespace Aurelian.Graphics.Plants;

public sealed class PlantRegistry
{
    public PlantRegistry(IReadOnlyList<PlantContext> plants)
        : this(CreateValidatedPlants(plants), skipValidation: true)
    {
    }

    private PlantRegistry(IReadOnlyList<PlantContext> plants, bool skipValidation)
    {
        Plants = plants
            .OrderBy(plant => plant.Id.Value)
            .ToArray();
        PresentationPlant = Plants.Single(plant => plant.IsPresentationPlant);
    }

    public IReadOnlyList<PlantContext> Plants { get; }

    public PlantContext PresentationPlant { get; }

    public static PlantRegistry SingleVulkanPlant(string displayName = "Vulkan Plant 0")
        => new(
        [
            new PlantContext(
                PlantId.Zero,
                PlantKind.Vulkan,
                GpuCapabilityTier.VulkanM0,
                displayName,
                IsPresentationPlant: true),
        ]);

    public static PlantRegistryResult Create(IReadOnlyList<PlantContext>? plants)
    {
        var diagnostics = Validate(plants);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == PlantRegistryDiagnosticSeverity.Error))
        {
            return new PlantRegistryResult(null, diagnostics);
        }

        return new PlantRegistryResult(new PlantRegistry(plants!, skipValidation: true), diagnostics);
    }

    public bool TryGet(PlantId id, out PlantContext context)
    {
        context = Plants.FirstOrDefault(plant => plant.Id == id)!;
        return context is not null;
    }

    public PlantContext GetRequired(PlantId id)
    {
        if (TryGet(id, out PlantContext context))
        {
            return context;
        }

        throw new KeyNotFoundException($"Plant '{id}' is not registered.");
    }

    public PlantSelection SelectDefault()
        => new(
            PresentationPlant.Id,
            PresentationPlant.Id == PlantId.Zero
                ? "M0 fixed plant-zero selection policy."
                : "M0 fixed presentation-plant selection policy.");

    private static IReadOnlyList<PlantContext> CreateValidatedPlants(IReadOnlyList<PlantContext>? plants)
    {
        var diagnostics = Validate(plants);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == PlantRegistryDiagnosticSeverity.Error))
        {
            string message = string.Join(" ", diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
            throw new ArgumentException(message, nameof(plants));
        }

        return plants!;
    }

    private static IReadOnlyList<PlantRegistryDiagnostic> Validate(IReadOnlyList<PlantContext>? plants)
    {
        if (plants is null || plants.Count == 0)
        {
            return
            [
                new PlantRegistryDiagnostic(
                    PlantRegistryDiagnosticCodes.NoPlants,
                    PlantRegistryDiagnosticSeverity.Error,
                    "A plant registry requires at least one plant."),
            ];
        }

        List<PlantRegistryDiagnostic> diagnostics = [];

        foreach (var duplicateGroup in plants.GroupBy(plant => plant.Id).Where(group => group.Count() > 1))
        {
            diagnostics.Add(new PlantRegistryDiagnostic(
                PlantRegistryDiagnosticCodes.DuplicatePlantId,
                PlantRegistryDiagnosticSeverity.Error,
                $"Plant id '{duplicateGroup.Key}' appears more than once.",
                duplicateGroup.Key));
        }

        int presentationPlantCount = plants.Count(plant => plant.IsPresentationPlant);
        if (presentationPlantCount == 0)
        {
            diagnostics.Add(new PlantRegistryDiagnostic(
                PlantRegistryDiagnosticCodes.MissingPresentationPlant,
                PlantRegistryDiagnosticSeverity.Error,
                "Exactly one presentation plant is required."));
        }
        else if (presentationPlantCount > 1)
        {
            diagnostics.Add(new PlantRegistryDiagnostic(
                PlantRegistryDiagnosticCodes.MultiplePresentationPlants,
                PlantRegistryDiagnosticSeverity.Error,
                "Only one presentation plant is allowed."));
        }

        return diagnostics;
    }
}
