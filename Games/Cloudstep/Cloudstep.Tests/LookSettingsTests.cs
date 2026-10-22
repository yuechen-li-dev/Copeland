using System.Text.Json;
using Aurelian.Rendering.Contracts.Models;
using Xunit;

namespace Cloudstep.Tests;

public sealed class LookSettingsTests
{
    [Fact]
    public void ResearchSettingsRoundTripPreservesVectorsAndDoesNotAlterTheWorld()
    {
        using var baseline = new CloudstepGame();
        var selected = baseline.Manifest.Graphics with
        {
            DirectDiffuseModel = DirectDiffuseModel3D.PathDiffuse,
            ToneMapColorRetention = .7f,
        };
        var options = new JsonSerializerOptions { IncludeFields = true };
        string json = JsonSerializer.Serialize(selected, options);
        var restored = JsonSerializer.Deserialize<Graphics3DSettings>(json, options)!;
        restored.Validate();
        Assert.Equal(selected, restored);
        using var experiment = new CloudstepGame(researchLook: restored);
        baseline.NewGame();
        experiment.NewGame();
        Assert.Equal(selected, experiment.Manifest.Graphics);
        Assert.Equal(JsonSerializer.Serialize(baseline.World.Capture(), CloudstepJsonContext.Default.CloudstepSnapshot),
            JsonSerializer.Serialize(experiment.World.Capture(), CloudstepJsonContext.Default.CloudstepSnapshot));
        Assert.Equal(DirectDiffuseModel3D.Lambert, baseline.Manifest.Graphics.DirectDiffuseModel);
        Assert.Equal(0, baseline.Manifest.Graphics.ToneMapColorRetention);
    }
}
