using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class SurfaceLightingContractsTests
{
    [Fact]
    public void LightAndProbeContractsRejectInvalidDomains()
    {
        var point = new LocalLight3D(Vector3.Zero, Vector3.One, 10, 4);
        point.Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => (point with { Range = float.NaN }).Validate());
        Assert.Throws<NotSupportedException>(() => (point with { CastShadows = true }).Validate());
        (point with { Kind = LocalLightKind.Spot, CastShadows = true }).Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReflectionProbe3D(Vector3.Zero, Vector3.Zero).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Graphics3DSettings.Default with { LocalShadowBudget = 3 }).Validate());
    }
}
