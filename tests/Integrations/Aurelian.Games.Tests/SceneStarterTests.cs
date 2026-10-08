using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.NativeComposition;
using Aurelian.World.Scenes;
using InputMan.Core;
using Xunit;

namespace Aurelian.Games.Tests;

public sealed class SceneStarterTests
{
    [Fact]
    public async Task ReusedTargetsKeepIndependentHealthAndRoundtripThroughDeliverance()
    {
        string directory = Path.Combine(Path.GetTempPath(), "aurelian-scene-tests", Guid.NewGuid().ToString("N"));
        using var first = GameStarter.Create("scene-test", GamePresets.FirstPersonShooter,
            saveDirectory: directory, sceneDocument: Document());
        first.Activate("start");
        first.Controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), true);
        first.Advance(TimeSpan.FromTicks(166667));
        first.Scene.Agent<int>("west.target").State = 1;
        Assert.Equal(8, first.Scene.Agent<int>("captain").State);
        Assert.Equal(3, first.Scene.Agent<int>("east.target").State);
        StarterSnapshot saved = first.Capture();
        await first.SaveAsync("composed");

        using var second = GameStarter.Create("scene-test", GamePresets.FirstPersonShooter,
            saveDirectory: directory, sceneDocument: Document(reverseAgents: true));
        Assert.Equal(first.Identity, second.Identity);
        await second.LoadAsync("composed");
        Assert.Equal(1, second.Scene.Agent<int>("west.target").State);
        Assert.Equal(3, second.Scene.Agent<int>("east.target").State);
        Assert.Equal(8, second.Scene.Agent<int>("captain").State);
        Assert.Equal(Json(saved), Json(second.Capture()));
        Assert.Equal(new[] { "captain", "east.target", "west.target" }, second.Agents.Select(agent => agent.Id));
        Assert.Contains(second.Scene.Project().Boxes, box => box.Id == "west.target.shape"
            && box.WorldTransform.Translation == new Vector3(-3, 1, -4));
        Assert.NotEmpty(second.BuildScene());
    }

    [Fact]
    public void SceneCollisionChangesMovementWithoutDependingOnVisualGeometry()
    {
        var document = Document();
        document = document with
        {
            Children = document.Children.Add(Scene.Box("wall", new(4, 2, 1), Vector4.One,
                at: new(0, 1, 6), collision: SceneCollision.Solid)),
        };
        using var game = GameStarter.Create("scene-test", GamePresets.FirstPersonShooter, sceneDocument: document);
        game.Activate("start");
        game.Controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), true);
        for (int tick = 0; tick < 60; tick++)
        {
            game.Advance(TimeSpan.FromTicks(166667));
        }
        Assert.InRange(game.Capture().Position.Z, 6.8f, 7);
    }

    [Fact]
    public void RotatedSolidBoxesUseTheirAuthoredFrameForQueries()
    {
        var document = Document();
        var wall = Scene.Box("wall", Vector3.One, Vector4.One, collision: SceneCollision.Solid) with
        {
            Transform = new(new(4, 1, 5), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f), Vector3.One),
        };
        using var solid = GameStarter.Create("scene-test", GamePresets.FirstPersonShooter,
            sceneDocument: document with { Children = document.Children.Add(wall) });
        Assert.Equal("wall", solid.SpatialWorld.Raycast(new(new(4, 1, 8), -Vector3.UnitZ, 10))!.Value.ColliderId);
        using var decorative = GameStarter.Create("scene-test", GamePresets.FirstPersonShooter,
            sceneDocument: document with { Children = document.Children.Add(wall with { Collision = SceneCollision.None }) });
        Assert.NotEmpty(decorative.BuildScene());
    }

    [Fact]
    public void SolidSceneGeometryStopsHitscanBeforeTheTarget()
    {
        var document = Document();
        document = document with
        {
            Children = document.Children.Add(Scene.Box("wall", new(4, 3, 1), Vector4.One,
                at: new(0, 1.5f, 3), collision: SceneCollision.Solid)),
        };
        using var game = GameStarter.Create("scene-test", GamePresets.FirstPersonShooter, sceneDocument: document);
        game.Activate("start");
        game.Controls.Adapter.RecordButton(Controls.Mouse(MouseButton.Primary), true);
        game.Advance(TimeSpan.FromTicks(166667));
        Assert.Equal(1, game.Observe().Shots);
        Assert.Equal(0, game.Observe().Hits);
        Assert.Equal(9, game.Scene.Agent<int>("captain").State);
    }

    [Fact]
    public void NativeProjectionTransformsPositionsAndNormalsThroughNestedNonuniformScale()
    {
        Vector3 normal = Vector3.Normalize(new Vector3(1, 1, 1));
        var mesh = Scene.Mesh("triangle",
            [new(Vector3.Zero, normal, Vector4.One), new(Vector3.UnitX, normal, Vector4.One),
             new(Vector3.UnitY, normal, Vector4.One)]) with
        {
            Transform = new(new(1, 2, 3), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f), new(2, 3, 4)),
        };
        var parent = Scene.Group("group", [mesh]) with
        {
            Transform = new(new(4, 0, 5), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f), new(3, 2, 1)),
        };
        using var scene = SceneCompiler.Compile(Scene.World("world", [parent])).Mount();
        var vertices = SceneGeometry3D.Build(scene.Project());
        Matrix4x4 matrix = mesh.Transform.Matrix() * parent.Transform.Matrix();
        Matrix4x4.Invert(matrix, out Matrix4x4 inverse);
        Assert.Equal(3, vertices.Length);
        Assert.Equal(Vector3.Transform(Vector3.UnitX, matrix), vertices[1].Position);
        Vector3 expected = Vector3.Normalize(Vector3.TransformNormal(normal, Matrix4x4.Transpose(inverse)));
        Assert.True(Vector3.Distance(expected, vertices[1].Normal) < 0.00001f);
    }

    private static SceneGroup Document(bool reverseAgents = false)
    {
        var target = new StarterTargetDefinition(3, new(0.8f, 1, 0.8f));
        var fragment = Scene.Group("post", [Scene.Agent("target", target, at: new(0, 1, 0))]);
        SceneNode[] actors =
        [
            Scene.Agent("player", new StarterPlayerDefinition(), at: new(0, 0, 7)),
            Scene.Instance("west", fragment, at: new(-3, 0, -4)),
            Scene.Instance("east", fragment, at: new(3, 0, -4)),
            Scene.Agent("captain", target with { Health = 9 }, at: new(0, 1, -6)),
        ];
        if (reverseAgents)
        {
            Array.Reverse(actors);
        }
        return Scene.World("arena",
            [Scene.Box("floor", new(24, 0.2f, 24), Vector4.One, at: new(0, -0.1f, 0), collision: SceneCollision.Solid), .. actors]);
    }

    private static string Json(StarterSnapshot snapshot)
    {
        return JsonSerializer.Serialize(snapshot, StarterJsonContext.Default.StarterSnapshot);
    }
}
