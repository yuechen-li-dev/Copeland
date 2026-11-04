using System.Numerics;
using Aurelian.Rendering.Contracts.Lighting;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class DynamicDiffuseLightingTests
{
    private static readonly DiffuseProbeGrid Grid = new(Vector3.Zero, Vector3.One, 2, 2, 2, 16);

    [Fact]
    public void DirectionalBudgetAndFixedConvolutionShapeAreAdmittedExplicitly()
    {
        new DynamicDiffuseLighting3D().Validate();
        var settings = new DynamicDiffuseLighting3D { Mode = DynamicDiffuseMode3D.Runtime, Grid = Grid };
        settings.Validate();
        Assert.Throws<ArgumentException>((settings with { Grid = null }).Validate);
        Assert.Throws<ArgumentException>((settings with { RaysPerProbePerFrame = 17 }).Validate);
        Assert.Throws<ArgumentException>((settings with { Grid = Grid with { SurfaceResolution = 4 } }).Validate);
        Assert.Throws<ArgumentOutOfRangeException>((settings with { Intensity = float.NaN }).Validate);
    }

    [Fact]
    public void CompiledFieldsPreserveSignedCorrectionsAndRejectInvalidPayloads()
    {
        float[] data = new float[Grid.Count * (Grid.RaysPerProbe + 64) * 4];
        data[0] = -.5f;
        var field = new CompiledDiffuseProbeField3D(1, Grid, data);
        field.Validate();
        Assert.Throws<InvalidDataException>((field with { Version = 2 }).Validate);
        Assert.Throws<InvalidDataException>((field with { Data = [] }).Validate);
        data[0] = float.PositiveInfinity;
        Assert.Throws<InvalidDataException>(field.Validate);
    }
}
