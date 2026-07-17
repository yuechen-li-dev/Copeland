# Copeland TypeScript Support Matrix (historical)

> Historical planning matrix. The current authoritative language law and implemented/intended distinction are in the [Copeland TS language profile](../language/copeland-ts-language-profile.md). In particular, proposed C# lowerings in this record do not decide future JavaScript semantics.

> **Current doctrine correction (CTS-TYPE-M0a):** the proposals below for nominal/direct-C# interfaces and direct-C# generic lowering are preserved as history, not current direction. The accepted architecture recommendation treats interfaces as erased structural requirement sets, aliases as transparent, and backend generic representation as private. See [CTS-TYPE-M0a](../language/copeland-ts-type-system-design-cts-type-m0a.md).

## Purpose

This document tracks Copeland language support as a **TypeScript-shaped compiler profile**, not a JavaScript runtime. The current C# backend is a proof backend; JavaScript is the planned first product backend.

## Status Legend

- **Implemented**: feature exists and is covered by tests through relevant stage.
- **Partial**: syntax or semantics exists, but lowering/runtime/docs/tests are incomplete.
- **Planned**: accepted design direction, not implemented yet.
- **Deferred**: likely needed later, intentionally out of current phase.
- **Rejected**: intentionally banned by Copeland profile.
- **Unknown**: needs source audit before claiming support.

## Compiler Pipeline Stages

- Lex
- Parse
- Bind/Semantics
- MIR
- Cope MIR text
- C# proof emit
- Roslyn Compile
- Runtime Invoke
- CLI
- Docs

## Current Language Identity

Copeland currently presents an **M1 strict profile**: explicit typed declarations, primitives, arrays, fallible signatures/propagation, `if` expressions, payload enums, and exhaustive `match`, with explicit bans on broad JavaScript/TypeScript dynamic behavior.

Copeland’s current truth is “safe restricted profile with end-to-end compiler proofs,” not broad TypeScript compatibility.

## Support Matrix

