# Aurelian Skyrim world owner

`SkyrimWorldOwnerRuntime` is the semantic owner above one authenticated
Marionette/Skyrim session. It is an `AiAgent` driven by the generated Dominatus
flow `aurelian.skyrim.world-owner.m3`.

Its persistent states are `Disconnected`, `AwaitingWorld`, `WorldReady`,
`WorldPaused`, `SaveLoading`, `RestoringCheckpoint`, `RollbackDetected`,
`RestorationRequired`, `ShuttingDown`, and `Failed`. Body commands are legal
only in `WorldReady`. Ordered `SkyrimWorldFact` messages enter through the
owner mailbox; duplicate or older sequences are ignored.

Public facts leave through the existing Dominatus event bus as typed events:
backend connected/disconnected, world ready/paused, save loading/loaded,
timeline changed, rollback, body loaded/lost, restoration required, and
shutdown. Directed work remains mailbox intent (`RequestCheckpoint`,
`RequestWorldRestore`, `ReleaseAllBindings`, and `ShutdownRequested`). There is
no second event bus. Body discovery tracks materialization; it never acquires
exclusive control or replaces M2's Dominatus ranking policy.

| Capability | Skyrim | Marionette | Aurelian | Dominatus |
| --- | --- | --- | --- | --- |
| world runtime facts | originates | translates | owns semantic lifecycle | executes owner flow |
| save/load | originates | observes | correlates timeline | checkpoints/restores |
| agent persistence | — | storage adapter | defines data meaning | owns runtime format |
| stable placed origin | plugin records | resolves | owns provenance | persists primitives |
| runtime FormID | owns | diagnostic mapping | hidden from policy | — |
| body materialization | owns | observes | tracks | reacts |
| candidate selection | supplies bodies | lowers | supplies candidates | ranks |
| body binding | materializes | lowers | owns contract | executes policy |
| scoped launch | executable | plugin transport | managed host | behavior runtime |
| fixture orchestration | — | — | — | tspack-owned outside runtime layers |
