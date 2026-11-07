using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;

namespace Marionette.Skyrim.App;

public sealed record HostPlaytestResult(string Command, string Status, string Reason,
    PlaytestObservation? Observation, ulong WorldGeneration, bool BehaviorsAllowed,
    double ReadySeconds, long ReadyTicks);

public sealed record HostHeartbeat(double ElapsedSeconds, double ReadySeconds, long ReadyTicks,
    ulong WorldGeneration, bool BehaviorsAllowed);

/// <summary>Consumes local commands; all wire work remains serial on the host loop.</summary>
internal sealed class SkyrimHostPlaytestControl : IAsyncDisposable
{
    private readonly PlaytestSession input;
    private readonly CancellationTokenSource shutdown;
    private readonly Task reader;
    private readonly Channel<string> commands = Channel.CreateBounded<string>(16);
    private PlaytestButtons held;
    private long releaseAt;
    private bool active;
    private WorldNpcResult? priorObservation;
    private WorldNpcObservation? priorNpc;
    private OwnedNpcIdentity? ownedNpc;
    private ulong ownedMovementId;
    private readonly OwnedNpcAgentBehavior? ownedAgent;
    private bool bodyInputPending;
    private long bodyReleaseAt;

    public SkyrimHostPlaytestControl(Stream pipe, TextReader commandInput, CancellationToken cancellationToken,
        OwnedNpcAgentBehavior? ownedAgent = null)
    {
        input = new PlaytestSession(pipe, ownsPipe: false);
        this.ownedAgent = ownedAgent;
        shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        reader = Task.Run(() => ReadCommandsAsync(commandInput));
        Console.Error.WriteLine("SKYRIM_HOST_CONTROL_READY inspect | input <buttons> <seconds> | look <x> <y> | release | save | load | npc-change | stale | owned-spawn | owned-inspect | owned-tune | owned-release | body-list | body-acquire <form> | body-switch <form> | body-roster | body-select <character-guid> | body-input <movement> <seconds> | body-action <action> | body-inspect | body-gameplay | body-equip <item> | body-unequip <item> | body-drop <item> | body-damage <vital> <amount> | body-release | quest-inspect | quest-reward");
    }

