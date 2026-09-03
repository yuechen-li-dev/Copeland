using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Tson;

namespace TinyFarm.Core;

public enum TinyFarmSimulationMode
{
    Paused,
    Playing,
    FastForward
}

public sealed record TinyFarmSimulationRates(
    int NormalRealSecondsPerGameMinute = 5,
    int FastForwardMultiplier = 10,
    int LocomotionHz = 60,
    int MaximumHostDeltaSeconds = 5)
{
    public static TinyFarmSimulationRates Default { get; } = new();
}

public abstract record TinyFarmSimulationCommand;

public sealed record SetSimulationModeCommand(TinyFarmSimulationMode Mode) : TinyFarmSimulationCommand;

public sealed record AdvanceMinutesCommand(int Minutes) : TinyFarmSimulationCommand;

public readonly record struct TinyFarmHostAdvanceResult(
    long HostTicksAccepted,
    long HostTicksDiscarded,
    int WorldMinutesAdvanced,
    long LocomotionStepsAdvanced,
    IReadOnlyList<IntentResult> Results,
    IReadOnlyList<NarrativeLine> Narrative);

public sealed class TinyFarmSimulationHost
{
    private readonly TinyFarmDefinitions definitions;
    private readonly TinyFarmSimulationRates rates;
    private long worldTickAccumulator;
    private long locomotionTickNumerator;
    private int playerMovementX;
    private int playerMovementY;

