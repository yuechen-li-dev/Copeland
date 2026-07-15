# CTS-TYPE-M3 foundational type-system closeout

## Scope

M3 is a closeout, audit, documentation, and adversarial-evidence milestone. It adds no type-system feature and makes no production semantic change. The canonical authority is [CTS-TYPE-M3](../Copeland/architecture/copeland-ts-foundational-type-system-closeout-cts-type-m3.md); M0a–M2b retain detailed historical/design rationale.

## Starting state

| Item | Value |
| --- | --- |
| Revision | `1409759e55bf18e9cf9400ebba6657be2f7629a1` |
| Branch | `main` |
| Upstream divergence | `0 behind / 0 ahead` of `origin/main` |
| Worktree | clean before M3 changes |
| SDK | .NET SDK `10.0.301` (also 8/9/10 SDKs installed) |

## Audit outcome

The implemented system matches the accepted M0b–M2b contract:

- transparent, non-generic compilation-unit aliases with canonical expansion;
- field-only erased interfaces as generic requirement sets;
- named, closed-specialized generic functions whose bodies bind once;
- deterministic bounded direct-argument inference and contextual staging;
- shared explicit/inferred specialization identity;
- concrete ordinary MIR and C#/Diagnostic-JavaScript/Symbolic-JavaScript emission;
- concrete TSON identity and collision-safe rendered specialization names.

`PrimitiveTypeSymbol.Error` and `ErrorNominalTypeSymbol` were confirmed as recovery implementation details, not authored type algebra. `void` remains restricted; `null` and `any` are rejected; `unknown` is unresolved/no symbol; signatures are not first-class function types. No production defect was found, so M3 deliberately does not broaden or refactor implementation semantics.

## Evidence added or consolidated

- `BinderTests.Allocates_Each_Rendered_Name_Collision_Extension_And_Escaped_Identity_Fallback` forces the 16, 24, 32, full-digest, and escaped-identity name-allocation stages.
- `BinderTests.Explicit_And_Inferred_Instantiation_Identity_Is_Independent_Of_Call_Order` proves one concrete identity for either discovery order.
- Existing `GenericBackendParityTests` supplies the repeated C#/Node/Diagnostic/Symbolic adversarial trace. Existing alias, generic, parser, diagnostic-inventory, fixture, corpus, TSON, and CLI tests remain the ownership points for the rest of the M3 matrix.
- Existing artifact pins retain inferred-reuse outputs: MIR `756` bytes / `3386F1B0B1B5B25A65B14188AA108A6B196353BB0ED5B5C5D07B20C93A5FB6AF`; C# `1175` bytes / `3E983E41DB6658CB9D9F5513A3958F871D18FAE4E4621ECBAA39EFF507A891DA`; Diagnostic JS `2685` bytes / `2A620DE6C9EAA21AC2DA56512A60DC8200F231CB34BBE245AA6516E6CFEE3EE5`; Symbolic JS `1819` bytes / `75116BFD2227A9F84C271F1D18D3849109EB3C2B13A1BB29E71EC69874FE737B`.

The source corpus was not regenerated. The M3 changes are tests and documentation only.

## Requirement ledger

| Status | Requirement family | Closeout evidence |
| --- | --- | --- |
| Satisfied | Alias canonicalization, cycles, erasure, TSON identity | M0b tests/fixtures and M3 authority. |
| Satisfied | Interface requirements and constraint-only boundary | M1b inventories, open/closed bound-tree tests, parity matrix. |
| Satisfied | Generic bodies, specialization, concrete MIR | binder/corpus/backend tests. |
| Satisfied | Inference, staging, limits, no partial specialization | M2b tests/fixtures and generic inventory. |
| Stronger evidence | Identity reuse and collision allocation | M3 focused tests and existing artifact pins. |
| Satisfied | Parser ambiguity, diagnostics, fixture discoverability | parser/diagnostic/fixture theories. |
| Satisfied | MIR/backend/TSON boundary | structural assertions and topology/dependency validation. |
| Accepted-scope exclusion | Union/callable/TSXML/static/interop and advanced TypeScript features | M3 exclusions/routing. |

Missing rows: **0**.

## Validation record

| Command/check | Outcome |
| --- | --- |
| Focused alias/interface/generic/inference/parser inventories and C#/Node parity | Passed: 122 frontend focused tests and the parity matrix. |
| `dotnet build Copeland.TS.slnx --no-restore` | Passed, 0 warnings/errors. |
| `dotnet test Copeland.TS.slnx --no-build` | Passed: 629 frontend, 123 JavaScript, and 182 C# tests. |
| `dotnet build Copeland.slnx --no-restore` and `dotnet test Copeland.slnx --no-build` | Passed, 0 build warnings/errors; 42 CLI, 82 Markdown, and the complete TS suites passed. |
| `dotnet build JointTaskForce.slnx --no-restore` and `dotnet test JointTaskForce.slnx --no-build` | Passed, 0 build warnings/errors; all included Copeland, Aurelian, and Machina suites passed. |
| `Validate-CopelandTsTopology.ps1` and `Validate-DependencyBoundaries.ps1` | Passed under Windows PowerShell 5.1 and PowerShell 7; 27 production projects remain within dependency boundaries. |
| Documentation links/fences, MIR/backend leakage search, corpus pins, and `git diff --check` | Passed. |

The TypeScript suites include the filesystem language fixture theories, complete foundational diagnostic inventory, C#/Node adversarial parity in Diagnostic and Symbolic JavaScript, CLI freshness/staleness/determinism, TSON identity, retained corpus hashes, and record/enum/table/Result/control-flow regressions.

## Result

CTS-TYPE is closed with no missing ledger rows and no new language capability. `CTS-UNION` is the recommended next independent ladder. M3 does not start it.
