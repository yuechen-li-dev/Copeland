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
            HostCommandKind.SetMoveIntent or HostCommandKind.SetLookIntent or HostCommandKind.ActivateTarget
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

public sealed record PlayerAnchorObservation(uint PlayerFormId, float PositionX, float PositionY, float PositionZ);

public sealed record CameraObservation(uint TargetFormId, HostCameraMode Mode);

public sealed record CrosshairObservation(uint TargetFormId);

public sealed record MovementObservation(bool ControllerObserved, bool Moving);

public sealed record HostActionResult(Guid RequestId, HostActionState State, string? FailureReason);

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
    HostActionResult? Action);

public interface IHostPresenterBackend
{
    ValueTask<HostCommandReceipt> SubmitAsync(HostCommandRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<HostRuntimeObservation> ObserveAsync(CancellationToken cancellationToken);
}
