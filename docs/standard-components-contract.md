# Machina.Standard component contract

`Machina.Standard` is the first reusable standard component package above `Machina.Core`.
It is a declaration-only layer: every component produces immutable Machina Core UI nodes that lower through the existing deterministic `UiLowerer` pipeline.

## Relationship to Machina.Core

`Machina.Core` remains the renderer-independent UI declaration layer. It owns the readable `UI.*` authoring surface, immutable `UiNode` records, immutable styles, semantic/action metadata, deterministic lowering, and the text-measurement seam.

`Machina.Standard` composes those Core primitives into a small, reusable component vocabulary. Standard components do not introduce a renderer, event dispatcher, DOM model, CSS model, validation engine, focus engine, or Copeland/Dominatus integration. They are ordinary C# declaration helpers that return `UiNode` values.

## shadcn-intent philosophy

Standard uses shadcn/ui as component inventory and concept-art reference only.

The package does **not** port or copy:

- Tailwind classes
- Radix primitives
- React components, hooks, or DOM structure
- CSS selectors or browser behavior
- focus, keyboard, popup, or input-dispatch systems

Instead, Standard re-expresses a small shadcn-shaped vocabulary in Machina-native terms: Core declarations, immutable style records, semantic metadata, action metadata where Core can carry it cleanly, and deterministic lowering.

## M0a supported components

M0a intentionally started small:

- `StandardUI.Button`
- `StandardUI.Card`
- `StandardUI.Badge`
- `StandardUI.Separator`

Buttons are declaration shells with semantic role `Button`. Enabled buttons preserve named action metadata. Disabled buttons keep disabled semantics and omit active action metadata.

Cards, badges, and separators are visual composition helpers over Core nodes and standard theme tokens.

## M0b supported form declarations

M0b adds the first form-oriented declaration vocabulary:

- `StandardUI.Field`
- `StandardUI.Label`
- `StandardUI.Input`
- `StandardUI.Checkbox`
- `StandardUI.Switch`

These are still declaration-only shells. They describe visual structure, stable ids, styles, roles, and optional changed-action metadata. They do not perform real input behavior.

### Field

`StandardUI.Field` is a vertical grouping helper for a label, control, description, and error message. It lowers to a Core column and includes only the optional pieces supplied by the caller.

When the field has an explicit id, generated children use deterministic suffixes:

- `{field-id}.label`
- `{field-id}.description`
- `{field-id}.error`

`Field` does not provide validation, form submission, or label/control binding. It is only a deterministic declaration grouping.

### Label

`StandardUI.Label` lowers to styled Core text with semantic role `Label`. It uses standard foreground color and small text sizing.

There is no `for`/target binding in M0b. Any association between a label and a control is currently represented only by composition and naming conventions.

### Input shell

`StandardUI.Input` is a visual and semantic shell for text-like input. It lowers to a styled Core rectangle containing text.

M0b input behavior is intentionally limited:

- if `value` is non-empty, the shell displays `value`
- otherwise, it displays `placeholder` when present
- placeholder text uses `PlaceholderTextStyle` and disabled text uses `DisabledForeground`
- disabled input uses muted background tokens
- enabled input emits semantic role `Input` and is focusable in metadata
- an enabled input preserves the supplied `changed` action as declaration metadata
- a disabled input omits the supplied `changed` action

There is no real text editing, caret, selection, keyboard handling, focus engine, validation, or event dispatch.

### Checkbox

`StandardUI.Checkbox` is a boolean declaration shell. It renders a small box, an optional deterministic checked marker (`✓`), and an optional label row.

M0b checkbox behavior is intentionally limited:

- `isChecked` changes the visible marker and active styling
- `disabled` changes styling and disabled semantics
- enabled checkboxes emit semantic role `Checkbox` and focusable metadata
- enabled checkboxes preserve supplied `changed` action metadata
- disabled checkboxes omit supplied `changed` action metadata

There is no real toggling, keyboard handling, focus engine, or event dispatch.

### Switch

`StandardUI.Switch` is a boolean declaration shell. It renders a track and thumb with deterministic on/off visual differences, plus an optional label row.

M0b switch behavior is intentionally limited:

- `isOn` changes active/inactive track styling and thumb placement declaration
- `disabled` changes styling and disabled semantics
- enabled switches emit semantic role `Switch` and focusable metadata
- enabled switches preserve supplied `changed` action metadata
- disabled switches omit supplied `changed` action metadata

There is no real toggling, animation, keyboard handling, focus engine, or event dispatch.

## Theme and tokens

`StandardTheme.Default` provides deterministic token bundles:

- `StandardColors` for background, foreground, primary, secondary, destructive, muted, border, and accent colors
- `StandardSpacing` for small spacing and padding values
- `StandardRadius` for future radius-aware renderers

