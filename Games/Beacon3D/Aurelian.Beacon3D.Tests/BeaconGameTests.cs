using System.Numerics;
using Aurelian.Beacon3D;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using InputMan.Core;
using Xunit;

namespace Aurelian.Beacon3D.Tests;

public sealed class BeaconGameTests
{
    [Fact]
    public void InputManAxesCancelAndHeldActionsEmitOneEdge()
    {
        using var controls = new BeaconControls();
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), true);
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.Space), true);
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.R), true);
        var first = controls.Tick(1f / 60);
        Assert.Equal(1, first.Movement.Forward);
        Assert.True(first.Movement.Jump);
        Assert.True(first.Movement.Reload);
        var held = controls.Tick(1f / 60);
        Assert.Equal(1, held.Movement.Forward);
        Assert.False(held.Movement.Jump);
        Assert.False(held.Movement.Reload);
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.S), true);
        Assert.Equal(0, controls.Tick(1f / 60).Movement.Forward);
    }

    [Fact]
    public void InputManFocusLossClearsMovementUntilFreshPress()
    {
        using var controls = new BeaconControls();
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.D), true);
        Assert.Equal(1, controls.Tick(1f / 60).Movement.Strafe);
        controls.Adapter.OnFocusChanged(false);
        Assert.Equal(0, controls.Tick(1f / 60).Movement.Strafe);
        controls.Adapter.OnFocusChanged(true);
        Assert.Equal(0, controls.Tick(1f / 60).Movement.Strafe);
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.D), true);
        Assert.Equal(1, controls.Tick(1f / 60).Movement.Strafe);
    }

    [Fact]
    public void InputManDrivesGameMovementLookAndPause()
    {
        using var controls = new BeaconControls();
        var game = new BeaconGame(combat: false);
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.W), true);
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.ArrowRight), true);
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.ArrowUp), true);
        for (int tick = 0; tick < 10; tick++)
        {
            game.Step(controls.Tick(1f / 60).Movement, 1f / 60);
        }
        Assert.True(game.Position.X > 0);
        Assert.True(game.Position.Y < 9);
        Assert.True(game.Yaw > 0);
        Assert.True(game.Pitch > -0.08f);
        controls.Adapter.RecordButton(Controls.Key(KeyboardKey.Escape), true);
        Assert.True(controls.Tick(1f / 60).Pause);
    }

    [Fact]
    public void WalkingIntoPillarStopsAtPlayerRadius()
    {
        var game = new BeaconGame(combat: false);
        Walk(game, new BeaconInput(1, 0, 0, 0), 600);
        Assert.InRange(game.Position.Y, 5.299f, 5.37f);
        Assert.Equal(0, game.CollectedCount);
    }

    [Fact]
    public void ArenaBoundaryStopsPlayer()
    {
        var game = new BeaconGame(combat: false);
        Walk(game, new BeaconInput(0, 1, 0, 0), 600);
        Assert.InRange(game.Position.X, 10.6f, 10.7f);
    }

    [Fact]
    public void DiagonalMovementHasSameSpeedAsStraightMovement()
    {
        var straight = new BeaconGame(combat: false);
        var diagonal = new BeaconGame(combat: false);
        Vector2 start = straight.Position;
        Walk(straight, new BeaconInput(1, 0, 0, 0), 30);
        Walk(diagonal, new BeaconInput(1, 1, 0, 0), 30);
        Assert.InRange(MathF.Abs(Vector2.Distance(start, straight.Position) - Vector2.Distance(start, diagonal.Position)), 0, 0.0001f);
    }

    [Fact]
    public void JumpRaisesEyeThenLands()
    {
        var game = new BeaconGame(combat: false);
        game.Step(new BeaconInput(0, 0, 0, 0, Jump: true), 1f / 60);
        Assert.True(game.Eye.Y > 1.6f);
        Walk(game, default, 120);
        Assert.Equal(0, game.Height);
        Assert.Equal(1.6f, game.Eye.Y);
    }

    [Fact]
    public void BeaconCollectsOnceAndDoesNotUnlockExitAlone()
    {
        var game = new BeaconGame(combat: false);
        Walk(game, new BeaconInput(0, -1, 0, 0), 105);
        Walk(game, new BeaconInput(1, 0, 0, 0), 90);
        Assert.Equal(1, game.CollectedCount);
        Assert.True(game.IsCollected(0));
        Walk(game, default, 60);
        Assert.Equal(1, game.CollectedCount);
        Assert.False(game.Won);
    }

    [Fact]
    public void PerspectiveUsesVulkanDepthAndShrinksDistantObjects()
    {
        var game = new BeaconGame(combat: false);
        Matrix4x4 camera = game.Camera(1.6f);
        Vector3 direction = new(0, MathF.Sin(game.Pitch), -MathF.Cos(game.Pitch));
        Vector4 near = Vector4.Transform(new Vector4(game.Eye + direction * 0.1f, 1), camera);
        Vector4 far = Vector4.Transform(new Vector4(game.Eye + direction * 80, 1), camera);
        Assert.InRange(near.Z / near.W, -0.0001f, 0.0001f);
        Assert.InRange(far.Z / far.W, 0.9999f, 1.0001f);
        Vector4 closePoint = Vector4.Transform(new Vector4(game.Eye + direction * 2 + Vector3.UnitX, 1), camera);
        Vector4 distantPoint = Vector4.Transform(new Vector4(game.Eye + direction * 4 + Vector3.UnitX, 1), camera);
        Assert.InRange(closePoint.X / closePoint.W / (distantPoint.X / distantPoint.W), 1.999f, 2.001f);
    }

    [Fact]
    public void SolidShaderBindsVertexCameraRowsWithExactPacking()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Solid3D.v.ts"));
        var module = Compile(source);
        Assert.True(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var material = Assert.Single(module.Materials);
        Assert.Equal(64, material.Size);
        Assert.Equal([0, 16, 32, 48], material.Fields.Select(field => field.Offset));
        Assert.Equal([VdMirGraphicsStage.Vertex], material.Visibility);
        Assert.Equal(["float3", "float3", "float4"], module.GraphicsProgram!.VertexInputs.Select(input => input.PhysicalType));
        var invalid = Compile(source.Replace("clipW: float4", "clipW: float3", StringComparison.Ordinal));
        Assert.False(invalid.Success);
        Assert.Contains(invalid.Diagnostics, item => item.Code == "COPE-GPU-MATERIAL-0003");
    }

    private static VdMirGraphicsModule Compile(string source)
    {
        return GpuGraphicsBinder.Compile(new GpuCompilationRequest([new GpuSourceFile("Solid3D.v.ts", source)]));
    }

    private static void Walk(BeaconGame game, BeaconInput input, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            game.Step(input, 1f / 60);
        }
    }
}
