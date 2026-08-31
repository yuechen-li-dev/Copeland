# CTS-TYPE-TEMPLATE-M1 review

## Semantic law

`type` declares a non-nominal compile-time structure. It neither executes nor
constructs runtime objects or CLR types. `record` remains nominal concrete data;
`interface` remains the preferred behavioral contract.

## Delivered slice

The compiler accepts named structural object aliases, optional and readonly
fields, finite unions/intersections, bounded structural projections, and structural assignment from compatible
object literals or records. Templates accept only explicitly marked static value
parameters:

```ts
template ConsoleApp<TModel>(static config: ConsoleConfig): ProjectTree
```

Calls use ordinary parentheses for static values and angle brackets for type
arguments: `ConsoleApp<User>({ name: "Hello", includeTests: true })`.
Fresh object literals reject unknown fields; named structural values use ordinary
assignability. Static values bind to typed bound values before the evaluator.

`reflect fieldsOf<T>()` and `reflect nameOf<T>()` are finite compile-time semantic operations.
`fieldsOf` is declaration ordered and immutable, and dogfood coverage traverses
it through `static for` to produce deterministic artifacts.

## Boundary

The evaluator receives `BoundTemplateStructuralObject`,
`BoundTemplateMemberAccess`, `BoundTemplateInvocation`, and bound metadata
arrays. It has no syntax or type inference path.

## Deliberate limits and follow-up

This first implementation does not yet provide generic structural aliases,
tuple types, result-contract validation beyond `ProjectTree`, named
top-level static constants, or completed LSP semantic-token/definition work.
Those are required before this milestone can be called complete. No conditional
types, `infer`, recursive mapped types, or unrestricted reflection are planned.

## Additional work performed

- The prior M0 parser explicitly rejected template object literals; replaced
  that rejection with normal bound structural values.
- Added deterministic metadata projection from the bound type model.
- Updated console dogfood source to demonstrate a typed static configuration and
  static field projection.
