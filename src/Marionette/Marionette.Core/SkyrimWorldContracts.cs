using System.Text.Json.Serialization;

using Aurelian.Actuation.Host;

namespace Marionette.Skyrim;

public readonly record struct SkyrimSessionId
{
    [JsonConstructor]
    public SkyrimSessionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Skyrim session identity cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }
}

/// <summary>Skyrim Calendar.GetCurrentGameTime(), measured in game days.</summary>
public readonly record struct SkyrimGameTimestamp
{
    [JsonConstructor]
    public SkyrimGameTimestamp(double gameDays)
    {
        if (!double.IsFinite(gameDays) || gameDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gameDays));
        }

        GameDays = gameDays;
    }

    public double GameDays { get; }
}

public sealed record SkyrimTimelineStamp(
    SkyrimSessionId Session,
    SkyrimGameTimestamp GameTime,
    long Sequence);

public sealed record SkyrimSaveIdentity(
    string SaveName,
    SkyrimTimelineStamp Timeline,
    long Revision = 0,
    string? StableFingerprint = null)
{
    public SkyrimSaveIdentity Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SaveName);
        if (SaveName.Length > 240 || SaveName.Contains('/') || SaveName.Contains('\\')
            || SaveName.Contains("..", StringComparison.Ordinal) || SaveName.Contains(':'))
        {
            throw new ArgumentException("Skyrim save identity must be a bounded symbolic name.", nameof(SaveName));
        }
        if (Timeline.Sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Timeline));
        }
        if (Revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Revision));
        }

        return this;
    }
}

public enum SkyrimWorldFactKind
{
    BackendConnected,
    BackendDisconnected,
    WorldReady,
    WorldPaused,
    SaveLoading,
    SaveLoaded,
    SaveOperationStarted,
    SaveOperationCompleted,
    LoadOperationStarted,
    LoadOperationCompleted,
    LoadOperationFailed,
    NewGameStarted,
    RevertOccurred,
    BodyLoaded,
    BodyLost,
    RestorationRequired,
    ShutdownRequested,
}

public sealed record SkyrimWorldFact(
    SkyrimWorldFactKind Kind,
    long Sequence,
    SkyrimTimelineStamp? Timeline = null,
    SkyrimSaveIdentity? Save = null,
    BodyObservation? Body = null,
    SkyrimActorOrigin? Origin = null,
    ImportedNpcData? ImportedData = null,
    string? Reason = null,
    long OperationId = 0,
    string? SourceCallback = null);

public sealed record SkyrimBackendConnected(long Sequence);
public sealed record SkyrimBackendDisconnected(long Sequence, string? Reason);
public sealed record SkyrimWorldReady(SkyrimTimelineStamp Timeline);
public sealed record SkyrimWorldPaused(SkyrimTimelineStamp Timeline);
public sealed record SkyrimSaveLoading(SkyrimSaveIdentity Save);
public sealed record SkyrimSaveLoaded(SkyrimSaveIdentity Save);
public sealed record SkyrimSaveOperationStarted(long OperationId, SkyrimSaveIdentity Save, string SourceCallback);
public sealed record SkyrimSaveOperationCompleted(long OperationId, SkyrimSaveIdentity Save, string SourceCallback);
public sealed record SkyrimLoadOperationStarted(long OperationId, SkyrimSaveIdentity Save, string SourceCallback);
public sealed record SkyrimLoadOperationCompleted(long OperationId, SkyrimSaveIdentity Save, string SourceCallback);
public sealed record SkyrimLoadOperationFailed(long OperationId, SkyrimSaveIdentity Save, string SourceCallback);
public sealed record SkyrimNewGameStarted(long OperationId, SkyrimTimelineStamp? Timeline);
public sealed record SkyrimRevertOccurred(long OperationId, SkyrimTimelineStamp? Timeline);
public sealed record SkyrimTimelineChanged(SkyrimTimelineStamp Previous, SkyrimTimelineStamp Current);
public sealed record SkyrimRollbackDetected(SkyrimTimelineStamp Previous, SkyrimTimelineStamp Loaded);
public sealed record SkyrimBodyLoaded(AgentId Agent, BodyObservation Body, SkyrimActorOrigin Origin, bool Rematerialized);
public sealed record SkyrimBodyLost(AgentId Agent, BodyId Body);
public sealed record SkyrimRestorationRequired(string Reason);
public sealed record SkyrimSessionShuttingDown(long Sequence);
public sealed record SkyrimWorldRestored(SkyrimSaveIdentity Save, string CheckpointArtifactId);
public sealed record SkyrimTimelineRebased(SkyrimTimelineStamp Timeline, string CheckpointArtifactId);

public sealed record RequestCheckpoint(SkyrimSaveIdentity Save);
public sealed record RequestWorldRestore(SkyrimSaveIdentity Save);
public sealed record ReleaseAllBindings(string Reason);
public sealed record ShutdownRequested(string Reason);
