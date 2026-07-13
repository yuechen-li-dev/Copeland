# JTF-M6c Copeland TS mise en place

## Status

Completed. This migration reorganizes the existing compiler proof path; it does not implement a JavaScript backend, a `.cope` parser/verifier, TSPack integration, TSX parsing, or language features.

## Migration decisions

- The old proof-era Script assembly/project/namespace was repository-only, so it was renamed cleanly to `Copeland.TS` without compatibility shims.
- `Copeland.TS.Mir` owns the canonical Cope MIR model and deterministic writer. It is BCL-only.
- `Copeland.TS` owns parsing, binding, diagnostics, and lowering through `CopelandCompiler.CompileToMir`.
- `Copeland.TS.Backend.CSharp` owns the current proof emitter and accepts `MirProgram` directly. `Copeland.Cli` composes the frontend with that backend.
- The former Cope Test proposal is superseded. TSPack owns `*.xtest.tsx`; TSX elements such as `<Fact>` are its executable test declaration surface. `.cope` is reserved exclusively for MIR text.
- The M6b cross-lane source-contract probe and its Aurelian test references were removed. Source infrastructure should be reconsidered only when two real lanes independently need compatible indexed-source behavior; Markdown's current contract is not normative.

## Ownership changes

`tests/Copeland/Copeland.TS.Tests/TestData/Corpus` now owns the compiler corpus. Former `.cope` program inputs were renamed to `.ts`; expected `.cope` files remain MIR projections. The frontend project owns syntax/binding/lowering and source-to-MIR tests. The C# backend project owns C# corpus comparisons and the unique Roslyn/runtime proof tests. CLI process behavior remains in its existing test project.

## Solution lanes

`Copeland.TS.slnx` contains the TS frontend, MIR, C# backend, and their focused tests. `Copeland.slnx` and `JointTaskForce.slnx` contain the renamed projects; Markdown remains independent.

## Validation record

| Lane | Build | Test | Result |
| --- | ---: | ---: | --- |
| `Copeland.TS.slnx` | 1.11s | 3.7s wall | 133 passed (90 frontend/MIR, 43 C# backend/runtime). |
| `Copeland.slnx` | 2.99s | 3.7s wall | 225 passed. |
| `Aurelian.slnx` | 4.68s | 16.4s wall | 583 passed; shader tests are independent again. |
| `JointTaskForce.slnx` | 6.19s | 17.0s wall | 1,478 passed. |
| `JointTaskForce.Integration.slnx` | 2.77s | 3.2s wall | 41 passed. |

The new focused lane is intentionally smaller than the Copeland umbrella and remains suitable for backend work. The older M1b timing record predates later repository work, so it is historical evidence rather than a directly comparable performance baseline. `Validate-DependencyBoundaries.ps1`, `Validate-CopelandTsTopology.ps1`, solution/project-path checks, cycle checks, fixture checks, stale-identity scans, documentation-link checks, and `git diff --check` passed.

Corpus equivalence is proved by the unchanged deterministic corpus comparisons: 74 `.ts` program sources and 13 expected `.cope` MIR projections passed, alongside all expected `.g.cs` comparisons. No `.g.js` fixture exists.

## Deferred closeout (JTF-M6d)

Verify the migrated focused and umbrella timing baseline, keep the boundary validator as part of ordinary fast-lane checks, and record the resulting baseline. Do not expand the scope into JavaScript emission, `.cope` parsing, TSPack execution, shared source infrastructure, or new language semantics.
