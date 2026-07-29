# CTS-LAYOUT-DATA-M0 review

M0 introduced the parser-level declaration; CTS-LAYOUT-ORIGIN-M0 now requires
the anchored form `layout [profile] Name<x, y> { ... }` for every concrete or
composition-only layout.
Layouts are now normal module declarations: the ordinary binder predeclares
`LayoutSymbol` values, binds `LayoutSlotSymbol` semantic paths, and carries the
fully bound immutable graph in `BoundProgram.Layouts`. The project module graph
resolves exported/imported layout aliases before composition binding.
The binder produces immutable `BoundLayoutDeclaration` and `BoundLayoutNode`
records, then deterministic `NormalizedLayoutGraph` identities. It validates
duplicate slots, grid columns, dimensions, unsupported profiles, unresolved
composition, and composition cycles with `COPE-LAYOUT-*` diagnostics.

The canonical invariant fixtures are in
`samples/copeland-ts/machina-layout-data-m0`: direct dimensions, named slots,
row/column, grid, overlay geometry, immutable composition, and named React
projection. They do not use the legacy factory-call API.

Deliberate M0 boundaries: no responsive algebra, DOM renderer, visual designer,
runtime mutation, methods, layout templates, table profile implementation, or
website-sample migration. Grid is a bounded structural node with a static
column count; richer CSS grid policy remains M1 work.

Recommended M1 is to integrate the normalized layout graph with the normal
project compiler and generated Copeland React surface, extend layout-aware LSP
semantic categories, and settle responsive/profile rules before reconsidering
the current website sample.
