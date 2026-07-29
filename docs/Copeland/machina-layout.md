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

## Layout types (CTS-LAYOUT-TYPES-M0)

> Copeland layout types let authors declare the intended spatial structure of their code so the compiler can catch accidental topology changes.

> Copeland TS is TypeScript because types are author-declared constraints over meaningful program domains—not merely annotations placed on JavaScript values.

`type` describes a finite compile-time data shape. `interface` describes runtime
behavior. `layout type` describes a finite compile-time spatial topology, while
`layout` remains a concrete immutable spatial value. A layout type cannot
execute, render, create a component, mutate, or contain arbitrary computation.

```ts
export layout type DesktopShell {
    row root {
        column sidebar;
        column main;
    }
}

layout DesktopLayout<0px, 0px> satisfies DesktopShell {
    width: 1440px;
    height: 900px;
    row root {
        column sidebar { width: 256px; height: fill; }
        column main { width: fill; height: fill; }
    }
}
```

`satisfies` is a compile-time check over bound semantic graphs, not a runtime
operation and not interface implementation. Every concrete layout has an
inferred shape consisting of named node kinds, nested child topology, and grid
track values. A named layout type is an opt-in closed constraint over that
inferred shape.

Named children are exact by default. At every declared node the concrete child
set must have the same names, kinds, and nesting. Missing children, unexpected
children, duplicate sibling names, wrong kinds, and nested mismatches are
errors. Diagnostics identify both layout and contract plus the semantic path,
for example `DesktopLayout.root.main`.

`grid` tracks and child cardinality are separate. A contract may constrain a
grid's `columns` value. A `grid` with `columns: 4` and no declared children
means exactly zero named children—not “exactly four items”; named children add
closed topology constraints. M0 deliberately has no variadic/open-child syntax,
generic layout types, unions/intersections,
conditional or recursive layout types, mapped type functions, runtime
reflection, or geometry invariants beyond a declared grid track count.

Layout types use ordinary exports, imports, aliases, visibility, hovers,
definition navigation, document symbols, completion, and unsaved-overlay
diagnostics. The layout type itself has no runtime value. A satisfying layout's
existing generated React accessor surface is semantic-name based and therefore
contains every contract node; no positional accessor is canonical.

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

## Typed component-to-slot binding

> Components do things. Layouts constrain space. Bindings state which component occupies which named region.

The binding declaration is deliberately a language declaration rather than an
ordinary object constructor:

```ts
bind Page {
    header: Header();
    content: Content();
    footer: Footer();
}
```

Its target must be a concrete compiler-known layout that has successfully
`satisfies`-checked a known layout type. The binder records a
`BoundLayoutBinding` and one `BoundLayoutBindingEntry` for each attachment. An
entry references the layout's `LayoutSlotSymbol`, not a generated class name or
positional tree path. That preserves authored paths such as
`Page.root.content` when unrelated siblings are reordered.

In the current M0 shape, only an explicit `slot` node is bindable. `row`,
`column`, `grid`, `anchor`, and `overlay` are structural and reject direct
attachments. Every slot is required and singular: it needs exactly one entry;
unknown, duplicate, missing, structural-node, non-concrete-layout, and
non-renderable entries are diagnostics. Optional slots and variable-cardinality
collection regions remain deferred. A fixed-child grid uses ordinary named
slots; `columns: 4` controls tracks and never means four components.

A binding value must already be a normal React-profile `ReactNode` expression,
such as a component invocation or TS-XML element. It may receive state,
dispatch, props, and event handlers through that expression. It may not declare
or mutate coordinates, dimensions, tracks, fill resolution, host bounds, or
intrinsic measurement. A component may depend on a layout contract without
owning or mutating its geometry.

The binding result is a compiler-generated module-local ReactNode factory named
`<LayoutName>Binding`, for example `PageBinding()`. It is an ordinary value in
the React entry path and recursively realizes the already-bound layout graph:

```tsx
createElement("div", { className: PageRootClass },
    createElement("div", { className: PageHeaderClass }, Header()),
    createElement("div", { className: PageContentClass }, Content()),
    createElement("div", { className: PageFooterClass }, Footer()))
```

Every projected layout node, including structural nodes and slots, has one
compiler-generated neutral `div` host. The generated class belongs to that
declared layout box; a bound component/view expression is its child. This is
not a fallback wrapper for a component that could not accept `className`:
Copeland does not inspect component roots, mutate opaque React elements, or
require a class-forwarding contract. Semantic host annotations are deliberately
deferred, so M0 never infers `header`, `main`, or `footer` from authored slot or
component names.

The editor recognizes the `bind` keyword, targets, and slot keys; valid local
binding bodies receive completion for bindable slots only, hover explains the
singular generated-host contract, and definition on a binding key navigates to
the slot declaration. The syntax and semantic binding work in unsaved overlays
through the ordinary compiler path.

This leaves the geometry boundary explicit: layout hosts and the solver resolve
region geometry; intrinsic browser measurements still determine
content-sensitive descendant geometry.
# Stream composition (M0)

`stream` is the concise composition form for a layout whose regions and
renderable content are declared together. A flat stream body is an implicit
`column root`; plain `name: Renderable()` entries are required singular slots
with exact bindings. Explicit `row`, `column`, `grid`, `anchor`, and `overlay`
nodes remain structural. See [CTS stream composition M0](reviews/cts-stream-composition-m0.md).

For finite grid content, `grid features: [A(), B()] { columns: 4; }` attaches
an ordered bounded collection to the single named `features` box. Collection
positions are not synthesized semantic region names.
