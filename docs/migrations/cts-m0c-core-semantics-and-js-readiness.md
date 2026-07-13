# CTS-M0c: core semantics and JavaScript readiness

## Result

CTS-M0c is a documentation-only language-design milestone. The detailed result is [Copeland TS core semantics and JavaScript-lowering design](../Copeland/language/copeland-ts-core-semantics-cts-m0c.md); the [canonical language profile](../Copeland/language/copeland-ts-language-profile.md) links to it and narrowly distinguishes currently implemented fallibility from the user-directed future surface.

No compiler, Cope MIR, backend, runtime, test, fixture, project, solution, or tooling behavior is changed.

## Audit record

- **Repository revision audited:** `2bf41d5026847f60f71650413e128a2c10ba9d35`.
- **Baseline:** revision `2bf41d5` contains the completed CTS-M0b documentation, tests, fixtures, project item, and topology-validator changes. M0c changes only the three documentation files listed below.
- **Method:** current-file inspection plus `git log`, `git log --follow`, `git log -S`, `git grep` against historical revisions, and current repository searches. No historical checkout or worktree rewrite was used.

## Evidence inspected

| Evidence family | Paths/revisions | Finding |
| --- | --- | --- |
| Canonical doctrine | `docs/Copeland/language/copeland-ts-language-profile.md` | CTS-M0a laws and CTS-M0b executable evidence; numbers, equality, evaluation order, optionality representation, and JS lowering were open. |
| Migration records | `cts-m0a-copeland-ts-language-doctrine-audit.md`, `cts-m0b-language-contract-fixtures.md` | Doctrine provenance, corpus/fixture separation, and the intentional `var` fixture gap. |
| Historical Copeland profile | `docs/Copeland/architecture/language-profile.md`; historical revision `4575d9e` | Fallible signature `T ! E`, postfix propagation `?`, Boolean branching, payload enums, and exclusions. No unwrap or `try`/`except`. |
| Historical support matrix | `docs/Copeland/architecture/copeland-typescript-support.md`; historical revision `be8466d` | Explicit fallibility and rejection of exception-driven flow; no source result constructors or handler syntax. |
| Current frontend | lexer, parser, syntax nodes/kinds, binder, symbols, bound nodes, diagnostics, compiler facade | Integer-only numeric literals; four equality spellings currently admitted by name equality; signature bang and prefix Boolean bang; postfix `?`; no postfix unwrap, result match, or `try`/`except`. |
| Cope MIR | `MirNodes.cs`, `MirTextWriter.cs`, `MirLowerer.cs` | Function error type and direct-call propagation flag exist; first-class fallible values, local handlers, unwrap, and result construction/match do not. |
| C# proof backend | `CSharpBackend.cs`, generated `.g.cs`, runtime tests | Demonstrates one result representation and direct propagation, but its records, exceptions, equality mapping, and emission order are not language authority. |
| Language evidence | CTS-M0b `Language` fixtures and M0/M1 corpus | Current declaration, Boolean, arithmetic, calls, arrays, payload enum/match, and direct `?` boundaries. No fixture was changed. |
| Focused tests | parser, binder, facade, MIR corpus, C# backend/runtime fallibility and payload-enum tests | Current acceptance/rejection and proof paths; no test establishes the proposed future surface. |
| Repository history | revisions `4575d9e`, `be8466d`, `dcae777`, and `git log -S`/historical `git grep` searches | No Copeland `except`, postfix unwrap, or `ok`/`err` source doctrine was recovered. Historical `Ok`/`Err` occurrences are generated C# support. |
| Adjacent Oct-shaped evidence | Aurelian SDSL-V compatibility matrix/audits and AST | Records prefix `try` propagation, prefix `unwrap`, postfix `?`/`!` compatibility, and `ok`/`err` match arms. It has no paired `except` semantics and is not Copeland authority. |

## Normative directives recorded

- `const` is non-reassignable and block scoped; `let` is mutable and block scoped; `var` is rejected.
- Copeland remains closed-world and rejects JavaScript coercion, loose equality, prototypes, implicit globals, ambient nullability, `null`, and ordinary `undefined`.
- Payload enums are nominal tagged data with exhaustive match.
- Fallibility is explicit value/control-flow semantics: `ok`, `err`, `?`, postfix unwrap `!`, `ok`/`err` matching, and `try`/`except` share one model.
- Copeland `try`/`except` is not JavaScript exception unwinding. Host exceptions can enter the result model only through future explicit conversion.

## Recommendations awaiting acceptance

| Area | Recommendation | Important alternative and tradeoff |
| --- | --- | --- |
| Numbers | Initial `number` is the complete IEEE-754 binary64 domain, preserving NaN, infinities, and signed zero; division/overflow do not trap. | A finite-only or checked model avoids special values but requires pervasive generated checks and diverges from current `double`/JS evidence. |
| Equality | Keep typed value `==`/`!=`; reserve/reject source `===`/`!==`; payload enums compare structurally. | Four operators could separate value and identity, but expose representation identity and pre-decide future class law. |
| Evaluation | Fix left-to-right evaluation, single scrutinee evaluation, selected-arm-only match behavior, and Boolean short circuiting. | Backend-defined order would make JS accidental doctrine and allow cross-backend disagreement. |
| Payload enum JS shape | Frozen null-prototype record with private nominal token, textual case tag, and frozen ordered payload. | Ordinary objects are smaller but leak prototype/mutation semantics; numeric tags renumber under declaration edits. |
| Unwrap | Postfix `!` on `err` enters a nonrecoverable Copeland panic/trap and never targets `except`. | Handler branching makes unwrap a redundant propagation spelling. |
| `try`/`except` | Expression-shaped paired handler; `?` targets the nearest lexical handler; reserve `try` for this paired form. | Statement-only handling complicates value recovery; importing Oct prefix `try expression` duplicates existing `?` and overloads `try`. |
| Fallible JS shape | Frozen null-prototype `ok`/`err` tagged records with generated tag branches, not JS `Error`/`throw`. | Native exceptions are concise but violate explicit typed failure flow. |
| Optionality | Use ordinary payload enums; add no privileged optional runtime or MIR representation. | A built-in `Option` would prematurely decide generics, standard-library ownership, and case spelling. |

