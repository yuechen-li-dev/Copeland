# Marionette/Aurelian M4a live proof

## Operator procedure

1. Run `go run ./cmd/tspack run skyrim --dominatus-skyrim --json --root C:\SkyrimDev\Plugins\MarionetteSSE`.
2. Wait for `M4A_MANAGED world_state=WorldReady lifecycle=polling`.
3. Let the imported placed agent reach the chosen observable A state.
4. Create named save `AURELIAN_M4A_A`.
5. Confirm `save_checkpoint_staged` then `save_checkpoint_committed` for one operation ID.
6. Advance the observable primitive state to B.
7. Create named save `AURELIAN_M4A_B` and confirm its commit.
8. Load `AURELIAN_M4A_A`.
9. Confirm native `load_started` then `load_completed`, with the same operation ID.
10. Confirm managed `checkpoint_restored`, `WorldReady`, A active, B inactive, and no B-only state.
11. Confirm the placed origin maps to the A-era AgentId, a fresh BodyId is observed, and binding is `Unbound`.
12. Optionally rerun M2 selection/bind/move/release, then exit Skyrim.
13. Confirm tspack reports both processes exited and no orphan remains.

## Evidence status

Automated native and managed correlation/rollback/rematerialization tests are
the committed proof. The interactive Skyrim menu sequence must be recorded from
the scoped run logs; it is not claimed by this document until those markers
exist. The precise save reliability limit is that SKSE has no post-save-success
message after its save routine returns.

## Observed scoped run — 2026-08-03

The operator loaded the fixture, then issued Skyrim console saves
`AURELIAN_M4A_A` and `AURELIAN_M4A_B`, followed by a Skyrim console load of
`AURELIAN_M4A_A`. This was a real SKSE-launched process run through the exact
`tspack run skyrim --dominatus-skyrim` command above.

`managed-controller.log` recorded the initial untracked fixture load as
`load_completed_without_checkpoint` and `WorldReady`, then:

```text
save_started    operation=3 sequence=4  save=AURELIAN_M4A_A
save_serialized operation=3 sequence=5  save=AURELIAN_M4A_A
save_started    operation=4 sequence=6  save=AURELIAN_M4A_B
save_serialized operation=4 sequence=7  save=AURELIAN_M4A_B
load_started    operation=5 sequence=8  save=AURELIAN_M4A_A
load_completed  operation=5 sequence=10 save=AURELIAN_M4A_A outcome=checkpoint_restored world_state=WorldReady
```

Native Marionette evidence recorded the corresponding callback ordering and
loaded timeline: A was captured at game day `1.740312`, B at `1.741650`, and
load A completed at `1.740312`. The checkpoint index contains A as active and
B as inactive; B's SHA-addressed `.dom` artifact remains present. This proves
the live save/load correlation and rollback lineage. The committed automated
`SaveAThenBLoadA_RestoresHistoricalWorldAndRematerializesPlacedAgent` test is
the deterministic proof for the B-only-agent absence, stable A AgentId, fresh
BodyId rematerialization, and unbound binding state; those semantic body
assertions were not separately claimed from this manual menu/console run.
