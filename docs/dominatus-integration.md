# Dominatus Integration M0

> Historical status: this records the original source-vendoring milestone. The
> `reference/dominatus` submodule was removed during the M7b2 cleanup. Package-only
> consumers retain centrally pinned packages; active source integrations use the
> standalone sibling Dominatus repository. Current ownership doctrine is in
> [JTF-M5b Dominatus ownership consolidation](architecture/jtf-dominatus-ownership-consolidation.md).

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

## Vendor hotfix: target framework alignment

Dominatus Vendor Hotfix M0 adjusted vendored project target frameworks so the integrated Copeland/Machina solution can build consistently.

Changed:
- `vendor/Dominatus/src/Dominatus.UtilityLite/Dominatus.UtilityLite.csproj`: `TargetFramework` `net8.0` -> `TargetFrameworks` `net8.0;net10.0`

Reason:
- `dotnet build Copeland.slnx --no-restore` exposed an integrated-solution TFM mismatch where `Dominatus.UtilityLite` remained single-target `net8.0` while its referenced vendored Dominatus projects were built for `net10.0` in this solution context.

Upstream recommendation:
- Yes. This is generic Dominatus multi-target consistency for project-reference consumers, not specific to Copeland behavior.


## Runtime input seam note (M0a)

`Machina.Runtime` M0a adds a Dominatus-free hit-test/action index that maps root-local pointer coordinates to `UiAction` values using `ResolvedLayoutDocument` and `UiLoweringResult.Actions`.

Dominatus mailbox/event ingress remains deferred to a later adapter milestone so input indexing stays separable from rendering actuation.


## Re-vendor: 2026-05-26

Source:
- repo: https://github.com/yuechen-li-dev/Dominatus
- branch: master
- commit: `608c0432c9f60ff07918ad83e34fe03f7420e908`
- vendoring date (UTC): 2026-05-26

Reason:
- refresh vendored source after upstream bug fix/version alignment
- address local Visual Studio target-framework project-reference errors

Vendored project target-framework audit:

| Project | TargetFramework(s) | Notes |
| --- | --- | --- |
| Dominatus.Core | `net8.0;net10.0` | No project references. |
| Dominatus.OptFlow | `net8.0;net10.0` | References `Dominatus.Core` which also multi-targets `net8.0;net10.0`. |
| Ariadne.OptFlow | `net8.0;net10.0` | References `Dominatus.Core` and `Dominatus.OptFlow`; both are multi-targeted. |
| Dominatus.UtilityLite | `net8.0;net10.0` | References `Dominatus.Core` and `Dominatus.OptFlow`; both are multi-targeted. |
| Dominatus.Actuators.Standard | `net8.0;net10.0` | References `Dominatus.Core`; both are multi-targeted. |
| Dominatus.Server | `net8.0;net10.0` | References `Dominatus.Core` and `Dominatus.Llm.OptFlow`; both are multi-targeted. |

Visual Studio compatibility conclusion:
- Yes, with evidence. The vendored Dominatus projects now all multi-target `net8.0;net10.0`, so the previously reported `net8.0` project referencing a `net10.0`-only Dominatus project mismatch should be resolved by framework selection in project references.

Validation:
- `dotnet restore Copeland.slnx` passed.
- `dotnet build Copeland.slnx --no-restore` passed.
- `dotnet test Copeland.slnx` ran and failed in `Machina.Dominatus.Tests` (`CounterUiRuntimeTests` expectations), while other test projects passed.

Local Visual Studio cleanup guidance after pulling this re-vendor:

```powershell
dotnet clean Copeland.slnx
Get-ChildItem -Recurse -Directory bin,obj | Remove-Item -Recurse -Force
dotnet restore Copeland.slnx
dotnet build Copeland.slnx
```

Also close and reopen Visual Studio, then reload the solution to clear stale design-time build state if needed.

Vendor patch status:
- The previous local `Dominatus.UtilityLite` target-framework hotfix is now upstreamed in this vendored commit (`net8.0;net10.0`), so no local TFM patch is retained.
- To keep Copeland vendoring focused and merge-safe, non-required upstream projects/tests were intentionally pruned after sync (for example `vendor/Dominatus/tests`, `src/Dominatus.StrideConn`, and non-required samples). Required libraries and the two reference samples remain vendored.
