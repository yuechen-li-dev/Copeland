# MARIONETTE-AURELIAN-M1

## Outcome

The automated M1 path is complete. One deterministic Aurelian agent can bind a
portable body candidate, choose `MoveBodyToward` through Dominatus 1.0, observe
completion, and release the body through the existing reversible Marionette
host session. Manual in-game execution is intentionally not claimed here.

The motivating path changed from an already-active host plus raw
`MoveToward` to:

```text
deterministic AgentId
  -> BindBody operation
  -> begin_host_session
  -> BodyBindingObservation(Bound)
  -> utility selects MoveToward
  -> MoveBodyToward operation with owner validation
  -> move_toward and observed position
  -> ReleaseBody operation
  -> restore_host_session
  -> BodyBindingObservation(Released)
```

The old wire-level `move_toward` remains as the compatibility lowering target;
the new agent path cannot reach it without a valid exclusive binding.

## Evidence

- portable contract tests cover deterministic identity, opaque body identity,
  exclusivity, duplicate bind, stale generation, idempotent release, explicit
  loss, wrong-owner rejection, and post-release rejection;
- backend tests prove bind/move/release lower to the existing host commands and
  unrelated agents cannot hijack a binding;
- generated agent tests cover successful binding, binding failure, movement
  success/failure, release success/failure, utility selection, stable durable
  state IDs, explicit operation sites, and zero hidden states;
- provenance tests load Dominatus assembly version 1.0 and reject any 0.4
  central version in the Skyrim path;
- Marionette native tests remain the proof that parsing, policy validation,
  bounded dispatch, disconnect restoration, stale host generations, actor
  unload, and main-thread runtime callbacks remain intact.

Validation recorded for M1:

| Lane | Result |
| --- | --- |
| Copeland `dotnet build Aurelian.slnx -c Release` | passed, 0 warnings, 0 errors |
| Copeland `dotnet test Aurelian.slnx -c Release --no-build` | passed, 630 tests |
| Dominatus `Dominatus.Release.slnx`, net8.0 and net10.0 | 1,379 passed, 8 credentialed live tests skipped; 40 pre-existing xUnit analyzer warnings |
| Marionette managed Release build/tests | passed, 10 tests |
| Marionette native Release build | `MarionetteSSE` and `MarionetteTests` passed |
| Marionette native tests | 68 passed, 1 intentional skip |
| Manual scoped Skyrim run | not executed; use the checklist below |

## Manual scoped-fixture operator checklist

Do not use the live Steam installation directly. Use only the repository's
disposable TSPack Skyrim fixture lifecycle.

1. Build and test Copeland `Aurelian.slnx` in Release.
2. Configure Marionette with `cmake --preset vs2026-x64`.
3. Build `MarionetteSSE` and `MarionetteTests` with the documented Release
   preset and run `MarionetteTests.exe`.
4. Verify the provisioned read-only `ed-m2b2d` fixture with
   `tspack skyrim fixture inspect ed-m2b2d --json --root C:\SkyrimDev\Plugins\MarionetteSSE`.
5. Review the exact deployment/launch plan with
   `tspack run skyrim --dominatus-skyrim --dry-run --json --root C:\SkyrimDev\Plugins\MarionetteSSE`.
6. Run `tspack run skyrim --dominatus-skyrim --json --root C:\SkyrimDev\Plugins\MarionetteSSE`.
   This is the only approved launch/deployment command; do not copy files into
   the live game root.
7. The run-scoped controller authenticates and executes the managed Dominatus
   Skyrim scenario.
8. Confirm the report contains deterministic agent ID
   `a0e11a00-0000-4000-8000-000000000001`, body ID
   `skyrim-fixture-body-1`, and final binding state `Released`.
9. Correlate the bind, movement, and release request IDs with native runtime
   log entries.
10. Capture the candidate FormID/pending generation and the active
   FormID/host-generation mapping returned after bind.
11. Confirm the movement lifecycle is accepted/in-progress/completed and the
   final distance is within stopping distance.
12. Confirm player FormID and camera target are both restored to `0x14`, the
    session is cleared, and disconnect/emergency restoration was not needed.
13. Retain the scoped SKSE log, managed report/trace, before/after positions,
    correlation IDs, and restoration evidence as run artifacts.

Do not describe this test as possession. It proves bounded agent-owned body
binding and restoration.

## Limitations and next milestone

M1 does not provide durable save/load body identity, load-order-independent
actor identity, navigation, animation locomotion, first-person camera, player
identity/input transfer, or gameplay-record migration. The next milestone
should make backend/session health and world load/loss events an explicit
Aurelian engine-owner boundary, then add durable identity design before any
active-session save/load support.
