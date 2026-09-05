# Aurelian game save and replay M6

## Outcome

Outcome A. Deliverance now provides the explicit snapshot, versioned container, migration, authenticated-encryption, and durable-slot substrate. TinyFarm uses it through a Dominatus persistence actuator and keeps replay as a separate checkpoint-plus-semantic-intent mechanism.

## Authority and data flow

Save capture is synchronous under `TinyFarmSimulationHost` authority. `TinyFarmDeliverancePersistence` copies `TinyFarmState`, next sequence, and recent semantic events into `TinyFarmSemanticSaveSnapshot`; only then may the Dominatus adapter serialize and perform IO on a worker. Load returns a candidate. The actuator queues that candidate and the application explicitly commits it while pumping completions on its authoritative thread.

```text
TinyFarm state -> explicit immutable snapshot -> Dominatus save actuation
               -> Deliverance v2 module -> slot store

slot store -> Deliverance decode/migrate/validate -> candidate
           -> Dominatus load completion -> TinyFarm host commit
```

Deliverance never walks the runtime object graph, discovers modules by reflection, or mutates the application during decode.

## Compatibility identities

| Axis | Owner | M6 behavior |
| --- | --- | --- |
| container format | Deliverance | stable `DLVR`, current writer v2, narrow v1 reader |
| module schema | TinyFarm domain module | current `tinyfarm.semantic-state` v2; explicit v1 DTO migration |
| application save version | TinyFarm composition | value 1; mismatch rejects before module commit |
| definition/content hash | TinyFarm definitions | exact match required |
| cadence configuration hash | simulation host | exact match required because cadence controls semantic time |
| replay format | TinyFarm replay envelope | version 1; independent of the save container |

## Semantic save boundary

| Included | Rebuilt or deliberately excluded |
| --- | --- |
| world and actor semantic state | renderer/GPU/window resources |
| inventory and selected hotbar slot | camera interpolation and presentation caches |
| active scene and actor placements | held input/device state |
| semantic world minute | audio devices, music/SFX voices, one-shot playback |
| NPC semantic goal/schedule state present in the world | DotRecast path plans/cache |
| authoritative sequence and recent semantic events | Spatial2D collider/broadphase cache |

After load, a new `TinyFarmSession` creates fresh resolver-owned navigation and spatial machinery. An NPC with a restored semantic movement goal has zero cached plans immediately after commit and replans on the next semantic observation. Held movement is cleared by `CommitLoadedSession`, so load cannot create stuck movement. Audio voice objects cannot enter the snapshot type; semantic state may be projected again, but one-shot voices are not replayed.

## Replay

`TinyFarmReplayEnvelope` contains replay format, application ID, definition and cadence identities, an initial semantic checkpoint and hash, and ordered `IntentEnvelope` records. `GameIntent` has explicit stable JSON discriminators. Replay changes only the source fact to `Replay` and invokes the ordinary authoritative `TinyFarmResolver`; there is no second movement or combat engine.

Replay rejects unsupported format, wrong application, definition mismatch, cadence mismatch, checkpoint mismatch, invalid index/sequence order, and per-record state-hash divergence. Divergence reports the record index and semantic sequence.

## Qualified scenarios

- Mid-action: Elias is actively wandering; the session advances, saves, is destroyed, and reloads to the identical semantic hash, sequence, and NPC position. The path cache is absent and replans.
- Scene transition: an accepted Farm transition plus hotbar slot 4 and minute 777 reload exactly. Navigation cache is empty and previously held player movement is absent.
- Migration: an actual TinyFarm schema-v1 JSON DTO is stored in Deliverance and loaded through the production v1→v2 migration before validation and commit.
- Replay: select-hotbar and attack intents replay from checkpoint through `TinyFarmResolver` to the exact final semantic hash.
- Boundaries: the snapshot's structural property set is asserted and contains no renderer, audio, input, path, or collider state.

## Storage and cryptography

The default `.dlv` filesystem store derives a confined safe filename from a semantic slot ID, uses a unique temporary, flushes before atomic replace, rotates bounded `.bak1`, `.bak2`, and later copies, and serializes writes per resolved path. Cancellation before commit leaves the primary intact. Backups are diagnostic recovery material and are never silently promoted over a corrupt primary.

Module bytes follow `serialize -> compress -> encrypt`; load reverses this. Encryption is opt-in AES-256-GCM using a fresh 96-bit nonce, a 128-bit tag, and a caller-owned 256-bit key provider. Associated data binds module identity, schema, serializer, compression, and semantic SHA-256. Unencrypted payloads still receive a semantic SHA-256 integrity check.

## Package boundary

`Deliverance.Core` remains engine-independent. `Deliverance.Dominatus` is the thin actuator adapter. TinyFarm.Runtime references both as source projects in the shared sibling-repository workspace. No Aurelian, TinyFarm, Machina, Dominatus, or Stride dependency enters Deliverance.Core.

## Evidence

The compact evidence set is in `artifacts/aurelian-game-save-replay-m6`. Executable proof lives in Deliverance's VNext, encryption, storage, and Dominatus integration tests plus `TinyFarmSaveReplayM6Tests`.

## Validation

| Command | Result |
| --- | --- |
| `dotnet test Deliverance.slnx -m:1` | 30 passed, 1 intentionally skipped fixture generator |
| `dotnet test Aurelian.slnx -m:1` | 728 passed |
| `dotnet test TinyFarm.slnx -m:1` | 312 passed |
| `dotnet test JointTaskForce.slnx -m:1` | 3,476 passed |
| Dominatus persistence/replay/checkpoint filter, net8 + net10 | 112 passed |
| `git diff --check` in Deliverance, Copeland, and Dominatus | clean |
