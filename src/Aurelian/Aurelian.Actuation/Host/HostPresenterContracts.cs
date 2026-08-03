namespace Aurelian.Actuation.Host;

/// <summary>
/// Bounded semantic operations a presenter may request from one active host.
/// This vocabulary deliberately exposes no scripts, method names, or platform APIs.
/// </summary>
public enum HostCommandKind
{
    QueryState,
    BeginHostSession,
    EndHostSession,
    SetMoveIntent,
    SetLookIntent,
    StopMovement,
    ActivateTarget,
    BasicAttack,
    EmergencyRestore,
    MoveToward,
    BindBody,
    ReleaseBody,
    QueryBodyBinding,
    MoveBodyToward,
}

public enum HostActionState
{
    None,
    Accepted,
    Running,
    Completed,
    Rejected,
    Failed,
    TimedOut,
    Blocked,
    Interrupted,
    TargetInvalid,
    ActorUnloaded,
    Unsupported,
    EngineRefused,
}

public enum HostCameraMode
{
    Unknown,
    FirstPerson,
    ThirdPerson,
}

public abstract record HostCommandArguments;

public sealed record EmptyHostCommandArguments : HostCommandArguments
{
    public static EmptyHostCommandArguments Instance { get; } = new();
}

public sealed record MoveIntentArguments(float Forward, float Right) : HostCommandArguments;

public sealed record LookIntentArguments(float YawDeltaDegrees, float PitchDeltaDegrees) : HostCommandArguments;

public sealed record ActivateTargetArguments(uint TargetFormId) : HostCommandArguments;

public readonly record struct HostActorId(uint FormId, ulong Generation)
{
    public bool IsValid => FormId != 0 && Generation != 0;
}

public readonly record struct HostPosition3(float X, float Y, float Z)
{
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);

    public float DistanceTo(HostPosition3 other)
    {
        float x = other.X - X;
        float y = other.Y - Y;
        float z = other.Z - Z;
        return MathF.Sqrt((x * x) + (y * y) + (z * z));
    }
}

public readonly record struct HostVelocity3(float X, float Y, float Z);

public enum HostActorLifeState
{
    Unknown,
    Alive,
    Dead,
}

public enum HostActorMovementState
{
    Unknown,
    Idle,
    Moving,
}

public enum HostCapabilitySupport
{
    Unsupported,
    Experimental,
    Supported,
}

public enum HostMovementSpeedPolicy
{
    Walk,
    Run,
}

public sealed record HostCapabilitySnapshot(
    HostCapabilitySupport BoundedDirectDisplacement,
    HostCapabilitySupport AnimatedLocomotion,
    HostCapabilitySupport GoalDirectedMovement,
    HostCapabilitySupport CameraFollowing,
    HostCapabilitySupport ActorActivation,
    HostCapabilitySupport Attack,
    HostCapabilitySupport Jump,
    HostCapabilitySupport Sneak)
{
    public bool CanMoveToward => GoalDirectedMovement is
        HostCapabilitySupport.Experimental or HostCapabilitySupport.Supported;
}

public sealed record MoveTowardArguments(
    HostActorId ActorId,
    HostPosition3 TargetPosition,
    float StoppingDistance,
    float MaximumDistance,
    HostMovementSpeedPolicy SpeedPolicy,
    ulong ExpectedObservationSequence) : HostCommandArguments;

