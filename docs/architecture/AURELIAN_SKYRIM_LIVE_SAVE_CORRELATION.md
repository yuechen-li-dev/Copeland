# Aurelian Skyrim live save correlation

## Callback evidence

The inspected SKSE save/load hook dispatches `kSaveGame` immediately before the
engine save routine with the save name, `kPreLoadGame` immediately before the
engine reads a save with the same kind of name, and `kPostLoadGame` after the
load routine returns. `kPostLoadGame` carries success as a pointer-sized boolean:
non-null is success and null is failure. `kNewGame` is separate. The SKSE
serialization interface separately calls Marionette's save, load, and revert
callbacks.

There is no SKSE post-save-success message. M4a therefore records two honest
save facts: `save_started` from `kSaveGame`, then `save_serialized` from the
serialization save callback. The latter is completion evidence for the SKSE
co-save contribution, not proof that every later filesystem action by Skyrim
succeeded. A checkpoint is provisional until this second fact. Cancellation
after serialization is the remaining reliability boundary.

The exact observed ordering is `kSaveGame` → serialization save callback for a
save, and `kPreLoadGame` → serialization revert/load callbacks →
`kPostLoadGame(success)` for a load. The pre-load calendar value belongs to the
currently running world; the post-load value belongs to the loaded world.
`kPostLoadGame(false)` is terminal and never restores. SKSE exposes no matching
main-menu or game-shutdown message in this interface; tspack process lifetime
supplies shutdown, while Marionette records revert separately. Quicksave,
autosave, and manual save type are preserved only insofar as SKSE includes that
distinction in the supplied name.

All callbacks run in the hooked Skyrim save/load path. Marionette reads
`RE::Calendar::GetCurrentGameTime()` there, copies only values, appends them to a
64-entry queue, and returns. The pipe worker neither queries engine objects nor
performs restore.

## Identity and ordering

The bounded identity is:

```text
normalized symbolic save name
+ Skyrim game time in days
+ process-local operation id
+ optional stable fingerprint (managed contract, unavailable from SKSE today)
```

Names are trimmed, an SKSE-supplied path is lowered to its basename, `.ess` is
removed case-insensitively, and empty names, traversal, control characters,
colons, or basenames over 240 bytes are rejected. Raw callback data is capped at
1024 bytes and directory details never cross the wire.
Comparison is case-insensitive for the symbolic name. Manual, quicksave, and
autosave spelling remains intact. An overwritten name matches exactly only when
its loaded game time (or a future supplied fingerprint) also matches; name alone
does not select the newest revision.

Native lifecycle sequence is monotonic per plugin process. Operation IDs join
pre/post facts. Duplicate terminal facts are ignored, stale managed sequences
are rejected, and a new game clears pending load correlation.

## Runtime path

```text
Skyrim callback
→ bounded native lifecycle queue
→ query_lifecycle_observations
→ SkyrimLiveLifecycleCoordinator
→ SkyrimWorldOwnerRuntime mailbox
→ SkyrimCheckpointStore
→ Dominatus.Core capture/restore
```

Load start gates body commands, publishes `ReleaseAllBindings`, and enters
`SaveLoading`. If an exclusive binding remains active, the owner enters
`RestorationRequired`. Load failure returns the existing safe world to
`WorldReady` and never restores. Load success selects a checkpoint and creates a
fresh owner before restore. Missing, corrupt, hash-mismatched, or incompatible
artifacts are explicit restoration failures.

The checkpoint calls remain `DominatusCheckpointBuilder.Capture`,
`DominatusSave.CreateCheckpointChunks`, `SaveFile.Write`, `SaveFile.Read`,
`DominatusSave.ReadCheckpointChunks`, and
`DominatusCheckpointBuilder.Restore`. Files retain the `DOM1` header. The JSON
index stores only correlation, artifact hash/name, version, parent, operation,
time, and lineage state.

> Skyrim save/load callbacks select historical Dominatus checkpoints. They do
> not serialize Aurelian agent state themselves.

> After restore, semantic agents survive; Skyrim bodies are rediscovered and
> rebound as fresh materializations.

## Ownership

| Capability | Skyrim | Marionette | Aurelian | Dominatus | tspack |
| --- | --- | --- | --- | --- | --- |
| save/load callbacks | originates | observes/translates | world owner owns lifecycle | — | orchestrates fixture |
| save identity | supplies facts | normalizes/maps | correlates | — | scopes storage |
| checkpoint payload | — | storage boundary only | requests/selects | owns format/restore | stores artifacts |
| rollback detection | supplies loaded timeline | transports | owns policy | restores selected state | — |
| semantic agent identity | — | maps origins | owns | persists | — |
| body rematerialization | owns actor refs | observes | reconnects | reacts | — |
| active binding | materializes | lowers release | owns contract | executes policy | — |
