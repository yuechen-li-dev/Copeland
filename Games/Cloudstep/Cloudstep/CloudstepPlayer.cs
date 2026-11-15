using System.Numerics;
using Aurelian.Spatial3D;

namespace Cloudstep;

/// <summary>Cloudstep's preset over the reusable engine controller.</summary>
public sealed record PlayerTuning : PlatformerSettings3D;

// Keep the published course save shape and game-facing types unchanged.
public sealed record PlayerSnapshot(float X, float Y, float Z, float VelocityX, float VerticalVelocity, float VelocityZ,
    float Facing, string? Support, bool Grounded, float Coyote, float Buffer, bool Rising);

public readonly record struct PlayerInput(Vector2 Move, bool JumpPressed, bool JumpHeld);
public readonly record struct PlayerStepResult(bool Jumped, bool Landed, float LandingSpeed, bool Crushed, float SteppedUp);

/// <summary>Game adapter; movement and jump state live exclusively in Aurelian.Spatial3D.</summary>
public sealed class CloudstepPlayer
{
    private readonly PlatformerCharacter3D character;

    public CloudstepPlayer(PlayerTuning? tuning = null)
    {
        Tuning = tuning ?? new();
        character = new(Tuning);
    }

    public PlayerTuning Tuning { get; }
    public CharacterMotorOptions3D Motor => character.Motor;
    public Vector3 Feet => character.Feet;
    public Vector3 Centre => character.Centre;
    public Vector2 Velocity => character.Velocity;
    public float VerticalVelocity => character.VerticalVelocity;
    public float Facing => character.Facing;
    public bool Grounded => character.Grounded;
    public string? Support => character.Support;
    public Capsule3D Capsule => character.Capsule;

    public void Place(Vector3 position, float facing) => character.Place(position, facing);

    public PlayerSnapshot Capture()
    {
        var state = character.Capture();
        return new(state.X, state.Y, state.Z, state.VelocityX, state.VerticalVelocity, state.VelocityZ,
            state.Facing, state.Support, state.Grounded, state.Coyote, state.Buffer, state.Rising);
    }

    public void Restore(PlayerSnapshot snapshot) => character.Restore(EngineState(snapshot));
    public static void Validate(PlayerSnapshot snapshot) => PlatformerCharacter3D.Validate(EngineState(snapshot));

    public PlayerStepResult Step(KinematicSpatialWorld3D world, PlayerInput input, float seconds)
    {
        var step = character.Step(world, new(input.Move, input.JumpPressed, input.JumpHeld), seconds);
        return new(step.Jumped, step.Landed, step.LandingSpeed, step.Crushed, step.SteppedUp);
    }

    private static PlatformerSnapshot3D EngineState(PlayerSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new(state.X, state.Y, state.Z, state.VelocityX, state.VerticalVelocity, state.VelocityZ,
            state.Facing, state.Support, state.Grounded, state.Coyote, state.Buffer, state.Rising);
    }
}
