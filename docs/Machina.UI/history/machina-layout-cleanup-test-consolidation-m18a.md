# Machina Layout Cleanup and Test Consolidation M18a

## Purpose

M18a fixes small known layout bugs and pays down ROALoop test-helper debt.

Test optimization means consolidating duplicated setup and helpers, not deleting coverage.

## Scope

M18a is a focused cleanup, bugfix, and test consolidation milestone. It fixes the known Oblivion inspector title clipping risk and consolidates high-value duplicated helpers in `tests/Machina.UI/Machina.Presenter.Sample.Tests`.

It does not add new product features or new layout primitives.

## Inspector title clipping fix

The wide Oblivion inspector title previously rendered `"Selected card inspector"` as raw `TextSize.H1` in a fixed inspector column. M18a keeps the title text, switches it to a readable body-sized title, and wraps it in a clipping rect so the title has an explicit overflow boundary.

The fix preserves compact/wide shell behavior and keeps the inspector title visible in normal render-command text assertions.

## Test helper duplication audit

Duplicated categories found:

- presenter docs-page state builders with selected, expanded, main-scroll, inspector-scroll, raw-source-scroll, and body-scroll setup
- repeated `1280x720` wide and `960x540` compact viewport setup
- repeated docs-page render helpers
- repeated manifest repo-root and JSON load helpers
- repeated Oblivion scroll-region lookup helpers
- repeated region containment and non-overlap assertions
- playback scenario path helpers already partly centralized in `PlaybackTestEnvironment`

## Shared helpers introduced

- `PresenterSampleTestHarness` centralizes common viewport constants, default theme/proof options, docs-page state setup, shell/page rendering, wheel events, and rectangle centers.
- `ManifestTestHelper` centralizes repo-root lookup, artifact manifest loading, milestone/kind assertions, boolean assertions, and forbidden-flag assertions.
- `PresenterRegionAssert` centralizes scroll-region lookup, rect-inside assertions, and non-overlap assertions.

## Tests migrated

M18a adds focused inspector clipping tests and helper-consolidation contract tests. `OblivionPageGridRefactorM17eTests` was also migrated where it had obvious duplicate docs-state, render, manifest, wheel, center, and raw-source region helpers.

## Opportunistic consolidation

The M17e grid-refactor test helpers were consolidated because they overlapped directly with the new shared helper roles. Older input-router helper families were left local because they encode test-specific event sequences and assertions.

## Coverage preservation

No tests were deleted and no coverage was intentionally reduced.

A shared helper is good only if it makes tests more readable and preserves the behavior being asserted.

Do not hide important assertions inside opaque helpers.

Prefer shared builders for setup and explicit assertions in tests.

## Iteration-time notes

The consolidation reduces repeated setup code and makes targeted M18a/Oblivion inspector filters easier to run. No reliable suite-wide speedup is claimed.

## What changed

- inspector title rendering now has an explicit clipping boundary
- high-value repeated test setup moved into small shared helpers
- M18a docs and manifests record the cleanup boundary

## What did not change

- no new layout primitive was added
- no product feature was added
- no playback scenario coverage was deleted
- no Markdown editing, notebook execution, Aurelian, or VD-MIR work was performed

## Deferred work

Further helper consolidation should be opportunistic and evidence-led. Candidate areas are older manifest assertion files and input-event helper families, but only when extraction preserves explicit assertions and does not create an opaque test framework.
