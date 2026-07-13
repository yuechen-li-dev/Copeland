# Copeland TS JavaScript primitive equality (CTS-M2)

## Outcome

CTS-M2 extends the MIR-only, runtime-free JavaScript backend with typed primitive equality. Copeland source `==` and `!=` lower to generated JavaScript `===` and `!==` respectively. The generated operators are an implementation detail: source `===` and `!==` remain deliberately rejected by the binder with `COPE-PROFILE-0009`.

The supported families are `boolean`, IEEE-754 binary64 `number`, and `string`. Strict JavaScript equality directly gives the required primitive laws: Boolean value equality, `NaN !== NaN`, `-0 === +0`, and same-code-unit string equality. The backend adds no coercion, `Object.is`, stringification, JSON, helper, runtime package, or reference-equality fallback.

## MIR audit and boundary

No MIR shape change was required. `MirBinaryExpression` already retains its canonical operator, ordered left and right `MirExpression` operands, and Boolean result type; every expression already has a `MirType`. Existing lowering transfers the bound types to both operands and preserves `==`/`!=`. Thus validated MIR distinguishes `boolean`, `number`, `string`, array names, enum names, and unknown/future names without reparsing source or referencing binder internals.

The binder rejects `===`/`!==` before lowering, so they cannot arrive in validated MIR. The C# proof backend continues to consume the unchanged MIR shape and its existing `.g.cs` corpus remains unchanged.

## Validation and diagnostics

The JavaScript validator admits equality only when the result is `boolean`, operand types match, and the operand type is exactly `boolean`, `number`, or `string`. It emits `COPE-JS-0001` and no artifact for arrays, payload-enum names, synthetic Result-family inputs, object/class-family inputs, closure/function-family inputs, and unknown future type names. Existing fallible function/call diagnostics remain the backend boundary for the currently representable fallibility form.

Emission retains MIR operand order and produces one JavaScript expression per operand. JavaScript evaluates binary operands left-to-right, so `left() == right()` emits `(left() === right())`; neither call is duplicated.

## Evidence

- Corpus: `primitive-equality.ts` proves Boolean, ordinary number, NaN, and signed-zero forms; `string-equality.ts` proves strings and quote/backslash literal escaping. Exact byte comparisons, LF output, and repeated-emission equality cover every JavaScript corpus artifact.
- Hash: `primitive-equality.g.js` SHA-256 is `AD297686E173C5A30FD9D6CFA030F90DC048D604CFB7808063DED441EC74B5FC`.
- Real engine: Node.js v26.2.0 executes the Boolean, ordinary-number, NaN, signed-zero, and string proof twice. Both runs exit `0`, produce `true, true, false, true, true, false, true, true, false, true, true` line-by-line, and write no stderr. This is Node evidence only, not a browser compatibility claim.
- CLI: the built CLI emits strict JavaScript with `--emit javascript`; a generated NaN inequality program executes in Node and prints `true`.

The process harness uses argument-list handling, closed stdin, concurrent stdout/stderr drains, a short timeout, process-tree termination, and unique temporary directories. It creates no tracked runtime artifact.

## Deliberate exclusions

Payload-enum structural equality, array equality, Result equality, object/class identity, closure/function values, loose JavaScript equality, and unknown future equality families remain unsupported. String support is deliberately limited to deterministic literal emission and primitive equality; this milestone does not add interpolation, concatenation, indexing, normalization, locale behavior, or a string library.

The project graph remains `Copeland.TS.Backend.JavaScript -> Copeland.TS.Mir`; the backend remains frontend-free and BCL-only.

## Next milestone

Recommend the smallest materially useful next family: payload-enum representation plus exhaustive-match emission, without equality. That makes tagged-data programs executable while leaving structural equality, arrays, and Result semantics independently specified.
