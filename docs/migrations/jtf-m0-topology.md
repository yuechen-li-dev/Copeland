# JTF-M0 — Joint Task Force monorepo topology and ownership baseline

## Changed

- Moved Copeland production projects from `src/Copeland.*` to `src/Copeland/<project>`.
- Moved Machina production projects from `src/Machina.*` to `src/Machina.UI/<project>`.
- Moved Aurelian production projects from `src/Aurelian.*` to `src/Aurelian/<project>`.
- Mirrored those ownership roots for tests under `tests/Copeland`, `tests/Machina.UI`, and `tests/Aurelian`.
- Grouped existing samples under `samples/Machina.UI` and `samples/Aurelian`.
- Repaired project references, solution paths, scripts, fixtures, artifact/export paths, and repository-relative documentation paths.
- Replaced the mixed `Copeland.slnx` with a Copeland-only lane.
- Added `Machina.UI.slnx` and renamed the slow lane to `Machina.UI.Slow.slnx`.
- Kept `Aurelian.slnx` as an Aurelian production/test-only lane.
- Added `JointTaskForce.slnx` for repository-wide production, test, and sample validation.
- Classified subsystem documentation into architecture/reference/history areas while retaining historical content and compatibility stubs where existing dogfood paths required them.
- Added the current topology doctrine and a deterministic dependency-boundary validator with two narrow, documented Machina Dominatus exceptions.

## Not changed

- Assembly names, namespaces, public APIs, package versions, and runtime behavior.
- Project boundaries or individual class ownership.
- Machina Dominatus, Machina renderer, Machina pipeline, Aurelian screen, Aurelian Vulkan, or Aurelian shader semantic ownership.
- SDSL-V/VD-MIR placement.
- Samples’ product behavior or architecture.
- Test coverage intentionally; slow font/tooling coverage remains separate.
- The future `Aurelian.Machina` bridge.

## Validation intent

The independent lanes are `Copeland.slnx`, `Machina.UI.slnx`, and `Aurelian.slnx`; `Machina.UI.Slow.slnx` remains a separate expensive font/tooling lane. `JointTaskForce.slnx` validates the full repository graph. The exact commands and results are recorded with the implementation handoff.

## Deferred follow-up

JTF-M1 should address semantic ownership migration only after the physical territories are stable: isolate Dominatus and renderer adapters, design the `Aurelian.Machina` integration lane, and decide the eventual home for the listed Aurelian and Machina exceptions. Those changes require explicit dependency/API review and are outside JTF-M0.
