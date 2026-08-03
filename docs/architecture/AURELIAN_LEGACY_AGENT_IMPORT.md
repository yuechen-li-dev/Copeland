# Aurelian legacy-agent import

Existing Skyrim actors are imported as Aurelian agents. Their bodies remain
Skyrim materializations, while their semantic identity and future behavior
move into Aurelian.

## Identity and provenance

`ImportedAgentRegistry` is scoped to one authenticated Aurelian/Marionette
session. It accepts only a portable `BodyObservation`; it never receives or
parses a FormID. The first observation creates an immutable
`ImportedNpcAgent(AgentId, AgentProvenance, ImportedNpcData)`. Repeated and
newer generations of the same opaque `BodyId` resolve the same agent. Older
generations fail with `stale_body_generation`. Marking a body lost preserves
the agent and makes loss inspectable.

The deterministic ID is the first 128 bits of SHA-256 over a domain separator,
the active session scope, and the opaque body ID, with UUID variant/version
bits normalized. This is deterministic for fixtures and an active session; it
does not claim save-stable or load-order-independent identity.

`AgentProvenanceKind` distinguishes `ImportedLegacy` from
`AurelianAuthored`. Imported provenance records `Skyrim/Marionette` as its
source and the opaque body ID as source identity. Provenance is inspectable
metadata, not agent identity.

## Composed data

M2 proves immutable composition with:

- `IdentityProfile(DisplayName, Archetype)`;
- `BodyProfile(Humanoid, Essential, Protected)`;
- `SelectionProfile(BasePreference, DistanceWeight, CapabilityWeight,
  ArchetypeWeight)`;
- `ImportedNpcData` composing those profiles.

The visible imported defaults are intentionally small. A later asset seam can
compose `base humanoid + race + profession + faction + regional archetype +
named-agent override + runtime state` without changing agent identity or
exposing backend identity to policy.

## Migration ladder

```text
legacy Skyrim actor
  -> ImportedLegacy agent with identity and body binding
  -> enriched compositional data
  -> mailbox-driven behavior
  -> Dominatus utility and state-machine behavior
  -> fully Aurelian-authored agent
  -> optional authored/LLM policy
```

M2 does not implement durable actor remapping, load-order migration, TOML
composition, or world-scale NPC import.

## M3 stable-origin migration

Placed references now resolve from normalized plugin filename plus local FormID,
independently of runtime FormID and session. Body loss detaches the current
materialization; rediscovery attaches a new BodyId/generation to the same
semantic agent. The former body-only API remains as an explicit dynamic/session
migration adapter. Dynamic references are not durable. See
[stable provenance](AURELIAN_SKYRIM_STABLE_PROVENANCE.md).
