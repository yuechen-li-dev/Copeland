using Aurelian.Graphics;
using Aurelian.Graphics.Vulkan;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class AurelianGraphicsProjectTests
{
    [Fact]
    public void AurelianGraphicsProject_Name_IsAurelianGraphics()
    {
        Assert.Equal("Aurelian.Graphics", AurelianGraphicsProject.Name);
    }

    [Fact]
    public void VulkanPackageSmoke_ExposesSilkTypes()
    {
        Assert.Equal("Vk", VulkanPackageSmoke.VulkanApiName);
        Assert.Equal("WindowOptions", VulkanPackageSmoke.WindowOptionsName);
    }

    [Fact]
    public void AurelianGraphics_DoesNotRequireWorldAssetsShadersOrNullRenderer()
    {
        string[] forbiddenReferences =
        [
            "Aurelian." + "World",
            "Aurelian." + "Assets",
            "Aurelian." + "Shaders",
            "Aurelian.Rendering." + "Null",
        ];

        var referencedAssemblyNames = typeof(AurelianGraphicsProject)
            .Assembly
            .GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name)
            .ToArray();

        foreach (string forbiddenReference in forbiddenReferences)
        {
            Assert.DoesNotContain(forbiddenReference, referencedAssemblyNames);
        }
    }
}
