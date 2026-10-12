using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Shaders.Library;

namespace Aurelian.Inference;

public sealed record VulkanInferenceOptions
{
    public string ApplicationName { get; init; } = "Aurelian inference";
    public int MaximumTensorElements { get; init; } = 1048576;
    public int MaximumDispatches { get; init; } = 512;
    public int AttentionQueryTileRows { get; init; } = 256;
    public bool EnableValidation { get; init; } = true;
    public bool UseCooperativeLinear { get; init; }
}

/// <summary>Headless model host over the existing Vulkan plant and compute owner.
/// Attach borrows a plant; Create owns one. Both own and dispose their math
/// resources before an owned plant. This class starts no window or worker.</summary>
public sealed class VulkanInferenceSession : IDisposable
{
    private readonly bool ownsPlant;
    private bool disposed;
    public AurelianVulkanPlant Plant { get; }
    public VulkanComputeMath Compute { get; }

    private VulkanInferenceSession(AurelianVulkanPlant plant, VulkanInferenceOptions options, bool ownsPlant)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.AttentionQueryTileRows is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Attention query tiles must be 1..256 rows.");
        }
        Plant = plant;
        this.ownsPlant = ownsPlant;
        var shaders = new BuiltinComputeShaderCache();
        Compute = new(plant, shaders.Get, options.MaximumDispatches, options.MaximumTensorElements)
        {
            AttentionQueryTileRows = options.AttentionQueryTileRows,
            UseCooperativeLinear = options.UseCooperativeLinear,
        };
    }

    public static VulkanInferenceSession Create(VulkanInferenceOptions? options = null)
    {
        options ??= new();
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new(EnableValidation: options.EnableValidation, ApplicationName: options.ApplicationName,
                EnableCooperativeMatrices: options.UseCooperativeLinear));
        if (!initialization.Success)
        {
            throw new InvalidOperationException("AUR-INFERENCE-DEVICE-001: "
                + string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        }
        var plant = initialization.Plant!;
        try
        {
            return new(plant, options, ownsPlant: true);
        }
        catch
        {
            plant.Dispose();
            throw;
        }
    }

    public static VulkanInferenceSession Attach(AurelianVulkanPlant plant, VulkanInferenceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plant);
        return new(plant, options ?? new(), ownsPlant: false);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        try
        {
            Compute.Dispose();
        }
        finally
        {
            if (ownsPlant)
            {
                Plant.Dispose();
            }
        }
    }
}
