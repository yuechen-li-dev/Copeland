# Machina Hybrid Authoring Bake-off (M4a)

## A. Tree-only model
Pros: strong component locality and quick prototyping.
Cons: top-level placement drifts into layout soup with hidden geometry.

## B. Row-only model
Pros: explicit inspectable table layout and deterministic placement.
Cons: component internals get scattered; checkbox/switch decomposition leaks low-level rows into app layout.

## C. Hybrid model (row-hosted components)
Pros: top-level layout remains explicit via flat rows; component internals regain locality via nested `UiNode` + `StandardUI`; row host is a clear boundary.
Cons/risks: lowering adds scoped-id complexity and hit-test ids are generated/scoped.

## M4a evidence
- `UiRow` can host `Component` and row helpers accept `component:`.
- `UiDocumentLowerer` emits host row unchanged and lowers component rows under scoped ids (`host/child`).
- Presenter sample now places only root + card host at top level, and defines card internals in one local component function.
- Pipeline hit-testing validates increment, checkbox, and switch actions through hosted component rows.

## Conclusion
Based on M4a sample and tests, hybrid appears preferred for app authoring: it keeps explicit screen placement while restoring component-local structure. Row-only and tree-only remain valid for special cases.

## M4b note (2026-05-26)
Reference audit aligns this document with imported MachinaLayout.JS frame/stack semantics in \.
\n## M4c layout-padding hardening note\n\nM4c clarifies that style padding is paint metadata only. Components that host child layout (for example Card, Input text content) must create an explicit inset content region with placement rows (AnchorFrame), rather than relying on  to move children. Stack behavior remains ordered arithmetic () and is not Flexbox.\n

## M4c layout-padding hardening note

M4c clarifies that style padding is paint metadata only. Components that host child layout (for example Card, Input text content) must create an explicit inset content region with placement rows (`AnchorFrame`), rather than relying on `UiStyle.Padding` to move children. Stack behavior remains ordered arithmetic (`StackArrange`) and is not Flexbox.

## M5d contract cleanup note
Hybrid authoring remains preferred for app screens: flat top-level placement + hosted `StandardUI` components. Manual row decomposition of checkbox/switch internals is now considered advanced customization only and is no longer default guidance. See `docs/standard-ui-vs-standard-view-m5d.md`.
