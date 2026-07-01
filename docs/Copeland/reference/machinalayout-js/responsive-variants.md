# Responsive variants (M4b)

Responsive variants let a single flat `LayoutRow[]` express row-local overrides selected by `rootRect` dimensions.

## Purpose

- Keep responsive behavior explicit and deterministic.
- Keep selection resolver-owned and driven only by caller-provided root geometry.
- Avoid userland row-array branching for common desktop/tablet/mobile style differences.

## Authoring model

Add `variants` to a `LayoutRow`. Each variant has a `when` condition plus optional override fields.

```ts
{
  id: "sidebar",
  parent: "root",
  frame: { kind: "anchor", left: 0, top: 64, bottom: 0, width: 280 },
  variants: [
    {
      when: { maxWidth: 800 },
      frame: { kind: "anchor", left: 0, right: 0, top: 64, height: 56 },
      slot: "mobileNav",
    },
    {
      when: {},
      slot: "desktopSidebar",
    },
  ],
}
```

## Selection semantics

- `resolveLayoutRows(rows, rootRect)` calls `selectLayoutRowsForRoot(rows, rootRect)` first.
- Conditions are inclusive (`>= min*`, `<= max*`).
- First matching variant wins.
- `when: {}` matches all root rects and can be used as a fallback.
- Effective rows are fresh copies and do not include `variants`.

## Phase separation

- `compileLayoutRows(rows)` directly uses base row values only.
- Variants are an authoring-time input feature, not part of compiled or resolved node types.

## Constraints and non-goals

M4b variants can override row metadata (`frame`, `arrange`, `offset`, `z`, `view`, `slot`, `debugLabel`) but cannot:

- change graph identity (`id`, `parent`, `order`),
- add/remove rows,
- use DOM measurement,
- use CSS media query semantics.
