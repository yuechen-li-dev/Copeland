# JTF-M3c — Aurelian.Machina presentation bridge

## Status

Completed by adding the narrow consumer-owned `Aurelian.Machina` production project and moving Machina–Aurelian CPU-raster parity coverage into the integration test lane.

## Migration record

- Added `MachinaPresentationTranslator.Translate`, which maps every current `MachinaPresentationFrame` operation one-for-one into `Resolved2DPlan`.
- Added the bridge to `JointTaskForce.slnx`, while keeping it out of `Machina.UI.slnx` and `Aurelian.slnx`.
- Added `Aurelian.Machina.Tests` to `JointTaskForce.Integration.slnx` and moved `AurelianCpuRasterParityTests` there.
- Replaced the parity test's test-local plan construction with the production bridge.
- Extended dependency-boundary validation with the bridge's two-reference allow-list, no-package rule, source-token checks, and integration-test fast-lane exclusion.

The canonical integration test lowers and lays out an authored Standard UI document, builds a `MachinaPresentationFrame`, translates it, and realizes it with `AurelianCpuRasterRenderer`. It compares the result to the retained legacy raster realization deterministically.

## Next milestone: JTF-M3d

JTF-M3d may retire or relocate `MachinaRasterPipeline`'s internally rasterizing compatibility path, `LegacyMachinaRenderCommandAdapter`, and the Machina raster realization projects only after preserving the bridge parity contract. It must leave Machina as the producer of presentation frames and keep backend selection at an external composition root.
