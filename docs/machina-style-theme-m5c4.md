# Machina M5c4: style model consolidation and canonical sample polish

M5c4 is a consolidation pass over M5c0/M5c1/M5c2/M5c3. It does not add new architecture.

## Doctrine (canonical)

- Style is ordinary immutable C# data.
- Theme is explicit input.
- `with` is the customization mechanism.
- No cascade.
- No hidden mutable global style.
- Basic component params are convenience.
- Advanced customization uses component style records.
- Layout-affecting style fields produce explicit layout rows/frames.
- Style padding is paint metadata only and should not move children.

## M5c summary

- M5c0: style/theme scaffold stabilized.
- M5c1: `StandardButtonStyle` and `StandardCardStyle` fully wired.
- M5c2: `StandardInputStyle` fully wired.
- M5c3: `StandardCheckboxStyle` and `StandardSwitchStyle` fully wired.
- M5c4: naming/docs/tests coherence pass.

## Family and naming consistency

All major control families now use the same structure:

- `StandardTheme.Button.Default`
- `StandardTheme.Card.Default`
- `StandardTheme.Input.Default`
- `StandardTheme.Checkbox.Default`
- `StandardTheme.Switch.Default`

Buttons additionally expose variant families (`Destructive`, `Outline`, `Secondary`, `Ghost`, `Link`).

## Style model table

| Control | Style record | Layout-affecting fields | Paint-only fields |
|---|---|---|---|
| Button | `StandardButtonStyle` | `Width`, `Height` | `Background`, `Foreground`, `BorderColor`, `BorderThickness`, `TextStyle` |
| Card | `StandardCardStyle` | `ContentInset` (used to create explicit content row) | `Background`, `Foreground`, `BorderColor`, `BorderThickness` |
| Input | `StandardInputStyle` | `Width`, `Height`, `ContentInset` (explicit `*.content` row) | `Background`, `Foreground`, `BorderColor`, `BorderThickness`, `TextStyle`, `PlaceholderTextStyle`, disabled colors |
| Checkbox | `StandardCheckboxStyle` | `BoxSize`, `MarkSize`, `Gap` | box/mark/label colors, border thickness, `LabelTextStyle` |
| Switch | `StandardSwitchStyle` | `TrackWidth`, `TrackHeight`, `ThumbSize`, `ThumbInset`, `Gap` | track/thumb/label colors, border thickness, `LabelTextStyle` |

## StandardUI vs StandardView guidance

- **StandardUI** is the primary component surface for app authoring. It is where localized component functions and style records are expected.
- **StandardView** is a lightweight flat-row metadata helper (`UiView`) for explicit row authoring and leaf/simple helper use.
- StandardView sub-part helpers (`CheckboxBox`, `SwitchTrack`, `SwitchThumb`) are advanced/manual composition tools, not the default app-authoring path.

## Leaf helpers

- `Label` and text helpers are leaf metadata declarations; they propagate deterministic text style metadata.
- `Badge` remains a simple styled label-like helper with deterministic colors and centered text.
- `Separator` remains a simple deterministic line/rect helper with explicit axis + thickness behavior.

## Canonical presenter sample

Presenter sample remains the canonical boring C# pattern:

- explicit root theme handoff (`SettingsScreen.Build(state, theme)`),
- plain C# dispatch,
- localized hosted component function (`SettingsCard`),
- explicit `with` customization in root `AppTheme`,
- headless tests for layout/style semantics in standard and pipeline suites.
