# CTS-LAYOUT-ORIGIN-M0 review

CTS-LAYOUT-ORIGIN-M0 makes a coordinate-space anchor mandatory for every
layout declaration:

```ts
layout [profile] Name<x, y> { ... }
layout [profile] Name<x, y> = Base with { ... };
```

The central invariant is: **Every layout has an origin because every spatial
box must exist somewhere in a coordinate space.** Missing headers are not
defaulted; they receive `COPE-LAYOUT-ORIGIN-0001` with the explicit source
form to use.

The compiler binds header literals into `BoundLayoutCoordinate` and
`BoundLayoutOrigin`, then carries the origin on `BoundLayoutDeclaration`.
Coordinates are signed `px` or `ui` literals only. `px` is a browser CSS pixel;
`ui` is a distinct Machina logical unit projected as
`calc(var(--machina-ui, 1px) * value)`, so the host controls its CSS scale.
No runtime expression, unitless number, or arbitrary coordinate arithmetic
can supply an origin.

Normalization attaches a non-null `NormalizedLayoutOrigin` to every graph
root. Its local value is declared source geometry; its host-relative value is
deliberately unset until a host/container context resolves it. Flow children
are marked as flow-derived; anchor and overlay children retain their existing
relative positioning laws. This establishes an anchored layout region without
pretending that all world coordinates or runtime component bounds are known.

Composition retains immutable graph composition. A derived declaration's
mandatory header establishes the derived local root origin, replacing the base
root origin while preserving the composed child graph relative to that root.
Cycles retain the existing diagnostics.

React projection rewrites the generated root frame to deterministic absolute
host-relative `left` and `top` CSS. Named semantic slot accessors do not change
and no runtime layout constructor is emitted. Canonical layout-data fixtures
now state origins, including `px`, `ui`, composition, and projection evidence.

This milestone proves layout-region coordinate anchoring. Component bounding
boxes still require component-to-slot binding and intrinsic measurement. The
next semantic question before resuming broader layout-data closure is how a
backend supplies host bounds and `ui` scale for a composed layout without
mistaking unresolved `fill` constraints for resolved component geometry.
