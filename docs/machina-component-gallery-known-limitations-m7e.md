# Machina Component Gallery Known Limitations M7e

## Purpose

M7e records the current component gallery baseline after the M7d Badge fix and makes the remaining rough edges explicit.

This document is for visual triage, expectation-setting, and milestone hygiene. It is not a request to broaden the renderer, add a layout solver, or chase every cosmetic wart with local hacks.

## Current stable baseline

The current gallery export is stable enough to serve as the canonical local visual workbench for StandardUI.

The default and interactive exports currently prove:

- typography and text sections render visible copy
- buttons are readable and dispatch-backed
- checkbox checked marks are visible
- switch off/on states remain distinct
- input placeholder and value states are readable
- Badge owns finite intrinsic size and local label placement
- cards and TextBlock hosting remain readable
- the explicit theme probe remains visible
- interactive and default exports differ in the intended state probes

## Known visual limitations

- Bitmap text is coarse and uppercase-styled.
- Limited glyph richness makes long captions and body copy look more mechanical than polished.
- Rich text inline metadata is preserved, but bold/emphasis/code/link visual fidelity is limited.
- TextBlock supports layout/render bridge proof, but not full typography.
- No ellipsis/scroll behavior yet.
- No dynamic font sizing.
- Gallery uses fixed root dimensions; no scroll/responsive layout yet.

## Known renderer/text limitations

- The current bitmap renderer is deterministic first and typographically rich second.
- Lowercase nuance, kerning, shaping, and broader font behavior are outside the current gallery contract.
- Inline styling is intentionally constrained by the current draw-text command surface.
- Pixel-diff visual regression automation does not exist yet.

## Known interaction/sample limitations

- Input is visual only; no text editing flow.
- The gallery is a local audit page, not a full interactive shell.
- Exported artifacts are local audit aids and ignored unless policy changes.
- The sample proves stable states and dispatch hooks, not full focus/keyboard/scroll behavior.

## Deferred feature candidates

- richer raster or adapter-backed typography
- ellipsis, clipping, and scroll behavior with explicit contracts
- responsive or scrollable gallery framing
- pixel-diff visual regression infrastructure
- broader control migration to richer text surfaces where justified

## Not bugs / intentional constraints

- No general constraint solver.
- No CSS-like cascade.
- Standard leaf components may own local intrinsic sizing.
- General layout still uses explicit rows/frames/ordered arithmetic.
- Headless tests remain the contract; gallery artifacts are visual audit aids.

## How to use this document

Use this register when a visual rough edge appears during gallery inspection.

- If the issue matches a listed limitation, treat it as known unless the severity has materially changed.
- If the issue breaks readability, stability, or deterministic geometry beyond this baseline, capture it as a new defect.
- If a proposed fix requires general layout semantics, a solver, a style cascade, or renderer expansion, stop and re-scope before patching.

## Update policy

- Update this document when the stable baseline materially improves or when a known limitation is retired.
- Add new items only when they are visible in the real gallery path or supported by headless evidence.
- Do not remove limitations simply because they are cosmetically tolerable; remove them when the capability actually exists.
- Keep generated PNG artifacts out of source control unless artifact policy changes explicitly.


## M8a font atlas architecture note

M8a keeps the gallery visuals unchanged and documents the long-term async MSDF font atlas path in `docs/machina-font-atlas-architecture-m8a.md`. The current deterministic bitmap text renderer remains a bootstrap/debug renderer until later milestones add font atlas records, worker preflight, and renderer consumption.
