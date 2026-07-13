# CTS-M0d: accepted core-semantic profile enforcement

## Outcome

CTS-M0d accepts the selected CTS-M0c recommendations as Copeland TS language doctrine. It makes only the narrow frontend changes needed before the first JavaScript-backend milestone: deliberate `var` rejection, deliberate strict-equality rejection, language-law fixtures, and structural frontend/MIR evidence. It does not add JavaScript emission, a JavaScript runtime, first-class results, postfix unwrap, or `try`/`except`.

## Accepted doctrine

- `const` is a non-reassignable block-scoped binding. `let` is a reassignable block-scoped binding with a fixed declared type. `var` is rejected legacy JavaScript syntax. `const` is not deep immutability.
- Initial `number` is the full IEEE-754 binary64 domain. Arithmetic, division by zero, NaN, signed infinities, signed zero, and overflow follow binary64 behavior; no implicit conversions exist. Literal syntax remains the current integer-only subset, and integer kinds remain deferred.
- `==` and `!=` are typed primitive value equality/inequality. `===` and `!==` are reserved and rejected. Payload-enum equality is intended to be nominal and structural but is not implemented; array/object/class equality remains unresolved.
- Evaluation is deterministic left-to-right for binary operands, call/array/payload arguments, and source statements. `match` evaluates its scrutinee once and exactly one arm. Boolean operators short-circuit. A backend may reorder only when observationally equivalent.
- Payload enums are nominal tagged values. A later JavaScript backend must own a private, null-prototype, frozen tagged representation with ordered payloads; that representation is not public ABI and is not emitted in M0d.
- The canonical failure model is `Result<T, E> = ok(T) | err(E)`. `?`, postfix `!`, result matching, and expression-shaped `try`/`except` have accepted meaning, but only existing fallible signatures and direct `?` propagation are implemented. Postfix unwrap, first-class `ok`/`err`, lexical handler targets, `try`/`except`, and complete result MIR remain absent.
- Optionality uses ordinary payload-enum semantics. No privileged optional MIR/runtime representation is added; standard naming, cases, generic syntax, and standard-library ownership remain unresolved.

## Frontend enforcement

| Area | Before M0d | After M0d | Diagnostic |
| --- | --- | --- | --- |
| `var` | Tokenized, then accidentally entered parser recovery. | Parses as an ordinary declaration and reaches binder validation; no usable MIR is produced. | `COPE-PROFILE-0001` |
| `===` / `!==` | Lexed, parsed, bound, and lowered as ordinary equality. | Lexed and parsed for intentional validation, then rejected before MIR. They are never aliases for `==` / `!=`. | `COPE-PROFILE-0009` |
| `==` / `!=` | Accepted for matching type names, including unsupported families. | Accepted only for same-type supported primitives; cross-type and unsupported equality remain rejected. | existing `COPE-TYPE-0007` for non-profile invalid operands |

The lexer longest-match corpus continues to prove recognition of all equality tokens. No existing source corpus accepted `===` or `!==`, so no corpus expectation required migration.

## Language fixtures and focused evidence

Added fixtures:

- `Language/Invalid/declarations/var-declaration.cl-invalid.ts`
- `Language/Valid/equality/typed-equality.cl-valid.ts`
- `Language/Invalid/equality/strict-equality.cl-invalid.ts`
- `Language/Invalid/equality/strict-inequality.cl-invalid.ts`
- `Language/Invalid/equality/cross-type-equality.cl-invalid.ts`

Focused parser/binder/facade tests prove parsed profile rejection, stable diagnostics, absence of generic parser recovery, and the MIR gate. MIR structural tests prove current preservation of binary operand order, call argument order, payload argument order, one match scrutinee node, and distinct `&&` lowering. Those tests also prove supported numeric arithmetic remains typed as `number` and keeps its operator identity. They do not prove runtime NaN, infinity, division-by-zero, overflow, or signed-zero behavior; executable backend evidence is still required.

## Production scope

Changed production files:

- `src/Copeland/Copeland.TS/Syntax/Parser.cs`
- `src/Copeland/Copeland.TS/Semantics/Binder.cs`

The parser now routes `var` through its existing variable-declaration representation, including a `for` initializer. The binder uses existing `COPE-PROFILE-0001` for `var` and adds `COPE-PROFILE-0009` for reserved strict equality spellings. It preserves accepted `let`/`const` behavior and does not change accepted-program Cope MIR shape or generated C# corpus output.

## Validation

Validation completed on 2026-07-13:

| Command or check | Result |
| --- | --- |
| Focused `Copeland.TS.Tests` parser, binder, facade, language-fixture, and MIR-order tests | Passed: 67 tests in 0.06 s (1.09 s command wall time). |
| `dotnet build Copeland.TS.slnx` | Passed in 1.14 s. |
| `dotnet test Copeland.TS.slnx --no-build` | Passed: 175 tests (132 frontend + 43 C# backend) in 2.89 s. C# generated-output corpus comparisons passed. |
| `dotnet build Copeland.slnx` | Passed in 1.36 s. |
| `dotnet test Copeland.slnx --no-build` | Passed: 267 tests in 3.02 s. |
| `dotnet build JointTaskForce.slnx` | Passed in 3.03 s. |
| `dotnet test JointTaskForce.slnx --no-build` | Passed: 1,520 tests in 16.01 s. |
| `pwsh -NoProfile -File tools/Validate-DependencyBoundaries.ps1` | Passed for 26 production projects; includes solution/project-path and graph-cycle checks. |
| `pwsh -NoProfile -File tools/Validate-CopelandTsTopology.ps1` | Passed; includes Copeland TS topology and project graph checks. |
| Language fixture topology/content validation | Passed through the ordinary language-fixture tests: 9 valid and 16 invalid fixtures. |
| Changed-document local-link/path and stale-claim searches | Passed for the three M0d documents and their fixture links; historical M0c recommendation text is explicitly marked historical. |
| `git diff --check` | Passed. |

The broader Machina, Aurelian, integration, and slow lanes were not separately selected because this change is localized to the Copeland TS frontend, tests, and documentation and does not alter shared project topology or dependencies. The required JointTaskForce lane nevertheless exercised its included shared test projects successfully.

## Next milestone

Recommend **CTS-M1**: introduce a minimal MIR-only JavaScript backend for the accepted nonfallible subset, reject unsupported MIR explicitly, and execute one end-to-end `main()` program returning `42`.
