# Machina Presenter M3e — flat form composition + form-row hardening

## Scope

M3e restores visible checkbox/switch controls in the presenter sample after the row-first rewrite, while keeping canonical flat `UiDocument` authoring.

## What M3e proves

- Top-level screens are authored as a flat row table.
- Field-style UI can be composed from explicit rows (control region + label), without a magic tree component.
- `StandardView` helpers remain metadata/painter helpers over `UiView`; they are not layout-tree constructors.
- Hit-testing and action dispatch still work for count/checkbox/switch transitions.

## Presenter form composition shape

Inside `settings-card`, the sample now uses:

- `title`
- `count`
- `increment`
- `separator`
- `email-row`
  - `email-box`
  - `email-label`
- `notifications-row`
  - `notifications-track`
  - `notifications-thumb`
  - `notifications-label`
- `footnote`

This keeps control geometry inspectable and deterministic in one flat row table.

## StandardView sub-part helpers

M3e adds optional helpers for composed form controls:

- `StandardView.CheckboxBox(...)`
- `StandardView.SwitchTrack(...)`
- `StandardView.SwitchThumb(...)`

These helpers only emit existing style/semantics/action metadata.

## Run

```bash
dotnet run --project samples/Machina.Presenter.Sample
```
\n## M4a hybrid note\nRow-hosted components are now supported: top-level placement stays flat rows, while local component internals use nested UiNode/StandardUI under a host row boundary.
