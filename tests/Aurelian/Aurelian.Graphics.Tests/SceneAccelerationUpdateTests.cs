using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.RayQueries;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class SceneAccelerationUpdateTests
{
    [Fact]
    public void OnlyTheActualMotionPredecessorCanUseAnUpdate()
    {
        var mesh = NativeAnalyticPrimitives3D.Create("sphere", AnalyticPrimitiveKind3D.UnitSphere);
        var material = new ModelMaterial("material");
        SceneInstance3D instance = new("instance", mesh, material, Matrix4x4.Identity);
        var original = new NativeInstanceSet3D([instance]);
        var equivalent = new NativeInstanceSet3D([instance]);
        var moved = new NativeInstanceSet3D([instance with { Transform = Matrix4x4.CreateTranslation(1, 0, 0) }], original);

        Assert.True(VulkanSceneAcceleration3D.CanUpdate(original, moved, SceneAccelerationUpdateMode3D.Automatic));
        Assert.False(VulkanSceneAcceleration3D.CanUpdate(equivalent, moved, SceneAccelerationUpdateMode3D.Automatic));
        Assert.False(VulkanSceneAcceleration3D.CanUpdate(null, moved, SceneAccelerationUpdateMode3D.Automatic));
        Assert.False(VulkanSceneAcceleration3D.CanUpdate(original, equivalent, SceneAccelerationUpdateMode3D.Automatic));
    }

    [Fact]
    public void ForcedRebuildAndUnrelatedCompositionNeverUseAnUpdate()
    {
        var mesh = NativeAnalyticPrimitives3D.Create("sphere", AnalyticPrimitiveKind3D.UnitSphere);
        var material = new ModelMaterial("material");
        SceneInstance3D instance = new("instance", mesh, material, Matrix4x4.Identity);
        var original = new NativeInstanceSet3D([instance]);
        var moved = new NativeInstanceSet3D([instance with { RayMask = 0 }], original);
        var added = new NativeInstanceSet3D([instance, instance with { Identity = "another" }]);

        Assert.True(VulkanSceneAcceleration3D.CanUpdate(original, moved, SceneAccelerationUpdateMode3D.Automatic));
        Assert.False(VulkanSceneAcceleration3D.CanUpdate(original, moved, SceneAccelerationUpdateMode3D.Rebuild));
        Assert.False(VulkanSceneAcceleration3D.CanUpdate(original, added, SceneAccelerationUpdateMode3D.Automatic));
        Assert.False(VulkanSceneAcceleration3D.CanUpdate(original, moved, (SceneAccelerationUpdateMode3D)999));
    }
}