/// <summary>
/// One correlation-safe, host-generation-checked command. The caller owns the
/// request ID and chooses a short bounded lifetime.
/// </summary>
public sealed record HostCommandRequest(
    Guid RequestId,
    ulong ExpectedHostGeneration,
    HostCommandKind Kind,
    TimeSpan Timeout,
    HostCommandArguments Arguments)
{
    public const float MaximumMoveIntent = 1.0f;
    public const float MaximumLookDeltaDegrees = 180.0f;
    public const float MaximumMoveTowardDistance = 64.0f;
    public const float MaximumStoppingDistance = 256.0f;
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromSeconds(10);

    public HostCommandValidationResult Validate()
    {
        if (RequestId == Guid.Empty)
        {
            return HostCommandValidationResult.Invalid("request_id_missing");
        }

        if (ExpectedHostGeneration == 0)
        {
            return HostCommandValidationResult.Invalid("host_generation_missing");
        }

        if (Timeout <= TimeSpan.Zero || Timeout > MaximumTimeout)
        {
            return HostCommandValidationResult.Invalid("timeout_out_of_range");
        }

        if (Arguments is null)
        {
            return HostCommandValidationResult.Invalid("arguments_missing");
        }

        return Kind switch
        {
            HostCommandKind.SetMoveIntent when Arguments is MoveIntentArguments move
                => ValidateMove(move),
            HostCommandKind.SetLookIntent when Arguments is LookIntentArguments look
                => ValidateLook(look),
            HostCommandKind.ActivateTarget when Arguments is ActivateTargetArguments activation
                => activation.TargetFormId == 0
                    ? HostCommandValidationResult.Invalid("target_form_id_missing")
                    : HostCommandValidationResult.Valid,
            HostCommandKind.MoveToward when Arguments is MoveTowardArguments moveToward
                => ValidateMoveToward(moveToward),
            HostCommandKind.BindBody when Arguments is BindBodyArguments bindBody
                => ValidateBindBody(bindBody),
            HostCommandKind.ReleaseBody when Arguments is ReleaseBodyArguments releaseBody
                => ValidateReleaseBody(releaseBody),
            HostCommandKind.QueryBodyBinding when Arguments is QueryBodyBindingArguments
                => HostCommandValidationResult.Valid,
            HostCommandKind.MoveBodyToward when Arguments is MoveBodyTowardArguments moveBody
                => ValidateMoveBodyToward(moveBody),
            HostCommandKind.SetMoveIntent or HostCommandKind.SetLookIntent or HostCommandKind.ActivateTarget
                or HostCommandKind.MoveToward or HostCommandKind.BindBody or HostCommandKind.ReleaseBody
                or HostCommandKind.QueryBodyBinding or HostCommandKind.MoveBodyToward
                => HostCommandValidationResult.Invalid("arguments_do_not_match_command"),
            _ when Arguments is EmptyHostCommandArguments => HostCommandValidationResult.Valid,
            _ => HostCommandValidationResult.Invalid("arguments_not_allowed_for_command"),
        };
    }

    private static HostCommandValidationResult ValidateMove(MoveIntentArguments arguments)
    {
        if (!float.IsFinite(arguments.Forward) || !float.IsFinite(arguments.Right)
            || MathF.Abs(arguments.Forward) > MaximumMoveIntent
            || MathF.Abs(arguments.Right) > MaximumMoveIntent)
        {
            return HostCommandValidationResult.Invalid("move_intent_out_of_range");
        }

        return HostCommandValidationResult.Valid;
    }

    private static HostCommandValidationResult ValidateLook(LookIntentArguments arguments)
    {
        if (!float.IsFinite(arguments.YawDeltaDegrees) || !float.IsFinite(arguments.PitchDeltaDegrees)
            || MathF.Abs(arguments.YawDeltaDegrees) > MaximumLookDeltaDegrees
            || MathF.Abs(arguments.PitchDeltaDegrees) > MaximumLookDeltaDegrees)
        {
            return HostCommandValidationResult.Invalid("look_intent_out_of_range");
        }

        return HostCommandValidationResult.Valid;
    }

    private HostCommandValidationResult ValidateMoveToward(MoveTowardArguments arguments)
    {
        if (!arguments.ActorId.IsValid || arguments.ActorId.Generation != ExpectedHostGeneration)
        {
            return HostCommandValidationResult.Invalid("actor_generation_mismatch");
        }

        if (!arguments.TargetPosition.IsFinite)
        {
            return HostCommandValidationResult.Invalid("target_position_invalid");
        }

        if (!float.IsFinite(arguments.StoppingDistance)
            || arguments.StoppingDistance < 0.0f
            || arguments.StoppingDistance > MaximumStoppingDistance)
        {
            return HostCommandValidationResult.Invalid("stopping_distance_out_of_range");
        }

        if (!float.IsFinite(arguments.MaximumDistance)
            || arguments.MaximumDistance <= 0.0f
            || arguments.MaximumDistance > MaximumMoveTowardDistance)
        {
            return HostCommandValidationResult.Invalid("move_toward_distance_out_of_range");
        }

        if (arguments.ExpectedObservationSequence == 0)
        {
            return HostCommandValidationResult.Invalid("observation_sequence_missing");
        }

        return HostCommandValidationResult.Valid;
    }

    private HostCommandValidationResult ValidateBindBody(BindBodyArguments arguments)
    {
        if (arguments.ExpectedBodyGeneration == 0
            || arguments.ExpectedBodyGeneration != ExpectedHostGeneration)
        {
            return HostCommandValidationResult.Invalid("body_generation_mismatch");
        }

        return arguments.Kind == BodyBindingKind.ExclusiveControl
            ? HostCommandValidationResult.Valid
            : HostCommandValidationResult.Invalid("binding_kind_unsupported");
    }

    private HostCommandValidationResult ValidateReleaseBody(ReleaseBodyArguments arguments)
    {
        return arguments.ExpectedBodyGeneration != 0
            && arguments.ExpectedBodyGeneration == ExpectedHostGeneration
            ? HostCommandValidationResult.Valid
            : HostCommandValidationResult.Invalid("body_generation_mismatch");
    }

    private HostCommandValidationResult ValidateMoveBodyToward(MoveBodyTowardArguments arguments)
    {
        if (arguments.ExpectedBodyGeneration == 0
            || arguments.ExpectedBodyGeneration != ExpectedHostGeneration)
        {
            return HostCommandValidationResult.Invalid("body_generation_mismatch");
        }

        return ValidateMoveToward(new MoveTowardArguments(
            new HostActorId(1, arguments.ExpectedBodyGeneration),
            arguments.TargetPosition,
            arguments.StoppingDistance,
            arguments.MaximumDistance,
            arguments.SpeedPolicy,
            arguments.ExpectedObservationSequence));
    }
}

