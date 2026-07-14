# CTS-REC-M0a immutable records audit

## Result

CTS-REC-M0a starts the separately named immutable-record product ladder after CTS-M4–M6 typed fallibility. It is documentation only. The implementation-ready authority is [Copeland TS immutable nominal records design](../Copeland/language/copeland-ts-immutable-records-design-cts-rec-m0a.md), and the [canonical language profile](../Copeland/language/copeland-ts-language-profile.md) records the accepted direction without claiming implementation.

No compiler, Cope MIR, backend, runtime, test, fixture, project, solution, or tooling behavior changes in this milestone.

## Starting state and method

- **Starting revision:** `1539762` (`Update fallibility docs for CTS-M6d closeout`).
- **Starting worktree:** clean (`git status --short` produced no paths).
- **Method:** current source/document inspection plus `rg`, `git log -S`, `git log --follow`, `git blame`, historical tree listings, and blob reads. No checkout, reset, restore, or worktree rewrite was used.

The audit inspected the canonical profile; parser/syntax, binder/bound nodes, type equivalence, lowering, Cope MIR/validator/writer, C# and JavaScript backends, enum/Result representations, evaluation-order tests, language fixtures/corpus, and topology/dependency validators.

## Historical and current findings

| Area | Finding | Classification |
| --- | --- | --- |
| objects | Brace property lists parse, but binding always rejects ordinary object literals. | implemented rejection plus reusable parser mechanism |
| fields/members | `receiver.name` parses; only enum-case members bind. | implemented rejection plus reusable syntax |
| assignment | member targets parse but only variable names bind as stores. | reusable syntax; record fields need stable immutable-field rejection |
| named aggregates | payload enums are nominal and ordered; no record/class/interface declaration exists. | reusable compiler mechanism |
| type equality | Result is structural; named MIR and fallback source equality still rely on names. | implemented current behavior, incompatible with robust record nominality |
| construction context | variables, returns, call arguments, arrays, Results, branches, and enum payloads already pass expected types. | reusable compiler mechanism supporting contextual literals |
| update | `with` is a reserved token inherited from early compiler history but has no parser/binder/MIR/backend contract. | proof-era residue |
| runtime | JavaScript enums/Results use private tokens and frozen null-prototype values; C# enums currently use synthesized records. | reusable enforcement, but backend representation is not language law |
| historical M1 profile | object/prototype semantics banned; objects/member access/classes/interfaces deferred. | compatible boundary evidence |
| historical support matrix | nominal records/classes and immutable record-like data proposed alongside interfaces, readonly, structural questions, and dictionary ideas. | historical proposal, not current law |
| Cope Test v0 | separate syntax-only test experiment with no aggregate law. | proof-era experiment |

The audit found no implemented or previously authoritative record, struct, interface, class, readonly-field, structural object, dictionary, or anonymous aggregate contract to inherit.

## Accepted decisions

- Declaration: `record Point { x: number; y: number; }`.
- Construction: contextual `{ x: 0, y: 0 }` with exactly one expected nominal record type.
- No anonymous record type, shape inference, structural conversion, explicit `Point { ... }` alternative, or callable `Point({ ... })` alternative.
- Nominality: stable compilation-local record type IDs and field IDs; same-shaped declarations remain distinct.
- Fields: explicit, required, closed, declaration-ordered, intrinsically immutable, and without defaults/setters/methods/modifiers.
- Construction: complete and exact, authored left-to-right evaluation, declaration-order canonical storage/emission.
- Access: `point.x`, known record receiver, stable field resolution, receiver evaluated once, field assignment rejected.
- `with`: same nominal type, source once first, replacements once left-to-right, all replacements see the original immutable value, empty update rejected, explicit nesting, no shorthand/computed fields/spread.
- Equality: `==` and `!=` rejected for records initially; no JavaScript reference or C# synthesized equality.
- Recursive records: rejected in the first slice; no recursive-type solver.
- Patterns, serialization, JSON, reflection, and host interop: unresolved/deferred.

## Architecture recommendations

Bound and MIR models receive dedicated record definitions, stable type/field identities, construction/access/update expressions, canonical declaration order, and authored initializer/replacement order. `MirRecordType` must carry record identity through functions, Results, enum payloads, arrays, and nested records. Shared MIR validation owns semantic completeness and identity checks; backend layouts remain absent from MIR.

The C# backend should generate ordinary sealed classes with complete compiler-owned construction and get-only members. It should not use C# `record` or `readonly record struct` as the first universal representation because those synthesize equality and other semantics Copeland has not accepted.

The JavaScript backend should use private per-record tokens, frozen null-prototype fixed-property objects, compiler-owned factories/validators, deterministic private names, and demand-driven helpers. Same-shaped host objects and different record declarations must not impersonate one another.

## Refined ladder

1. **CTS-REC-M0a:** documentation-only audit/design.
2. **CTS-REC-M0b:** deliberate syntax recognition, stable feature rejection, and invalid language-law fixtures without acceptance through MIR.
3. **CTS-REC-M1:** declarations, contextual construction, access, bound/MIR identities and nodes, validation, deterministic `.cope`; explicit backend rejection remains mandatory.
4. **CTS-REC-M2:** sealed-class C# representation and runtime proof.
5. **CTS-REC-M3:** private nominal frozen JavaScript representation and Node/C# parity.
6. **CTS-REC-M4:** dedicated `with` implementation and exactly-once/order parity.
7. **CTS-REC-M5:** closeout, diagnostics, stress/privacy/artifact stability; separately approve any equality or pattern follow-up.

Keeping C# in M2 matches the repository's separate frontend/MIR and backend corpus/runtime topology. It is not permission for silent miscompilation: M1 must make unsupported record MIR an explicit backend diagnostic. Keeping `with` in M4 isolates genuinely observable copy/update sequencing after both record representations exist and forbids temporary spread/mutation emulation.

## Files changed

- `docs/Copeland/language/copeland-ts-immutable-records-design-cts-rec-m0a.md`
- `docs/Copeland/language/copeland-ts-language-profile.md`
- `docs/migrations/cts-rec-m0a-immutable-records-audit.md`

## Validation

Validation completed on 2026-07-13:

| Check | Result |
| --- | --- |
| Exact changed-path and extension check | Passed: exactly the three Markdown files listed above; no production, test, fixture, project, solution, or tooling path changed. |
| Changed-document relative links, Markdown tables, fenced code blocks, referenced paths, and required terminology | Passed. |
| `pwsh -NoProfile -File tools/Validate-CopelandTsTopology.ps1` | Passed. |
| `pwsh -NoProfile -File tools/Validate-DependencyBoundaries.ps1` | Passed for 27 production projects; no exceptions. |
| `git diff --check` | Passed; Git emitted only its working-copy LF-to-CRLF advisory for the modified canonical profile. |

Full builds/tests were intentionally not run because the exact final scope is documentation-only.
