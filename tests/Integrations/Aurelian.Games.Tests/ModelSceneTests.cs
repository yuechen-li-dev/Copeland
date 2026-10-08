using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Assets.Models;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;
using Xunit;

namespace Aurelian.Games.Tests;

public sealed class ModelSceneTests
{
    private static StaticModel Load(string filename) => GlbModelImporter.Load("crate",
        Path.Combine(AppContext.BaseDirectory, "Assets", filename)).Model!;

    [Fact]
    public void ModelInstancesShareAssetDefinitionsAndKeepIndependentMaterialOverrides()
    {
        var slot = new ModelSlot(Load("crate.glb"));
        ModelMaterial panel = slot.Current.Occurrences[0].Primitive.Material;
        var document = StarterScenes.TrainingRange([]);
        document = document with
        {
            Children = document.Children.AddRange(new SceneNode[] {
            Scene.Model("original", slot, new(-2, 0, -4)),
            Scene.Model("customized", slot, new(2, 0, -4)) with {
                Materials = ImmutableDictionary<string, ModelMaterial>.Empty.Add("panel", panel with { BaseColor = new(0, 1, 0, 1) }) },
        })
        };
        using var game = GameStarter.Create("model-test", GamePresets.FirstPersonShooter, sceneDocument: document);
        var frame = game.BuildRenderScene();
        Assert.Equal(4, frame.Models.Count);
        Assert.Same(slot, game.Scene.Plan.Models[0].Asset);
        Assert.Same(slot, game.Scene.Plan.Models[1].Asset);
        Assert.Equal(panel.BaseColor, frame.Models[0].Material.BaseColor);
        Assert.Equal(new Vector4(0, 1, 0, 1), frame.Models[2].Material.BaseColor);
        Assert.Equal(4, frame.Models[2].Vertices[0].Position.X - frame.Models[0].Vertices[0].Position.X, 5);
        Assert.Equal(frame.Models[0].Vertices[0].Uv, frame.Models[2].Vertices[0].Uv);
        Assert.Single(game.SpatialWorld.Colliders);
        Assert.Equal("floor", game.SpatialWorld.Colliders[0].Id);
        var player = game.Scene.Agent<Vector3>("player");
        string identity = game.Scene.Plan.ContentIdentity;
        slot.Replace(Load("crate-replacement.glb"));
        Assert.Same(player, game.Scene.Agent<Vector3>("player"));
        Assert.Equal(identity, game.Scene.Plan.ContentIdentity);
        Assert.Equal(new Vector4(0, 1, 0, 1), game.BuildRenderScene().Models[2].Material.BaseColor);
        Assert.NotEqual(frame.Models[0].Vertices[0].Position, game.BuildRenderScene().Models[0].Vertices[0].Position);
    }

    [Fact]
    public void MissingOverrideSlotAndInvalidMaterialFailBeforeAgentsMount()
    {
        var slot = new ModelSlot(Load("crate.glb"));
        var missing = Scene.Model("model", slot) with { Materials = ImmutableDictionary<string, ModelMaterial>.Empty.Add("missing", new("missing")) };
        Assert.Contains("unknown", Assert.Throws<InvalidDataException>(() => SceneCompiler.Compile(Scene.World("test", [missing]))).Message);
        var invalid = missing with { Materials = ImmutableDictionary<string, ModelMaterial>.Empty.Add("panel", new("panel") { Roughness = float.NaN }) };
        Assert.Contains("Invalid material", Assert.Throws<InvalidDataException>(() => SceneCompiler.Compile(Scene.World("test", [invalid]))).Message);
    }

    [Fact]
    public void MaterialContentAndInstanceTransformsParticipateInInitialSceneIdentity()
    {
        var slot = new ModelSlot(Load("crate.glb"));
        var node = Scene.Model("crate", slot);
        string first = SceneCompiler.Compile(Scene.World("test", [node])).ContentIdentity;
        Assert.NotEqual(first, SceneCompiler.Compile(Scene.World("test", [node with { Transform = SceneTransform.At(Vector3.UnitX) }])).ContentIdentity);
        var changed = node with { Materials = ImmutableDictionary<string, ModelMaterial>.Empty.Add("panel", new("panel") { Metallic = 0 }) };
        Assert.NotEqual(first, SceneCompiler.Compile(Scene.World("test", [changed])).ContentIdentity);
    }
}
