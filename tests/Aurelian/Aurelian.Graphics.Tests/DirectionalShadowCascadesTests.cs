using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class DirectionalShadowCascadesTests
{
    [Fact]
    public void LongAndInfiniteProjectionsDoNotImposeAnArtificialOneKilometreCap()
    {
        var settings = Graphics3DSettings.Default with { ShadowDistance = 5000 };
        foreach (float far in new[] { 10000f, float.PositiveInfinity })
        {
            Matrix4x4 camera = Matrix4x4.CreatePerspectiveFieldOfView(1, 1.5f, .1f, far);
            camera.M22 *= -1;
            var cascades = DirectionalShadowCascades3D.Create(settings, camera, Vector3.Zero);
            Assert.Equal(5000, cascades.Ends.Z, 2);
        }
    }

    [Fact]
    public void CoverageStopsAtTheActualFiniteCameraFarPlane()
    {
        Matrix4x4 camera = Matrix4x4.CreatePerspectiveFieldOfView(1, 1.5f, .1f, 20);
        camera.M22 *= -1;
        var cascades = DirectionalShadowCascades3D.Create(Graphics3DSettings.Default, camera, Vector3.Zero);
        Assert.InRange(cascades.Ends.Z, 19.99f, 20.01f);
    }

    [Fact]
    public void PracticalSplitsConcentrateResolutionNearTheCamera()
    {
        Matrix4x4 camera = Matrix4x4.CreatePerspectiveFieldOfView(1, 1.5f, .1f, 500);
        camera.M22 *= -1;
        var cascades = DirectionalShadowCascades3D.Create(Graphics3DSettings.Default, camera, Vector3.Zero);
        // The default 100 m distance used to put the first split at 16 m; the log/uniform blend puts it near 9 m.
        Assert.InRange(cascades.Ends.X, 8.5f, 9.5f);
        Assert.True(cascades.Ends.X < cascades.Ends.Y && cascades.Ends.Y < cascades.Ends.Z);
        Assert.Equal(100, cascades.Ends.Z, 2);
    }

    [Theory]
    [InlineData(.1f, 100f)]
    [InlineData(.5f, 40f)]
    [InlineData(5f, 6f)]
    [InlineData(50f, 20f)]
    public void PracticalSplitsIncreaseAndEndAtTheShadowDistance(float near, float far)
    {
        float previous = 0;
        for (int index = 1; index <= 3; index++)
        {
            float split = DirectionalShadowCascades3D.PracticalSplit(near, far, index);
            Assert.True(float.IsFinite(split) && split > previous && split <= far + .001f);
            previous = split;
        }
        Assert.Equal(far, DirectionalShadowCascades3D.PracticalSplit(near, far, 3), 3);
    }
}
