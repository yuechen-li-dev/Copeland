# Audit 0001 — A1 Dominatus Runtime Smoke

## 1. Files changed

- Added vendored Dominatus source under `vendor/Dominatus/`:
  - `vendor/Dominatus/README.md`
  - `vendor/Dominatus/LICENSE.txt`
  - `vendor/Dominatus/src/Dominatus.Core/`
  - `vendor/Dominatus/src/Dominatus.OptFlow/`
- Updated `Aurelian.slnx` to include only `Dominatus.Core` and `Dominatus.OptFlow` from the vendor tree.
- Updated `src/Aurelian.Runtime/Aurelian.Runtime.csproj` so `Aurelian.Runtime` references `Dominatus.Core`.
- Added `src/Aurelian.Runtime/DominatusSmokeRuntime.cs`.
- Added `tests/Aurelian.Runtime.Tests/DominatusRuntimeSmokeTests.cs`.
- Updated A1 status and boundaries in:
  - `README.md`
  - `docs/architecture/vendor-strategy.md`
  - `docs/architecture/aurelian-charter.md`
  - `docs/architecture/mvp-roadmap.md`

## 2. Task scope

A1 installed the runtime spine only. It did not implement rendering, windowing, asset/shader work, an Aurelian world store, Stri-V migration, Machina migration, WyrmCoil migration, Stride linkage, or graphics/windowing packages.

## 3. Dominatus vendor source

Dominatus was copied from:

```text
https://github.com/yuechen-li-dev/Dominatus/
```

Source commit:

```text
220df609fc5c4aebca63ed07b953aa13be969ac2
```

Copied minimal buildable source:

```text
vendor/Dominatus/src/Dominatus.Core/
vendor/Dominatus/src/Dominatus.OptFlow/
```

Also copied lightweight root metadata:

```text
vendor/Dominatus/README.md
vendor/Dominatus/LICENSE.txt
```

No generated `bin/` or `obj/` artifacts are intentionally vendored.

## 4. Projects added to solution

Added only:

```text
vendor/Dominatus/src/Dominatus.Core/Dominatus.Core.csproj
vendor/Dominatus/src/Dominatus.OptFlow/Dominatus.OptFlow.csproj
```

No Dominatus tests, samples, actuators, server, LLM projects, Stride connector projects, or utility projects were added.

## 5. Project reference changes

`Aurelian.Runtime` now references:

```text
vendor/Dominatus/src/Dominatus.Core/Dominatus.Core.csproj
```

`Aurelian.Core` remains Dominatus-free. `Aurelian.Actuation` does not reference Dominatus. `Aurelian.Runtime.Tests` reaches Dominatus through `Aurelian.Runtime` only.

## 6. Dominatus API surface used

The smoke uses the following Dominatus API surface:

- `Dominatus.Core.StateId`
- `Dominatus.Core.Blackboard.BbKey<T>`
- `Dominatus.Core.Hfsm.HfsmGraph`
- `Dominatus.Core.Hfsm.HfsmInstance`
- `Dominatus.Core.Nodes.AiStep`
- `Dominatus.Core.Nodes.Steps.Act`
- `Dominatus.Core.Nodes.Steps.AwaitActuation<T>`
- `Dominatus.Core.Nodes.Steps.Succeed`
- `Dominatus.Core.Runtime.ActuationId`
- `Dominatus.Core.Runtime.ActuatorHost`
- `Dominatus.Core.Runtime.AiAgent`
- `Dominatus.Core.Runtime.AiCtx`
- `Dominatus.Core.Runtime.AiWorld`
- `Dominatus.Core.Runtime.Commands.LogCommand`
- `Dominatus.Core.Runtime.Commands.LogHandler`

`Dominatus.OptFlow` is present and buildable in the solution as requested, but Aurelian does not reference it yet.

## 7. Smoke runtime design

`DominatusSmokeRuntime.TickOnce()` constructs a tiny Dominatus actuation host, world, HFSM graph, agent, and root node. The root node dispatches a `LogCommand`, awaits the immediate typed actuation completion, stores the payload in the Dominatus blackboard, and succeeds. One deterministic world tick advances time by `1f / 60f` and returns a typed `DominatusSmokeResult`.

`DominatusSmokeRuntime.TickOnceAndReport()` converts that typed result into a deterministic trace string for a narrow smoke assertion.

This file is intentionally smoke-level and does not define Aurelian's final runtime/world API.

## 8. Tests added

Added `DominatusRuntimeSmokeTests` with two assertions:

- `DominatusSmokeRuntime_TickOnce_CompletesDeterministically`
- `DominatusSmokeRuntime_TickOnce_ProducesExpectedTrace`

The tests prove that Aurelian can build against vendored Dominatus, construct a minimal Dominatus runtime graph/session-like world, tick deterministically, observe an immediate actuation result, and produce a stable report.

## 9. Reference boundary checks

Boundary checks performed:

```bash
rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src tests vendor/Dominatus -g '*.cs' -g '*.csproj' || true
```

Result: no Aurelian production/test source or project reference linked `CodeReferences`, Stride, Machina, WyrmCoil, or Copeland. The vendored Dominatus source itself contains its own `Dominatus.StrideConn` references in README text only because the upstream README was copied as metadata; no `Dominatus.StrideConn` project was copied or linked.

`dotnet list Aurelian.slnx reference || true` was also run as requested. The .NET CLI reported `Project 'Aurelian.slnx' is invalid.` for this command even though `dotnet build Aurelian.slnx` and `dotnet test Aurelian.slnx` both succeeded; this appears to be a CLI limitation for `dotnet list ... reference` with `.slnx` inputs.

## 10. Validation results

Successful validation:

```bash
dotnet build Aurelian.slnx -c Debug
```

```bash
dotnet test Aurelian.slnx -c Debug
```

The final build completed with zero warnings and zero errors. The final test run passed all Aurelian tests, including the new Dominatus runtime smoke tests.

## 11. Next recommendation

A2 — Data world M0

A2 should:

- create minimal entity/world/component-store contracts;
- keep Dominatus linked but not over-integrated;
- produce a world snapshot/query surface;
- keep rendering out of scope.
