using Marionette.Skyrim;

namespace Marionette.Skyrim.App;

/// <summary>A loaded reference in one world generation. Persistent roster identity is separate.</summary>
public sealed record LoadedActorBody(ulong WorldGeneration, uint FormId);
public sealed record SkyrimBodyLease(ulong WorldGeneration, ulong LeaseId, uint FormId);

[Flags]
public enum BodyMovement : uint
{
    None = 0,
    Forward = 1,
    Back = 2,
    Left = 4,
    Right = 8,
}

public enum SkyrimBodyAction
{
    Jump,
    DrawWeapon,
    Melee,
    Activate,
    Inventory,
    CharacterMenu,
}

public sealed record BodyControlRequest(
    int ProtocolVersion, string MessageKind, string RequestId, int TimeoutMilliseconds,
    ulong ExpectedWorldGeneration, ulong ExpectedBodyLeaseId, uint TargetFormId, string BodyAction,
    uint InputButtons = 0, float InputMouseX = 0, float InputMouseY = 0,
    string BodyPluginName = "", uint BodyLocalFormId = 0,
    uint BodyItemFormId = 0, uint BodyExpectedItemCount = 0, uint BodyItemCount = 0,
    uint BodyActorValue = 0, float BodyDamage = 0,
    string BodyCellPlugin = "", uint BodyCellLocalFormId = 0,
    float TargetPositionX = 0, float TargetPositionY = 0, float TargetPositionZ = 0);

public sealed record BodyControlObservation(
    ulong LeaseId = 0, ulong WorldGeneration = 0, uint FormId = 0, uint CellFormId = 0,
    string Phase = "idle", string Reason = "", bool Loaded = false, bool Dead = false,
    bool AiEnabled = false, bool ControlsDriven = false, bool WeaponDrawn = false, bool Midair = false,
    float PositionX = 0, float PositionY = 0, float PositionZ = 0, float Heading = 0,
    float Health = 0, float AnimationSpeed = 0, uint CameraTarget = 0,
    uint HitCause = 0, uint HitTarget = 0, uint HitCount = 0, bool ThirdPersonCamera = false,
    string PluginName = "", uint LocalFormId = 0,
    ulong MenuRequestSequence = 0, BodyCharacterInfo? Character = null);

public sealed record BodyControlResult(
    int ProtocolVersion, string MessageKind, string RequestId, ulong ServerSequence,
    string Status, string FailureReason, ulong WorldGeneration, BodyControlObservation BodyControl,
    uint ResolvedBodyFormId = 0, BodyGameplayObservation? Gameplay = null, BodyCharacterInfo? Character = null);

public sealed class SkyrimBodyControlException(string reason, BodyControlObservation observation) : Exception(reason)
{
    public string Reason { get; } = reason;
    public BodyControlObservation Observation { get; } = observation;
    public bool IsDeferred => IsDeferredReason(Reason);

    internal static bool IsDeferredReason(string reason)
    {
        return reason is "dispatch_timeout" or "dispatch_unavailable" or "runtime_unavailable" or "queue_full";
    }
}

/// <summary>
/// Serial C# owner of a native body lease. The host calls HeartbeatAsync; no background worker is installed.
/// Refused switches preserve the previous lease. Unknown native leases are never adopted.
/// </summary>
public sealed partial class SkyrimBodyControl
{
    private readonly Func<BodyControlRequest, CancellationToken, ValueTask<BodyControlResult>> exchange;
    private BodyMovement movement;
    private ulong lastSequence;
    private readonly Action<BodyControlObservation>? selected;
    private readonly Action? released;

    internal SkyrimBodyControl(Func<BodyControlRequest, CancellationToken, ValueTask<BodyControlResult>> exchange,
        Action<BodyControlObservation>? selected = null, Action? released = null)
    {
        this.exchange = exchange;
        this.selected = selected;
        this.released = released;
    }

    public SkyrimBodyLease? Lease { get; private set; }
    public BodyControlObservation Observation { get; private set; } = new();

