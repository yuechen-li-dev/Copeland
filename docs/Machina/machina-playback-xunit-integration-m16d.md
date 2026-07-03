# Machina Playback xUnit Integration M16d

## Purpose

M16d integrates the existing Machina playback regression coverage into normal xUnit test execution without changing the playback feature boundary.

This milestone is test-runner integration only. It does not add new playback semantics, native OS automation, pixel-golden diffing, or product UI behavior.

## TOML is cassette, xUnit is director

Core doctrine:

```text
TOML playback is a cassette.

xUnit is the director.

TOML scenarios describe setup, linear input steps, assertions, and outputs.

C# tests choose which scenarios to run, loop over scenarios, apply environment guards, generate cases, and perform higher-order assertions.

Do not turn TOML into a programming language.
```

TOML remains linear and data-only:

- no loops
- no conditionals
- no variables
- no expression language
- no macros or includes

## Scenario discovery

Scenario discovery now lives in C# under `tests/Machina.Presenter.Sample.Tests`.

Current discovery responsibilities:

- discover starter scenarios
- discover regression scenarios
- discover all canonical scenarios from the M16c suite manifest
- preserve deterministic ordering
- keep scenario id uniqueness testable
- keep parse/assertion-reason/programming-boundary validation in xUnit

The suite manifest remains list-only TOML. Higher-order orchestration still belongs in C#.

## Starter suite tests

Starter playback scenarios now run as normal xUnit theories.

Current starter xUnit layer:

- generated `[Theory]` cases from C# discovery
- deterministic artifact output under `artifacts/m16d/xunit-playback/starter/<scenario-id>/`
- xUnit failures that surface scenario id, assertion type, reason, expected, actual, and artifact path directly in the failure message

## Regression suite tests

Regression playback scenarios now run as normal xUnit theories too.

Current regression xUnit layer:

- generated `[Theory]` cases from C# discovery
- deterministic artifact output under `artifacts/m16d/xunit-playback/regressions/<scenario-id>/`
- aggregate xUnit coverage for all canonical scenarios through the same shared playback runner

The existing M16c suite runner remains available and is not replaced.

## Artifact output

xUnit playback artifacts are written under:

```text
artifacts/m16d/xunit-playback/<test-suite>/<scenario-id>/
```

Required per-scenario files:

```text
scenario.normalized.toml
playback-trace.json
playback-manifest.json
playback-manifest.txt
final.png
```

Failure doctrine:

```text
A failing playback xUnit test must produce enough artifacts for Codex or a reviewer to debug without rerunning manually:
  scenario.normalized.toml
  playback-trace.json
  playback-manifest.json
  playback-manifest.txt
  final.png
  failure summary
```

On failure, xUnit playback also writes `failure.txt` into the same scenario artifact directory.

Current policy is to prefer deterministic artifact generation for canonical scenarios because the playback traces, manifests, normalized TOML, and final PNG are useful review evidence even when the scenario passes.

## Failure messages

Playback xUnit failures are formatted for direct test-runner use rather than console archaeology.

A failing assertion message includes:

- scenario id
- assertion index
- step index when the assertion is step-specific
- assertion type
- assertion reason
- expected
- actual
- artifact directory

`failure.txt` also records scenario path, trace path, final PNG path, the failing test name, and the assertion failure hint when one exists.

## Environment guards

M16d keeps environment control in C#, not in TOML.

Current playback runs are internal and deterministic, so special guards are minimal. If a future playback scenario needs an environment check or skip, that belongs in C# xUnit code rather than in the scenario cassette.

## Relationship to M16c suite runner

M16d keeps the M16c suite runner intact.

Relationship summary:

- M16c remains the shared playback suite runner/reporting layer
- M16d adds a thin xUnit orchestration layer on top
- the xUnit layer reuses the same parser and scenario runner
- aggregate suite report generation still works through the M16c path

The result is one playback core with two entry points:

- suite/tooling entry
- xUnit entry

## Non-goals

M16d does not add:

- TOML loops
- TOML conditionals
- TOML variables
- native OS automation
- Selenium, Playwright, Appium, Win32 input, or `SendInput`
- pixel-golden screenshot diffing
- screenshot comparison gates
- product UI behavior changes
- Markdown editing
- notebook execution
- Roslyn notebook/runtime execution
- Aurelian work
- `VD-MIR` work

## Deferred work

- opt-in suite traits if future canonical playback volume becomes materially slow
- richer higher-order suite assertions if a real regression needs them
- additional environment guards only if a future playback scenario introduces a real dependency beyond the current deterministic internal path

## Follow-up note after M17a

M17a is recon only and does not change playback semantics or UI behavior.

What it does change is planning confidence:

- playback xUnit coverage is now explicitly treated as the regression harness for the upcoming Machina/Oblivion layout-authoring refactor slices
- the recommended next implementation order is stack-authoring parity first, card-renderer migration second, grid-authoring parity third, and page-shell migration fourth
- broad JS parity remains staged rather than one-shot
