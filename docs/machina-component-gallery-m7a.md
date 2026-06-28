# Machina Component Gallery M7a

M7b follow-up note:

- export contract, script, and artifact policy are now formalized in `docs/machina-component-gallery-export-m7b.md`

## Purpose

M7a adds a dedicated Machina component gallery sample.

This gallery is the canonical local visual workbench for current StandardUI components and states. It is intentionally not a Storybook clone, does not introduce browser dependencies, and does not add automated pixel diffs yet.

## Sample structure

Sample project:

- `samples/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj`
- `samples/Machina.ComponentGallery.Sample/Program.cs`
- `samples/Machina.ComponentGallery.Sample/GalleryActions.cs`
- `samples/Machina.ComponentGallery.Sample/GalleryState.cs`
- `samples/Machina.ComponentGallery.Sample/GalleryScreen.cs`
- `samples/Machina.ComponentGallery.Sample/GallerySections.cs`
- `samples/Machina.ComponentGallery.Sample/GalleryTheme.cs`

Test project:

- `tests/Machina.ComponentGallery.Sample.Tests/Machina.ComponentGallery.Sample.Tests.csproj`
- `tests/Machina.ComponentGallery.Sample.Tests/GalleryScreenTests.cs`
- `tests/Machina.ComponentGallery.Sample.Tests/GalleryGeometryTests.cs`
- `tests/Machina.ComponentGallery.Sample.Tests/GalleryRenderTests.cs`

Authoring split:

- `GalleryScreen` owns the flat `UiDocument` rows and explicit section placement.
- `GallerySections` owns localized StandardUI/UI component subtrees.
- `GalleryState` owns immutable sample state and plain C# dispatch.
- `GalleryActions` owns explicit action ids.
- `Program` hosts the Windows sample window and deterministic raster export mode.

## Gallery sections

The gallery renders one deterministic wall of widgets with these sections:

- Header with primitive `UI.Text` title, subtitle, and click-count text.
- Typography / Text:
  primitive `UI.Text` caption,
  `StandardUI.TextBlock` plain paragraph,
  `StandardUI.TextBlock` markup paragraph,
  `StandardUI.TextBlock` bullet list.
- Buttons:
  default button,
  outline variant button.
- Checkbox / Switch:
  unchecked checkbox,
  checked checkbox,
  switch off,
  switch on.
- Badges / Separator:
  two badge variants and a separator.
- Interactive Probes:
  live checkbox and live switch bound to gallery actions/state.
- Input:
  placeholder example and value example.
- Cards:
  simple card with primitive text + button,
  card with `StandardUI.TextBlock`.
- Theme Probe:
  nested custom-theme section proving explicit theme handoff.

## State/actions

`GalleryState` is an immutable record:

- `PrimaryClicks`
- `SecondaryClicks`
- `LiveCheckboxChecked`
- `LiveSwitchOn`
- `InputValue`

Action ids:

- `gallery.button.primary.click`
- `gallery.button.secondary.click`
- `gallery.checkbox.toggle`
- `gallery.switch.toggle`

Dispatch is plain C# through `GalleryState.Dispatch(...)`. No hidden runtime framework or implicit theme cascade is introduced.

## Headless tests

Dedicated gallery tests cover:

- document shape and flat hosted section rows
- expected component ids
- checkbox checked-mark visibility
- `TextBlock` render command emission
- hit targets wired to `GalleryActions`
- explicit theme propagation
- deterministic render command summaries
- stable live-control geometry across state changes

Headless tests remain the primary structural contract. M7a does not replace them with screenshot-only proof.

## Local visual validation

Launch the Windows sample:

```powershell
dotnet run --project samples/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj
```

Historical deterministic export mode used during M7a:

```powershell
dotnet run --project samples/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m7a --export-name component-gallery-initial
dotnet run --project samples/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m7a --export-name component-gallery-final --primary-clicks 1 --checkbox on --switch on
```

M7a wrote `.ppm` directly. M7b replaces that ad hoc export note with the stable `.png` contract documented in `docs/machina-component-gallery-export-m7b.md`.

## Deferred issues

- no automated pixel diff system yet
- no broad migration of old primitive text labels/buttons to `TextBlock`
- no text editing behavior
- no scrolling/resizing workbench UX yet
- current bitmap text renderer still has limited glyph coverage and small-text fidelity
- richer rich-text visual styling remains deferred

## Future use

This gallery is now the default “look at all the widgets” page for local StandardUI inspection and regression hardening.

Future milestones can add more components or states here, but should preserve the same boring local-first role:

- explicit sample project
- deterministic raster export
- local visual inspection on Windows
- headless structural contracts

## M7d badge note

M7d keeps the gallery itself structurally unchanged while fixing `StandardUI.Badge` locally:

- badge shell size is now deterministic and finite
- badge label placement uses a local explicit label region
- badge row geometry is regression-tested so gallery badge examples do not overflow or trigger negative stack-space failures

No gallery-only workaround, no general layout change, and no style cascade was introduced.
