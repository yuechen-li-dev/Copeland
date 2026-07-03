# Machina Presenter Playback M16a

Follow-up note after M16b:

- the two M16a parity blockers are now closed by [Machina Playback Input Parity M16b](machina-playback-input-parity-m16b.md)
- starter scenarios now pass through the internal presenter input/routing path
- this document remains the MVP landing record; M16b is the stabilization follow-through

Follow-up note after M16c:

- the playback harness is now organized into starter and regression suites under `samples/Machina.Presenter.Sample/PlaybackScenarios/`
- a suite runner and aggregate report now exist; see [Machina Playback Regression Suite M16c](machina-playback-regression-suite-m16c.md)
- playback still does not add native OS automation, TOML programming features, or pixel-golden diffing

## Purpose

M16a introduces a sample-local deterministic playback MVP for the Machina presenter.

The goal is to turn UI interaction paths into reproducible artifacts that Codex and human reviewers can run, inspect, and reason about without relying on manual mouse work.

## Why playback exists

Required doctrine:

```text
Input scenarios are artifacts.

A UI bug should become:
  1. a TOML playback scenario,
  2. a failing assertion or trace,
  3. a fix,
  4. a passing scenario.

Playback proves interaction paths, not just final rendered states.

M16a drives Machina's internal presenter input model, not native OS input.
```

M15 proved final states with PNG exports, but manual interaction testing became the bottleneck. M16a adds deterministic playback so the path to a state is reviewable too.

## Internal playback versus native OS automation

M16a routes playback through the presenter's existing internal input model and reducer paths.

- no `SendInput`
- no Win32 desktop automation
- no Selenium/Playwright-style external driver
- no pixel-golden screenshot gate

This is presenter-local proof tooling, not end-to-end OS automation.

## Scenario TOML format

Scenarios use `*.machina-playback.toml`.

Each scenario contains:

- `[scenario]` metadata and initial setup
- `[output]` artifact toggles
- `[[steps]]` ordered input steps
- `[[assertions]]` post-run assertions

Normalized copies are written to `scenario.normalized.toml` in the scenario artifact folder.

## Mandatory assertion reasons

Required policy:

```text
Every assertion must include a reason.

The reason explains what behavior the assertion is protecting and why future readers should care.

An assertion without a reason is invalid.
```

M16a enforces this at parse time.

- missing `reason` is rejected
- empty `reason` is rejected
- assertion reasons are copied into trace and manifest output

## Initial state

M16a supports deterministic initial setup for the current Oblivion presenter surface:

- viewport width and height
- selected section
- selected tab
- selected card
- expanded card
- main stack scroll
- expanded card body scroll
- inspector scroll
- raw-source scroll

Unsupported setup fields should be rejected explicitly rather than ignored silently.

## Steps

Supported M16a step kinds:

- `wait`
- `click`
- `wheel`
- `key`
- `drag`

`drag` is implemented for semantic scrollbar targets and explicit point-to-point pointer drags. The current MVP still has known parity gaps for main-stack and raw-source wheel playback versus the older M15f direct interaction seam.

## Semantic targets

Current semantic targets:

- `main-stack`
- `card-header`
- `expanded-body`
- `inspector-pane`
- `raw-source`
- `main-stack-scrollbar-thumb`
- `expanded-body-scrollbar-thumb`
- `inspector-scrollbar-thumb`
- `raw-source-scrollbar-thumb`

The resolver renders the current presenter state, finds the semantic region, returns stable coordinates, and records the resolved target in the trace.

## Assertions

Current assertion kinds:

- `selected-card`
- `card-expanded`
- `scroll-offset-changed`
- `scroll-offset-equals`
- `scroll-offset-greater-than`
- `shell-mode`
- `region-exists`

Assertions are evaluated after playback and their reasons remain visible in both trace and manifest output.

## Trace output

`playback-trace.json` records:

- step index and step type
- requested target and resolved coordinates
- emitted internal input/action
- before and after state snapshots
- selected card and expanded card state
- main-stack, expanded-body, inspector, and raw-source scroll offsets
- shell mode
- assertion results with required reasons

This makes failures diagnosable without depending on memory of the milestone.

## Artifact output

Default scenario output layout:

```text
artifacts/m16a/playback/<scenario-id>/
  scenario.normalized.toml
  playback-trace.json
  playback-manifest.json
  playback-manifest.txt
  final.png
```

M16a also writes milestone-level status files:

```text
artifacts/m16a/machina-playback-mvp-manifest.json
artifacts/m16a/machina-playback-mvp-manifest.txt
```

## Starter scenarios

Starter scenarios live under `samples/Machina.Presenter.Sample/PlaybackScenarios`.

Current starter set:

- `oblivion-expand-collapse`
- `oblivion-expanded-body-scroll`
- `oblivion-main-stack-scroll`
- `oblivion-inspector-scroll`
- `oblivion-raw-source-scroll`

In M16c these now live under `samples/Machina.Presenter.Sample/PlaybackScenarios/starter/`.

Two of those scenarios currently document an honest blocker in playback parity:

- `oblivion-main-stack-scroll`
- `oblivion-raw-source-scroll`

The older direct M15f interaction coverage still works, but the new playback seam does not yet move those two wheel targets correctly.

## Non-goals

M16a does not implement:

- native OS automation
- pixel-golden diffing
- screenshot-comparison gates
- Markdown editing
- notebook execution
- Roslyn/xUnit execution
- Aurelian work
- `VD-MIR` work
- renderer architecture rewrite

## Deferred work

Deferred after M16a:

- main-stack wheel playback parity with the older direct interaction seam
- raw-source wheel playback parity with the older direct interaction seam
- broader semantic target coverage
- broader drag authoring coverage
- possible extraction from the sample after the seams prove out