    public TinyFarmSimulationHost(
        TinyFarmSession session,
        TinyFarmDefinitions definitions,
        TinyFarmSimulationMode initialMode = TinyFarmSimulationMode.Paused,
        TinyFarmSimulationRates? rates = null)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        this.definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        this.rates = rates ?? TinyFarmSimulationRates.Default;
        ValidateRates(this.rates);
        Session.EnableFixedNpcLocomotion();
        Mode = initialMode;
    }

    public TinyFarmSession Session { get; private set; }

    public TinyFarmSimulationMode Mode { get; private set; }

    public TinyFarmSimulationRates Rates => rates;

    public long HostUpdates { get; private set; }

    public long RenderFramesObserved { get; private set; }

    public long WorldMinutesAdvanced { get; private set; }

    public long LocomotionStepsAdvanced { get; private set; }
    public long PlayerLocomotionReductions { get; private set; }
    public long NpcLocomotionReductions => Session.NpcLocomotionReductionCount;
    public long AnchorArrivals => Session.AnchorArrivalCount;

    public void ObserveRenderFrame()
    {
        RenderFramesObserved++;
    }

    public void SetPlayerMovement(int deltaX, int deltaY)
    {
        if (deltaX == 0 && deltaY == 0)
        {
            playerMovementX = 0;
            playerMovementY = 0;
            return;
        }
        if (Math.Abs(deltaX) + Math.Abs(deltaY) != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaX), "Movement must be one cardinal direction or zero.");
        }
        if (deltaX != playerMovementX || deltaY != playerMovementY)
        {
        }
        playerMovementX = deltaX;
        playerMovementY = deltaY;
    }

    public void Execute(TinyFarmSimulationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        switch (command)
        {
            case SetSimulationModeCommand setMode:
                Mode = setMode.Mode;
                break;
            case AdvanceMinutesCommand advance:
                AdvanceMinutes(advance.Minutes);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown TinyFarm simulation command.");
        }
    }

    public TinyFarmStepResult ExecuteIntent(GameIntent intent)
    {
        bool evaluateNpcDecisions = intent is not SpatialMoveIntent
            and not SelectHotbarSlotIntent;
        return Session.Step(intent, evaluateNpcDecisions);
    }

    public TinyFarmHostAdvanceResult AdvanceHostTime(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed), "Host elapsed time cannot be negative.");
        }

        HostUpdates++;
        if (Mode == TinyFarmSimulationMode.Paused || elapsed == TimeSpan.Zero)
        {
            return new TinyFarmHostAdvanceResult(0, 0, 0, 0, [], []);
        }

        long maximumTicks = checked((long)rates.MaximumHostDeltaSeconds * TimeSpan.TicksPerSecond);
        long acceptedTicks = Math.Min(elapsed.Ticks, maximumTicks);
        long discardedTicks = elapsed.Ticks - acceptedTicks;
        int multiplier = Mode == TinyFarmSimulationMode.FastForward
            ? rates.FastForwardMultiplier
            : 1;

        long semanticTicks = checked(acceptedTicks * multiplier);
        long ticksPerGameMinute = checked((long)rates.NormalRealSecondsPerGameMinute * TimeSpan.TicksPerSecond);
        long locomotionBefore = LocomotionStepsAdvanced;
        int minutesBefore = checked((int)WorldMinutesAdvanced);
        var results = new List<IntentResult>();
        var narrative = new List<NarrativeLine>();

        while (semanticTicks > 0)
        {
            long ticksToLocomotion = DivideRoundUp(
                TimeSpan.TicksPerSecond - locomotionTickNumerator,
                rates.LocomotionHz);
            long ticksToMinute = ticksPerGameMinute - worldTickAccumulator;
            long advance = Math.Min(semanticTicks, Math.Min(ticksToLocomotion, ticksToMinute));
            semanticTicks -= advance;
            worldTickAccumulator += advance;
            locomotionTickNumerator += checked(advance * rates.LocomotionHz);

            if (locomotionTickNumerator >= TimeSpan.TicksPerSecond)
            {
                locomotionTickNumerator -= TimeSpan.TicksPerSecond;
                AdvanceLocomotion(results, narrative);
            }
            if (worldTickAccumulator >= ticksPerGameMinute)
            {
                worldTickAccumulator -= ticksPerGameMinute;
                TinyFarmHostAdvanceResult minute = AdvanceMinutes(1);
                results.AddRange(minute.Results);
                narrative.AddRange(minute.Narrative);
            }
        }

        return new TinyFarmHostAdvanceResult(
            acceptedTicks,
            discardedTicks,
            checked((int)WorldMinutesAdvanced - minutesBefore),
            LocomotionStepsAdvanced - locomotionBefore,
            results,
            narrative);
    }

    public TinyFarmHostAdvanceResult AdvanceMinutes(int minutes)
    {
        if (minutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes));
        }

        var results = new List<IntentResult>();
        var narrative = new List<NarrativeLine>();
        for (int remaining = minutes; remaining > 0; remaining--)
        {
            TinyFarmStepResult step;
            try
            {
                step = Session.Step(new WaitIntent(1));
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"TinyFarm simulation failed while advancing authoritative minute {Session.State.Minute + 1}.",
                    exception);
            }
            results.AddRange(step.Results);
            narrative.AddRange(step.Narrative);
        }

        WorldMinutesAdvanced += minutes;
        return new TinyFarmHostAdvanceResult(0, 0, minutes, 0, results, narrative);
    }

    public void ReplaceSession(TinyFarmSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Session.EnableFixedNpcLocomotion();
        worldTickAccumulator = 0;
        locomotionTickNumerator = 0;
    }

    private void AdvanceLocomotion(List<IntentResult> results, List<NarrativeLine> narrative)
    {
        LocomotionStepsAdvanced++;
        if (playerMovementX != 0 || playerMovementY != 0)
        {
            TinyFarmStepResult player = Session.AdvancePlayerLocomotion(
                playerMovementX,
                playerMovementY,
                ScenePosition.UnitsPerTile / 8);
            PlayerLocomotionReductions++;
            results.AddRange(player.Results);
            narrative.AddRange(player.Narrative);
        }

        if (Session.HasActiveNpcNavigation)
        {
            TinyFarmStepResult npc = Session.AdvanceActiveNpcLocomotionWithoutStateSnapshot();
            results.AddRange(npc.Results);
            narrative.AddRange(npc.Narrative);
        }
    }

    private static long DivideRoundUp(long numerator, int denominator)
    {
        return (numerator + denominator - 1) / denominator;
    }

    public TinyFarmSimulationSnapshot Snapshot()
    {
        return TinyFarmSimulationSnapshotProjector.Project(Session, definitions, Mode);
    }

    private static void ValidateRates(TinyFarmSimulationRates rates)
    {
        if (rates.NormalRealSecondsPerGameMinute <= 0
            || rates.FastForwardMultiplier <= 0
            || rates.LocomotionHz <= 0
            || rates.MaximumHostDeltaSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rates), "Simulation rates must be positive.");
        }
    }
}

