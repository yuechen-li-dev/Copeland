using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Actuation.Host;
using Dominatus.Core.Persistence;

namespace Aurelian.Marionette.Transport;

public sealed record SkyrimCheckpointIndexEntry(
    int FormatVersion,
    SkyrimSaveIdentity Save,
    string CheckpointArtifactId,
    string ArtifactFileName,
    DateTimeOffset CreatedUtc,
    string? ParentArtifactId,
    bool ActiveLineage);

public enum SkyrimCheckpointStatus
{
    Completed,
    ActiveBinding,
    WorldNotReady,
    RestorationUncertain,
    CheckpointUnavailable,
    CheckpointCorrupt,
    VersionMismatch,
    TimelineMismatch,
}

public sealed record SkyrimCheckpointResult(
    SkyrimCheckpointStatus Status,
    SkyrimCheckpointIndexEntry? Entry = null,
    SkyrimWorldOwnerRuntime? RestoredWorld = null,
    string? FailureReason = null)
{
    public bool Completed => Status == SkyrimCheckpointStatus.Completed;
}

/// <summary>
/// Correlates Skyrim save facts with canonical Dominatus chunk files. The JSON
/// index contains correlation metadata only; agent/HFSM/blackboard data remains
/// solely inside the DOM1 artifact produced by Dominatus.Core.
/// </summary>
public sealed class SkyrimCheckpointStore
{
    private const int IndexFormatVersion = 1;
    private const string IndexFileName = "skyrim-checkpoints.index.json";
    private readonly string directory;

