using System.Numerics;
using Aurelian.Aetheris;
using Aurelian.Games;
using Aurelian.NativeComposition;
using Aurelian.Spatial3D;
using Aurelian.World.Scenes;
using Xunit;

namespace Aurelian.Games.Tests;

public sealed class AetherisRoomTests
{
    private static AetherisSceneAsset Load() => AetherisSceneAsset.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "room.aurelian.json"));

    [Fact]
    public void AuthoredRoomSharesCollisionAndRenderingAndPreservesSourceIdentity()
    {
        AetherisSceneAsset asset = Load();
        Assert.All(asset.Definitions, definition => Assert.Equal("planar-exact", definition.Representation));
        var document = Scene.World("room-game", [Scene.Instance("room", asset.Compose()),
            Scene.Agent("player", new StarterPlayerDefinition(), at: new(6, 0, -6))]);
        using var game = GameStarter.Create("room-test", GamePresets.FirstPersonShooter, sceneDocument: document);
        var floor = game.SpatialWorld.Raycast(new(new(6, 1, -6), -Vector3.UnitY, 5))!.Value;
        Assert.Contains("hall.floor.panel", floor.ColliderId);
        Assert.Contains(asset.SourceSha256, floor.SourceIdentity);
        Assert.Equal(1, floor.Distance, 5);
        Assert.Equal(17, game.SpatialWorld.Colliders.Length);
        Assert.Equal(204 * 3, SceneGeometry3D.Build(new(game.Scene.Plan.Boxes, game.Scene.Plan.Meshes)).Length);
        var wall = game.SpatialWorld.Raycast(new(new(5, 1, -3), Vector3.UnitZ, 5));
        Assert.NotNull(wall);
        Assert.Contains("southWall", wall.Value.ColliderId);
        Assert.Null(game.SpatialWorld.Raycast(new(new(3, 1, -3), Vector3.UnitZ, 5)));
    }

    [Fact]
    public void MotorUsesRoomFloorAndWalksThroughRealDoorway()
    {
        var world = SceneSpatial3D.Build(SceneCompiler.Compile(Load().Compose()));
        var motor = new CharacterMotor3D();
        CharacterState3D state = new(new(3, 0, -3), 0);
        for (int tick = 0; tick < 60; tick++) state = motor.Step(world, state, Vector3.UnitZ * 2, false, 1f / 60).State;
        Assert.True(state.Feet.Z > -1.5f);
        Assert.True(state.Feet.Y < 0);
    }

    [Fact]
    public void InvalidCollisionRepresentationAndMissingSourceHashAreRejected()
    {
        var asset = Load();
        Assert.Throws<InvalidDataException>(() => (asset with { SourceSha256 = "" }).Compose());
        var invalid = asset.Definitions[0] with { Representation = "implicit-brep-fallback" };
        Assert.Throws<InvalidDataException>(() => (asset with { Definitions = [invalid] }).Compose());
    }
}
