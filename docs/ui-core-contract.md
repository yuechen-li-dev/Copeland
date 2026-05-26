# Machina.Core UI Declaration Contract (M0c)

Machina.Core is the declaration layer above Machina.Layout. It provides readable C# data objects for UI authoring, then lowers those declarations to the flat `LayoutRow[]` model consumed by Machina.Layout.

## Layer boundary

Machina.Core M0c depends on Machina.Layout only. It does not depend on Copeland, Dominatus, DOM, CSS, renderer adapters, browser engines, or platform UI frameworks.

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

Supported declarations are:

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

M0b replaces M0a placeholder text and button sizing with a deterministic measurement seam:

- `UiLoweringOptions` can provide an `ITextMeasurer`;
- if no measurer is supplied, `DeterministicTextMeasurer` is used;
- text lowers to the measured intrinsic size when arranged in a stack;
- direct wrapper text children use anchor frames with the measured intrinsic width and height;
- buttons measure their label text and add deterministic padding before applying minimum dimensions;
- `HSpace(width)` lowers to `FixedFrame(width, 0)`;
- `VSpace(height)` lowers to `FixedFrame(0, height)`;
- stack roots carry `StackArrange`; stack children are emitted as direct flat child rows;
- direct wrapper children use anchor frames so they can compile through the layout document model.

Container alignment values are declaration data in Machina.Core M0c. True alignment behavior is deferred to a later arranger/lowering milestone.

## Style model

Styles are immutable C# records. Updates use `with` expressions. There is no CSS cascade and no ambient inheritance in Machina.Core M0c.

Machina.Core includes:

- `ColorToken`
- `TextSize`
- `TextStyle`
- `UiStyle`
- `Theme` placeholder data

Style metadata is emitted separately from layout rows so future renderers can consume style data without changing the layout contract.

## Semantics and actions

Semantics are data for future render, accessibility, and runtime adapters. Machina.Core M0c emits text semantics for `Text` and button semantics for `Button`.

Actions are metadata/intents, not executable callbacks. `UiAction.Named("save")` records the intent name. Actual dispatch, event handling, runtime integration, and Dominatus coordination are intentionally outside Machina.Core M0c.

Disabled buttons are marked disabled and non-focusable in semantics. Their action metadata is omitted.

## M0b implemented scope

M0b adds deterministic text measurement and lowering snapshots.

The default text measurer is fake and deterministic. It is not real font measurement and exists to make layout lowering stable and testable. The default rules are intentionally simple:

- `TextSize.Sm`: character width `7`, height `16`;
- `TextSize.Md`: character width `8`, height `20`;
- `TextSize.H1`: character width `14`, height `36`;
- width is `text.Length * characterWidth`;
- empty text has width `0`.

Button sizing uses the same deterministic text measurer with default medium text. It adds `24` horizontal padding and `12` vertical padding, then applies a minimum size of `80` by `32`.

Measurement is a seam, not a renderer. Future renderer or platform adapters can provide real platform-aware measurement later without changing the declaration model.

`UiLoweringSnapshotWriter` provides a stable text artifact for rows, styles, text styles, semantics, and actions. Snapshots are diagnostics and test artifacts. They are not renderer output, and they contain no DOM, CSS, platform font metrics, absolute paths, or object hash codes.

## M0c implemented scope

M0c polishes the C# authoring surface. The preferred style uses normal C# method calls, named arguments, collection expressions, immutable records, and explicit ids when readability matters.

Tree-shaped declarations remain an authoring convenience. Lowering still emits deterministic flat `LayoutRow[]` values for Machina.Layout.

```csharp
var content = UI.Rect(
    id: "paused-panel",
    height: 400,
    color: ColorToken.Hex(0x101820DD),
    padding: 20,
    child: UI.Column(
        id: "paused-content",
        gap: 12,
        children:
        [
            UI.Text(
                "Paused",
                id: "title",
                color: ColorToken.White,
                size: TextSize.H1),

            UI.VSpace(100, id: "title-gap"),

            UI.Text(
                "Count: 3",
                id: "count",
                color: ColorToken.Gray,
                size: TextSize.Sm),

            UI.Row(
                id: "buttons",
                gap: 8,
                children:
                [
                    UI.Button(
                        "Resume",
                        id: "resume",
                        action: UiAction.Named("resume"),
                        color: ColorToken.Gold),

                    UI.HSpace(50, id: "button-gap"),

                    UI.Button(
                        "Increment",
                        id: "increment",
                        action: UiAction.Named("increment"),
                        color: ColorToken.White),
                ]),
        ]));

var root = UI.Container(
    id: "root",
    alignX: Align.Center,
    alignY: Align.Center,
    child: content);

var lowered = UiLowerer.Lower(root);
var layout = LayoutCompiler.CompileLayoutRows(lowered.Rows);
```

Explicit ids are optional, but they are recommended for readable diagnostics and stable snapshots. Missing ids are still generated deterministically as `ui_0`, `ui_1`, `ui_2`, and so on in traversal order.

Style shortcuts merge into immutable style records before lowering:

- `UI.Text(..., style: baseTextStyle, color: ..., size: ...)` starts from `baseTextStyle`; `color` and `size` override the corresponding text style fields.
- `UI.Rect(..., style: baseUiStyle, color: ..., padding: ...)` starts from `baseUiStyle`; `color` overrides `Background`, and `padding` overrides `Padding` only when provided.
- `UI.Button(..., style: baseUiStyle, color: ...)` starts from `baseUiStyle`; `color` overrides `Foreground`.

M0c is still declaration and lowering only. It does not add a renderer, drawing, real input dispatch, focus behavior, Dominatus integration, Copeland integration, DOM, CSS, platform adapters, Standard components, or HMI components.

## Non-goals for M0c

Machina.Core M0c does not implement:

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

## M1b rectangular border metadata

`UiStyle` now includes declarative rectangular border metadata: `BorderColor` and `BorderThickness`.

`UI.Rect` supports border style shortcuts through `borderColor` and `borderThickness` arguments when authoring nodes.

This metadata remains declarative in Core; whether and how a backend renders borders is renderer-dependent.

Current border semantics are intentionally limited to plain rectangular strokes. Border radius, per-side borders, dashed/dotted lines, and other advanced border models are not part of M1b.

## M2a authoring reset

M2a adds placement-first primitives so major panel placement is explicit and readable:

- `UI.Surface` for root independent-position composition
- `UI.Layer` for non-root independent-position composition
- `UI.At` lowering to `AbsoluteFrame`
- `UI.Anchor` lowering to `AnchorFrame`

`UI.Row`/`UI.Column` remain supported and are now documented as flow primitives, not primary screen-placement primitives.
\n\n## M3a flat authoring note\nRow-first UiDocument/UiRow authoring is canonical for top-level screens; nested UiNode trees remain optional sugar.