| Feature | TS/Copeland Shape | Status | Current Stage | Lowering Strategy | Notes / Next Step |
|---|---|---|---|---|---|
| `number`/`string`/`boolean`/`void` | Primitive types | Implemented | Lex -> Runtime Invoke -> Docs | Direct primitive mapping | Core M1 profile.
| `let`/`const` | Explicit typed declarations | Implemented | Lex -> Runtime Invoke -> CLI -> Docs | Direct variable lowering | `var` rejected.
| Functions | Typed params + typed returns | Implemented | Lex -> Runtime Invoke -> CLI -> Docs | Direct function lowering | Foundation present.
| `return` | Explicit return statements | Implemented | Parse -> Runtime Invoke | Direct statement lowering | Covered by parser/binder/backend tests.
| Blocks | Statement blocks | Implemented | Parse -> Runtime Invoke | Structured block lowering | Baseline control-flow substrate.
| `if` statements | Boolean-only branch | Implemented | Parse -> Runtime Invoke -> Docs | Conditional lowering | Truthy/falsy rejected.
| `if` expressions | Expression-valued branch | Implemented | Bind/Semantics -> Runtime Invoke -> Docs | Lower to expression-compatible C# structure | Branch type agreement enforced.
| `while` | Looping construct | Unknown | Unknown / needs audit | Unknown | Verify syntax+binder+backend coverage before claim.
| `for` | Looping construct | Unknown | Unknown / needs audit | Unknown | Verify support/ban status explicitly.
| Assignment | Typed assignment | Implemented | Parse -> Runtime Invoke | Direct assignment lowering | Type mismatch diagnostics documented.
| Binary/unary operators | Arithmetic/logical/comparison | Partial | Parse -> Bind/Semantics -> MIR | Direct operator lowering where supported | Operator coverage matrix needs dedicated audit.
| Arrays | `T[]` | Implemented | Parse -> Runtime Invoke -> Docs | Direct `T[]` lowering | Part of M1 supported surface.
| Object literals | JS/TS object literal | Deferred | Docs | Planned Copeland-native typed object model later | Explicitly deferred in M1 docs.
| Member access | Property/member access | Deferred | Docs | To be defined with nominal data model | Deferred with object semantics.
| Calls | Function calls | Implemented | Parse -> Runtime Invoke | Direct invocation lowering | Fallibility rules apply.
| Fallible functions `! Error` | Signature-level fallibility | Implemented | Parse -> Bind/Semantics -> MIR -> C# Emit -> Runtime Invoke -> Docs | Copeland-native error channel lowering | Core identity feature.
| Propagation `?` | Fallible propagation | Implemented | Parse -> Bind/Semantics -> MIR -> C# Emit -> Runtime Invoke -> Docs | Copeland-native propagation lowering | Enforced by type diagnostics.
| Null ban | No `null` | Rejected | Parse/Binder diagnostics -> Docs | N/A | Explicit profile ban.
| `var` ban | No `var` | Rejected | Parse/Binder diagnostics -> Docs | N/A | Explicit profile ban.
| `eval` ban | No runtime eval | Rejected | Parse/Binder diagnostics -> Docs | N/A | Explicit profile ban.
| Ternary ban | No `?:` | Rejected | Parse/Binder diagnostics -> Docs | N/A | Use `if` expression.
| Optional chaining ban | No `?.` | Rejected | Parse/Binder diagnostics -> Docs | N/A | Use explicit fallible/branching model.
| Implicit global ban | No undeclared global assignment | Rejected | Bind/Semantics -> Docs | N/A | Explicit profile hardening.
| Payload enums | Tagged enum + payloads | Implemented | Parse -> Bind/Semantics -> MIR -> C# Emit -> Runtime Invoke -> Docs | Copeland enum lowering | Constructor/match diagnostics documented.
| `match` expressions | Exhaustive enum/domain branch | Implemented | Parse -> Bind/Semantics -> MIR -> C# Emit -> Runtime Invoke -> Docs | Copeland-native match lowering | Exhaustiveness/type checks documented.
| Interface | TS interface subset | Planned | Docs | Likely direct C# interface (nominal) | TS-M2a candidate.
| Class | TS class subset | Planned | Docs | Likely strict C# class subset | TS-M2b candidate.
| Constructor | Class constructor subset | Planned | Docs | Lower in class pipeline | Depends on class milestone.
| `this` | Instance member receiver | Planned | Docs | Nominal class semantics | Depends on class model.
| Access modifiers | `public/private/protected` subset | Planned | Docs | Direct C# access modifiers | Keep strict subset only.
| `readonly` | Read-only members | Planned | Docs | Direct C# readonly/init mapping | Define exact profile first.
| Generics | Restricted generic parameters | Planned | Docs | Direct C# generics subset | TS-M2e candidate.
| Type aliases | Alias-only type defs | Planned | Docs | Compile-time alias mapping | Constrain to non-advanced forms first.
| Modules/import/export | Project/module boundaries | Planned | CLI/Docs | Namespace/module class lowering | TS-M2c candidate.
| `async`/`await` | Async functions and await points | Planned | Docs | `Task`/`Task<T>` lowering | TS-M2d candidate.
| Promise model | `Promise` compatibility shape | Deferred | Docs | Map to `Task` semantics | Do not implement JS event-loop semantics.
| Arrow functions | Lambda syntax | Supported bounded subset | CTS-CALL-M1 | Explicit capture only; no async/generator/default/rest/destructuring. | See CTS-CALL-M1 complete callable semantics.
| Function values/delegates | First-class callable values | Supported bounded subset | CTS-CALL-M1 | Exact signatures, immutable environments, no equality/serialization. | See CTS-CALL-M1 complete callable semantics.
| Typed object construction | Structural-like construction | Planned | Docs | Prefer nominal records/classes | TS-M2f candidate.
| Record-like data | Immutable data records | Planned | Docs | C# record-style lowering | Coordinate with object model.
| String literal unions | Narrow union-of-literals | Deferred | Docs | Candidate via enums or constrained unions | Needs design.
| Union types | TS unions | Deferred | Docs | Copeland payload enums + match | Prefer native replacement strategy.
| Optional properties | `prop?: T` | Deferred | Docs | Needs nominal option model | Avoid JS `undefined` semantics.
| Index signatures | Dynamic key maps | Deferred | Docs | Possibly mapped to dictionary-like types | Needs strict rules.
| Mapped types | Type-level mapping | Rejected (initial phases) | Docs | N/A | Intentionally out for early milestones.
| Conditional types | Type-level conditionals | Rejected (initial phases) | Docs | N/A | Intentionally out for early milestones.
| Decorators | TS decorator metadata system | Deferred | Docs | TBD | Very likely post-core milestone.
| Namespace/declaration merging | TS merging behavior | Rejected | Docs | N/A | Incompatible with strict nominal profile.
| Ambient declarations | `declare` ecosystem model | Deferred | Docs | TBD interop stubs later | Consider after module/interop baseline.
| Task/async lowering | CLR async interop | Planned | Docs | Lower to `Task`/`Task<T>` | Needs runtime proof tests.
| Async fallibility | CopeResult + async | Planned | Docs | Compose fallible + async lowering | Requires design for `?` in async context.
| NuGet/CLR interop | Referencing external .NET APIs | Unknown | Unknown / needs audit | Unknown | Verify current CLI/compiler facade capabilities.
| Native C# interface/class emit | Direct C# type emission | Planned | Docs | Direct nominal C# output | Tied to interface/class milestones.
| NativeAOT readiness | AOT constraints | Unknown | Unknown / needs audit | Unknown | Not enough evidence yet.
| Source maps / diagnostics mapping | TS-to-generated mapping | Partial | Diagnostics + CLI docs | Diagnostic IDs exist; mapping depth unclear | Audit spans/artifact source mapping.

