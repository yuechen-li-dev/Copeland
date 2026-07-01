# A64 — Dominatus-backed Runtime Tick M0

Canonical document: [`docs/Aurelian/audits/0064-a64-dominatus-backed-runtime-tick-m0.md`](../Aurelian/audits/0064-a64-dominatus-backed-runtime-tick-m0.md)

## Compatibility note

This compatibility file exists because some runtime tests still resolve the historical root-level audit path.

ParallelAiWorldRunner inspection/integration decision:

- `IAurelianAiWorldRunner` remains the abstraction seam for runtime tick orchestration.
- `ParallelAiWorldRunner` inspection/integration remains deferred.
- The deferred status is documentation compatibility only here; the canonical audit lives under the Aurelian docs subtree.
