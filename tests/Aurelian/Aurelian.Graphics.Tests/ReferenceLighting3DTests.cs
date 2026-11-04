using System.Numerics;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Graphics.Tests;

public sealed class ReferenceLighting3DTests
{
    [Fact]
    public void ReferenceUsesTheOrdinarySunAndEnvironmentCoefficients()
    {
        var settings = Graphics3DSettings.Default with { SunColor = new(.8f, .6f, .4f), SunIntensity = 4, EnvironmentIntensity = .7f };
        var source = new EnvironmentRadiance3D(1, 1, [1, 2, 3, 1]);
        ReferenceLighting3D lighting = ReferenceLighting3D.FromSettings(settings, source);
        Assert.Equal(settings.SunDirection, lighting.Sun!.Direction);
        Assert.Equal(new Vector3(3.2f, 2.4f, 1.6f), lighting.Sun.Irradiance);
        Assert.Same(source, lighting.Environment);
        Assert.Equal(.7f, lighting.EnvironmentIntensity);
    }
}
