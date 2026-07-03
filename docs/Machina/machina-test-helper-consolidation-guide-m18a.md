# Machina Test Helper Consolidation Guide M18a

## Purpose

This guide records the M18a test-helper doctrine for Machina presenter tests.

## ROALoop helper duplication failure mode

ROALoop-generated tests tend to repeat local helper functions for state setup, manifest loading, viewport dimensions, playback paths, scroll-region lookup, and small geometry assertions. The duplication is useful during rapid generation, but it raises later friction because every small behavior change needs many near-identical edits.

## What belongs in shared helpers

Shared helpers are appropriate for common setup:

- canonical viewport constants
- default presenter model/theme/proof options
- common Oblivion docs-page state builders
- common render entry points
- playback scenario path discovery
- manifest loading and milestone/kind assertions
- region lookup and geometry primitives

## What should stay explicit in tests

Assertions that define the behavior under test should remain visible in the test body. A helper may find a region or build a state, but the test should still say what property matters.

## Shared helper catalog

- `PresenterSampleTestHarness`: viewport constants, docs state, render helpers, wheel event helpers, point helpers.
- `ManifestTestHelper`: repo-root lookup, artifact manifest loading, milestone/kind checks, boolean checks.
- `PresenterRegionAssert`: single scroll-region lookup, rect containment, non-overlap checks.
- `PlaybackTestEnvironment`: playback runner, canonical scenario roots, output paths, and existing playback shell helpers.

## Assertion preservation rule

Test optimization means consolidating duplicated setup and helpers, not deleting coverage.

A shared helper is good only if it makes tests more readable and preserves the behavior being asserted.

Do not hide important assertions inside opaque helpers.

Prefer shared builders for setup and explicit assertions in tests.

## Adding future tests

Future tests should first look for an existing setup helper before adding another local copy. Add a new shared helper only when at least two tests need the same setup or lookup and the helper name makes the behavior clearer.

## Non-goals

M18a does not create a broad test framework, delete tests, weaken assertions, add product features, add layout primitives, or introduce pixel-golden screenshot diffing.
