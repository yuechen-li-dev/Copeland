# Copeland semantic reflection — VIZ-M0

## Language law

`reflect` makes a compiler semantic query visually distinct from both an
ordinary call and ordinary compile-time evaluation:

```ts
foo();                         // runtime computation
static buildTable(256);        // ordinary static-safe Copeland computation
reflect fieldsOf<Model>();     // compiler-owned semantic metadata
```

VIZ-M0 supports exactly `reflect nameOf<T>()`, `reflect fieldsOf<T>()`, and
`reflect enumCasesOf<T>()`. Reflection is legal in template bodies, including
values consumed by `static if`, `static for`, and `static match`. A reflected
expression in a runtime function is rejected with `COPE-REFLECT-0001` and can
never reach MIR.

Unmarked historic calls are a migration error (`COPE-REFLECT-0004`). They are
not a permanent alias: maintained source must add `reflect`.

## Semantic metadata

The compiler returns ordinary immutable values, never syntax or symbol handles.

- `nameOf<T>` returns the semantic type name.
- `fieldsOf<T>` returns declaration-ordered `{ name, typeName, optional,
  readonly }` values for structural types and records.
- `enumCasesOf<T>` returns declaration-ordered `{ name, payloadCount,
  payloadTypes }` values for payload enums.

Concrete targets are bound immediately. A template type parameter retains only
a bounded query kind and parameter ordinal until specialization resolves its
semantic type. No AST node, source text, compiler service, CLR `Type`, or symbol
object is exposed to template code.

## Runtime and NativeAOT

Copeland semantic reflection is a compiler feature. It does not require runtime
reflection metadata and does not weaken trimming or NativeAOT assumptions.
Generated/runtime code has no `System.Reflection`, dynamic metadata lookup, or
compiler callback. Materialization completes in the compiler/tool process.

Future executable queries such as `reflect callsOf<F>()`, `reflect
controlFlowOf<F>()`, and `reflect effectsOf<F>()` may be investigated only as
bounded semantic queries. VIZ-M0 does not implement them and does not expose an
AST escape hatch.

## Syntax and dogfood assessment

The maintained record and enum proofs found `reflect fieldsOf<T>()` clear and
natural. One keyword is enough verbosity to distinguish the operation from both
`fieldsOf<T>()`-shaped runtime code and `static buildTable()` computation, and
the prefix form is straightforward for humans and LLMs to generate. Reflection
removed the duplicate lists of field names, field types, optionality, enum case
names, and payload types; only visualization policy remained authored.

The bounded `recordDiagram`/`enumDiagram` construction adapters are the M0
rough edge: they compensate for the static language not having a general typed
collection transform. VIZ-M1 should not add a graph-specific language. Its
minimum scope should first test one bounded semantic query for executable code,
preferably `reflect callsOf<F>()`, with stable callable identity, source
correlation, legality, limits, and a Diagram adapter. `controlFlowOf` and
`effectsOf` should remain deferred until that single query proves the boundary.