public sealed record TinyFarmSimulationActorSnapshot(
    ActorId Id,
    SceneId Scene,
    ScenePosition Position,
    int Energy,
    bool IsResting,
    TinyFarmScheduleRegime Regime,
    SceneAnchorId Goal);

public sealed record TinyFarmSimulationSnapshot(
    string Version,
    TinyFarmSimulationMode SimulationMode,
    int Day,
    int Minute,
    SceneId? ActiveScene,
    IReadOnlyList<TinyFarmSimulationActorSnapshot> Actors,
    string StateHash,
    TinyFarmPlayerUiView? PlayerUi = null,
    IReadOnlyList<string>? GroundItems = null,
    string? InteractionTarget = null,
    IReadOnlyList<string>? Plots = null);

public static class TinyFarmSimulationSnapshotProjector
{
    public static TinyFarmSimulationSnapshot Project(
        TinyFarmSession session,
        TinyFarmDefinitions definitions,
        TinyFarmSimulationMode mode)
    {
        TinyFarmState state = session.State;
        TinyFarmSimulationActorSnapshot[] actors = state.Actors
            .Where(actor => !actor.IsPlayer)
            .OrderBy(actor => actor.Id.Value, StringComparer.Ordinal)
            .Select(actor =>
            {
                ActorEnergyState energy = state.EnergyFor(actor.Id);
                TinyFarmScheduleDecision decision = TinyFarmNpcSchedule.Decide(
                    definitions.Schedules,
                    actor.Id,
                    state.Minute,
                    TinyFarmNpcController.CurrentAnchor(state, actor, definitions.Scenes, definitions.Schedules),
                    energy: energy.Energy);
                ActorSceneState placement = state.ActorScene(actor.Id);
                SceneAnchorId goal = placement.Scene == state.CurrentScene
                    ? session.NavigationTargetFor(actor.Id) ?? decision.SelectedAnchor
                    : decision.SelectedAnchor;
                return new TinyFarmSimulationActorSnapshot(
                    actor.Id,
                    placement.Scene,
                    placement.WorldPosition,
                    energy.Energy,
                    energy.IsResting,
                    decision.Regime,
                    goal);
            })
            .ToArray();
        TinyFarmPlayerUiView? playerUi = state.Version >= TinyFarmState.PlayerUiSaveVersion
            ? TinyFarmPlayerUiProjector.Project(state, definitions)
            : null;
        bool hasItemActions = state.Version >= TinyFarmState.ItemActionSaveVersion;
        InteractionTarget? target = hasItemActions
            ? TinyFarmSpatialQueries.SelectInteractionTarget(state, TinyFarmIds.Player, definitions.Scenes)
            : null;
        IReadOnlyList<string>? groundItems = hasItemActions
            ? state.Items
                .Where(item => item.Owner is null && item.GroundScene is not null && item.GroundPosition is not null)
                .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
                .Select(item => $"{item.Id.Value}:{item.GroundScene!.Value.Value}:{item.GroundPosition!.Value.XUnits}:{item.GroundPosition.Value.YUnits}")
                .ToArray()
            : null;
        IReadOnlyList<string>? plots = hasItemActions
            ? state.FarmPlots
                .OrderBy(plot => plot.Id.Value, StringComparer.Ordinal)
                .Select(plot => $"{plot.Id.Value}:{plot.Crop?.Value ?? string.Empty}:{plot.GrowthStage}")
                .ToArray()
            : null;
        return new TinyFarmSimulationSnapshot(
            hasItemActions
                ? "tiny-farm-simulation@3"
                : playerUi is null ? "tiny-farm-simulation@1" : "tiny-farm-simulation@2",
            mode,
            state.Day,
            state.Minute,
            state.CurrentScene,
            actors,
            TinyFarmSemanticHash.Compute(state),
            playerUi,
            groundItems,
            target?.StableId,
            plots);
    }