Radius tokens are stored for future component and renderer use. M0a and M0b do not emit radius because the current Core `UiStyle` only models foreground, background, and padding.

Button sizes are accepted as stable declaration inputs and are currently represented as deterministic padding style tokens. Core button intrinsic measurement still owns actual button frame sizing.

Separators lower as deterministic rectangles. A horizontal separator uses a default `100 x thickness` rectangle; a vertical separator uses a default `thickness x 100` rectangle.

## Form card example

```csharp
var ui = StandardUI.Card(
    id: "settings-card",
    child: UI.Column(
        id: "settings-content",
        gap: 12,
        children:
        [
            UI.Text("Settings", id: "title", size: TextSize.H1),

            StandardUI.Field(
                id: "username-field",
                label: "Username",
                control: StandardUI.Input(
                    id: "username",
                    value: "ada",
                    placeholder: "Enter username"),
                description: "This appears in your profile."),

            StandardUI.Checkbox(
                id: "email-updates",
                label: "Email updates",
                isChecked: true,
                changed: UiAction.Named("email-updates.changed")),

            StandardUI.Switch(
                id: "notifications",
                label: "Notifications",
                isOn: false,
                changed: UiAction.Named("notifications.changed")),

            StandardUI.Separator(id: "rule"),

            StandardUI.Button(
                "Save",
                id: "save",
                action: UiAction.Named("save")),
        ]));
```

The example builds a Core declaration tree. Lowering it with `UiLowerer.Lower(ui)` produces deterministic layout rows plus style, text-style, semantic, and action metadata. Compiling the lowered rows with `LayoutCompiler.CompileLayoutRows(...)` validates that the declaration remains inside the Machina.Core and Machina.Layout pipeline.

## Non-goals retained after M0b

M0b does not add:

- real text editing
- real checkbox or switch toggling
- focus management
- keyboard navigation
- validation
- form submission
- renderer adapters
- DOM, CSS, Tailwind, Radix, or React behavior
- animation
- accessibility tree export beyond existing semantics metadata
- dialog, popover, dropdown, select, combobox, command, or data table components
- Copeland, Dominatus, or HMI dependencies

## M1b border styling in standard components

Standard components now use simple rectangular borders where appropriate:

- `StandardUI.Card`
- `StandardUI.Input`
- `StandardUI.Checkbox` box shell
- `StandardUI.Button` outline variant

These borders use standard theme border color tokens and thickness metadata.

This is foundational styling, not a polished final design system guarantee. Borders are currently plain rectangular strokes only.

## M1e visual tuning pass

M1e applies a deterministic visual tuning pass using existing Core style fields only:

- button padding tokens were tuned to better fit current bitmap text proportions
- checkbox box dimensions and label text sizing were tuned for readability
- switch track/thumb sizing and border styling were tuned for clearer on/off states
- card usage in the presenter sample moved to a more intentional panel layout with explicit surface spacing

M1e does not add renderer primitives, rounded corners, shadows, gradients, anti-aliasing, or dynamic interaction states.
\n\n## M3a flat authoring note\nRow-first UiDocument/UiRow authoring is canonical for top-level screens; nested UiNode trees remain optional sugar.

## M3b StandardView flat metadata coverage

`StandardView` is a row-metadata helper surface that returns `UiView` objects for flat row authoring.

Coverage includes:
- `StandardView.Card`
- `StandardView.Button`
- `StandardView.Checkbox`
- `StandardView.Switch`
- `StandardView.Text`
- `StandardView.Label`
- `StandardView.Badge`
- `StandardView.Separator`
- `StandardView.Input`

For field-like UI, prefer explicit rows (label row + input row) instead of a single synthetic `Field` mega-view.
\n\n### M3d text alignment\nTextStyle now includes horizontal (TextAlignX) and vertical (TextAlignY) alignment metadata. Defaults remain Left/Top for backward compatibility. Alignment only changes glyph paint origin inside the resolved text rectangle; layout geometry is unchanged. M3d does not add wrapping, ellipsis, multiline layout, baseline typography, kerning, anti-aliasing, or external font dependencies.

## M3e flat-view composition note

`StandardView` includes optional sub-part view helpers (`CheckboxBox`, `SwitchTrack`, `SwitchThumb`) so field controls can be composed with explicit flat rows while preserving deterministic semantics/action metadata.


## M3f rectangular control skin hardening

`CheckboxBox`, `SwitchTrack`, and `SwitchThumb` remain metadata-only helpers. They now emit stronger rectangular control chrome styling (background/border/fill contrast) so flat row composition reads as real controls without adding new primitives.
\n## M4a hybrid note\nRow-hosted components are now supported: top-level placement stays flat rows, while local component internals use nested UiNode/StandardUI under a host row boundary.
\n## M4c layout-padding hardening note\n\nM4c clarifies that style padding is paint metadata only. Components that host child layout (for example Card, Input text content) must create an explicit inset content region with placement rows (AnchorFrame), rather than relying on  to move children. Stack behavior remains ordered arithmetic () and is not Flexbox.\n

