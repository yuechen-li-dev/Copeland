# CTS-STREAM-COMPOSITION-M0

`stream` is the ergonomic static composition surface for Machina layout. It
combines named regions, their geometry, and their renderable content while
lowering through the existing layout type, layout, binding, normalized graph,
and React host pipeline.

> `stream` automates layout, slot, and binding declarations when those relationships are uniquely determined by one authored structural composition.

> The compiler should generate repetitive proof obligations whose correct answer is already known; authors should spend their attention on the layout and content decisions that are not inferable.

## M0 grammar and laws

```ts
stream Page<0px, 0px> {
    width: 800px;
    height: 600px;
    header: Header() { height: 64px; }
    content: Content() { height: fill; }
    footer: Footer() { height: 48px; }
}
```

A flat body has one implicit `column root`. Each plain `name: expression`
entry is an exact, required, singular `slot name`; its expression is its exact
binding. `row`, `column`, `grid`, `anchor`, and `overlay` are explicit
structural containers. In M0 containers cannot directly carry content: put a
named singular region inside them. This prevents a container from becoming an
ambiguous host/content hybrid.

```ts
stream Page<0px, 0px> satisfies AppShell {
    width: 1200px;
    height: 800px;
    row root {
        column sidebar { width: 256px; height: fill; navigation: Navigation() { height: fill; } }
        column main { width: fill; height: fill; content: Content() { height: fill; } }
    }
}
```

An explicit `root` structural node replaces the implicit column root. A stream
without `satisfies` receives an internal exact `<StreamName>Shape` contract;
with `satisfies`, the inferred topology is checked using normal layout-type
exactness diagnostics.

## Lowering and React

The binder constructs bound layout nodes and binding entries directly; it does
not generate source text. It then validates the normal concrete layout and
binding representations, normalizes their box identities, and emits
`<StreamName>Stream(): ReactNode`. React lowering creates neutral `div` hosts
with generated layout classes and inserts each bound expression as a child.
Components are not inspected and do not need to forward `className`.

`stream` does not infer semantic HTML tags, component roots, responsiveness,
intrinsic dimensions, state ownership, or dynamic collection cardinality.
Dynamic collection syntax is deliberately deferred. Explicit `layout type`,
`layout`, and `bind` remain the reusable and advanced forms.

## Bounded grid collections

M0 also accepts a finite literal as direct content of a named `grid` region:

```ts
grid features: [BridgeCard(), ReactCard(), TemplateCard()] {
    columns: 4;
    gap: 16px;
}
```

This binds one `BoundStreamCollection` attached to `features`, with ordered
renderable items. It does not create public item slots or names. `columns` is
grid geometry; literal length is bounded content cardinality, and they are
independent. Non-literal/dynamic collection operations are outside M0.

> Names express semantic identity. Collection order expresses bounded position. The compiler must not fabricate semantic names from item indices.

The model follows the SDSL/SDSL-V idea of named structural channels: a region
is a compiler-known channel, a renderable is the contribution flowing through
it, and the stream declaration states both without manually threading the
intermediate relations.
