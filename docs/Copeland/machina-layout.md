# Copeland Machina layout data (M0)

`layout` declares immutable, finite spatial structure. A layout is a value: it
does not return a view, run code, allocate a component, or invoke a callback.
The compiler binds it directly to a typed layout record/tree, validates that
tree, normalizes stable named identities, and projects it through Machina.

> The function-based HStack/VStack/Fixed/Fill authoring API is legacy experimental infrastructure, not the canonical Copeland TS layout model.

## Authoring

```ts
layout DesktopLayout<0px, 0px> {
    width: 1440px;
    height: 900px;

    row root {
        gap: 18px;

        column sidebar {
            width: 256px;
            height: fill;
        }

        column main {
            width: fill;
            height: fill;
            slot hero { height: 520px; }
            grid features { columns: 4; gap: 16px; height: fill; }
            slot footer { height: 44px; }
        }
    }
}
```

M0 supports `row`, `column`, `grid`, `anchor`, `overlay`, and `slot`. Names
are semantic slot identities, so projection consumers use `root`, `sidebar`,
`hero`, and so on rather than generated tree coordinates. Slot names are unique
within a layout. Reordering siblings does not change an unrelated slot name.

Width, height, gap, padding, and position are direct data values. Dimensions
currently accept a physical or logical length, `fill`, and `fit`; M0 implements
only the existing bounded Machina sizing behavior. `grid` requires a positive
static `columns` value. `anchor` and `overlay` use `x`, `y`, `width`, and
`height` direct geometry where a child is not flow-laid out.

Styles attach as immutable records:

```ts
slot hero {
    height: 520px;
    style: { fill: "#05060d", border: { width: 1px, color: "#273152" } };
}
```

## Coordinate origins

> Every layout has an origin because every spatial box must exist somewhere in a coordinate space.

Every concrete or reusable layout declaration therefore has a required header
origin: `layout [profile] Name<x, y>`. It is declaration geometry, not an
optional body property, and the compiler never infers `<0px, 0px>`. A missing
origin reports `COPE-LAYOUT-ORIGIN-0001` with the authored replacement form.

`px` is a CSS-pixel coordinate in browser projection. `ui` is a typed Machina
logical coordinate; the browser lowering emits it as
`calc(var(--machina-ui, 1px) * value)`. A host may supply `--machina-ui` to
choose its logical-unit scale; without one, one logical unit is one CSS pixel.
The two units remain distinct in `BoundLayoutCoordinate` and
`BoundLayoutOrigin`, and the header accepts only signed `px` or `ui` literals.
It does not accept runtime expressions, unitless values, or coordinate
arithmetic.

The declared origin is local to the layout host. A top-level host can be a
viewport, canvas, document surface, or any backend root; a nested host is its
containing slot or parent layout box. The normalizer retains the declared local
origin and intentionally leaves its host-relative resolved origin absent until
a backend has a host. It does not claim world coordinates merely because a
layout graph is normalized.

Rows and columns derive child origins from their container flow; anchors and
overlays retain their existing relative constraints. Thus child nodes do not
gain headers. Composition is deterministic: every derived declaration states
its own required local origin, which replaces the base declaration's root
origin; the composed graph itself stays relative to that derived root.

Origin plus size and host context can solve layout-region bounding boxes. A
runtime component bounding box still additionally needs component-to-slot
binding and intrinsic measurements such as text or image size.

## Laws and composition

`layout` is constrained record sugar, not a second object system. It has no
methods, mutation, runtime branches, loops, calls, or arbitrary statements.
Every child is finite and compiler-known. Invalid node/property combinations,
bad dimensions, duplicate slots, unsupported profiles, unresolved composition,
and recursive composition are diagnostics rather than runtime failures.

M0 has immutable whole-layout composition:

```ts
layout DesktopHero<20px, 10px> = SharedHero with { width: 960px; };
```

Overrides are a deterministic record patch. Composition never invokes user
code; direct or indirect cycles are rejected. `layout [profile] Name` is parsed
to leave room for future profiles such as `layout table CustomerTable`; M0
accepts the general profile only.

## Projection and tooling

Normalization emits a typed `NormalizedLayoutGraph` with stable identities such
as `DesktopLayout.root.hero`. The existing Machina resolver and
`MachinaBrowserLowerer.LowerForReact` consume the lowered graph. The public
projection is a named slot map:

```tsx
const layout = DesktopLayout;
<aside className={layout.sidebar.className} />
<main className={layout.main.className}>
  <Hero className={layout.hero.className} />
</main>
```

`LayoutDataCompiler.ProjectReact` emits a deterministic typed `as const`
surface with the same names, so the canonical form is
`DesktopLayout.hero.className`; `ClassesBySlot` is retained only as a dynamic
low-level map. The lowerer may retain positional identities internally for
debug provenance, but they are not the author-facing contract. React continues to own semantic DOM,
accessibility, lifecycle, and events; Machina owns geometry and immutable style
projection.

The normal Copeland binder predeclares `LayoutSymbol` and `LayoutSlotSymbol`
instances in ordinary module scope. Export/import aliases resolve through the
project module graph before the layout-specific bound graph phase runs; imported
composition consumes that resolved immutable declaration, not filesystem lookup.

The language server recognizes `layout` in completion and document symbols,
shows declarations, origins, and named slots for hover/definition, and reports
layout diagnostics from unsaved overlay text. It provides the required origin
snippet after a layout name and classifies origin unit literals as layout
coordinates. Document symbols and definition behavior remain unchanged.

## Relationship to templates and products

`layout` is concrete immutable spatial data. `template` remains the future
parameterized structural-generation mechanism and `static` remains bounded
structural selection/traversal; M0 does not turn layouts into parameterized
functions.

MachinaLayout.JS is a conventional library for tsc TypeScript/JavaScript and
therefore uses function/library APIs. Machina UI for Copeland TS is a
language-integrated layout-data model using layout declarations, records,
named slots, templates, and static. They are separate products with no source
compatibility requirement.

The legacy function API remains supported as experimental migration evidence.
It is intentionally untouched in M0, and existing samples can continue to use
it while migration is deferred.
