using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Audio;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;
using Xunit;

namespace Aurelian.Integration.Tests;

public sealed class WeatherCooperationTests
{
    [Fact]
    public void QueriesUseTheSameTriangleAndWetnessAsTheRenderedGround()
    {
        var values = new[] { new WetGroundSample3D(.1f, 0, 0), new(.1f, 1, 1), new(.1f, .2f, .2f), new(-.1f, .8f, .8f) };
        var field = new WetGroundField3D(Vector2.Zero, Vector2.One, 2, 2, 0, values);
        values[0] = new(100, 1, 1);
        var (ground, water) = WetGroundMesh3D.Create("ground", field, new("soil") { Metallic = 0 }, new());
        var primitive = ground.Current.Occurrences.Single().Primitive;
        Assert.True(primitive.Material.VertexWetness);
        // First triangle is (0,0), (0,1), (1,1). Query barycentric weights are .25,.5,.25.
        Vector2 point = new(.25f, .75f);
        var sample = field.Sample(point);
        Assert.Equal(.25f * primitive.Vertices[0].Color.W + .5f * primitive.Vertices[1].Color.W
            + .25f * primitive.Vertices[2].Color.W, sample.Wetness, 5);
        Assert.Equal(.05f, sample.Height, 5);
        var other = field.Sample(new(.75f, .25f));
        Assert.Equal(.25f * primitive.Vertices[3].Color.W + .25f * primitive.Vertices[4].Color.W
            + .5f * primitive.Vertices[5].Color.W, other.Wetness, 5);
        Assert.Equal(.05f, other.Height, 5);
        Assert.NotNull(water);
        var waterPrimitive = water.Current.Occurrences.Single().Primitive;
        Assert.All(waterPrimitive.Vertices, vertex =>
        {
            Assert.Equal(0, vertex.Position.Y);
            Assert.True(field.Sample(new(vertex.Position.X, vertex.Position.Z)).WaterDepth(field.WaterLevel) >= .00099f);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() => field.Sample(new(-.01f, 0)));
        Assert.Throws<ArgumentException>(() => new WetGroundField3D(Vector2.Zero, Vector2.One, 2, 2, 0, []));
    }

    [Fact]
    public void ShelterAndStandingWaterInfluenceTheSameAuthoredSample()
    {
        var roof = new RainShelter3D(new(-2, -1, -2), new(2, 3, 2));
        Assert.Equal(1, roof.ShelterAt(Vector3.Zero));
        Assert.Equal(0, roof.ShelterAt(new(3, 0, 0)));
        Assert.Equal(0, roof.ShelterAt(new(0, 4, 0)));
        Assert.InRange(roof.ShelterAt(new(1.75f, 0, 0)), .49f, .51f);
        var sheltered = WetGroundSample3D.FromHeight(.2f, 0, 1, 1 - roof.ShelterAt(Vector3.Zero));
        var exposed = WetGroundSample3D.FromHeight(.2f, 0, 1, 1);
        var puddle = WetGroundSample3D.FromHeight(-.02f, 0, 0, 0);
        Assert.Equal(0, sheltered.Wetness);
        Assert.Equal(1, exposed.Wetness);
        Assert.Equal(1, puddle.Wetness);
        Assert.Equal(.02f, puddle.WaterDepth(0));
    }

    [Fact]
    public void AmbienceBlendsThroughTheMixerPausesAndReleasesItsVoices()
    {
        using var resources = new AudioResourceScope();
        var outside = resources.Add(new ProceduralSoundRecipe(ProceduralSoundKind.Rain).Prepare(new("outside")));
        var inside = resources.Add(new ProceduralSoundRecipe(ProceduralSoundKind.ShelteredRain).Prepare(new("inside")));
        using var backend = new NullAudioOutputBackend();
        using var mixer = new AurelianAudioRuntime(resources, backend);
        var ambience = new EnvironmentalAmbience(mixer, "weather", outside.Id, inside.Id);
        ambience.Update(TimeSpan.FromSeconds(2), 1, 0);
        mixer.Update(TimeSpan.FromSeconds(.1));
        var outdoors = ambience.State;
        Assert.True(outdoors.ExteriorGain > .99f);
        ambience.Update(TimeSpan.FromSeconds(2), 1, 1);
        mixer.Update(TimeSpan.FromSeconds(.1));
        Assert.True(ambience.State.ExteriorGain < .1f);
        Assert.True(ambience.State.InteriorGain > .34f);
        var beforePause = mixer.Inspect().ActiveVoices.Select(voice => voice.Age).ToArray();
        ambience.Update(TimeSpan.FromSeconds(1), 0, 0, paused: true);
        mixer.Update(TimeSpan.FromSeconds(.1));
        Assert.Equal(beforePause, mixer.Inspect().ActiveVoices.Select(voice => voice.Age));
        Assert.Throws<ArgumentOutOfRangeException>(() => ambience.Update(TimeSpan.Zero, float.NaN, 0));
        ambience.Dispose();
        Assert.Empty(mixer.Inspect().ActiveVoices);
        Assert.Equal(2, mixer.DrainCompletions().Count);
    }

    [Fact]
    public void WetnessAndSpecularFlagsComposeWithoutChangingTheMaterialAbi()
    {
        var material = new ModelMaterial("wet") { VertexWetness = true, SpecularAntiAliasing = true };
        Assert.Equal(3, material.PackedSurfaceFlags);
        Assert.Throws<InvalidDataException>(() => (material with { AlphaMask = true }).Validate());
        Assert.Throws<InvalidDataException>(() => (material with { BaseColor = new(1, 1, 1, .5f) }).Validate());
        Assert.Throws<InvalidDataException>(() => (material with
        {
            BaseColorTexture = new(new("transparent", 1, 1, ImmutableArray.Create<byte>(255, 255, 255, 127)), new()),
        }).Validate());
        Assert.Throws<InvalidDataException>(() => (material with
        {
            BaseColorTexture = new(new("invalid", 1, 1, default), new()),
        }).Validate());
    }

    [Fact]
    public void DryFieldsHaveNoWaterAndInvalidOrUnrepresentableGridsAreRejected()
    {
        WetGroundSample3D[] values = Enumerable.Repeat(new WetGroundSample3D(.2f, 0, 1), 4).ToArray();
        var field = new WetGroundField3D(Vector2.Zero, Vector2.One, 2, 2, 0, values);
        var mesh = WetGroundMesh3D.Create("dry", field, new("stone"), new());
        Assert.Null(mesh.Water);
        Assert.Equal(.2f, field.Sample(Vector2.One).Height);
        Assert.Throws<ArgumentOutOfRangeException>(() => new WetGroundField3D(Vector2.Zero, Vector2.One, 1, 2, 0, values));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WetGroundField3D(new(1e30f), Vector2.One, 2, 2, 0, values));
        values[0] = new(float.NaN, 0, 1);
        Assert.Throws<ArgumentException>(() => new WetGroundField3D(Vector2.Zero, Vector2.One, 2, 2, 0, values));
    }

    [Fact]
    public void EnvironmentalBlendIsIndependentOfFrameSubdivision()
    {
        using var resources = new AudioResourceScope();
        resources.Add(new ProceduralSoundRecipe(ProceduralSoundKind.Rain).Prepare(new("rain")));
        using var firstMixer = new AurelianAudioRuntime(resources, new NullAudioOutputBackend());
        using var secondMixer = new AurelianAudioRuntime(resources, new NullAudioOutputBackend());
        using var first = new EnvironmentalAmbience(firstMixer, "first", new("rain"), new("rain"));
        using var second = new EnvironmentalAmbience(secondMixer, "second", new("rain"), new("rain"));
        first.Update(TimeSpan.FromSeconds(1), .7f, .6f);
        for (int frame = 0; frame < 60; frame++)
        {
            second.Update(TimeSpan.FromSeconds(1d / 60), .7f, .6f);
        }
        Assert.Equal(first.State.ExteriorGain, second.State.ExteriorGain, 5);
        Assert.Equal(first.State.InteriorGain, second.State.InteriorGain, 5);
    }
}
