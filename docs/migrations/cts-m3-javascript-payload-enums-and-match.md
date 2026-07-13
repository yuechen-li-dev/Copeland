# CTS-M3: JavaScript payload enums and match

## Change

CTS-M3 extends `Copeland.TS.Backend.JavaScript` from the CTS-M1/M2 primitive subset to non-recursive payload-enum construction and exhaustive `match`. The work is backend-only: Cope MIR was audited and already carries nominal enum declaration identity, stable source case identity within that nominal type, ordered typed payload fields and arguments, one match scrutinee, ordered arm bindings, and typed expression results. No MIR, lowering, textual `.cope`, or C# backend change was necessary.

The selected private JavaScript shape is a frozen null-prototype record containing a per-enum object token, textual case tag, and frozen ordered payload array. Null prototypes remove ordinary prototype lookup from match semantics; freezing makes generated values immutable. Both are physical backend choices, not source ABI. A deterministic generated-name allocator avoids all emitted user function, parameter, and local identifiers.

Matches emit a local IIFE with a `const` scrutinee, private validator call, `switch`, selected-case bindings, and a default invariant panic. This preserves exactly-once scrutinee evaluation and one-arm evaluation without exceptions as match control flow. The panic is a bounded `Error` throw only for invalid backend/host values.

## Files changed

- `src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs`
- `tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/JavaScriptBackendTests.cs`
- `tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/JavaScriptRuntimeTests.cs`
- `tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/payload-enum-match.ts` and `.g.js`
- `tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/nominal-enum-types.ts` and `.g.js`
- `tests/Copeland/Copeland.Cli.Tests/CliIntegrationTests.cs`
- `docs/Copeland/language/copeland-ts-language-profile.md`
- this record and the CTS-M3 architecture record

## Evidence and validation

Node.js `v26.2.0` executes the payload/nested-match program twice with `nested`, and the selected-arm/scrutinee test twice with `1` plus the stable corruption message. The built CLI emits a pair payload/match program that Node executes as `ordered`.

| Artifact | SHA-256 |
| --- | --- |
| `payload-enum-match.g.js` | `C7FAD5A76AB26FF93396BE8038D496B70236B49B6316BCEB43F1ACE8DE59AD79` |
| `nominal-enum-types.g.js` | `EA992B0D572259A139FE56F785487D67F111AFDBC666FB89ADA097F04B9BE4FD` |

The corpus compares exact LF bytes and repeats emission; its two new hash assertions make the artifact identities explicit. M1/M2 JavaScript artifacts, all `.cope` artifacts, and C# `.g.cs` artifacts remain unchanged.

| Validation | Result |
| --- | --- |
| `dotnet build Copeland.TS.slnx` | Passed, 0 warnings/errors, 0.91 s. |
| `dotnet test Copeland.TS.slnx --no-build` | Passed 210 tests: 132 frontend, 43 C# backend, 35 JavaScript backend; longest reported shard 1 s. |
| `dotnet build Copeland.slnx` | Passed, 0 warnings/errors, 1.07 s. |
| `dotnet test Copeland.slnx --no-build` | Passed 306 tests: 132 frontend, 43 C# backend, 35 JavaScript backend, 14 CLI, and 82 Markdown; longest reported shard 1 s. |
| `dotnet build JointTaskForce.slnx` | Passed, 0 warnings/errors, 2.51 s. |
| `dotnet test JointTaskForce.slnx --no-build` | Passed 1,559 tests; longest reported shard 13 s. |
| `pwsh -NoProfile -File tools/Validate-CopelandTsTopology.ps1` | Passed: solution/project paths, graph cycles, backend isolation, and fixture ownership. |
| `pwsh -NoProfile -File tools/Validate-DependencyBoundaries.ps1` | Passed for 27 production projects. |
| `git diff --check` | Passed. |

Machina, Aurelian, integration, and slow-specific lanes are not separately selected because this change is confined to the Copeland compiler/backend/test graph; the required `JointTaskForce.slnx` pass remains the shared validation lane.

## Follow-up

Recommend CTS-M4: a targeted first-class Result/Cope-MIR design, explicitly separate from this private JavaScript enum layout.