## M4c layout-padding hardening note

M4c clarifies that style padding is paint metadata only. Components that host child layout (for example Card, Input text content) must create an explicit inset content region with placement rows (`AnchorFrame`), rather than relying on `UiStyle.Padding` to move children. Stack behavior remains ordered arithmetic (`StackArrange`) and is not Flexbox.

## M4d component-geometry note
Button/Checkbox/Switch internals are explicit local rows/frames. Geometry and clickable surfaces are validated via headless resolved-rect + hit-test tests; GUI screenshots are confirmation only.
\n- M4e note: presenter sample geometry is now validated with headless resolved-rectangle assertions; manual GUI checks are secondary.


## M4f note
M4f adds semantic-text separation and state-stable control geometry. Semantic labels are not paint; explicit text visuals emit draw text. Checkbox/switch state changes should preserve row identity/shape and adjust stable style/geometry values instead of adding/removing rows.

- M5c update: StandardTheme now carries typed component style records and supports explicit root theme handoff with `with` customization.


## M5c1 Button/Card style record contract

- `StandardUI.Button` supports explicit `style:` overrides; explicit style wins over variant + size defaults.
- Without `style:`, button style resolves from `theme.Button` and variant mapping.
- `StandardUI.Card` supports explicit `style:` overrides; explicit style wins over `theme.Card.Default`.
- Card content inset is structural (anchored content region), not implicit style padding.


## M5c2 input style wiring

`StandardInputStyle` fully defines current Input shell/content behavior:

- shell metadata: `Background`, `Foreground`, `BorderColor`, `BorderThickness`
- shell dimensions: `Width`, `Height`
- explicit content geometry: `ContentInset` mapped to anchored `*.content` region
- value text style: `TextStyle`
- placeholder text style: `PlaceholderTextStyle`

No CSS cascade is added. Style precedence remains explicit parameter first, then theme default.


## M5c3 Checkbox and Switch style wiring

M5c3 fully wires `StandardCheckboxStyle` and `StandardSwitchStyle` into `StandardUI.Checkbox` and `StandardUI.Switch`. Checkbox and switch geometry, visual style, gap spacing, and label text style now resolve deterministically from the selected style record (`style:` if supplied, otherwise theme default). Checked/on state changes values (for example mark fill and thumb X) without changing row identity.

## M5c4 consolidation note

StandardUI remains the primary localized component authoring surface. StandardView remains a lightweight row-metadata helper surface; sub-part helpers are advanced/manual composition helpers rather than the default path.

## M5d contract cleanup note
Canonical app authoring now defaults to `StandardUI` component helpers for controls/components. `StandardView` remains for lightweight row metadata, and sub-part helpers (`CheckboxBox`, `SwitchTrack`, `SwitchThumb`) are advanced/manual composition helpers only. See `docs/standard-ui-vs-standard-view-m5d.md`.



## M5e headless harness
M5e standardizes component/document headless assertions through tests/Machina.Testing/GeometryHarness.cs so component tests can assert resolved rectangles, row presence, metadata, and hit targets without repeating lowering/resolve plumbing.


## M5g presenter visual regression audit
M5g requires converting presenter screenshot regressions into headless geometry/render-command tests. Verify UI.Text visibility by DrawTextCommand presence + visible text color + in-card rect; verify default button text-style size fits default shell; verify checkbox checked mark via explicit mark geometry and visible fill when checked (transparent when unchecked). Dynamic text fitting remains deferred.

## M6a Machina.Text boundary note

M6a establishes `Machina.Text` as a separate subsystem contract. Frame/stack/table layout still places component rectangles; `Machina.Text` will lay out text only inside those assigned boxes.

Wrap, overflow, leading, block/list spacing, and text alignment are text-domain primitives and must not be added to general layout semantics.

Headings remain a component/layout responsibility (for example title variant selection in `StandardUI.Card`), not a supported inline markup mechanism inside restricted Machina text source.

The current simple `UI.Text` path is transitional until `Machina.Text` parser/model/layout integration milestones (M6b+) are complete.

## Standard-owned rich text model (M6b)

Standard now owns the rich text model/parser namespace: `Machina.Standard.Text`. This keeps typography variants, labels, captions, titles, and control text policies near Standard components and themes while keeping `Machina.Core` independent of Standard.

M6b is not a component migration. Buttons, checkboxes, switches, labels, raster rendering, and layout resolver behavior continue to use the existing paths. Future Standard components may consume `MachinaTextSpec` and Standard text helpers after measurement/layout/rendering integration lands.

Restricted markup headings remain forbidden; Standard component composition and typography variants should express title hierarchy.
