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
}
