using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Assets.Models;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;
using Xunit;

namespace Aurelian.Games.Tests;

public sealed class ModelSceneTests
{
    private static StaticModel Load(string filename) => GlbModelImporter.Load("crate",
        Path.Combine(AppContext.BaseDirectory, "Assets", filename)).Model!;

    [Fact]
    public void MovingShadowCastersChangeLightingRevisionAndPreserveVertexCorrespondence()
    {
        SceneBox box = Scene.Box("caster", Vector3.One, Vector4.One);
        using var firstScene = SceneCompiler.Compile(Scene.World("room", [box])).Mount();
        using var secondScene = SceneCompiler.Compile(Scene.World("room", [box with
        {
            Transform = SceneTransform.At(Vector3.UnitX),
        }])).Mount();
        var first = SceneGeometry3D.BuildScene(firstScene.Project());
        var second = SceneGeometry3D.BuildScene(secondScene.Project());
        Assert.Equal(first.TemporalRevision, second.TemporalRevision);
        Assert.NotEqual(first.LightingRevision, second.LightingRevision);
    }

    [Fact]
    public void AuthoredOpticalPropertiesParticipateInSceneContentIdentity()
    {
        var glass = new ModelMaterial("glass") { Metallic = 0, Transmission = 1, Thickness = .1f };
        SceneBox pane = Scene.Box("pane", Vector3.One, Vector4.One) with { Material = glass };
        string initial = SceneCompiler.Compile(Scene.World("room", [pane])).ContentIdentity;
        ModelMaterial[] changes = [glass with { Thickness = .2f }, glass with { Transmission = .8f },
            glass with { IndexOfRefraction = 1.3f }, glass with { AttenuationColor = new(.4f), AttenuationDistance = 1 }];
        foreach (ModelMaterial changed in changes)
        {
            string identity = SceneCompiler.Compile(Scene.World("room", [pane with { Material = changed }])).ContentIdentity;
            Assert.NotEqual(initial, identity);
        }
    }

    [Fact]
    public void PrimitiveMaterialUsesTheSharedModelPathAndParticipatesInSceneIdentity()
    {
        var material = new ModelMaterial("lamp") { Emissive = new(8, 3, 1), Metallic = 0 };
        SceneBox box = Scene.Box("lamp", Vector3.One, Vector4.One, collision: SceneCollision.Solid) with { Material = material };
        ScenePlan plan = SceneCompiler.Compile(Scene.World("room", [box]));
        using var scene = plan.Mount();
        var frame = SceneGeometry3D.BuildScene(scene.Project());
        Assert.Empty(frame.Geometry);
        Assert.Single(frame.Models);
        Assert.Same(material, frame.Models[0].Material);
        Assert.Equal(36, frame.Models[0].Vertices.Length);
        Assert.Equal(SceneCollision.Solid, scene.Project().Boxes[0].Collision);
        var changed = SceneCompiler.Compile(Scene.World("room", [box with { Material = material with { Emissive = new(9, 3, 1) } }]));
        Assert.NotEqual(plan.ContentIdentity, changed.ContentIdentity);
        Assert.NotEqual(plan.ContentIdentity, SceneCompiler.Compile(Scene.World("room", [box with { Material = null }])).ContentIdentity);
    }

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
