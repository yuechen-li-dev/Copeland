# Aurelian agent candidate selection

M2 moves candidate selection out of Skyrim-specific orchestration and into
Dominatus policy.

## Boundary

```text
bounded Skyrim actor scan
  -> EligibleHostFixtureCandidate wire facts
  -> SkyrimCandidateLowerer
  -> opaque BodyObservation + ImportedNpcAgent
  -> AgentBodyCandidate
  -> Dominatus DecisionSlot("Aurelian.Skyrim.CandidateAgent")
  -> AcquireBodyIntent to selected agent mailbox
  -> existing bind/move/release body flow
```

`SkyrimCandidateLowerer` is the last layer that sees a FormID. It hashes the
authenticated session ID and native stable sort key into an opaque `BodyId`.
Its separate backend mapping retains the raw actor FormID. Once the selected
actor passes `evaluate_host_request`, the adapter refreshes the body to the
returned pending generation and registers `HostActorId(FormId, Generation)`
with `BodyBindingHostBackend`.

The query does not expose a host generation for every scanned actor. The
lowerer therefore mints portable session-import generation `1` and keeps the
wire runtime sequence only as `BodyObservation.Sequence`. It never disguises
that sequence as a host generation. Selection is followed by native
`evaluate_host_request`; its returned pending generation replaces the import
generation before binding. Detecting a preselection unload/reload of the same
legacy reference remains outside M2's identity guarantee.

The native query remains bounded to radius 1,024, at most eight returned
candidates, and at most 64 inspected actors. It admits only the existing
corpse-host fixture law: valid actor/reference, loaded 3D, humanoid corpse,
intact remains, non-essential, and non-protected. This conservative native
law is defense in depth and fixture-only admission for M2; it is not the
semantic ranking policy.

Aurelian repeats semantic eligibility explicitly and inspectably:

- body and candidate are loaded;
- movement and exclusive-binding capabilities are present;
- the current M2 policy requires a humanoid corpse;
- essential and protected actors are excluded.

This duplication is deliberate. Native code refuses unsafe mutation even if
managed policy is wrong, while Aurelian owns the authored reason a candidate
does or does not participate.

## Utility arbitration

Every dynamic candidate is a real `UtilityOption`. Named score factors are
reported as `base_preference`, `distance_from_player`,
`required_capabilities`, `archetype_preference`, and
`imported_provenance`. Semantic-invalid candidates score zero.
`NoSafeCandidate` scores `0.01` and routes explicitly to `NoCandidate`.

The generated selection flow uses eight authored states (including root):

```text
AwaitCandidates
  -> MaterializeAgents
  -> EvaluateCandidates
  -> RequestSelectedBinding
  -> Completed | NoCandidate | Failed
```

The coordinator receives `CandidateSetUpdated` through Dominatus' existing
typed mailbox/event bus. It creates one mailbox-ready Dominatus runtime agent
per imported Aurelian agent. The selected agent receives
`AcquireBodyIntent(AgentId, BodyId, Generation)` privately. No second event
bus or shared mutable selection channel exists.

Candidate options are normalized by `AgentId` before evaluation. Dominatus
uses zero hysteresis and zero minimum commitment for this one-shot selection,
with tie epsilon `0.0001`. First-in-order wins an initial exact tie, making
ties stable by semantic agent ID rather than native query order.

The coordinator never receives movement authority. The selected agent's
`AgentId` is written into the exclusive binding, and the existing registry
rejects movement by every non-selected agent before native lowering.

This is not possession. The player reference, input authority, inventory,
quests, factions, dialogue identity, and first-person camera are unchanged.

## M3 world-owner input

The native candidate observation now carries placed/dynamic classification and
plugin/local provenance. `SkyrimWorldOwnerRuntime` routes each materialization
through the shared registry and publishes body facts before
`CandidateSetUpdated`. The owner does not rank candidates or acquire control;
the M2 Dominatus decision and selected-agent mailbox intent remain authoritative.
