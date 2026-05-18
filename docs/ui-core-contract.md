# Machina.Core UI Declaration Contract (M0a)

Machina.Core is the declaration layer above Machina.Layout. It provides readable C# data objects for UI authoring, then lowers those declarations to the flat `LayoutRow[]` model consumed by Machina.Layout.

## Layer boundary

Machina.Core M0a depends on Machina.Layout only. It does not depend on Copeland, Dominatus, DOM, CSS, renderer adapters, browser engines, or platform UI frameworks.

The pipeline for this milestone is:

```text
UiNode tree
  -> UiLowerer.Lower
  -> LayoutRow[]
  -> LayoutCompiler.CompileLayoutRows
```

Flat layout rows remain the canonical layout input. Tree-shaped C# declarations are an authoring convenience for readability, not the conceptual truth for layout.

## Authoring shape

The static `UI` builder surface is intentionally small and Iriza-style:

```csharp
var ui = UI.Rect(
    padding: 16,
    child: UI.Column(
        children:
        [
            UI.Text("Hello"),
            UI.Button("Save", action: UiAction.Named("save")),
        ]));

var lowered = UiLowerer.Lower(ui);
```

Supported M0a declarations are:

- `Text`
- `Rect`
- `Row`
- `Column`
- `Container`
- `Button`
- `HSpace`
- `VSpace`

## Lowering behavior

Lowering emits deterministic flat `LayoutRow` values. If a declaration has an explicit `NodeId`, that id is preserved. Missing ids are generated deterministically as `ui_0`, `ui_1`, `ui_2`, and so on in traversal order. Duplicate explicit ids fail with `DuplicateUiNodeId`; empty or whitespace explicit ids fail with `InvalidUiNodeId`.

M0a uses simple placeholder sizing because it does not implement intrinsic text measurement or adaptive layout measurement:

- text lowers to a fixed placeholder based on `text.Length * 8` by `20` when arranged in a stack;
- buttons lower to `max(80, text.Length * 8 + 24)` by `32` when arranged in a stack;
- `HSpace(width)` lowers to `FixedFrame(width, 0)`;
- `VSpace(height)` lowers to `FixedFrame(0, height)`;
- stack roots carry `StackArrange`; stack children are emitted as direct flat child rows;
- direct wrapper children use anchor frames so they can compile through the layout document model.

Container alignment values are declaration data in M0a. True alignment behavior is deferred to a later arranger/lowering milestone.

## Style model

Styles are immutable C# records. Updates use `with` expressions. There is no CSS cascade and no ambient inheritance in M0a.

M0a includes:

- `ColorToken`
- `TextSize`
- `TextStyle`
- `UiStyle`
- `Theme` placeholder data

Style metadata is emitted separately from layout rows so future renderers can consume style data without changing the layout contract.

## Semantics and actions

Semantics are data for future render, accessibility, and runtime adapters. M0a emits text semantics for `Text` and button semantics for `Button`.

Actions are metadata/intents, not executable callbacks. `UiAction.Named("save")` records the intent name. Actual dispatch, event handling, runtime integration, and Dominatus coordination are intentionally outside M0a.

Disabled buttons are marked disabled and non-focusable in semantics. Their action metadata is omitted.

## Non-goals for M0a

M0a does not implement:

- rendering;
- drawing;
- input or event dispatch;
- focus or keyboard navigation;
- hit testing;
- accessibility tree export;
- animation;
- routing or state framework;
- Dominatus integration;
- Copeland integration;
- DOM, CSS, WebView2, Chrome, or platform adapters;
- Standard/shadcn or HMI component packages.