public sealed record HostCommandValidationResult(bool IsValid, string? FailureReason)
{
    public static HostCommandValidationResult Valid { get; } = new(true, null);

    public static HostCommandValidationResult Invalid(string failureReason) => new(false, failureReason);
}

public sealed record HostCommandReceipt(
    Guid RequestId,
    bool Accepted,
    ulong RuntimeSequence,
    string? FailureReason);

public sealed record ActiveHostObservation(
    ulong HostGeneration,
    uint HostFormId,
    bool HostDead,
    bool HostLoaded,
    float PositionX,
    float PositionY,
    float PositionZ,
    float RotationZ,
    float VelocityX,
    float VelocityY,
    float VelocityZ,
    string? AnimationState);

/// <summary>
/// Small value-only actor snapshot suitable for deterministic decision, fake,
/// and replay use. Nullable fields mean the backend could not safely observe
/// the value; they are not invitations to dump host state.
/// </summary>
public sealed record HostActorObservation(
    HostActorId ActorId,
    HostPosition3 Position,
    float? HeadingRadians,
    HostVelocity3? Velocity,
    HostActorLifeState LifeState,
    HostActorMovementState MovementState,
    bool Loaded,
    uint? CurrentCellFormId,
    HostActorId? CurrentTarget,
    float? DistanceToGoal,
    HostActionState ActionState,
    HostCapabilitySnapshot Capabilities,
    ulong Sequence);

public sealed record PlayerAnchorObservation(uint PlayerFormId, float PositionX, float PositionY, float PositionZ);

public sealed record CameraObservation(uint TargetFormId, HostCameraMode Mode);

public sealed record CrosshairObservation(uint TargetFormId);

public sealed record MovementObservation(bool ControllerObserved, bool Moving);

public sealed record HostActionResult(
    Guid RequestId,
    HostActionState State,
    string? FailureReason,
    HostActorObservation? Observation = null,
    BodyCommandResult? BodyResult = null)
{
    public bool IsTerminal => State is HostActionState.Completed
        or HostActionState.Rejected
        or HostActionState.Failed
        or HostActionState.TimedOut
        or HostActionState.Blocked
        or HostActionState.Interrupted
        or HostActionState.TargetInvalid
        or HostActionState.ActorUnloaded
        or HostActionState.Unsupported
        or HostActionState.EngineRefused;
}

/// <summary>
/// Ordered host observation envelope. Sequence numbers are assigned by the
/// backend and let presenters correlate receipts with eventual completion.
/// </summary>
public sealed record HostRuntimeObservation(
    ulong RuntimeSequence,
    ActiveHostObservation? ActiveHost,
    PlayerAnchorObservation PlayerAnchor,
    CameraObservation Camera,
    CrosshairObservation Crosshair,
    MovementObservation Movement,
    HostActionResult? Action,
    HostActorObservation? Actor = null,
    BodyBindingObservation? BodyBinding = null,
    BodyObservation? Body = null);

public interface IHostPresenterBackend
{
    ValueTask<HostCommandReceipt> SubmitAsync(HostCommandRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<HostRuntimeObservation> ObserveAsync(CancellationToken cancellationToken);
}
