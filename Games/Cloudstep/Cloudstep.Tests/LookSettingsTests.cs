using System.Text.Json;
using System.Numerics;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;
using Xunit;

namespace Cloudstep.Tests;

public sealed class LookSettingsTests
{
    [Fact]
    public void PaletteConversionKeepsGeometryAndCachesConvertedMeshes()
    {
        var profile = new CloudstepLookProfile { DecodePaletteSrgb = true };
        var palette = new CloudstepPalette(profile);
        var vertex = new SceneVertex(new(1, 2, 3), Vector3.UnitY, new(.5f, .25f, .75f, .6f));
        var source = new PlacedSceneMesh("hero.body", Matrix4x4.CreateTranslation(4, 0, 2), [vertex])
        {
            GeometryIdentity = "test-geometry",
            Collision = SceneCollision.Solid,
        };
        var frame = new SceneFrame([], [source]);
        SceneFrame converted = palette.Apply(frame);
        SceneVertex actual = converted.Meshes[0].Vertices[0];
        Assert.Equal(vertex.Position, actual.Position);
        Assert.Equal(vertex.Normal, actual.Normal);
        Assert.InRange(actual.Color.X, .214040f, .214042f);
        Assert.InRange(actual.Color.Y, .050875f, .050877f);
        Assert.InRange(actual.Color.Z, .522520f, .522523f);
        Assert.Equal(vertex.Color.W, actual.Color.W);
        Assert.Equal(source.GeometryIdentity, converted.Meshes[0].GeometryIdentity);
        Assert.Equal(source.Collision, converted.Meshes[0].Collision);
        Assert.Equal(source.WorldTransform, converted.Meshes[0].WorldTransform);
        Assert.Equal(converted.Meshes[0].Vertices, palette.Apply(frame).Meshes[0].Vertices);
    }

    [Fact]
    public void PalettePreservesCloudAndModelMaterialColorsAndBoundsReflectance()
    {
        var palette = new CloudstepPalette(new() { DecodePaletteSrgb = true, HeroTint = new(4) });
        var cloud = new PlacedSceneBox("cloud-sea", Matrix4x4.Identity, Vector3.One, new(.6f, .7f, .8f, 1), SceneCollision.None);
        var emitter = cloud with { Id = "emitter", Material = new("lamp") { Emissive = new(5, 1, 0) } };
        var hero = cloud with { Id = "hero.body", Color = new(.9f, .9f, .9f, 1) };
        SceneFrame result = palette.Apply(new([cloud, emitter, hero], []));
        Assert.Equal(cloud.Color, result.Boxes[0].Color);
        Assert.Equal(emitter.Color, result.Boxes[1].Color);
        Assert.Same(emitter.Material, result.Boxes[1].Material);
        Assert.Equal(Vector4.One, result.Boxes[2].Color);
        Assert.Throws<InvalidDataException>(() => new CloudstepPalette(new() { GrassTint = new(float.NaN) }));
    }

    [Fact]
    public void CompleteProfileRoundTripRetainsPaletteLightingAndWorldState()
    {
        using var baseline = new CloudstepGame();
        var profile = new CloudstepLookProfile
        {
            Graphics = baseline.Manifest.Graphics with { ToneMapGamutCompression = true },
            DecodePaletteSrgb = true,
            GrassTint = new(1.3f, .7f, .6f),
        };
        var options = new JsonSerializerOptions { IncludeFields = true };
        var decoded = JsonSerializer.Deserialize<CloudstepLookProfile>(JsonSerializer.Serialize(profile, options), options)!;
        Assert.Equal(profile, decoded);
        using var game = new CloudstepGame(researchProfile: decoded);
        baseline.NewGame();
        game.NewGame();
        Assert.Equal(JsonSerializer.Serialize(baseline.World.Capture(), CloudstepJsonContext.Default.CloudstepSnapshot),
            JsonSerializer.Serialize(game.World.Capture(), CloudstepJsonContext.Default.CloudstepSnapshot));
        Assert.Equal(baseline.View(new(960, 540))!.Camera, game.View(new(960, 540))!.Camera);
    }

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
        Assert.Equal(DirectDiffuseModel3D.PathDiffuse, baseline.Manifest.Graphics.DirectDiffuseModel);
        Assert.Equal(0, baseline.Manifest.Graphics.ToneMapColorRetention);
    }
}
