# Copeland TS JavaScript payload enums and match (CTS-M3)

## Outcome

CTS-M3 makes Copeland payload enums and exhaustive expression-valued `match` executable in the MIR-only JavaScript backend. It covers zero, one, and ordered multiple payloads of `boolean`, `number`, `string`, and non-recursive supported payload-enum types. Structural enum equality, Result/fallibility, `?`, postfix `!`, and `try`/`except` remain outside the backend.

## MIR finding

No MIR change was required. `MirEnum` and `MirEnumCase` retain declaration and payload order; `MirEnumValueExpression` retains nominal enum and case names plus ordered arguments; and `MirMatchExpression` retains one typed scrutinee, ordered arms, bindings, and typed arm results. The binder/lowerer already preserve these facts. The JavaScript backend resolves a case against the typed scrutinee enum, validates synthetic MIR defensively, and leaves the C# proof backend and `.cope` text unchanged.

## Private representation and lowering

The backend considered plain stable-shape objects, null-prototype records, and positional arrays. It selects frozen null-prototype records: each enum declaration receives one private frozen null-prototype token; each value is a frozen null-prototype record with private `$type`, textual `$tag`, and a frozen ordered `$payload` array. This keeps nominal comparison by token identity, makes case dispatch simple, prevents generated Copeland code from mutation, and ensures prototype lookup cannot participate in matching. The field names, tokens, tags, constructors, and panic helper are generated implementation details, not ABI.

Generated names use a deterministic backend-local `__cope_m3_` allocator that skips all emitted user identifiers. Construction calls a private helper with a payload array literal, whose JavaScript evaluation order is left-to-right.

`match` emits a local IIFE. It stores the scrutinee in one generated `const`, fully validates its private representation and payload shape, switches on the textual tag, creates payload bindings in declared order only inside the selected case block, and returns that arm's expression. The default uses a private invariant panic. The IIFE is generated control flow, not a Copeland source closure.

The validator rejects wrong type tokens, unknown tags, non-null-prototype or unfrozen records, missing/frozen-malformed payload storage, wrong payload arity, sparse payload positions, and wrong primitive/nested-enum payload types. It throws `Error("Copeland JavaScript backend invariant failure.")`; this is a compiler/host-boundary corruption path, never Copeland fallibility and never catchable by future `try`/`except` semantics.

## Boundary

Recursive payload shapes are diagnosed as unsupported for this milestone to keep malformed host cyclic values on the deterministic validation path. Backend validation also diagnoses unknown enum/case references, malformed payload metadata, non-exhaustive synthetic matches, unsupported payload or match-result types, and enum equality. Diagnostic failure returns no JavaScript artifact.

The project graph remains `Copeland.TS.Backend.JavaScript -> Copeland.TS.Mir`; no shared runtime package, frontend reference, global lookup, `eval`, or public tagged-value abstraction is introduced.

## Evidence

The backend-owned corpus adds `payload-enum-match` (zero/multiple/nested payload cases and nested match) and `nominal-enum-types` (two same-shaped enums with distinct tokens). Focused tests also cover synthetic malformed MIR, one scrutinee temporary, deterministic panic through bounded host plumbing, repeated Node execution, and built-CLI emission followed by Node execution. Artifact hashes and final validation are recorded in the companion migration record.

## Next milestone

CTS-M4 should design first-class Result/Cope-MIR deliberately, reusing the proven semantic tagged-value laws without treating this JavaScript record shape as a universal runtime representation.
