# Machina UI.Stack Authoring M17b

## Purpose

M17b adds an authoring-level stack surface to `Machina.Core` without changing the low-level layout engine or refactoring product-facing Oblivion renderer code yet.

The goal is to let authors express fixed-and-fill stack composition directly, instead of writing manual cursor arithmetic around `UI.Anchor(...)`.

## Relationship to M17a recon

M17a identified that the main C# gap was not missing stack math.

The repo already had working low-level layout primitives, but the main sample authoring path still leaked manual `top` math and `.slot` wrapper conventions into human-facing code.

M17b is the first implementation step on that ladder.

## Existing low-level StackArrange / FillFrame

The low-level layout engine already contained:

- `StackArrange`
- `FillFrame`
- stack padding support through `EdgeInsets`

M17b reuses those existing primitives.

It does not add a second layout engine and does not duplicate resolver math outside the central layout pipeline.

## Why UI.Stack exists

`UI.Row(...)` and `UI.Column(...)` remain useful for simple same-axis flow, but they infer child frames from the child node type.

That is not enough for authoring cases that need an explicit sequence of:

- fixed main-axis sections
- weighted fill sections
- stack-local gap
- stack-local padding
- deterministic wrapper rows that authors do not have to name by hand

`UI.Stack(...)` provides that richer authoring surface while keeping `UI.Row(...)` and `UI.Column(...)` stable.

## API shape

Primary API:

```csharp
UI.Stack(
    id: "example.stack",
    axis: StackAxis.Vertical,
    gap: 6,
    padding: UiPadding.All(12),
    children:
    [
        UI.StackItem.Fixed(main: 24, child: title),
        UI.StackItem.Fixed(main: 18, child: subtitle),
        UI.StackItem.Fill(weight: 1, child: body),
    ]);
```

Convenience aliases:

```csharp
UI.VStack(
    id: "example.stack",
    gap: 6,
    padding: UiPadding.All(12),
    children:
    [
        UI.StackItem.Fixed(main: 24, child: title),
        UI.StackItem.Fill(weight: 1, child: body),
    ]);

UI.HStack(
    id: "toolbar",
    gap: 8,
    children:
    [
        UI.StackItem.Fixed(main: 120, child: primary),
        UI.StackItem.Fill(weight: 1, child: spacerBody),
    ]);
```

## Stack items

`UI.Stack(...)` accepts explicit `UiStackItem` values.

The author-facing helper surface is `UI.StackItem`.

## Fixed items

Fixed items declare a main-axis size:

```csharp
UI.StackItem.Fixed(main: 24, child: node)
```

For a vertical stack, `main` becomes the item height.

For a horizontal stack, `main` becomes the item width.

In M17b, fixed items derive their wrapper cross-axis size deterministically from the child when possible, with a stable fallback when the child does not expose an intrinsic cross size through the current lowering path.

## Fill items

Fill items declare a remaining-space weight:

```csharp
UI.StackItem.Fill(weight: 1, child: node)
```

They lower to the existing `FillFrame` machinery and participate in weighted remaining-space distribution.

Weighted fill is supported because the low-level `FillFrame` and resolver already support weights.

## Gap and padding

`UI.Stack(...)` supports:

- `gap`
- uniform or explicit `UiPadding`

Example:

```csharp
UI.Stack(
    id: "panel",
    axis: StackAxis.Vertical,
    gap: 6,
    padding: UiPadding.All(12),
    children:
    [
        UI.StackItem.Fixed(main: 24, child: header),
        UI.StackItem.Fill(weight: 1, child: body),
    ]);
```

`UiPadding` lowers to the existing low-level `EdgeInsets` stack padding.

## Wrapper id derivation

M17b hides stack-item wrapper ids from authors for explicit `UI.Stack(...)` items.

The convention is:

```text
<stack-id>.item-<index>
```

Examples:

- `card.layout.item-0`
- `card.layout.item-1`

This is deterministic, stable, and scoped to the stack node id.

Authors do not need to write `.slot` ids manually for `UI.Stack(...)` children.

## Relationship to UI.Row / UI.Column

`UI.Row(...)` and `UI.Column(...)` are preserved.

They still use the existing implicit child-frame lowering path and keep their current behavior.

M17b does not convert existing callers to wrapper rows automatically.

`UI.Stack(...)` is the richer explicit fixed/fill authoring surface.

## Lowering model

Lowering stays inside the existing authoring-to-layout pipeline:

- `UI.Stack(...)` creates a `StackNode` with explicit stack-item metadata
- `UiLowerer` lowers the stack node to the existing `StackArrange`
- explicit fixed/fill items lower to wrapper rows that use existing `FixedFrame` or `FillFrame`
- wrapper children then lower through the normal direct-child frame path
- layout resolution still happens in `Machina.Layout` through the existing resolver

This keeps deterministic ordering, deterministic ids, and one central layout engine.

## Examples

Simple vertical stack:

```csharp
UI.VStack(
    id: "settings.panel",
    gap: 10,
    padding: UiPadding.All(16),
    children:
    [
        UI.StackItem.Fixed(main: 24, child: UI.Text("Settings", id: "title")),
        UI.StackItem.Fixed(main: 18, child: UI.Text("Project local options", id: "subtitle")),
        UI.StackItem.Fill(weight: 1, child: UI.Rect(id: "body")),
    ]);
```

Simple horizontal stack:

```csharp
UI.HStack(
    id: "actions",
    gap: 8,
    children:
    [
        UI.StackItem.Fixed(main: 100, child: UI.Button("Save", id: "save")),
        UI.StackItem.Fixed(main: 100, child: UI.Button("Cancel", id: "cancel")),
        UI.StackItem.Fill(weight: 1, child: UI.Rect(id: "stretch")),
    ]);
```

## What changed

M17b adds:

- `UI.Stack(...)`
- `UI.VStack(...)`
- `UI.HStack(...)`
- `UI.StackItem.Fixed(...)`
- `UI.StackItem.Fill(...)`
- `UiPadding`
- deterministic stack-item wrapper id derivation for explicit stack items
- tests proving deterministic lowering and resolved layout behavior
- deterministic M17b docs and manifest output

## What did not change

M17b does not:

- add a new low-level layout engine
- reimplement stack math outside the existing resolver
- implement `UI.Grid(...)`
- implement `GuideFrame`
- implement row variants
- implement proportional `UiLength`
- implement `DeusMachine`
- refactor `OblivionCardRenderer`
- refactor `OblivionWorkbenchCatalog`
- refactor page layout
- change playback scenarios intentionally
- change product runtime behavior intentionally

There is no Oblivion renderer migration yet.

## Deferred work

Deferred after M17b:

- M17c card-renderer migration onto `UI.Stack(...)`
- card-specific measurement/render cleanup
- M17d authoring-level grid/cell surface
- M17e page-shell grid migration
- cross-node guide/reference placement
- row variants
- proportional `UiLength`
- DeusMachine parity
