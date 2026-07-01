namespace Aurelian.Graphics.Plants;

public sealed record PlantContext(
    PlantId Id,
    PlantKind Kind,
    GpuCapabilityTier Capability,
    string DisplayName,
    bool IsPresentationPlant);
