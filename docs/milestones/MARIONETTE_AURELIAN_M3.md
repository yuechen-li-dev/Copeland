# Marionette/Aurelian M3

M3 establishes explicit Skyrim world ownership, stable placed provenance, and
a bounded save/timeline correlation seam.

Implemented proof:

- ten-state generated world owner with mailbox facts, typed events, ordering,
  readiness gating, body lifecycle, and rollback detection;
- native plugin/local origin and `Calendar` game-day observations on the
  existing main-thread bounded query;
- load-order-independent imported identity and rematerialization;
- real Dominatus `DOM1` capture/fresh restore, metadata-only index,
  active-binding rejection, historical selection, and lineage exclusion;
- tspack-managed controller command, PID/log reporting, startup failure,
  termination, and scoped checkpoint directory.

Automated proof covers portable contracts, generated flows, native protocol,
M1/M2 regressions, checkpoint boundaries, and tspack planning. Disposable live
save/reload and operator-assisted rollback remain an interactive checklist; no
live M3 save/load proof is claimed here. Active-binding save/load, durable
dynamic actors, and replay of uncertain native mutations remain non-goals.

## Scoped live checklist

The noninteractive dry-run is proven. The interactive proof remains pending:

```powershell
go run ./cmd/tspack run skyrim --dominatus-skyrim --json --root C:\SkyrimDev\Plugins\MarionetteSSE
```

Run it from the tspack checkout with only the disposable `ed-m2b2d` fixture.
Expected evidence is one managed-controller PID/log, world-ready state, placed
plugin/local origin, M2 selection/bind/move/release, a `DOM1` artifact under
`build/skyrim/checkpoints`, clean process exit, and verified config/INI restore.
For rollback, create fixture save A, advance and checkpoint B, load A, and
verify checkpoint A is selected while B remains present but inactive. Do not
claim this operator-assisted save/reload proof until those observations exist.
