# Machina Playback Regression Suite M16c

## Purpose

M16c turns the M16a/M16b playback harness into a canonical regression suite for the current presenter surface.

The suite is tooling only. It adds no product UI behavior, no native OS automation, and no pixel-golden screenshot gate.

## Why scenario suites exist

Core doctrine:

```text
Playback scenarios are regression artifacts.

A UI regression should become a named TOML scenario with:
  setup,
  linear input steps,
  assertions with reasons,
  trace output,
  final PNG,
  manifest output.

The suite proves interaction paths through internal presenter input/routing.

The suite does not compare pixels.

The suite does not drive native OS input.

TOML remains data, not a programming language.
```

Companion doctrine:

```text
A scenario is a cassette tape.

A suite is a box of cassette tapes plus a report.

If a test needs loops, generated cases, branches, or complex logic, write C# suite/test code that invokes scenarios.
Do not add loops or conditionals to TOML.
```

## Relationship to M16a/M16b

- M16a introduced the sample-local playback MVP: scenario TOML, normalized scenario output, trace JSON, manifest output, and final PNG output.
- M16b closed the two playback parity blockers for `main-stack` and `raw-source` wheel routing and kept TOML linear/data-only.
- M16c keeps the same runner doctrine and adds organized scenario coverage, a suite runner, aggregate reporting, and milestone manifests.
- M16d then adds normal xUnit orchestration on top of the same playback core; see [Machina Playback xUnit Integration M16d](machina-playback-xunit-integration-m16d.md).

## Scenario organization

Current layout:

```text
samples/Machina.Presenter.Sample/PlaybackScenarios/
  starter/
    oblivion-expand-collapse.machina-playback.toml
    oblivion-expanded-body-scroll.machina-playback.toml
    oblivion-main-stack-scroll.machina-playback.toml
    oblivion-inspector-scroll.machina-playback.toml
    oblivion-raw-source-scroll.machina-playback.toml

  regressions/
    m15b-wide-shell-mode.machina-playback.toml
    m15b-compact-shell-mode.machina-playback.toml
    m15c-expand-collapse-selection.machina-playback.toml
    m15d-expanded-reading-surface.machina-playback.toml
    m15e-independent-scroll-panes.machina-playback.toml
    m15e-partial-viewport-culling.machina-playback.toml
    m15f-main-stack-scroll-regression.machina-playback.toml
    m15f-inspector-raw-source-lag-guard.machina-playback.toml
    m16b-raw-source-routing-regression.machina-playback.toml

  m16c-oblivion-playback-suite.machina-playback-suite.toml
```

M15b is intentionally split into wide and compact shell scenarios because current playback assertions are final-state assertions. The resize regression is still covered, but the proof is two cassettes instead of one multi-phase cassette.

## Canonical M15 regression scenarios

- M15b is covered by `m15b-wide-shell-mode` and `m15b-compact-shell-mode`.
- M15c is covered by `m15c-expand-collapse-selection`.
- M15d is covered by `m15d-expanded-reading-surface`.
- M15e is covered by `m15e-independent-scroll-panes` and `m15e-partial-viewport-culling`.
- M15f is covered by `m15f-main-stack-scroll-regression` and `m15f-inspector-raw-source-lag-guard`.
- M16b routing is preserved by `m16b-raw-source-routing-regression`.

## Suite runner

The sample runner now accepts either:

- a directory of `*.machina-playback.toml` files
- a suite manifest TOML file that lists scenario paths explicitly

Example directory run:

```powershell
.\tools\Export-MachinaPresenter.ps1 -PlaybackSuite samples/Machina.Presenter.Sample/PlaybackScenarios/regressions -OutputDirectory artifacts/m16c/playback
```

Example manifest run:

```powershell
.\tools\Export-MachinaPresenter.ps1 -PlaybackSuite samples/Machina.Presenter.Sample/PlaybackScenarios/m16c-oblivion-playback-suite.machina-playback-suite.toml -OutputDirectory artifacts/m16c/playback
```

Per-scenario output stays under `artifacts/m16c/playback/<scenario-id>/`.

Suite summary files are written alongside that playback directory:

- `artifacts/m16c/playback-suite-report.json`
- `artifacts/m16c/playback-suite-report.txt`
- `artifacts/m16c/machina-playback-regression-suite-manifest.json`
- `artifacts/m16c/machina-playback-regression-suite-manifest.txt`

## Suite report

The aggregate suite report records:

- suite id and suite name
- scenario count
- passed count
- failed count
- skipped count
- starter/regression inclusion flags
- validation status
- per-scenario id, path, output directory, pass/fail state, and failure reasons/messages

Ordering is deterministic and timestamps are intentionally omitted by default.

## Assertion reasons

Every assertion still requires a non-empty `reason`.

M16c keeps the parser-level rejection for missing or whitespace-only reasons and expands the scenario library so milestone/regression intent is written down directly in the cassette.

## TOML remains data

M16c does not add:

- conditionals
- loops
- variables
- expression language
- includes
- macros

When intermediate evidence is required, the suite adds simple declarative assertions or uses C# suite/test code. It does not turn playback TOML into a scripting language.

## Non-goals

M16c does not implement:

- native OS automation
- Selenium, Playwright, Appium, Win32 input, or `SendInput`
- pixel-golden screenshot diffing
- screenshot comparison gates
- product UI feature work
- Markdown editing
- notebook execution
- Roslyn/xUnit execution
- Aurelian work
- `VD-MIR` work

## Deferred work

- richer suite-level performance counters for raw-source layout/build churn
- broader semantic-target coverage if future regressions need it
- additional declarative assertions only when a real regression needs them
- xUnit remains the preferred place for generated cases, scenario selection, and environment guards after M16d
