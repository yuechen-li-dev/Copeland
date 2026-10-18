# Machina.UI architecture index

Use the [authoring guide](../authoring-guide.md) first for current authoring and frame-preparation flow. This index groups the existing architecture notes by question; milestone records under `../history/` explain how the contracts evolved.

## Current contracts and ownership

- [Flat authoring](machina-flat-authoring.md) — screen row tables and hosted local components.
- [UI Core contract](ui-core-contract.md) — declaration, lowering, and layout boundary.
- [Presentation pipeline](machina-pipeline.md) — preparing a frame from authoring input.
- [Renderer-neutral presentation](../machina-renderer-neutral-presentation-architecture.md) — presentation intent and renderer ownership.
- [Runtime dispatch](machina-runtime-dispatch.md) and [hit testing](machina-runtime-hit-testing.md) — runtime interaction contracts.
- [Input routing](machina-canonical-input-routing-jtf-m4c.md) — canonical input path.
- [Dominatus runtime](machina-dominatus-runtime.md) and [rendering](machina-dominatus-rendering.md) — integration boundaries.
- [Text conformance architecture](../machina-text-conformance-architecture.md) — text model, layout, and rendering boundaries.

## Historical or planning notes

- [Authoring model timeline](machina-authoring-model.md) collects successive authoring decisions. The [authoring guide](../authoring-guide.md) is the concise current entry point.
- [Support roadmap](machina-support-roadmap.md) is planning context; check current source and tests before treating a roadmap item as implemented.
- Files with milestone suffixes (`jtf-m*`) preserve milestone-specific design and proof context. Their scope should not be generalized beyond the named milestone.
