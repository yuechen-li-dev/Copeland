# CTS-LAYOUT-TYPES-M0 review

## Decision

M0 uses `layout type Name { ... }` and `layout Name<origin> satisfies Contract
{ ... }`. A layout type is a first-class, compile-time-only module symbol. It
is neither an ordinary object type nor a runtime interface.

## Semantic law

The compiler binds layout-type syntax into `BoundLayoutTypeDeclaration` and
`BoundLayoutTypeNode`. Concrete layouts bind into their existing immutable
`BoundLayoutDeclaration`, from which `InferredLayoutShape` is produced.
Conformance compares those bound graphs after binding and before projection;
the emitter never reparses layout syntax.

The law is closed named topology: child names, node kinds, and nesting must
match exactly. Grid `columns` is checked only when a contract declares it; it
is geometric track data, not an inference of the number of children. A grid
contract with no children describes an empty named child set, never four items.

## Boundaries

`type` is a finite data constraint. `interface` is a runtime behavior
constraint. `layout type` is a finite spatial constraint. `layout` is concrete
immutable spatial data. No layout type can be called, rendered, used as a
runtime value, or treated as an interface.

## Module and generated API behavior

Layout types share normal export/import and alias handling. `LayoutTypeSymbol`
uses module-qualified identity and has no special import mechanism. A
satisfying layout retains its authored semantic slots; React projection emits
the same named accessor surface, so contract members are present without any
positional public API.

## Diagnostics and tooling

Diagnostics report missing, unexpected, duplicate, wrong-kind, nested, and
grid-track mismatches with layout/contract names and paths. The language server
recognizes layout type document symbols, contract hover, `satisfies` completion
and semantic token classification, definition navigation, and normal unsaved
overlay diagnostics.

## Fixtures

`samples/copeland-ts/machina-layout-types-m0` contains exact row, missing,
unexpected, wrong-kind, nested, grid-track, imported-contract, and React
accessor fixtures. The focused compiler tests additionally exercise exactness,
inferred shape, aliases, and grid child/cardinality separation.

## Deferred features

Open/variadic regions, generic/conditional/recursive layout types, type-level
functions, runtime reflection, component-to-slot binding, intrinsic component
measurement, responsive behavior, and website migration are intentionally out
of scope.

## Next semantic question

Before component-to-slot binding, decide how a component parameter names a
layout contract and which contract slots are legal attachment points, without
making components responsible for intrinsic geometry.