    public ValueTask<BodyControlObservation> AcquireAsync(LoadedActorBody body, CancellationToken cancellationToken = default)
    {
        Validate(body);
        if (Lease is not null)
        {
            throw new InvalidOperationException("body_control_already_owned");
        }
        return SendAsync("acquire", body.WorldGeneration, 0, body.FormId, BodyMovement.None, 0, 0, cancellationToken);
    }

    public ValueTask<BodyControlObservation> SwitchAsync(LoadedActorBody body, CancellationToken cancellationToken = default)
    {
        Validate(body);
        SkyrimBodyLease current = RequireLease();
        if (body.WorldGeneration != current.WorldGeneration)
        {
            throw new ArgumentException("body_world_generation_changed", nameof(body));
        }
        return SendAsync("switch", current.WorldGeneration, current.LeaseId, body.FormId,
            BodyMovement.None, 0, 0, cancellationToken);
    }

    public ValueTask<BodyControlObservation> ObserveAsync(ulong worldGeneration, CancellationToken cancellationToken = default)
    {
        if (worldGeneration == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldGeneration));
        }
        return SendAsync("observe", worldGeneration, 0, 0, BodyMovement.None, 0, 0, cancellationToken);
    }

    public async ValueTask<LoadedActorBody> ResolveAsync(SkyrimPlacedActorOrigin origin, ulong worldGeneration,
        CancellationToken cancellationToken = default)
    {
        if (worldGeneration == 0) throw new ArgumentOutOfRangeException(nameof(worldGeneration));
        origin = new SkyrimPlacedActorOrigin(origin.PluginName, origin.LocalFormId);
        var request = new BodyControlRequest(1, "body_control", Guid.NewGuid().ToString("N"), 2000,
            worldGeneration, 0, 0, "resolve", BodyPluginName: origin.PluginName, BodyLocalFormId: origin.LocalFormId);
        BodyControlResult result = await SendResultAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.ResolvedBodyFormId is 0 or 20 || result.WorldGeneration != worldGeneration)
            throw new InvalidDataException("body_reference_receipt_invalid");
        return new LoadedActorBody(worldGeneration, result.ResolvedBodyFormId);
    }

    public async ValueTask<BodyControlObservation> UpdateAsync(BodyMovement input, float lookX = 0, float lookY = 0,
        CancellationToken cancellationToken = default)
    {
        if ((uint)input > 15 || !float.IsFinite(lookX) || !float.IsFinite(lookY) || Math.Abs(lookX) > 200 || Math.Abs(lookY) > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }
        SkyrimBodyLease current = RequireLease();
        BodyControlObservation result = await SendAsync("renew", current.WorldGeneration, current.LeaseId,
            0, input, lookX, lookY, cancellationToken).ConfigureAwait(false);
        movement = result.Phase == "active" ? input : BodyMovement.None;
        return result;
    }

    public ValueTask<BodyControlObservation> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        return UpdateAsync(movement, cancellationToken: cancellationToken);
    }

    public ValueTask<BodyControlObservation> ActAsync(SkyrimBodyAction action, CancellationToken cancellationToken = default)
    {
        string operation = action switch
        {
            SkyrimBodyAction.Jump => "jump",
            SkyrimBodyAction.DrawWeapon => "draw",
            SkyrimBodyAction.Melee => "melee",
            SkyrimBodyAction.Activate => "activate",
            SkyrimBodyAction.Inventory => "inventory",
            SkyrimBodyAction.CharacterMenu => "menu",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        SkyrimBodyLease current = RequireLease();
        return SendAsync(operation, current.WorldGeneration, current.LeaseId, 0, BodyMovement.None, 0, 0, cancellationToken);
    }

    public ValueTask<BodyControlObservation> ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (Lease is null)
        {
            released?.Invoke();
            return ValueTask.FromResult(Observation);
        }
        return SendAsync("release", Lease.WorldGeneration, Lease.LeaseId, 0, BodyMovement.None, 0, 0, cancellationToken);
    }

    internal void ObserveWorld(ulong generation)
    {
        // A dispatcher timeout has no world snapshot and reports generation zero.
        // Only an observed nonzero generation can establish a world reset.
        if (generation != 0 && Lease is not null && generation != Lease.WorldGeneration)
        {
            Lease = null;
            movement = BodyMovement.None;
            Observation = new BodyControlObservation(Reason: "world_generation_changed");
        }
    }

    internal void ForgetOwnership(string reason)
    {
        Lease = null;
        movement = BodyMovement.None;
        Observation = new BodyControlObservation(Reason: reason);
    }

    private SkyrimBodyLease RequireLease()
    {
        return Lease ?? throw new InvalidOperationException("body_control_not_owned");
    }

    private static void Validate(LoadedActorBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.WorldGeneration == 0 || body.FormId is 0 or 20)
        {
            throw new ArgumentException("body_identity_invalid", nameof(body));
        }
    }

    private async ValueTask<BodyControlObservation> SendAsync(string action, ulong generation, ulong leaseId, uint target,
        BodyMovement input, float lookX, float lookY, CancellationToken cancellationToken)
    {
        var request = new BodyControlRequest(1, "body_control", Guid.NewGuid().ToString("N"), 2000,
            generation, leaseId, target, action, (uint)input, lookX, lookY);
        return (await SendResultAsync(request, cancellationToken).ConfigureAwait(false)).BodyControl;
    }

    private async ValueTask<BodyControlResult> SendResultAsync(BodyControlRequest request, CancellationToken cancellationToken)
    {
        BodyControlResult result = await exchange(request, cancellationToken).ConfigureAwait(false);
        if (result.ProtocolVersion != 1 || result.MessageKind != "body_control_result" ||
            result.RequestId != request.RequestId || result.ServerSequence <= lastSequence)
        {
            throw new InvalidDataException("body_response_correlation_invalid");
        }
        lastSequence = result.ServerSequence;
        if (result.Status != "completed" && SkyrimBodyControlException.IsDeferredReason(result.FailureReason))
        {
            // The dispatcher did not provide a body snapshot. Its empty envelope
            // cannot revoke an existing token or authorize adopting another owner.
            movement = BodyMovement.None;
            Observation = Observation with { Phase = "suspended", Reason = result.FailureReason };
            throw new SkyrimBodyControlException(result.FailureReason, Observation);
        }
        Observation = result.BodyControl;
        if (result.Status == "completed" && (request.BodyAction is "acquire" or "switch"))
        {
            if (Observation.LeaseId == 0 || Observation.FormId != request.TargetFormId ||
                Observation.WorldGeneration != request.ExpectedWorldGeneration)
            {
                throw new InvalidDataException("body_acquisition_receipt_invalid");
            }
            Lease = new(request.ExpectedWorldGeneration, Observation.LeaseId, request.TargetFormId);
            movement = BodyMovement.None;
            selected?.Invoke(Observation);
        }
        else if (Lease is not null && (Observation.LeaseId != Lease.LeaseId || Observation.WorldGeneration != Lease.WorldGeneration))
        {
            Lease = null;
            movement = BodyMovement.None;
        }
        if (result.Status != "completed")
        {
            throw new SkyrimBodyControlException(result.FailureReason, Observation);
        }
        if (request.BodyAction == "release") released?.Invoke();
        if (request.BodyAction == "renew" && Lease is not null && Observation.Loaded && Observation.Character is not null)
        {
            selected?.Invoke(Observation);
        }
        return result;
    }
}

internal static class BodyControlTransport
{
    internal static async ValueTask<BodyControlResult> ExchangeAsync(Stream pipe, BodyControlRequest request,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        await MarionetteWireProtocol.WriteAsync(pipe, request, deadline.Token).ConfigureAwait(false);
        return await MarionetteWireProtocol.ReadAsync<BodyControlResult>(pipe, deadline.Token).ConfigureAwait(false);
    }
}

