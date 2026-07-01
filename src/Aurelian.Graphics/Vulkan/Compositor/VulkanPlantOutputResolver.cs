using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.Rendering.Contracts.Compositor;

namespace Aurelian.Graphics.Vulkan.Compositor;

public static class VulkanPlantOutputResolver
{
    public static VulkanPlantOutputResolutionResult Resolve(
        VulkanPlantOutputImageSet? imageSet,
        PlantOutputRef outputRef)
    {
        if (imageSet is null)
        {
            return Rejected([
                Diagnostic(
                    VulkanPlantOutputDiagnosticCodes.ImageSetMissing,
                    "Cannot resolve a plant output without a plant output image set.",
                    null,
                    outputRef.ImageId),
            ]);
        }

        if (!imageSet.TryGet(outputRef, out VulkanPlantOutputImage output))
        {
            if (imageSet.TryGetByFrameAndImage(outputRef, out VulkanPlantOutputImage plantMismatch))
            {
                return Rejected([
                    Diagnostic(
                        VulkanPlantOutputDiagnosticCodes.PlantMismatch,
                        $"Plant output ref plant {outputRef.PlantId} does not match resolved output plant {plantMismatch.PlantId.Value}.",
                        plantMismatch.PlantId,
                        outputRef.ImageId),
                ]);
            }

            return Rejected([
                Diagnostic(
                    VulkanPlantOutputDiagnosticCodes.OutputMissing,
                    $"Plant output '{outputRef}' was not found in the Vulkan plant output image set.",
                    null,
                    outputRef.ImageId),
            ]);
        }

        if (output.PlantId.Value != outputRef.PlantId)
        {
            return Rejected([
                Diagnostic(
                    VulkanPlantOutputDiagnosticCodes.PlantMismatch,
                    $"Plant output ref plant {outputRef.PlantId} does not match resolved output plant {output.PlantId.Value}.",
                    output.PlantId,
                    outputRef.ImageId),
            ]);
        }

        if (output.Texture.IsDisposed)
        {
            return Rejected([
                Diagnostic(
                    VulkanPlantOutputDiagnosticCodes.TextureDisposed,
                    $"Plant output '{outputRef}' references a disposed Vulkan texture.",
                    output.PlantId,
                    outputRef.ImageId),
            ]);
        }

        if ((output.Texture.Usage & VulkanTextureUsage.TransferSource) == 0)
        {
            return Rejected([
                Diagnostic(
                    VulkanPlantOutputDiagnosticCodes.TextureMissingTransferSourceUsage,
                    $"Plant output '{outputRef}' texture is missing TransferSource usage.",
                    output.PlantId,
                    outputRef.ImageId),
            ]);
        }

        return new VulkanPlantOutputResolutionResult(VulkanPlantOutputStatus.Resolved, output, []);
    }

    private static VulkanPlantOutputResolutionResult Rejected(IReadOnlyList<VulkanPlantOutputDiagnostic> diagnostics)
        => new(VulkanPlantOutputStatus.Rejected, null, diagnostics);

    private static VulkanPlantOutputDiagnostic Diagnostic(string code, string message, Aurelian.Graphics.Plants.PlantId? plantId, string? imageId)
        => new(code, VulkanPlantOutputDiagnosticSeverity.Error, message, plantId, imageId);
}
