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