    public SkyrimCheckpointStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
    }

    public SkyrimCheckpointResult Capture(
        SkyrimWorldOwnerRuntime runtime,
        SkyrimSaveIdentity save,
        BodyBindingRegistry bindings)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(bindings);
        save.Validate();
        if (runtime.RestorationIsRequired)
        {
            return new SkyrimCheckpointResult(
                SkyrimCheckpointStatus.RestorationUncertain,
                FailureReason: "checkpoint_blocked_by_uncertain_restoration");
        }
        if (!runtime.CanIssueBodyCommands)
        {
            return new SkyrimCheckpointResult(
                SkyrimCheckpointStatus.WorldNotReady,
                FailureReason: "checkpoint_requires_world_ready");
        }
        if (bindings.HasActiveExclusiveBinding)
        {
            return new SkyrimCheckpointResult(
                SkyrimCheckpointStatus.ActiveBinding,
                FailureReason: "active_exclusive_binding_must_be_released");
        }

        Directory.CreateDirectory(directory);
        DominatusCheckpoint checkpoint;
        try
        {
            checkpoint = DominatusCheckpointBuilder.Capture(runtime.DominatusWorld);
        }
        catch (InvalidOperationException exception)
        {
            return new SkyrimCheckpointResult(
                SkyrimCheckpointStatus.RestorationUncertain,
                FailureReason: exception.Message);
        }

        IReadOnlyList<SaveChunk> chunks = DominatusSave.CreateCheckpointChunks(checkpoint);
        string temporaryPath = Path.Combine(directory, $"checkpoint-{Guid.NewGuid():N}.tmp");
        SaveFile.Write(temporaryPath, chunks);
        string artifactId = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(temporaryPath)))
            .ToLowerInvariant();
        string artifactFileName = $"{artifactId}.dom";
        string artifactPath = Path.Combine(directory, artifactFileName);
        File.Move(temporaryPath, artifactPath, overwrite: true);

        List<SkyrimCheckpointIndexEntry> entries = ReadIndex();
        string? parent = entries.LastOrDefault(entry => entry.ActiveLineage)?.CheckpointArtifactId;
        var created = new SkyrimCheckpointIndexEntry(
            IndexFormatVersion,
            save,
            artifactId,
            artifactFileName,
            DateTimeOffset.UtcNow,
            parent,
            ActiveLineage: true);
        entries.Add(created);
        WriteIndex(entries);
        return new SkyrimCheckpointResult(SkyrimCheckpointStatus.Completed, created);
    }

    public SkyrimCheckpointResult Restore(
        SkyrimSaveIdentity loadedSave,
        string sessionScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionScope);
        loadedSave.Validate();
        List<SkyrimCheckpointIndexEntry> entries = ReadIndex();
        SkyrimCheckpointIndexEntry? selected = entries
            .Where(entry => string.Equals(
                entry.Save.SaveName,
                loadedSave.SaveName,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.CreatedUtc)
            .FirstOrDefault();
        selected ??= entries
            .Where(entry => entry.Save.Timeline.GameTime.GameDays
                <= loadedSave.Timeline.GameTime.GameDays)
            .OrderByDescending(entry => entry.Save.Timeline.GameTime.GameDays)
            .ThenByDescending(entry => entry.Save.Timeline.Sequence)
            .FirstOrDefault();
        if (selected is null)
        {
            return new SkyrimCheckpointResult(
                SkyrimCheckpointStatus.CheckpointUnavailable,
                FailureReason: "matching_historical_checkpoint_unavailable");
        }

        string artifactPath = Path.Combine(directory, selected.ArtifactFileName);
        DominatusCheckpoint checkpoint;
        try
        {
            IReadOnlyList<SaveChunk> chunks = SaveFile.Read(artifactPath);
            (checkpoint, _) = DominatusSave.ReadCheckpointChunks(chunks);
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("version", StringComparison.OrdinalIgnoreCase))
        {
            return new SkyrimCheckpointResult(
                SkyrimCheckpointStatus.VersionMismatch,
                selected,
                FailureReason: exception.Message);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or InvalidOperationException)
        {
            return new SkyrimCheckpointResult(
                SkyrimCheckpointStatus.CheckpointCorrupt,
                selected,
                FailureReason: exception.Message);
        }

        var restored = new SkyrimWorldOwnerRuntime(sessionScope);
        try
        {
            foreach (AgentCheckpoint agentCheckpoint in checkpoint.Agents.Skip(1))
            {
                Dictionary<string, object> values = BbJsonCodec.DeserializeSnapshot(
                    agentCheckpoint.BlackboardBlob);
                if (!values.ContainsKey(SkyrimWorldCheckpointKeys.SemanticAgentId.Name))
                {
                    continue;
                }

                restored.AddRestoredImportedRuntimeAgent(ReadImportedAgent(values));
            }

            DominatusCheckpointBuilder.Restore(restored.DominatusWorld, checkpoint);
            restored.RestoreSemanticStateFromBlackboards();
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or ArgumentException or OverflowException)
        {
            return new SkyrimCheckpointResult(
                SkyrimCheckpointStatus.CheckpointCorrupt,
                selected,
                FailureReason: exception.Message);
        }

        entries = entries.Select(entry => entry with
        {
            ActiveLineage = entry.Save.Timeline.GameTime.GameDays
                <= loadedSave.Timeline.GameTime.GameDays,
        }).ToList();
        WriteIndex(entries);

        return new SkyrimCheckpointResult(
            SkyrimCheckpointStatus.Completed,
            selected,
            restored);
    }

    public IReadOnlyList<SkyrimCheckpointIndexEntry> ReadEntries() => ReadIndex();

    private static ImportedNpcAgent ReadImportedAgent(IReadOnlyDictionary<string, object> values)
    {
        Guid semanticId = (Guid)values[SkyrimWorldCheckpointKeys.SemanticAgentId.Name];
        string plugin = (string)values[SkyrimWorldCheckpointKeys.PluginName.Name];
        uint localFormId = checked((uint)(long)values[SkyrimWorldCheckpointKeys.LocalFormId.Name]);
        var placed = new SkyrimPlacedActorOrigin(plugin, localFormId);
        SkyrimActorOrigin origin = SkyrimActorOrigin.ForPlaced(placed);
        return new ImportedNpcAgent(
            new Aurelian.Actuation.Host.AgentId(semanticId),
            new AgentProvenance(
                AgentProvenanceKind.ImportedLegacy,
                "Skyrim/Marionette",
                origin.StableKey)
            {
                SkyrimOrigin = origin,
            },
            new ImportedNpcData(
                new IdentityProfile(
                    (string)values[SkyrimWorldCheckpointKeys.DisplayName.Name],
                    (string)values[SkyrimWorldCheckpointKeys.Archetype.Name]),
                new BodyProfile(
                    (bool)values[SkyrimWorldCheckpointKeys.Humanoid.Name],
                    (bool)values[SkyrimWorldCheckpointKeys.Essential.Name],
                    (bool)values[SkyrimWorldCheckpointKeys.Protected.Name]),
                SelectionProfile.ImportedDefault));
    }

    private List<SkyrimCheckpointIndexEntry> ReadIndex()
    {
        string path = Path.Combine(directory, IndexFileName);
        if (!File.Exists(path))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<SkyrimCheckpointIndexEntry>>(
            File.ReadAllText(path)) ?? [];
    }

    private void WriteIndex(IReadOnlyList<SkyrimCheckpointIndexEntry> entries)
    {
        string path = Path.Combine(directory, IndexFileName);
        string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(path, json + Environment.NewLine);
    }
}