## Feature Details

- The implemented core currently centers on explicit typing + deterministic lowering across bound/MIR/C# stages.
- Fallibility (`! Error` + `?`) and enum/match are first-class language identity features, not add-ons.
- CLI support is currently artifact-oriented (`mir` and `csharp` emission), with runtime proof mainly exercised in tests. `.cope` is emitted Cope MIR text, not source input.

## Intentional Profile Bans

Known bans in current profile:

- `eval`
- `var`
- implicit `any`
- implicit globals
- `null` / `undefined`
- optional chaining `?.`
- ternary `?:`
- truthy/falsy coercion branching

Likely/initial bans for upcoming compatibility phases:

- prototype mutation
- dynamic property addition
- declaration merging
- mapped types (initially)
- conditional types (initially)
- template-literal types (initially)

## Direct C# Lowering Candidates

Likely direct, strict-subset lowering targets:

- `interface` -> C# interface (nominal, not structural).
- `class` -> C# class (strict Copeland subset).
- `async`/`await` -> `Task`/`Task<T>`.
- generics -> constrained C# generics subset.
- arrays -> `T[]`.
- modules/import/export -> namespace/static module class pattern.
- arrow functions -> delegates/lambdas (later phase).

## Copeland-Native Replacements

Preferred model where TypeScript semantics do not map cleanly:

- TS union types -> payload enums + `match`.
- optional chaining -> explicit fallible/option-style modeling.
- ternary -> `if` expression.
- `null`/`undefined` -> banned from profile.
- `Promise` -> `Task` model.
- exception-driven flow -> fallible function model.
- TS enum patterns -> Copeland nominal enum model where possible.

## Runtime / CLR Integration Matrix

| Area | Status | Notes |
|---|---|---|
| C# emission pipeline | Implemented | End-to-end artifact generation exists.
| Roslyn compile proof | Implemented | CLR proof path called out in README/tests.
| Runtime invoke proof | Implemented (test-path) | Real runtime proof exists in tests; CLI runtime execution is not primary path yet.
| CLI compile `--emit mir|csharp` | Implemented | Artifact probe mode available.
| External package interop | Unknown | Needs explicit audit and examples.
| Async/Task integration | Planned | Part of upcoming compatibility milestones.

## Test Coverage Matrix

| Area | Status | Evidence Level |
|---|---|---|
| Lexer/parser core | Implemented | Dedicated lexer/parser tests and corpora present.
| Binder/type checks | Implemented | Binder/type/diagnostic tests present.
| MIR lowering | Implemented | MIR corpus/tests present.
| C# backend | Implemented | Backend tests present.
| Runtime invoke proof | Implemented | Runtime tests present.
| CLI artifact compile | Implemented | CLI integration tests present.
| Advanced TS compatibility (classes/modules/generics/async) | Planned/Deferred | No current support claims.

## Near-Term Roadmap

- **TS-M2a**: interface syntax + nominal C# interface lowering.
- **TS-M2b**: class syntax + strict class lowering (`this`, constructors, access modifiers subset).
- **TS-M2c**: minimal modules/import/export project model.
- **TS-M2d**: async/await + `Task` lowering, including fallible async interaction.
- **TS-M2e**: strict generics subset.
- **TS-M2f**: typed object construction / record-like data.

## Open Questions

- Do we permit any structural typing, or stay fully nominal for M2?
- What exact async fallibility shape should replace `throw`-driven flows?
- Should type aliases be syntax-only sugar or preserved as named semantic artifacts?
- What interop contract is required for NuGet/API boundary safety?
- Which currently-unknown features (`while`, `for`, advanced operator set) are already implemented but undocumented?

## References

- `README.md`
- `docs/Copeland/architecture/language-profile.md`
- `docs/diagnostics.md`
