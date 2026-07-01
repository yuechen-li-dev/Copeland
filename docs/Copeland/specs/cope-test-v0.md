# Cope Test v0 (syntax target)

## Purpose

Cope Test defines the script-surface test dialect for Copeland corpus files. The design target is **xUnit-shaped semantics** with a **TypeScript-like script syntax**, so corpus authors (including LLMs) can write clear tests that future Copeland compiler stages can parse, lower, and execute consistently.

> Milestone scope: syntax and corpus conventions only. No lexer/parser/runtime behavior is implemented by this document.

## Test declarations

### `fact` (v0)

Facts are the only concrete executable test declaration in v0 syntax:

```cope
fact "simple arithmetic" {
  let x = 2;
  let y = 3;
  assert.equal(x + y, 5);
}
```

Rules:
- `fact` uses a required quoted test name.
- `fact` body is a block.
- Statements inside the block are script-native (TS-shaped), not attribute/decorator based.

### `theory` (reserved for later milestone)

`theory` is reserved now to preserve a stable future shape for inline-data tests. Representative target form:

```cope
theory "addition works" [
  [1, 2, 3],
  [5, 7, 12]
] (a, b, expected) {
  assert.equal(a + b, expected, "Theory case failed.");
}
```

v0 does **not** execute or fully validate `theory`; this is a forward-compatibility syntax direction.

## Assertion API surface (v0)

Cope Test v0 reserves this assertion surface:

- `assert.true(condition, message?)`
- `assert.false(condition, message?)`
- `assert.equal(actual, expected, message?)`
- `assert.notEqual(actual, expected, message?)`
- `assert.null(value, message?)`
- `assert.notNull(value, message?)`

### Custom failure message rule (required)

Every assertion API accepts an optional author-provided failure message as the final argument.

Example:

```cope
assert.equal(actual, 4, "Expected arithmetic result to remain stable.");
```

## Corpus conventions (v0)

- `.cope` files are source-first corpus artifacts.
- Future test discovery will come from `fact`/`theory` declarations in source, not reflection.
- Assertions are part of script surface syntax, not host-language metadata.
- This milestone defines syntax/examples only to anchor future lexer/parser/lowering work.

## Explicitly unsupported in v0

- fixtures
- hooks
- async tests
- parameterized execution (theory runner)
- custom matchers
- snapshots
- decorators/attributes
- reflection-based discovery
- arbitrary metadata annotations

## Non-goals for this milestone

This document does not implement lexer, parser, binder, lowering, assertion runtime, or execution engine behavior.
