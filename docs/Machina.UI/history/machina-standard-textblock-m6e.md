# Machina Standard TextBlock M6e

## Outcome

M6e lands the first visible Standard-owned rich text surface: `StandardUI.TextBlock(...)`.

This milestone is intentionally narrow:

- `StandardUI.TextBlock` is the only new Standard rich text component.
- It accepts `MachinaTextSpec` from `Machina.Standard.Text.Text.*` helpers.
- It renders through `MachinaTextLayoutEngine` and `MachinaTextRenderBridge`.
- Primitive `UI.Text` remains intact and unchanged.
- Existing Standard controls are not broadly migrated.

## Canonical component name

M6e chooses `StandardUI.TextBlock(...)`.

Why this name:

- it reads like a real component rather than a primitive
- it avoids colliding with `UI.Text(...)`
- it leaves room for rich block content, not just raw strings

## Authoring API

Current authoring shape:

```csharp
StandardUI.TextBlock(
    id: "rich-text-probe",
    text: Text.Markup(
        """
        This card now renders **Standard.Text** through the layout bridge.

        - wrapped text
        - bullet list
        - deterministic geometry
        """,
        variant: MachinaTextVariant.Caption),
    theme: theme,
    foreground: theme.Colors.MutedForeground)
```

Plain text also works:

```csharp
StandardUI.TextBlock(
    id: "body-copy",
    text: Text.Plain("Hello from Standard.Text"),
    theme: theme)
```

## Rendering integration

Ownership remains layered:

- `Machina.Standard.Text`
  owns the text model, parser, policy, and layout engine
- `Machina.Standard`
  owns `StandardUI.TextBlock` and the Standard-owned metadata payload
- `Machina.Core`
  carries an opaque rich text leaf plus lowering metadata storage
- `Machina.Dominatus.Rendering.Bridge`
  recognizes the Standard text metadata, lays it out in the assigned bounds, and emits `DrawTextCommand` output through `MachinaTextRenderBridge`

The render path for `TextBlock` is:

1. `StandardUI.TextBlock(...)`
2. `MachinaTextSpec`
3. lowered rich text payload metadata
4. resolved assigned box
5. `MachinaTextLayoutEngine.Layout(...)`
6. `MachinaTextRenderBridge.ToDrawTextCommands(...)`
7. existing draw text commands

## Assigned-box behavior

M6e keeps the M6a doctrine intact:

- frames place the text box
- `TextBlock` consumes that assigned box
- text layout wraps only inside that box
- text layout does not place other components

The presenter probe uses `TextBlock` as the final child in the card so it can safely consume the remaining card content area without changing general layout semantics.

## Presenter proof

The presenter sample now includes a controlled rich text probe in `SettingsCard`.

Scope of the probe:

- one `StandardUI.TextBlock`
- short paragraph plus bullet list
- no migration of button, checkbox, switch, input, or title/count labels

## Tests

M6e adds or updates headless coverage for:

- Standard component metadata and spec preservation
- Dominatus rich text render-command emission, wrapping, bullet output, assigned-bounds usage, determinism, and primitive `UI.Text` isolation
- presenter probe existence and rich text command presence

## Deferred limitations

Still deferred after M6e:

- broad migration of Standard controls to `StandardUI.TextBlock`
- replacing primitive `UI.Text`
- inline bold/italic/link-decoration fidelity in the current raster renderer
- ellipsis, scroll, and clip-fidelity improvements
- dynamic font sizing
- shaping, kerning, glyph atlas, or alternate font backends

The current bitmap renderer now draws the bullet marker correctly, but rich inline styling still mainly survives as layout metadata rather than distinct visual styling.

## M7a follow-up

M7a adopts `StandardUI.TextBlock` inside the dedicated component gallery sample as the canonical visual workbench proof for current Standard rich text:

- plain paragraph
- markup paragraph
- bullet list
- card-hosted `TextBlock`

This keeps `TextBlock` visible in the standard widget wall without implying broad migration of primitive `UI.Text` labels or control chrome.
