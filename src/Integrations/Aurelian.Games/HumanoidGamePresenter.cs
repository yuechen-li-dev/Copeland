using System.Numerics;
using Aetheris.Humanoid;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Humanoid;

namespace Aurelian.Games;

/// <summary>A retained GPU body used by any game's authored character pose.</summary>
public sealed class HumanoidGamePresenter : IDisposable
{
    private readonly HumanoidGpuBody body;
    public int DispatchCount => body.DispatchCount;

    public HumanoidGamePresenter(AurelianVulkanPlant plant, HumanoidPlayerOptions options, GameAssets? assets = null)
    {
        options.Validate();
        assets ??= new();
        body = new(plant, assets.ComputeShader("HumanoidSkinning.v.ts"), options.Body);
    }

    public NativeGpuGeometry3D? Present(SolvedHumanoidPose pose, Matrix4x4 world, bool visible)
    {
        if (!visible)
        {
            return null;
        }
        body.Present(pose, world);
        return body.Geometry;
    }

    public void Dispose() => body.Dispose();
}
