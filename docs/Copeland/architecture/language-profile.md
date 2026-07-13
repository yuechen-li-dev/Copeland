# Copeland M1 language profile (historical)

> Historical M1 record. The current authoritative language contract is the [Copeland TS language profile](../language/copeland-ts-language-profile.md). This document preserves the earlier Browser TypeScript-to-CLR framing and must not be read as a claim about current implementation or JavaScript-backend semantics.

Copeland M1 is a Browser TypeScript-to-CLR source profile for a compiler pipeline, not a JavaScript runtime.

## Supported surface

- Primitive types: `number`, `string`, `boolean`, `void`
- Arrays: `T[]`
- Typed variable declarations: `let` / `const` with explicit annotations
- Functions with typed parameters and typed return types
- Fallible function signatures: `function f(): T ! ErrorType`
- Fallible propagation: `expr?`
- `if` expressions
- Nominal tagged enums with optional payloads
- Exhaustive `match` over enum/domain variants

## Branching model

Copeland M1 uses three distinct branching forms:

- `?` — fallible propagation
- `if` — boolean expression branching
- `match` — enum/domain branching

```ts
function choose(flag: boolean): number {
  return if flag {
    1
  } else {
    2
  };
}
```

```ts
enum Status {
  Idle,
  Loaded(name: string),
}

function label(status: Status): string {
  return match status {
    Idle => "idle",
    Loaded(name) => name,
  };
}
```

```ts
function caller(text: string): number ! ParseError {
  const x: number = parseNumber(text)?;
  return x;
}
```

## Banned or deferred in M1

- `null`
- `undefined`
- `any`
- `var`
- `eval`
- implicit globals
- ternary `?:`
- optional chaining `?.`
- JavaScript truthy/falsy conditions
- JavaScript object/prototype semantics
- object literals/member access (deferred)
- imports/modules (deferred)
- classes/interfaces/generics/unions (deferred)
- TSX/DOM (deferred)
