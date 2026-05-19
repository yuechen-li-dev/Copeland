# Dominatus Integration M0

## Purpose

Dominatus is vendored as source under `vendor/Dominatus` so Copeland and Machina authors can inspect and integrate against the real implementation patterns instead of recreating approximations.

M0 is a vendoring and architecture milestone only.

Vendored from:
- Repository: https://github.com/yuechen-li-dev/Dominatus
- Commit: `2dea9079dfb1647b9d7c34814240c477670ee3ce`
- Vendoring date (UTC): 2026-05-19

## Dependency boundaries

- `Machina.Layout`
  - Pure layout/data/math package.
  - Must not depend on Dominatus.
- `Machina.Core`
  - UI declaration and lowering package.
  - Must not depend on Dominatus in M0.
- `Machina.Standard`
  - Standard component declaration package.
  - Must not depend on Dominatus in M0.
- `Machina.Renderer` (future)
  - May depend on Dominatus deliberately.
- `Machina.Runtime` (future)
  - Should depend on Dominatus deliberately.
- `vendor/Dominatus`
  - Source-visible runtime and control-plane dependency source.

## Why vendoring is deliberate

- Gives source visibility for Codex and humans.
- Enables direct project-reference integration instead of NuGet-only black-box usage.
- Avoids fake reimplementation of Dominatus semantics and authoring patterns.
- Lets future renderer/runtime packages build against real Dominatus APIs.

## What M0 includes

- Vendored source for required library projects:
  - `Dominatus.Core`
  - `Dominatus.OptFlow`
  - `Ariadne.OptFlow`
  - `Dominatus.UtilityLite`
  - `Dominatus.Actuators.Standard`
- Vendored source for optional library project:
  - `Dominatus.Server`
- Vendored sample/reference projects (not solution-linked):
  - `Ariadne.Console`
  - `Dominatus.FishTank`
- Vendored upstream Dominatus docs in `vendor/Dominatus/docs`.
- Solution wiring for required libraries and `Dominatus.Server`.

## Dominatus.Server decision for M0

`Dominatus.Server` is vendored and linked in `Copeland.slnx` for M0 because it builds cleanly in this repository using framework references and does not require broad dependency changes.

If future dependency friction appears, it can be vendored-only without affecting the M0 architecture boundaries.

## What M0 explicitly does not do

- No Machina renderer implementation.
- No Machina runtime implementation.
- No draw-command actuator implementation in Machina.
- No UI action dispatch implementation.
- No Dominatus dependency additions to `Machina.Layout`, `Machina.Core`, or `Machina.Standard`.
- No behavioral runtime integration work.

## Local integration changes made to vendored source

- No logical behavior changes were made to Dominatus source in M0.
- Source was copied as vendored code and solution wiring was done from `Copeland.slnx`.

## M0a status (2026-05-19)

- Implemented `Machina.Dominatus` snapshot render-actuation adapter.
- Rendering commands are modeled as typed Dominatus actuations and registered on `ActuatorHost`.
- M0a validates deterministic command ordering and immediate completion semantics without pixel rendering.
