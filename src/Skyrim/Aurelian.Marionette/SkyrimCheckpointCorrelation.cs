using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Actuation.Host;
using Marionette.Skyrim;
using Dominatus.Core.Persistence;

namespace Marionette.Skyrim.App;

public sealed record SkyrimCheckpointIndexEntry(
    int FormatVersion,
    SkyrimSaveIdentity Save,
    string CheckpointArtifactId,
    string ArtifactFileName,
    DateTimeOffset CreatedUtc,
    string? ParentArtifactId,
    bool ActiveLineage,
    long SaveOperationId = 0,
    string? ArtifactSha256 = null);

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
    Staged,
    SaveOperationUnavailable,
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
    private const int IndexFormatVersion = 2;
    private const string IndexFileName = "skyrim-checkpoints.index.json";
    private readonly string directory;
    private readonly Dictionary<long, SkyrimCheckpointIndexEntry> staged = new();

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
        long operationId = DateTimeOffset.UtcNow.Ticks;
        SkyrimCheckpointResult provisional = CaptureProvisional(runtime, save, bindings, operationId);
        return provisional.Status == SkyrimCheckpointStatus.Staged
            ? Commit(operationId)
            : provisional;
    }

    public SkyrimCheckpointResult CaptureProvisional(
        SkyrimWorldOwnerRuntime runtime,
        SkyrimSaveIdentity save,
        BodyBindingRegistry bindings,
        long operationId)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(bindings);
        save.Validate();
        if (operationId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(operationId));
        }
        if (staged.TryGetValue(operationId, out SkyrimCheckpointIndexEntry? existing))
        {
            return new SkyrimCheckpointResult(SkyrimCheckpointStatus.Staged, existing);
        }
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
        string artifactHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(temporaryPath)))
            .ToLowerInvariant();
        string artifactFileName = $"{artifactHash}.dom";
        string artifactPath = Path.Combine(directory, artifactFileName);
        File.Move(temporaryPath, artifactPath, overwrite: true);

        var created = new SkyrimCheckpointIndexEntry(
            IndexFormatVersion,
            save,
            $"{save.Timeline.Session.Value:N}-{operationId}",
            artifactFileName,
            DateTimeOffset.UtcNow,
            ParentArtifactId: null,
            ActiveLineage: false,
            SaveOperationId: operationId,
            ArtifactSha256: artifactHash);
        staged.Add(operationId, created);
        return new SkyrimCheckpointResult(SkyrimCheckpointStatus.Staged, created);
    }

    public SkyrimCheckpointResult Commit(long operationId)
    {
        List<SkyrimCheckpointIndexEntry> entries = ReadIndex();
        SkyrimCheckpointIndexEntry? committed = entries.SingleOrDefault(
            entry => entry.SaveOperationId == operationId);
        if (committed is not null)
        {
            return new SkyrimCheckpointResult(SkyrimCheckpointStatus.Completed, committed);
        }
        if (!staged.Remove(operationId, out SkyrimCheckpointIndexEntry? provisional))
        {
            return new SkyrimCheckpointResult(
                SkyrimCheckpointStatus.SaveOperationUnavailable,
                FailureReason: "provisional_save_operation_unavailable");
        }

        string? parent = entries.LastOrDefault(entry => entry.ActiveLineage)?.CheckpointArtifactId;
        committed = provisional with
        {
            ParentArtifactId = parent,
            ActiveLineage = true,
        };
        entries.Add(committed);
        WriteIndex(entries);
        return new SkyrimCheckpointResult(SkyrimCheckpointStatus.Completed, committed);
    }

    public bool Abort(long operationId)
    {
        if (!staged.Remove(operationId, out SkyrimCheckpointIndexEntry? provisional))
        {
            return false;
        }
        string artifactPath = Path.Combine(directory, provisional.ArtifactFileName);
        bool committedArtifact = ReadIndex().Any(
            entry => string.Equals(entry.ArtifactFileName, provisional.ArtifactFileName, StringComparison.Ordinal));
        if (!committedArtifact && File.Exists(artifactPath))
        {
            File.Delete(artifactPath);
        }
        return true;
    }

    public SkyrimCheckpointResult Restore(
        SkyrimSaveIdentity loadedSave,
        string sessionScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionScope);
        loadedSave.Validate();
        List<SkyrimCheckpointIndexEntry> entries = ReadIndex();
        SkyrimCheckpointIndexEntry? selected = entries
            .Where(entry => ExactIdentityMatches(entry.Save, loadedSave))
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
            string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(artifactPath)))
                .ToLowerInvariant();
            string expectedHash = selected.ArtifactSha256
                ?? Path.GetFileNameWithoutExtension(selected.ArtifactFileName);
            if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
            {
                return new SkyrimCheckpointResult(
                    SkyrimCheckpointStatus.CheckpointCorrupt,
                    selected,
                    FailureReason: "checkpoint_artifact_hash_mismatch");
            }
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

        HashSet<string> activeArtifacts = BuildActiveLineage(entries, selected);
        entries = entries.Select(entry => entry with
        {
            ActiveLineage = activeArtifacts.Contains(entry.CheckpointArtifactId),
        }).ToList();
        WriteIndex(entries);

        return new SkyrimCheckpointResult(
            SkyrimCheckpointStatus.Completed,
            selected,
            restored);
    }

    public IReadOnlyList<SkyrimCheckpointIndexEntry> ReadEntries() => ReadIndex();

    private static bool ExactIdentityMatches(SkyrimSaveIdentity indexed, SkyrimSaveIdentity loaded)
    {
        if (!string.Equals(indexed.SaveName, loaded.SaveName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (loaded.StableFingerprint is not null)
        {
            return string.Equals(
                indexed.StableFingerprint,
                loaded.StableFingerprint,
                StringComparison.Ordinal);
        }
        return Math.Abs(indexed.Timeline.GameTime.GameDays - loaded.Timeline.GameTime.GameDays) < 0.000001;
    }

    private static HashSet<string> BuildActiveLineage(
        IReadOnlyList<SkyrimCheckpointIndexEntry> entries,
        SkyrimCheckpointIndexEntry selected)
    {
        Dictionary<string, SkyrimCheckpointIndexEntry> byId = entries.ToDictionary(
            entry => entry.CheckpointArtifactId,
            StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        SkyrimCheckpointIndexEntry? current = selected;
        while (current is not null && active.Add(current.CheckpointArtifactId))
        {
            current = current.ParentArtifactId is not null
                && byId.TryGetValue(current.ParentArtifactId, out SkyrimCheckpointIndexEntry? parent)
                ? parent
                : null;
        }
        return active;
    }

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
