# Copeland TS JavaScript backend (CTS-M1)

## Scope

CTS-M1 adds the first real product backend for the Copeland TS lane. Its graph is intentionally concrete:

```text
Copeland.TS ----------------> Copeland.TS.Mir <---------------- Copeland.TS.Backend.CSharp
       |
       +------------------> Copeland.TS.Mir <---------------- Copeland.TS.Backend.JavaScript

Copeland.Cli -> Copeland.TS + Copeland.TS.Mir + C# backend + JavaScript backend
```

Both backends reference only `Copeland.TS.Mir` and the BCL. The frontend references only MIR. The CLI is the explicit composition host. `tools/Validate-CopelandTsTopology.ps1` enforces these edges, solution paths, graph acyclicity, frontend isolation, and JavaScript-fixture ownership.

## Public surface and diagnostics

`JavaScriptBackend.Emit(MirProgram)` returns `JavaScriptCompilation`. A successful compilation has deterministic `SourceText`; a rejected or invalid MIR input has backend-local `JavaScriptDiagnostic` values and a null artifact. `COPE-JS-0001` identifies a deliberately unsupported M1 feature and `COPE-JS-0002` identifies structurally invalid MIR such as a missing call target. The backend never parses source, calls the frontend compiler, or mutates MIR.

## Supported M1 MIR

- Nonfallible functions with `number`/`boolean` parameters and `number`, `boolean`, or `void` returns.
- Read-only local declarations, return statements, and expression statements.
- Boolean and current numeric literals, variables, direct nonfallible calls, and `+`, `-`, `*`, `/`, `%` binary64 arithmetic.
- Boolean `MirIfExpression` values.

Emission preserves MIR function and statement order. JavaScript’s defined left-to-right operand and argument evaluation preserves the selected subset’s binary and call order; a conditional expression evaluates its condition and exactly one branch. Literals are formatted with invariant culture, output uses LF newlines and semicolons, and the artifact begins with strict mode.

## Rejected M1 MIR

The backend diagnoses, rather than silently lowering, fallible functions and calls (including propagation), strings, arrays, assignment/mutable locals, loops, unary/logical/equality operations, enum values, payload enums, match, statement `if`, objects/member access, and every unknown MIR node. Current MIR has no first-class result, closure, module, import/export, async, class, or interop node; their absence is not an implementation claim. No runtime package, source maps, minification, browser host, DOM API, or stable generated ABI is added.

## CLI and executable proof

The selected CLI spelling is `copeland compile <source> --emit javascript [--out <path>]`. It compiles source through `Copeland.TS`, passes validated MIR to this backend, and writes no artifact if backend diagnostics occur. Existing `mir` and `csharp` paths are unchanged.

The backend-owned corpus is `tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/TestData/Corpus/main-returns-42.ts` with its byte-for-byte `.g.js` artifact. The source contains `add(40, 2)` and a Boolean `if` expression; Node test plumbing appends `console.log(main());` and observes `42`. This calls a known generated function only for testing. It is not an export or interop ABI.

The proof host is Node.js, not a browser. Node execution establishes ordinary ECMAScript execution for this generated closed-world program; it does not establish browser, DOM, npm, bundling, or host-interop compatibility.

## Fast loop

```powershell
dotnet build Copeland.TS.slnx
dotnet test tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/Copeland.TS.Backend.JavaScript.Tests.csproj --no-build
dotnet test tests/Copeland/Copeland.Cli.Tests/Copeland.Cli.Tests.csproj --no-build
pwsh -NoProfile -File tools/Validate-CopelandTsTopology.ps1
pwsh -NoProfile -File tools/Validate-DependencyBoundaries.ps1
```

The CTS-M1 migration record contains the completed validation commands, engine version, test counts, timings, artifact hash, and broader-lane scope decision.
