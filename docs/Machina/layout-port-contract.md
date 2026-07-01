# Machina UI C# Layout Port Contract (M0a)

## Product thesis

Machina UI C# starts as a deterministic managed layout core. It is not a DOM/CSS clone and it is not a React/WPF clone. M0a establishes project structure and architectural contract only.

## Core pipeline

```text
LayoutRow[]
  -> CompileLayoutRows
  -> LayoutDocument
  -> ResolveLayoutDocument
  -> ResolvedLayoutDocument
  -> optional ToResolvedTree
  -> renderer adapter
```

## Canonical model rules

- Flat rows are the canonical authoring input.
- Indexed document structures are the compiled representation.
- Flat resolved documents are the canonical geometry output.
- Tree output is derived for rendering/debugging convenience only.
- Normal C# records describe layout data.
- Renderer adapters consume resolved geometry.

## Foundational frames

Planned records:

- `RootFrame`
- `AbsoluteFrame`
- `AnchorFrame`

Constraints and validity rules:

- `AbsoluteFrame` represents explicit parent-local `x/y/width/height`.
- `AnchorFrame` requires exactly two horizontal constraints selected from `left`, `right`, `width`.
- `AnchorFrame` requires exactly two vertical constraints selected from `top`, `bottom`, `height`.
- Invalid constraint combinations fail.
- Negative resolved sizes fail.

## Row graph model

Planned `LayoutRow` fields:

- `Id`
- `Parent`
- `Order`
- `Frame`
- `Arrange`
- `Slot`
- `DebugLabel`
- `Z`
- `Layer`
- `Offset`

Graph and ordering rules:

- Parent-child defines coordinate-space containment only.
- Parent-child does not imply component ownership, state ownership, event ownership, routing, styling inheritance, or DOM hierarchy.
- Sibling ordering is deterministic: by `Order`, then source row index.

## Style model

- Style records are immutable.
- Style modifications use C# `with` expressions.
- There is no CSS cascade.
- There is no ambient inheritance unless explicitly modeled later.

Example:

```csharp
var danger = baseButton with
{
    Background = Colors.Red,
    Foreground = Colors.White,
};
```

## Future behavior model

- Mount/patch/unmount is not the primary model.
- UI declarations are produced by state/control frames.
- Planned Dominatus-style integration contract:
  - push declaration frame
  - actuate requests
  - pop declaration frame
- The layout core remains independent of Dominatus.

## Non-goals for M0

- No renderer.
- No DOM.
- No CSS.
- No event system.
- No state framework.
- No Copeland dependency.
- No Dominatus dependency.
- No WPF/MAUI/Avalonia binding.
- No WebView2/Chrome compatibility actuator yet.

## M0b implemented scope

M0b implements `Rect`, `UiLength`, `RootFrame`, `AbsoluteFrame`, `AnchorFrame`, and direct frame resolution for absolute/anchor frames.
Rows/documents/stack/grid/rendering remain out of scope.

## M0c implemented scope

M0c adds `LayoutRow`, `LayoutNode`, `LayoutDocument`, and `CompileLayoutRows`.

The compiler validates root count, ids, duplicate ids, unknown parents, cycles, RootFrame placement, and deterministic child ordering by `Order` then source index.

It does not resolve document rectangles yet.

## M0d implemented scope

M0d adds `ResolvedLayoutNode`, `ResolvedLayoutDocument`, and `ResolveLayoutDocument`.

The resolver uses caller-provided root geometry for the root node, resolves children in parent coordinate space through `FrameResolver`, preserves metadata and children order, and emits flat resolved geometry.

Stack/grid arrangement, derived tree output, styles, and renderer adapters remain out of scope.

## M0e implemented scope

M0e adds `ResolvedLayoutTree`, `ToResolvedTree`, and flattening helpers.

The tree is a derived projection from `ResolvedLayoutDocument` for renderer/debug convenience. Flat rows remain canonical authoring input, and flat resolved documents remain canonical geometry output.

## M1a implemented scope

M1a adds `FixedFrame`, `FillFrame`, and `StackArrange`.

Stack arrangement is ordered arithmetic over direct children, not Flexbox. Direct stack children must use `FixedFrame` or `FillFrame`. `AbsoluteFrame` and `AnchorFrame` remain direct frame-resolution primitives for non-arranged parents.

Stack supports axis, gap, padding, justify, align, and weighted fill distribution. It deliberately excludes shrink, wrap, margins, and min/max/basis negotiation.

## M1b implemented scope

M1b adds `CellFrame`, `GridTrack`, and `GridArrange`.

Grid arrangement is explicit deterministic arithmetic over declared columns and rows, not CSS Grid. Direct grid children must use `CellFrame`.

Grid supports fixed/fill tracks, row/column gaps, padding, and row/column spans. It deliberately excludes auto-placement, implicit tracks, minmax, named areas, subgrid, margins, and item alignment.

## M2a note

Machina.Core now exposes placement-first helpers (`UI.At` / `UI.Anchor`) that lower directly to this port's existing `AbsoluteFrame` and `AnchorFrame` frame model.

## M4b note (2026-05-26)
Reference audit aligns this document with imported MachinaLayout.JS frame/stack semantics in \.
