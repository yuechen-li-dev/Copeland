# CTS-M1: minimal MIR-only JavaScript backend

## Outcome

CTS-M1 introduces `Copeland.TS.Backend.JavaScript`, a BCL-only backend that consumes `MirProgram` and emits deterministic strict-mode JavaScript for one nonfallible subset. It emits no artifact when validation reports a backend diagnostic. The frontend remains backend-free and the CLI explicitly composes the new backend through `--emit javascript`.

## Changed files

- `src/Copeland/Copeland.TS.Backend.JavaScript/`: JavaScript emitter, local diagnostics, literal formatting, identifier encoding, and writer.
- `src/Copeland/Copeland.Cli/`: explicit JavaScript backend reference and emit selection.
- `tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/`: backend corpus, deterministic-output, rejection, invariant-culture, and Node execution evidence.
- `tests/Copeland/Copeland.Cli.Tests/`: JavaScript CLI output and no-partial-artifact coverage.
- `Copeland.TS.slnx`, `Copeland.slnx`, and `JointTaskForce.slnx`: production and test project membership.
- `tools/Validate-CopelandTsTopology.ps1`: JavaScript backend graph and `.g.js` ownership validation.
- Copeland TS topology, profile, and fast-loop documentation.

## Boundary

Supported M1 MIR is limited to nonfallible functions, typed `number`/`boolean` parameters, read-only locals, returns, Boolean/numeric literals, variables, direct named calls, arithmetic `+ - * / %`, and Boolean `MirIfExpression`. Arrays, equality, mutation, loops, enums/match, strings, fallibility, first-class results, objects/classes, modules, closures, async, and interop are rejected or absent from current MIR. There is no runtime package, minification, source map, browser claim, or source-language expansion.

## Determinism and proof

The emitter preserves function and statement order, writes LF newlines and semicolons, and formats numbers with invariant culture. Its corpus compares exact `.g.js` bytes and repeated emission compares the full artifact. Node.js executes the complete source -> validate -> MIR -> JavaScript sequence twice with a known test-only `main()` call; both runs return `42`.

Node is an execution host prerequisite. It is invoked with `ProcessStartInfo.ArgumentList`, closed stdin, concurrent stdout/stderr draining, a ten-second timeout, process-tree termination, unique temporary directories, and best-effort cleanup. No embedded JavaScript engine package is introduced. Node validation is not browser or DOM validation.

## Validation

Validation completed on 2026-07-13:

| Command or check | Result |
| --- | --- |
| Node engine | `node` v26.2.0. The backend test invokes `node <unique-temp-dir>/program.js` twice; both runs exit 0, write `42`, and have empty stderr. |
| Focused JavaScript backend tests | Passed: 12 tests in 0.19 s. This includes byte-for-byte corpus output, invariant-culture formatting, unsupported-MIR rejection, and repeated real-engine execution. |
| Focused CLI integration tests | Passed: 12 tests in 0.91 s, including `--emit javascript` and backend rejection without output creation. |
| `dotnet build Copeland.TS.slnx` | Passed in 1.04 s. |
| `dotnet test Copeland.TS.slnx --no-build` | Passed: 187 tests in 3.09 s (132 frontend, 43 C# backend, 12 JavaScript backend). Existing MIR and C# corpus comparisons passed unchanged. |
| `dotnet build Copeland.slnx` | Passed in 1.36 s. |
| `dotnet test Copeland.slnx --no-build` | Passed: 281 tests in 3.57 s. |
| `dotnet build JointTaskForce.slnx` | Passed in 6.81 s. |
| `dotnet test JointTaskForce.slnx --no-build` | Passed: 1,534 tests in 17.05 s. |
| `pwsh -NoProfile -File tools/Validate-CopelandTsTopology.ps1` | Passed: solution/project paths, graph-cycle checks, backend isolation, frontend isolation, and `.g.js` ownership. |
| `pwsh -NoProfile -File tools/Validate-DependencyBoundaries.ps1` | Passed for 27 production projects. |
| Vertical-slice artifact | `main-returns-42.g.js` SHA-256: `5219F8B5FC3B95298F53919F2CEACBF98D5169BFA9AEFD9EA78A02ECCD532082`. |
| `git diff --check` | Passed. |

The Machina, Aurelian, integration, and slow lanes are not separately selected because production changes remain inside the Copeland TS projects, their tests, and repo-level solution membership. The required `JointTaskForce.slnx` lane is still run because it exercises the changed graph.

## Recommended next milestone

The evidence supports choosing the next smallest semantic family only after M1: equality for primitive values or a narrowly specified tagged-data representation are candidates. This record does not precommit to payload enums, source maps, browser hosting, or fallibility.
