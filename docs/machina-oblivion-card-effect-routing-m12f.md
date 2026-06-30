# Machina Oblivion Card Effect Routing M12f

## Purpose

M12f adds the explicit action and effect routing skeleton for Oblivion cards.

This milestone prepares the execution runway without executing anything.

## Why effect routing exists before execution

Execution needs a stable seam before Roslyn, xUnit, artifact opening, or any future host integration can be added safely.

M12f establishes that seam first so later behavior lands behind a bounded contract instead of leaking per-kind branching into the shell.

## Card action invocation

M12f introduces an explicit card action invocation record with:

- card id
- action id
- page id
- source path

The shell/router owns selected-card action routing.

## Effect request model

Handlers now create localized effect requests.

Each request records:

- deterministic request id
- card id
- effect kind
- intent
- string properties

## Effect result model

The shell router now returns explicit effect results with:

- request id
- card id
- effect kind
- status
- message
- diagnostics
- artifacts

In M12f those results are deferred or rejected only.

## Effect router

`OblivionCardEffectRouter` is the generic routing seam.

Known effect kinds route to deterministic deferred results.

Unknown or custom effect kinds route to deterministic rejected results.

No side effects occur.

## Handler responsibilities

Every card remains a mini-app/applet.

Card handler owns:

- action descriptors
- effect request creation
- local diagnostics
- local artifacts
- compact view
- inspector view

## Shell responsibilities

Shell/router owns:

- selected card action routing
- effect request dispatch
- effect result storage
- generic deferred handling

## Inspector display

The inspector now shows:

- available actions
- action routing state
- latest routed effect request
- latest routed effect result
- explicit deferred messaging

The core user-facing reminder is:

```text
Effect routing skeleton only.
Execution deferred to M13+.
```

## Relationship to Dominatus

Dominatus remains the intended future effect host/orchestration layer.

M12f only documents and preserves the seam:

`card action -> effect request -> generic router -> deferred result`

## What changed

- added card action invocation model
- added effect request and effect result contracts
- added deterministic effect router skeleton
- added effect state storage keyed by card id
- added shell dispatch for card action invocation
- updated handlers to create localized effect requests
- updated inspector rendering to surface routed request/result state
- added M12f manifests, tests, and exports

## What did not change

- no Roslyn execution
- no xUnit `[Fact]` / `[Theory]` execution
- no shell command execution
- no artifact generation from actions
- no file mutation from actions
- no Visionary implementation
- no new card species

## Deferred work

- Dominatus-backed effect execution
- Roslyn compilation and execution
- xUnit execution host
- real artifact open/export behavior
- inspector action hit regions
- richer effect history beyond last-result-per-card
