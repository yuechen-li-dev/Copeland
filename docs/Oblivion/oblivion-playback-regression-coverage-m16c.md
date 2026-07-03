# Oblivion Playback Regression Coverage M16c

## Purpose

This document maps the current Oblivion reading-surface regressions and golden paths onto concrete playback scenarios.

## Covered golden paths

- expand a Markdown card and collapse it deterministically
- scroll an expanded Markdown body
- scroll the wide main stack without moving the inspector
- scroll the inspector without moving the main stack
- scroll raw Markdown source locally inside the inspector

## Covered M15b regressions

- `m15b-wide-shell-mode`
- `m15b-compact-shell-mode`

M15b is split into two scenarios because playback assertions are final-state assertions. The resize regression is still covered without adding imperative TOML logic.

## Covered M15c regressions

- `m15c-expand-collapse-selection`

This preserves the distinction between selected card state and expanded card state.

## Covered M15d regressions

- `m15d-expanded-reading-surface`

This proves the formatted Markdown body stays inline in the stack while raw source remains in the inspector.

## Covered M15e regressions

- `m15e-independent-scroll-panes`
- `m15e-partial-viewport-culling`

These cover independent stack/inspector scrolling plus the partial-content viewport path.

## Covered M15f regressions

- `m15f-main-stack-scroll-regression`
- `m15f-inspector-raw-source-lag-guard`

The lag-guard scenario currently proves repeated raw-source scrolling remains deterministic. A stronger suite-visible raw-source layout-build counter remains deferred.

## Covered M16b regressions

- `m16b-raw-source-routing-regression`

This proves raw-source wheel input remains local to raw source instead of falling back to inspector-pane or main-stack scrolling.

## Remaining coverage gaps

- no native OS automation coverage by design
- no pixel-golden screenshot comparisons by design
- no suite-visible raw-source layout-build counter yet
- no per-step shell-mode resize proof in one scenario because playback assertions are final-state assertions

## How to add a new regression scenario

1. Name the regression clearly and create a new `*.machina-playback.toml` cassette under `starter/` or `regressions/`.
2. Keep TOML linear and data-only: setup, ordered steps, assertions with reasons.
3. Reuse existing semantic targets when possible; add new ones only for real grounded UI regions.
4. If the regression needs loops, branching, generated coverage, or synthetic combinations, keep that logic in C# suite/test code.
5. Add the scenario to `m16c-oblivion-playback-suite.machina-playback-suite.toml`.
6. Add or update tests so the scenario becomes part of the checked regression contract.
