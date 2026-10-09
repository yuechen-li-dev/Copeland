using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class Graphics3DSettingsTests
{
    [Fact]
    public void SettingsRejectNonfiniteOrDegenerateLightingBeforeRendering()
    {
        Graphics3DSettings.Default.Validate();
        Graphics3DSettings.Basic.Validate();
        Graphics3DSettings[] invalid =
        [
            Graphics3DSettings.Default with { SunDirection = Vector3.Zero },
            Graphics3DSettings.Default with { SunDirection = new(float.MaxValue, 1, 1) },
            Graphics3DSettings.Default with { SkyAmbient = new(float.NaN, 0, 0) },
            Graphics3DSettings.Default with { SunIntensity = -1 },
            Graphics3DSettings.Default with { Exposure = 0 },
            Graphics3DSettings.Default with { ShadowRadius = float.PositiveInfinity },
            Graphics3DSettings.Default with { ShadowBias = -.1f },
            Graphics3DSettings.Default with { SolidRoughness = 0 },
        ];
        foreach (var settings in invalid)
        {
            Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
        }
    }
}
