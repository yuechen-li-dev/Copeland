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

