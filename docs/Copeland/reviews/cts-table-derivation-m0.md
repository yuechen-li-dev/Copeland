# CTS-TABLE-DERIVATION-M0 — immutable row derivation

Relative positioning in Copeland is derivation, not solving.

M0 introduces the compiler-internal `BoundRowDerivation` model: a compiler-known intrinsic derives selected fields of one immutable row from typed fields of another row. A derivation records its target relation and row, source row, transform, read/write sets, authored order, status, and source provenance. The only public M0 profile is `layout::Boxes`.

`with` derives immutable rows. Layout alignment is one typed application of that table operation. The source box is never changed and React receives only the resolved normalized box data; no JavaScript geometry evaluator or component measurement is emitted.

## Layout surface

Use a closed transform call after an overlay box body:

```ts
dialog: Dialog() { width: 480px; height: 320px; } with centerIn(root);
tooltip: Tooltip() { width: 180px; height: 48px; }
  with placeAbove(dialog, 8px)
  with alignRight(dialog);
halo: Halo() { } with expandFrom(dialog, 16px);
```

M0 supports `centerIn`, `centerXIn`, `centerYIn`, edge alignment, adjacency, `insetFrom`, and `expandFrom`. Their field contracts are fixed and compiler-owned. For example, `centerIn` reads source `x/y/width/height` and target `width/height`, and writes target `x/y`; `expandFrom` reads the source frame and padding, then writes the entire target frame.

Every derived field has exactly one writer. Direct values, flow ownership, and relative transforms cannot compete. Derivations form a directed source-to-target graph, are evaluated in deterministic topological order, and reject direct and indirect cycles. Copeland automates coordinate arithmetic whose answer is uniquely determined; it rejects competing equations rather than guessing.

Inputs must be compiler/host-resolvable fixed geometry. Intrinsic component measurement is unavailable. A transform cannot mix `px` and `ui`, and runtime or unitless gap/padding values are rejected. `layout::Derivations` exposes the normalized plan beside `layout::Boxes`; `layout inspect` reads the same projected relations.

General public table transforms are intentionally deferred. M0 does not add formulas, priorities, inequalities, arbitrary transforms, or a 2D constraint solver.

## Closure: CSV and browser proof

The CSV form is now part of M0 and is equivalent to chained `with`:

```ts
csv overlay root {
    name, content, width, height, derivations;
    page, Page(), 1280px, 720px, [];
    dialog, Dialog(), 480px, 320px, [centerIn(root)];
    tooltip, Tooltip(), 180px, 48px, [placeAbove(dialog, 8px), alignRight(dialog)];
    halo, Halo(), derived, derived, [expandFrom(dialog, 16px)];
}
```

`derivations` is an ordinary parsed array expression, but its elements must be compiler-known layout intrinsics with one source box and, where required, a static typed gap or padding. Strings, arbitrary calls, runtime values, and formulas are rejected. `derived` is a CSV dimension-cell marker that contributes no direct writer, allowing `expandFrom` or `insetFrom` to derive the full frame. Empty lists and omitted columns mean no derivations.

Nested and CSV forms both produce the same `BoundRelativeDerivationSpec`, then the same `BoundRowDerivation` and `layout::Derivations` rows. The focused equivalence test compares resolved `layout::Boxes` geometry and transform contracts across both surfaces.

The TSPack fixture at `samples/copeland-ts/machina-table-derivation-m0/10-browser-proof` builds the real React host and runs a Playwright rectangle proof. It finds semantic hosts and uses `getBoundingClientRect()` with `0.01px` tolerance. The observed px geometry is root `(0,0)-(640,400)`, dialog `(200,140)-(440,260)`, tooltip `(300,88)-(440,128)`, and backdrop `(180,120)-(460,280)`. It proves both dialog-center axes, `tooltip.bottom + 12px == dialog.top`, right-edge equality, and all four `20px` backdrop expansion edges; console, page, and request diagnostics must remain empty.

Existing same-unit `ui` derivations remain symbolic normalized lengths and retain `ui` identity; `px`/`ui` mixing is rejected. Responsive-root generalization, cross-root derivation, implicit conversion, and intrinsic measurement remain deferred rather than being inferred by M0.
