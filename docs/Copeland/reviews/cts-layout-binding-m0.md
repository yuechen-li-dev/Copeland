# CTS-LAYOUT-BINDING-M0 review

Status: meaningful progression. Compiler/backend lowering is complete; the
canonical TSPack + Playwright browser fixture remains outstanding.

The compiler now recognizes `bind Layout { slot: Component(); }`, resolves the target as a concrete immutable layout that satisfies a layout type, and retains a `BoundLayoutBinding` with `BoundLayoutBindingEntry` values. Every entry refers to a `LayoutSlotSymbol`, preserving its authored semantic path and declaration source.

Current enforced laws:

- only `slot` nodes are bindable; rows, columns, grids, anchors, and overlays are structural;
- each ordinary slot is required and singular;
- missing, unknown, duplicate, structural-node, non-concrete-target, non-contract, and non-renderable values diagnose;
- grid `columns` remains track geometry. Fixed grid children are normal named slots, not a positional cardinality API;
- a binding expression must already type-check as `ReactNode`; it neither accepts nor computes geometry.

React lowering consumes every valid `BoundLayoutBinding`. The binder produces a
`BoundLayoutReactRealization` from the already-bound concrete layout and its
generated class map. MIR lowers it to a compiler-generated
`<LayoutName>Binding(): ReactNode` function. That function recursively emits
one intrinsic `div` host for every declared projected layout node, attaches the
generated class to that host, and places the bound React expression inside the
corresponding `slot` host.

This is an explicit layout-box realization law, not a compatibility wrapper:
components are never inspected, mutated, or required to forward `className`.
Ordinary and third-party React expressions are normal children of their typed
region host. M0 intentionally emits neutral hosts only; no semantic-host
annotation is inferred from a slot or component name.
