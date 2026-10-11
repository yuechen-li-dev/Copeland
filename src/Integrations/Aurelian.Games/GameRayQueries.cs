using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Spatial3D;
using Aurelian.Spatial3D.Vulkan;

namespace Aurelian.Games;

public static class GameRayQueries
{
    public static VulkanSpatialRayQueries3D Create(AurelianVulkanPlant plant, SpatialWorld3D world)
    {
        var assets = new GameAssets();
        return new(plant, assets.ComputeShader("RayQuery.v.ts"), world);
    }
}
