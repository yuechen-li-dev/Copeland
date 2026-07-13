# CTS-M4c JavaScript Result backend

CTS-M4c removes the JavaScript backend's CTS-M4b Result rejection boundary. The project edge remains `Copeland.TS.Backend.JavaScript -> Copeland.TS.Mir`; no frontend, C# backend, Roslyn, JavaScript engine package, shared runtime, or universal backend abstraction is introduced.

## Private representation

Each structural `MirResultType(T, E)` receives one deterministic, per-emitted-program private token. Equivalent Result shapes deduplicate; nested shapes receive separate tokens. A value is a frozen null-prototype object with compiler-private `$type`, `$tag` (`"ok"` or `"err"`), and one-item frozen `$payload` array. Enum and Result tokens are separate. Validators recursively check the token, tag, payload storage, and component type (including nominal enum identity) before a match or propagation consumes a Result.

The representation is not a host ABI. Construction uses the existing private frozen-value helper, so generated Copeland code cannot mutate tags or payloads and prototypes have no semantic role. `void ! E` uses `null` only as a private unit payload; source `undefined` remains excluded from Copeland.

## Lowering

`MirOkExpression` and `MirErrExpression` evaluate their payload once and construct the corresponding private value. Result values are passed, stored, and returned directly. Result matches lower to statements: evaluate and validate the scrutinee once, switch on the tag, bind exactly one arm-local payload, and assign one match value. The backend retains the older compact enum-match expression only where no Result control flow occurs.

The backend-local `EmittedExpression` pairs a value expression with ordered prelude statements. Binary operands, call arguments, constructors, and selected branches compose preludes left-to-right. `MirPropagateExpression` evaluates and validates its operand once; an `err` immediately returns an error Result from the actual enclosing generated function, while `ok` contributes its payload. There is no IIFE for Result control transfer and no JavaScript exception flow for ordinary `err`.

Malformed private Result values use the existing deterministic compiler-invariant panic (`Copeland JavaScript backend invariant failure.`). This private throw is not Result failure and is not a future `try`/`except` mechanism. Result equality remains rejected; generated JavaScript never falls back to reference equality.

## Evidence

Backend corpus fixtures are `result-construction-match` and `result-propagation`. Their SHA-256 hashes are respectively `E41DADDE7417A84A81F8A20CF22EE849B182703F1743190A89510310D0C32974` and `63734BDEE21591612CF1D6A1B064CC445F130E5D35DA36C038F5275E8ECDDE3F`. Existing primitive and payload-enum hashes remain `AD297686E173C5A30FD9D6CFA030F90DC048D604CFB7808063DED441EC74B5FC`, `C7FAD5A76AB26FF93396BE8038D496B70236B49B6316BCEB43F1ACE8DE59AD79`, and `EA992B0D572259A139FE56F785487D67F111AFDBC666FB89ADA097F04B9BE4FD`.

Node 26.2.0 runtime tests execute Result construction, both match arms, forwarding, direct and stored propagation, nested Result, Result enum payloads, and void success twice. The bounded parity test emits the same canonical MIR to Node and Roslyn-generated C#, observing `21` on both paths. It intentionally compares primitives rather than backend-private Result objects.

Postfix unwrap `!` and `MirUnwrapExpression` are implemented by CTS-M5. Lexical handlers and `try`/`except` remain unimplemented; [CTS-M6a](../language/copeland-ts-try-except-design-cts-m6a.md) selects a future JavaScript private structured-flow lowering and forbids JavaScript exception handling for ordinary Result `err`.

## Validation and changed scope

Validation used Node 26.2.0 and these commands: `dotnet build Copeland.TS.slnx`, `dotnet test Copeland.TS.slnx --no-build` (219 tests: 134 frontend/MIR, 40 JavaScript, 45 C# at the final scope); `dotnet build Copeland.slnx`, `dotnet test Copeland.slnx --no-build`; and `dotnet build JointTaskForce.slnx`, `dotnet test JointTaskForce.slnx --no-build`. The three requested solution build/test pairs completed successfully in about 35 seconds of wall time in this workspace. Focused Result tests report 3 JavaScript and 2 C# tests; the CLI Result path reports 15 CLI tests. `Validate-DependencyBoundaries.ps1`, `Validate-CopelandTsTopology.ps1`, corpus comparison, retired-symbol searches, and `git diff --check` also pass.

Changed files are limited to the JavaScript backend, its backend-owned corpus/tests, a test-only C#/Node parity harness and its test project reference, CLI integration tests, and these Result documentation records/profile updates. No production C# backend, frontend, MIR, project topology, Machina, Aurelian, or integration source changed.
