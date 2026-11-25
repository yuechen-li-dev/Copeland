# Layouts, streams, and bindings

Layouts model spatial relations; they are not a CSS or Flexbox compatibility
layer. The layout binder normalizes boxes, layers, origins, relative
derivations, streams, component hosts, and provenance before CSS/browser
realization or projected-table inspection consumes them.

A minimal tested stream shape is:

```ts
stream Page<0px, 0px> {
    width: 160px;
    height: 40px;
    content: Badge() { height: fill; }
}
```

Row, column, grid, overlay, layers/z-order, typed layout contracts,
component-to-slot bindings, CSV-shaped authoring, relative derivations,
overflow, and declared text regions are bounded features. The full accepted
authoring surface and examples live in [Machina layout data](../machina-layout.md).

Use `tscl layout inspect` and `layout::*` projected relations to inspect a
normalized layout. `layout::Derivations` owns derivation provenance; a rendered
rectangle is only a realization/projection. Browser CSS and host selectors must
derive from these compiler facts rather than recomputing layout identity.

Text fitting does not change the layout frame. Compiler text-region metadata
declares the allowed box and policy; the browser realization measures and
selects presentation within that boundary. See [text documents](text-documents.md).

## Stateless View adapters in a workspace

A Copeland workspace can bind layouts and contribution streams to `View`
values through a stateless JavaScript adapter. In `tsconfig.tsx`, add
`CopelandViews` to `tscl.types` and declare the factory inside `tscl`.

```ts
presentationAdapter: {
    identity: "docs-static",
    elementModule: "docs-view",
    elementExport: "create",
},
```

Declare and materialize `docs-view` in `manifest.tsx`. The factory takes
`(tag, props, children)` with children as an array and HTML spellings for
`class` and `for`. The adapter owns the returned value and its serialization.
A Node renderer imports the emitted ES module for functions returning
`View`. The `--node-interop` facade can still expose data exports from the
same project, including native `Document` values.

`tscl check --standalone` and `tscl build --standalone` use the local package.
A TSPack-managed target also declares the factory export in its existing
`npmContracts`. Its resolved descriptor supplies the package binding, and
Copeland reads the adapter from `tsconfig.tsx`. Workspace adapters use the
stateless presentation contract. Component-local `state` and `on` handlers
require an adapter with a qualified commit protocol.