## Proposed fallibility desugaring

The proposed expression:

```ts
try {
  const value: number = parseNumber(text)?;
  value + 1
} except (error: ParseError) {
  0
}
```

has canonical meaning equivalent to:

```ts
match parseNumber(text) {
  ok(value) => value + 1,
  err(error) => 0,
}
```

For multiple `?` operations, the continuation nests under each `ok` arm and every `err` transfers to one lexical handler without duplicating handler effects. Outside `try`, `?` returns the compatible `err` from the current fallible function. Postfix `!` extracts `ok` or traps; it never enters `except`.

This syntax and desugaring are proposed, not implemented.

## MIR findings

Current Cope MIR can express fallible function signatures and direct call propagation to the enclosing function. It cannot express:

- a first-class value of type `T ! E`;
- `ok` or `err` construction;
- explicit `ok`/`err` matching;
- propagation to a lexical handler rather than function return;
- unwrap and its panic edge;
- the control-flow join required by expression-shaped `try`/`except`.

The recommended later work is a small fallibility-specific MIR expansion: explicit result type/value construction, result inspection, propagation target, and panic edge. `try`/`except` should desugar before or during MIR construction; it should not introduce exception regions or a universal effects/runtime abstraction. This gap does not block the deliberately nonfallible CTS-M1 slice.

## JavaScript representation and readiness

The detailed semantic matrix is maintained in the [M0c decision document](../Copeland/language/copeland-ts-core-semantics-cts-m0c.md#javascript-semantic-matrix). In summary:

- direct JS is safe for validated lexical bindings, Boolean conditions, named calls, and accepted binary64 operations;
- equality, arrays, payload enums/match, and fallibility require either accepted representation law or generated enforcement;
- tagged enum and result values must not inherit public prototype semantics;
- no shared runtime package is justified for the first slice;
- general host interop remains deferred.

## First JS slice

CTS-M1 should emit only nonfallible MIR for Boolean/numeric literals, read-only locals, numeric arithmetic needed by the proof, named functions/calls, one `if` expression, and return. It should reject every other MIR node with backend diagnostics.

The candidate program computes `main() == 42` using `add(40, 2)` and a Boolean `if` expression. Backend test plumbing may invoke the known generated function directly; that is not a stable host ABI. The slice needs no runtime helpers and proves source-to-MIR-to-JS execution without relying on unresolved equality, arrays, enums, fallibility, or interop.

## Unresolved and deferred decisions

Unresolved:

- optional type/name/case/generic/standard-library spelling;
- deep immutability and future object/class identity;
- array equality, mutation, indexing, and bounds;
- panic diagnostics, termination ABI, and host observability;
- complete explicit interop syntax and ABI.

Deferred:

- general inference and expanded numeric literal/conversion syntax;
- integer types;
- statement-shaped `try`/`except` sugar;
- classes, modules, closures, async fallibility, and general interop.

## Fixture implications

No fixture changes belong in M0c. Later fixture work is bounded:

- CTS-M0d deliberately recognizes/rejects `var` and adds `Invalid/declarations/var-declaration.cl-invalid.ts`.
- Accepted equality spelling/type restrictions need focused valid/invalid language fixtures before JS equality emission.
- Numeric special-value and evaluation-order laws need executable backend/runtime evidence once the frontend can express motivating cases; malformed syntax is not evidence.
- `ok`/`err`, postfix unwrap, result match, and `try`/`except` fixtures wait for accepted grammar, binder, and MIR support.

## Bounded implementation sequence

1. **CTS-M0d:** accept or revise first-slice recommendations; implement only necessary profile enforcement, including intentional `var` rejection and its fixture. Do not add generalized backend/runtime abstractions.
2. **CTS-M1:** add the smallest MIR-only JavaScript backend, reject unsupported MIR explicitly, and execute the nonfallible `main() == 42` vertical slice.
3. **Later CTS milestones:** add one resolved family at a time—payload enums/match, equality, arrays after their law, and then fallibility after targeted MIR work. Implement `!` and `try`/`except` only with accepted semantics.

## Files changed by CTS-M0c

- `docs/Copeland/language/copeland-ts-core-semantics-cts-m0c.md`
- `docs/Copeland/language/copeland-ts-language-profile.md`
- `docs/migrations/cts-m0c-core-semantics-and-js-readiness.md`

## Validation

Validation completed on 2026-07-13:

| Check | Result |
| --- | --- |
| Changed-document local-link, Markdown table-column, and fenced-code-block checker | Passed for all three M0c documents. |
| `pwsh -NoProfile -File tools/Validate-DependencyBoundaries.ps1` | Passed for 26 production projects; no exceptions. |
| `pwsh -NoProfile -File tools/Validate-CopelandTsTopology.ps1` | Passed. |
| `git diff --check` | Passed. Git emitted only its working-copy LF-to-CRLF advisory for the modified canonical profile. |
| Exact changed-path and extension check over `git status --porcelain=v1` | Passed: exactly the three files listed above, all Markdown. No production, test, fixture, project, solution, or tooling path changed. |

Builds and tests were intentionally not run. The exact final scope check proves that CTS-M0c is documentation-only, so no compiler, MIR, fixture, backend, project, solution, or validation-tool behavior changed.