    public static string WriteCanonicalTson(TinyFarmSimulationSnapshot snapshot)
    {
        var text = new StringBuilder();
        string schemaVersion = snapshot.GroundItems is not null
            ? "v3"
            : snapshot.PlayerUi is null ? "v1" : "v2";
        text.AppendLine($"const $schema: string = \"copeland://tiny-farm/simulation-snapshot/{schemaVersion}\";");
        text.AppendLine();
        text.AppendLine("record ActorId { value: string; }");
        text.AppendLine("record SceneId { value: string; }");
        text.AppendLine("record SceneAnchorId { value: string; }");
        text.AppendLine("enum SimulationMode { Paused, Playing, FastForward, }");
        text.AppendLine("enum ScheduleRegime { Required, Open, }");
        text.AppendLine();
        text.AppendLine("record ActorSnapshot {");
        text.AppendLine("    id: ActorId;");
        text.AppendLine("    scene: SceneId;");
        text.AppendLine("    xUnits: number;");
        text.AppendLine("    yUnits: number;");
        text.AppendLine("    energy: number;");
        text.AppendLine("    isResting: boolean;");
        text.AppendLine("    regime: ScheduleRegime;");
        text.AppendLine("    goal: SceneAnchorId;");
        text.AppendLine("}");
        text.AppendLine();
        text.AppendLine("record SimulationSnapshot {");
        text.AppendLine("    version: string;");
        text.AppendLine("    simulationMode: SimulationMode;");
        text.AppendLine("    day: number;");
        text.AppendLine("    minute: number;");
        text.AppendLine("    activeScene: string;");
        text.AppendLine("    actors: ActorSnapshot[];");
        text.AppendLine("    stateHash: string;");
        if (snapshot.PlayerUi is not null)
        {
            text.AppendLine("    money: number;");
            text.AppendLine("    selectedHotbarSlot: number;");
            text.AppendLine("    selectedSemanticId: string;");
            text.AppendLine("    inventorySummary: string[];");
            text.AppendLine("    hotbarSummary: string[];");
            if (snapshot.GroundItems is not null)
            {
                text.AppendLine("    groundItemSummary: string[];");
                text.AppendLine("    interactionTarget: string;");
                text.AppendLine("    plotSummary: string[];");
            }
        }
        text.AppendLine("}");
        text.AppendLine();
        text.AppendLine("const $value = $record.SimulationSnapshot({");
        AppendString(text, "version", snapshot.Version, 1);
        text.AppendLine($"    \"simulationMode\": SimulationMode.{snapshot.SimulationMode},");
        AppendNumber(text, "day", snapshot.Day, 1);
        AppendNumber(text, "minute", snapshot.Minute, 1);
        AppendString(text, "activeScene", snapshot.ActiveScene?.Value ?? string.Empty, 1);
        text.AppendLine("    \"actors\": [");
        foreach (TinyFarmSimulationActorSnapshot actor in snapshot.Actors)
        {
            text.AppendLine("        $record.ActorSnapshot({");
            AppendIdentity(text, "id", "ActorId", actor.Id.Value, 3);
            AppendIdentity(text, "scene", "SceneId", actor.Scene.Value, 3);
            AppendNumber(text, "xUnits", actor.Position.XUnits, 3);
            AppendNumber(text, "yUnits", actor.Position.YUnits, 3);
            AppendNumber(text, "energy", actor.Energy, 3);
            text.AppendLine($"            \"isResting\": {actor.IsResting.ToString().ToLowerInvariant()},");
            text.AppendLine($"            \"regime\": ScheduleRegime.{actor.Regime},");
            AppendIdentity(text, "goal", "SceneAnchorId", actor.Goal.Value, 3);
            text.AppendLine("        }),");
        }
        text.AppendLine("    ],");
        AppendString(text, "stateHash", snapshot.StateHash, 1);
        if (snapshot.PlayerUi is TinyFarmPlayerUiView playerUi)
        {
            AppendNumber(text, "money", playerUi.Money, 1);
            AppendNumber(text, "selectedHotbarSlot", playerUi.SelectedSlot.Value, 1);
            AppendString(text, "selectedSemanticId", playerUi.SelectedSemanticId ?? string.Empty, 1);
            AppendStringArray(
                text,
                "inventorySummary",
                playerUi.Inventory.Select(item => $"{item.SemanticId}:{item.Count}"),
                1);
            AppendStringArray(
                text,
                "hotbarSummary",
                playerUi.Hotbar.Select(slot => $"{slot.Slot.Value}:{slot.SemanticId ?? string.Empty}:{slot.Count}"),
                1);
            if (snapshot.GroundItems is not null)
            {
                AppendStringArray(text, "groundItemSummary", snapshot.GroundItems, 1);
                AppendString(text, "interactionTarget", snapshot.InteractionTarget ?? string.Empty, 1);
                AppendStringArray(text, "plotSummary", snapshot.Plots ?? [], 1);
            }
        }
        text.AppendLine("});");
        string authored = text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        TsonReadResult read = TsonDocumentReader.ReadSelfDescribed(
            authored,
            TsonDocumentProfile.ObjectTypeScript);
        if (!read.Success || read.Document is null)
        {
            string diagnostics = string.Join("; ", read.SyntaxDiagnostics
                .Select(item => item.ToString())
                .Concat(read.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
            throw new InvalidOperationException($"TinyFarm simulation snapshot TSON is invalid: {diagnostics}");
        }
        return TsonCanonicalPrinter.Print(read.Document);
    }

    public static string ComputeTsonHash(TinyFarmSimulationSnapshot snapshot)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WriteCanonicalTson(snapshot))))
            .ToLowerInvariant();
    }

    private static void AppendString(StringBuilder text, string name, string value, int indentation)
    {
        string escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        text.Append(' ', indentation * 4).Append('"').Append(name).Append("\": \"").Append(escaped).AppendLine("\",");
    }

    private static void AppendNumber(StringBuilder text, string name, int value, int indentation)
    {
        string bits = BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);
        text.Append(' ', indentation * 4).Append('"').Append(name).Append("\": $number(\"").Append(bits).AppendLine("\"),");
    }

    private static void AppendStringArray(
        StringBuilder text,
        string name,
        IEnumerable<string> values,
        int indentation)
    {
        text.Append(' ', indentation * 4).Append('"').Append(name).AppendLine("\": [");
        foreach (string value in values)
        {
            string escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
            text.Append(' ', (indentation + 1) * 4).Append('"').Append(escaped).AppendLine("\",");
        }
        text.Append(' ', indentation * 4).AppendLine("],");
    }

    private static void AppendIdentity(
        StringBuilder text,
        string name,
        string type,
        string value,
        int indentation)
    {
        string escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        text.Append(' ', indentation * 4).Append('"').Append(name).Append("\": $record.").Append(type).AppendLine("({");
        text.Append(' ', (indentation + 1) * 4).Append("\"value\": \"").Append(escaped).AppendLine("\",");
        text.Append(' ', indentation * 4).AppendLine("}),");
    }
}

public static class TinyFarmSimulationCommandParser
{
    public static TinyFarmSimulationCommand Parse(string command)
    {
        string[] parts = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && parts[0].Equals("pause", StringComparison.OrdinalIgnoreCase))
        {
            return new SetSimulationModeCommand(TinyFarmSimulationMode.Paused);
        }
        if (parts.Length == 1 && parts[0].Equals("play", StringComparison.OrdinalIgnoreCase))
        {
            return new SetSimulationModeCommand(TinyFarmSimulationMode.Playing);
        }
        if (parts.Length == 1 && parts[0].Equals("fast-forward", StringComparison.OrdinalIgnoreCase))
        {
            return new SetSimulationModeCommand(TinyFarmSimulationMode.FastForward);
        }
        if (parts.Length == 2
            && parts[0].Equals("advance", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
            && minutes >= 0)
        {
            return new AdvanceMinutesCommand(minutes);
        }
        throw new FormatException("Expected pause, play, fast-forward, or advance <minutes>.");
    }
}
