# JTF-M5a — Dominatus authoring ergonomics audit

## Status

Completed as an architecture/documentation milestone.

> Later status: JTF-M5b superseded only the temporary-location/exception follow-up. It moved the counter proof to `src/Integrations/Machina.Dominatus`, removed the exceptions, and did not consume the unpublished transition surface. See [JTF-M5b Dominatus ownership consolidation](jtf-m5b-dominatus-ownership-consolidation.md).

M5a changes documentation only. It does not implement the proposed transition API, add a project, update a package, alter a public API, change runtime behavior, migrate the scrollbar or Aurelian lifecycle, remove a Dominatus integration, or remove either dependency-boundary exception.

## Audited revisions

- Joint Task Force repository: `8cb9e48b3a16807b1112319c86fbd947e003525b` at audit start.
- JTF package consumption: `Dominatus.Core` 0.4.0 and `Dominatus.OptFlow` 0.4.0.
- Package-recorded Dominatus source commit: `0d60cba322dfb4e4f5f61c72867d24d4da2fe33d`.
- JTF reference submodule: the same `0d60cba` commit, inspection-only.
- Current separate Dominatus checkout: `e3654bcc81a3029bae90a4ee695a6a8fc58d411d`; pre-existing dirty Godot sample files were not modified.

Core, OptFlow, UtilityLite, relevant tests, and authoring docs are unchanged between the package-recorded commit and the current checkout. The intervening committed changes are Godot TinyTown/SpriteForge-adjacent.

## Findings

- Scrollbar dragging and close acceptance are deterministic transition problems, not utility decisions.
- The current scrollbar reducer remains preferable to the raw Dominatus HFSM path and should not be migrated for dogfooding.
- `Ai.Decide`/`Ai.Option` already supply utility option scoring, named slots, hysteresis, minimum commitment, current-option tie behavior, and decision trace reports. No duplicate utility API is proposed.
- Dominatus lacks a small independent typed transition operation returning next state plus ordered effects. Current use requires agent/world/HFSM/node/event/tick activation.
- The recommended additive design belongs in `Dominatus.Core`, not UtilityLite, Machina, a new assembly, or a second utility facade.
- Direct switches remain the baseline. Adoption requires demonstrated validation, trace/replay, reuse, or runtime-adapter value beyond line-count reduction.

The complete evidence, API sketches, rubric, rejected alternatives, ownership decision, and M5b recommendation are in [JTF-M5a Dominatus UI authoring ergonomics](../architecture/jtf-dominatus-ui-authoring-ergonomics.md).

## Documentation reconciliation

M5a marks the old Dominatus vendoring/render-integration documents as historical and corrects the current target-boundary status:

- the legacy Machina Dominatus renderer route was retired in JTF-M3d, not deferred to M5;
- package consumption is NuGet-based and the submodule is reference-only;
- a future generic Dominatus authoring dependency may be reviewed in a selected Machina app/runtime layer, but the present `Machina.Dominatus` exceptions remain temporary debt rather than permission for general UI coupling.

Historical milestone details are otherwise preserved.

## Validation

The milestone validation is documentation-appropriate:

- Markdown relative-link/path and code-fence check passed for all eight changed documents;
- `pwsh ./tools/Validate-DependencyBoundaries.ps1` passed for 24 production projects and retained the two recorded temporary exceptions;
- `git diff --check` passed (Git reported only its normal LF-to-CRLF working-copy notices);
- changed-path and project/package/solution diff inspection confirmed documentation-only changes.

No solution build or test is required because M5a touches no source, project, package, solution, or shared tooling. Existing failures, if any, are reported separately rather than repaired here.

## JTF-M5b handoff

Implement the proposed pure transition definition, validation/inspection, deterministic metadata, NativeAOT coverage, and optional runtime-node adapter in the Dominatus repository; publish it from a recorded revision; update JTF only to a published package; and dogfood one Aurelian-owned `AurelianRuntimeSession` start/stop lifecycle. Keep the scrollbar and close method as direct-code controls. Retain the dogfood only if the architecture document's adoption threshold passes.

Dominatus ownership consolidation and removal of the two current Machina exceptions belong to a later M5 closeout after that decision, not to M5a.