    private async Task ReadCommandsAsync(TextReader commandInput)
    {
        try
        {
            while (await commandInput.ReadLineAsync(shutdown.Token).ConfigureAwait(false) is { } line)
            {
                await commands.Writer.WriteAsync(line, shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            commands.Writer.TryComplete();
        }
    }

    public async ValueTask<bool> TickAsync(SkyrimHostContext context, bool behaviorsAllowed,
        double readySeconds, long readyTicks, CancellationToken cancellationToken)
    {
        string? command = null;
        try
        {
            if (bodyInputPending && Stopwatch.GetTimestamp() >= bodyReleaseAt)
            {
                if (context.Bodies?.Lease is not null)
                {
                    await context.Bodies.UpdateAsync(BodyMovement.None, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                bodyInputPending = false;
            }
            if (active && context.Bodies?.Lease is null)
            {
                PlaytestObservation state = await input.ObserveAsync(cancellationToken).ConfigureAwait(false);
                active = state.Active;
                bool finished = Stopwatch.GetTimestamp() >= releaseAt;
                // Gameplay keys never survive a managed lifecycle gate closing.
                bool blocked = !behaviorsAllowed && ((uint)held & (127U | 32768U | 65536U)) != 0;
                if (active)
                {
                    await input.UpdateAsync(finished || blocked ? PlaytestButtons.None : held,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                if (finished || blocked || !active)
                {
                    held = PlaytestButtons.None;
                }
            }
            if (!commands.Reader.TryRead(out command))
            {
                if (active && commands.Reader.Completion.IsCompleted)
                {
                    await input.EndAsync(cancellationToken).ConfigureAwait(false);
                    active = false;
                    held = PlaytestButtons.None;
                }
                return false;
            }
            if (command.Length > 512) throw new ArgumentException("command_too_long");
            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            PlaytestObservation? observation = null;
            bool transition = false;
            if (parts.Length > 0 && parts[0].StartsWith("body-", StringComparison.Ordinal))
            {
                SkyrimBodyControl bodies = context.Bodies ?? throw new ArgumentException("body_control_capability_missing");
                if (parts is ["body-list"])
                {
                    WorldNpcResult candidates = await context.ObserveNpcsAsync(cancellationToken, refresh: true).ConfigureAwait(false);
                    Console.WriteLine("SKYRIM_HOST_BODIES " + JsonSerializer.Serialize(candidates,
                        MarionetteWireJsonContext.Default.WorldNpcResult));
                    Emit(command, "completed", "", null, context, behaviorsAllowed, readySeconds, readyTicks);
                    return false;
                }
                if (parts is ["body-roster"])
                {
                    Console.WriteLine("SKYRIM_EMBODIMENT_ROSTER " + JsonSerializer.Serialize(
                        context.WorldOwner?.ReadEmbodiment(), EmbodimentJsonContext.Default.EmbodimentState));
                    Emit(command, "completed", "", null, context, behaviorsAllowed, readySeconds, readyTicks);
                    return false;
                }
                BodyControlObservation body;
                if (parts is ["body-gameplay"])
                {
                    await EmitGameplayAsync(bodies, cancellationToken);
                    Emit(command, "completed", "", null, context, behaviorsAllowed, readySeconds, readyTicks);
                    return false;
                }
                if (parts is [var inventoryAction, var itemText] && inventoryAction is "body-equip" or "body-unequip" or "body-drop"
                    && uint.TryParse(itemText, out uint itemForm))
                {
                    BodyGameplaySnapshot source = await bodies.ObserveGameplayAsync(cancellationToken);
                    if (inventoryAction == "body-drop")
                    {
                        await bodies.DropAsync(source, itemForm, cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await bodies.EquipAsync(source, itemForm, inventoryAction == "body-unequip", cancellationToken);
                    }
                    await EmitGameplayAsync(bodies, cancellationToken);
                    Emit(command, "completed", "", null, context, behaviorsAllowed, readySeconds, readyTicks);
                    return false;
                }
                if (parts is ["body-damage", var vitalText, var amountText] &&
                    Enum.TryParse(vitalText, true, out SkyrimVital vital) &&
                    float.TryParse(amountText, NumberStyles.Float, CultureInfo.InvariantCulture, out float amount))
                {
                    await bodies.DamageVitalAsync(vital, amount, cancellationToken);
                    await EmitGameplayAsync(bodies, cancellationToken);
                    Emit(command, "completed", "", null, context, behaviorsAllowed, readySeconds, readyTicks);
                    return false;
                }
                if (parts is ["body-inspect"])
                {
                    body = await bodies.ObserveAsync(context.State.WorldGeneration, cancellationToken).ConfigureAwait(false);
                }
                else if (parts is ["body-release"])
                {
                    body = await bodies.ReleaseAsync(cancellationToken).ConfigureAwait(false);
                    bodyInputPending = false;
                    active = false;
                    held = PlaytestButtons.None;
                }
                else if (parts is ["body-select", var characterText] && Guid.TryParse(characterText, out Guid characterId))
                {
                    if (!behaviorsAllowed) throw new ArgumentException("gameplay_lifecycle_gate_closed");
                    var character = context.WorldOwner?.ReadEmbodiment()?.Characters.SingleOrDefault(item => item.CharacterId == characterId)
                        ?? throw new ArgumentException("embodiment_character_unknown");
                    LoadedActorBody target = await bodies.ResolveAsync(character.Origin, context.State.WorldGeneration, cancellationToken);
                    body = await context.AcquireBodyAsync(target, bodies.Lease is not null, cancellationToken);
                    active = false;
                    held = PlaytestButtons.None;
                    bodyInputPending = false;
                }
                else if (parts is [var operation, var actorText] && operation is "body-acquire" or "body-switch"
                    && uint.TryParse(actorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint actor))
                {
                    if (!behaviorsAllowed) throw new ArgumentException("gameplay_lifecycle_gate_closed");
                    body = await context.AcquireBodyAsync(new(context.State.WorldGeneration, actor),
                        operation == "body-switch", cancellationToken).ConfigureAwait(false);
                    active = false;
                    held = PlaytestButtons.None;
                }
                else if (parts is ["body-action", var actionText] && Enum.TryParse(actionText, true, out SkyrimBodyAction action))
                {
                    body = await bodies.ActAsync(action, cancellationToken).ConfigureAwait(false);
                }
                else if (parts is ["body-input", var movementText, var timeText]
                    && Enum.TryParse(movementText, true, out BodyMovement movement)
                    && double.TryParse(timeText, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                    && double.IsFinite(seconds) && seconds is > 0 and <= 10)
                {
                    body = await bodies.UpdateAsync(movement, cancellationToken: cancellationToken).ConfigureAwait(false);
                    bodyInputPending = true;
                    bodyReleaseAt = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);
                }
                else
                {
                    throw new ArgumentException("body_command_invalid");
                }
                Console.WriteLine("SKYRIM_HOST_BODY " + JsonSerializer.Serialize(body,
                    MarionetteWireJsonContext.Default.BodyControlObservation));
            }
            else if (parts is ["inspect"])
            {
                observation = await input.ObserveAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (parts is ["quest-inspect"] or ["quest-reward"])
            {
                if (parts[0] == "quest-reward" && !behaviorsAllowed)
                {
                    throw new ArgumentException("gameplay_lifecycle_gate_closed");
                }
                var result = await context.QuestUiAsync(parts[0] == "quest-reward" ? "reward" : "query", cancellationToken).ConfigureAwait(false);
                Console.WriteLine("SKYRIM_HOST_QUEST " + JsonSerializer.Serialize(result.QuestUi,
                    MarionetteWireJsonContext.Default.QuestUiObservation));
            }
            else if (parts is ["release"])
            {
                observation = await input.EndAsync(cancellationToken).ConfigureAwait(false);
                active = false;
                held = PlaytestButtons.None;
            }
            else if (parts is ["input", var buttonsText, var durationText]
                && Enum.TryParse(buttonsText, true, out PlaytestButtons buttons)
                && (uint)buttons <= 131071
                && double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                && double.IsFinite(seconds) && seconds is > 0 and <= 5)
            {
                if (!behaviorsAllowed && ((uint)buttons & (127U | 32768U | 65536U)) != 0)
                {
                    throw new ArgumentException("gameplay_lifecycle_gate_closed");
                }
                if (!active && context.Bodies?.Lease is null)
                {
                    await input.BeginForMenusAsync(cancellationToken).ConfigureAwait(false);
                    active = true;
                }
                observation = await input.UpdateAsync(buttons, cancellationToken: cancellationToken).ConfigureAwait(false);
                held = buttons;
                releaseAt = Stopwatch.GetTimestamp() + (long)(seconds * Stopwatch.Frequency);
                transition = ((uint)buttons & ~127U) != 0;
            }
            else if (parts is ["pointer", var pointerX, var pointerY]
                && float.TryParse(pointerX, NumberStyles.Float, CultureInfo.InvariantCulture, out float px)
                && float.TryParse(pointerY, NumberStyles.Float, CultureInfo.InvariantCulture, out float py))
            {
                if (!active && context.Bodies?.Lease is null)
                {
                    await input.BeginForMenusAsync(cancellationToken).ConfigureAwait(false);
                    active = true;
                }
                observation = await input.PointMenuAsync(px, py, cancellationToken).ConfigureAwait(false);
                transition = true;
            }
            else if (parts is ["look", var xText, var yText]
                && float.TryParse(xText, NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(yText, NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                if (!behaviorsAllowed) throw new ArgumentException("gameplay_lifecycle_gate_closed");
                if (context.Bodies?.Lease is not null)
                {
                    BodyControlObservation body = await context.Bodies.UpdateAsync(BodyMovement.None, x, y,
                        cancellationToken).ConfigureAwait(false);
                    Console.WriteLine("SKYRIM_HOST_BODY " + JsonSerializer.Serialize(body,
                        MarionetteWireJsonContext.Default.BodyControlObservation));
                    Emit(command, "completed", "", null, context, behaviorsAllowed, readySeconds, readyTicks);
                    return false;
                }
                if (!active)
                {
                    await input.BeginAsync(cancellationToken).ConfigureAwait(false);
                    active = true;
                }
                observation = await input.UpdateAsync(PlaytestButtons.None, x, y, cancellationToken).ConfigureAwait(false);
                held = PlaytestButtons.None;
            }
            else if (parts is ["save"] or ["load"])
            {
                if (!behaviorsAllowed) throw new ArgumentException("gameplay_lifecycle_gate_closed");
                if (parts[0] == "save" && ownedAgent is not null)
                {
                    await ownedAgent.PrepareSaveAsync(context, cancellationToken).ConfigureAwait(false);
                }
                observation = parts[0] == "save"
                    ? await input.SaveCheckpointAsync(cancellationToken).ConfigureAwait(false)
                    : await input.LoadCheckpointAsync(cancellationToken).ConfigureAwait(false);
                active = false;
                held = PlaytestButtons.None;
                transition = true;
            }
            else if (parts is ["owned-move", var dxText, var dyText, var dzText]
                && float.TryParse(dxText, NumberStyles.Float, CultureInfo.InvariantCulture, out float dx)
                && float.TryParse(dyText, NumberStyles.Float, CultureInfo.InvariantCulture, out float dy)
                && float.TryParse(dzText, NumberStyles.Float, CultureInfo.InvariantCulture, out float dz))
            {
                if (!behaviorsAllowed || ownedNpc is null) throw new ArgumentException("owned_move_not_ready");
                WorldNpcResult source = await context.ObserveOwnedNpcAsync(cancellationToken).ConfigureAwait(false);
                WorldNpcObservation actor = source.Npcs.Single(npc => npc.FormId == ownedNpc.FormId);
                WorldNpcResult result = await context.MoveOwnedNpcAsync(source, ownedNpc,
                    actor.PositionX + dx, actor.PositionY + dy, actor.PositionZ + dz, cancellationToken).ConfigureAwait(false);
                ownedMovementId = result.OwnedMovement?.MovementId ?? 0;
                Console.WriteLine("SKYRIM_HOST_OWNED_NPC " + JsonSerializer.Serialize(result, MarionetteWireJsonContext.Default.WorldNpcResult));
            }
            else if (parts is ["owned-cancel"])
            {
                if (ownedNpc is null || ownedMovementId == 0) throw new ArgumentException("owned_move_not_held");
                WorldNpcResult result = await context.CancelOwnedMovementAsync(ownedNpc, ownedMovementId,
                    cancellationToken).ConfigureAwait(false);
                Console.WriteLine("SKYRIM_HOST_OWNED_NPC " + JsonSerializer.Serialize(result, MarionetteWireJsonContext.Default.WorldNpcResult));
            }
            else if (parts is ["owned-spawn"] or ["owned-inspect"] or ["owned-tune"] or ["owned-release"])
            {
                WorldNpcResult result;
                if (parts[0] == "owned-spawn")
                {
                    if (!behaviorsAllowed) throw new ArgumentException("gameplay_lifecycle_gate_closed");
                    result = await context.SpawnOwnedNpcAsync(cancellationToken).ConfigureAwait(false);
                    ownedNpc = result.OwnedNpc;
                }
                else if (parts[0] == "owned-release")
                {
                    if (ownedNpc is null) throw new ArgumentException("owned_identity_not_held");
                    result = await context.ReleaseOwnedNpcAsync(ownedNpc, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    result = await context.ObserveOwnedNpcAsync(cancellationToken).ConfigureAwait(false);
                    if (parts[0] == "owned-tune")
                    {
                        if (!behaviorsAllowed || ownedNpc is null || result.OwnedNpc != ownedNpc)
                        {
                            throw new ArgumentException("owned_identity_not_held");
                        }
                        WorldNpcObservation actor = result.Npcs.Single(npc => npc.FormId == ownedNpc.FormId);
                        await context.SetBaseSpeedAsync(result, actor, actor.BaseSpeedMultiplier + 5,
                            cancellationToken, ownedNpc).ConfigureAwait(false);
                        result = await context.ObserveOwnedNpcAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                Console.WriteLine("SKYRIM_HOST_OWNED_NPC " +
                    JsonSerializer.Serialize(result, MarionetteWireJsonContext.Default.WorldNpcResult));
            }
            else if (parts is ["npc-change"])
            {
                if (!behaviorsAllowed) throw new ArgumentException("gameplay_lifecycle_gate_closed");
                priorObservation = await context.ObserveNpcsAsync(cancellationToken, refresh: true).ConfigureAwait(false);
                priorNpc = priorObservation.Npcs.FirstOrDefault(npc => npc.Loaded && !npc.Dead
                    && npc.BaseSpeedMultiplier is >= 1 and <= 295)
                    ?? throw new ArgumentException("no_ordinary_npc");
                WorldNpcResult changed = await context.SetBaseSpeedAsync(priorObservation, priorNpc,
                    priorNpc.BaseSpeedMultiplier + 5, cancellationToken).ConfigureAwait(false);
                Console.WriteLine("SKYRIM_HOST_NPC_CHANGE " + JsonSerializer.Serialize(changed, MarionetteWireJsonContext.Default.WorldNpcResult));
            }
            else if (parts is ["stale"])
            {
                if (!behaviorsAllowed || priorObservation is null || priorNpc is null)
                {
                    throw new ArgumentException("stale_probe_not_ready");
                }
                await context.SetBaseSpeedAsync(priorObservation, priorNpc,
                    priorNpc.BaseSpeedMultiplier + 5, cancellationToken).ConfigureAwait(false);
                throw new InvalidDataException("stale_probe_unexpectedly_accepted");
            }
            else throw new ArgumentException("unknown_or_out_of_range_command");
            Emit(command, "completed", "", observation, context, behaviorsAllowed, readySeconds, readyTicks);
            return transition;
        }
        catch (SkyrimBodyControlException exception)
        {
            bodyInputPending = false;
            Emit(command ?? "body-renew", "rejected", exception.Reason, null, context, behaviorsAllowed, readySeconds, readyTicks);
            return true;
        }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("body_", StringComparison.Ordinal))
        {
            bodyInputPending = false;
            Emit(command ?? "body-renew", "rejected", exception.Message, null, context, behaviorsAllowed, readySeconds, readyTicks);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or SkyrimWorldCommandDeferredException
            || exception is InvalidDataException && (exception.Message.StartsWith("playtest_failed:", StringComparison.Ordinal)
                || exception.Message.StartsWith("world_npc_command_failed:", StringComparison.Ordinal)))
        {
            held = PlaytestButtons.None;
            if (command == "save" && exception is InvalidDataException)
            {
                ownedAgent?.CancelSavePreparation();
            }
            Emit(command ?? "renew", "rejected", exception.Message, null, context, behaviorsAllowed, readySeconds, readyTicks);
            return true;
        }
    }

    private static async ValueTask EmitGameplayAsync(SkyrimBodyControl bodies, CancellationToken cancellationToken)
    {
        BodyGameplaySnapshot snapshot = await bodies.ObserveGameplayAsync(cancellationToken);
        Console.WriteLine("SKYRIM_BODY_GAMEPLAY " + JsonSerializer.Serialize(snapshot.Observation,
            MarionetteWireJsonContext.Default.BodyGameplayObservation));
    }

    private static void Emit(string command, string status, string reason, PlaytestObservation? observation,
        SkyrimHostContext context, bool allowed, double readySeconds, long readyTicks)
    {
        var result = new HostPlaytestResult(command, status, reason, observation,
            context.State.WorldGeneration, allowed, readySeconds, readyTicks);
        Console.WriteLine("SKYRIM_HOST_CONTROL " + JsonSerializer.Serialize(result, MarionetteWireJsonContext.Default.HostPlaytestResult));
    }

    public async ValueTask DisposeAsync()
    {
        shutdown.Cancel();
        try
        {
            // Windows terminal reads can ignore cancellation. They never touch
            // the transport and must not hold host shutdown waiting for a line.
            await reader.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        await input.DisposeAsync().ConfigureAwait(false);
        shutdown.Dispose();
    }
}
