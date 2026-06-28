# Machina Component Gallery MSDF Proof M8m

## Purpose

M8m brings the standalone CPU-side MSDF proof stack into Machina's canonical component gallery as an explicit local audit mode.

The goal is visual proof only:

- keep normal gallery export unchanged by default
- keep current bitmap `UI.Text` and `StandardUI.TextBlock` behavior unchanged
- show a side-by-side comparison inside the gallery artifact before any production renderer integration or contract cleanup work

## Opt-in proof mode

M8m adds an explicit opt-in export flag:

- PowerShell: `-IncludeMsdfFontProof`
- sample CLI: `--include-msdf-font-proof`

Default gallery exports still produce:

- `component-gallery-default.png`
- `component-gallery-interactive.png`

Proof mode additionally produces:

- `component-gallery-msdf-proof.png`

The proof mode is experimental and local. It is not enabled unless the flag is passed.

## Implementation approach

M8m keeps the integration inside `samples/Machina.ComponentGallery.Sample` and its tests.

The flow is:

1. normal gallery document builds as before
2. proof mode adds one extra gallery section with:
   current bitmap text on the left
   a reserved proof image slot on the right
3. after normal gallery rasterization, the sample calls the existing `Machina.Fonts.ReferenceRendering.DistanceFieldTextPipeline`
4. the sample renders proof strings with the CPU reference MSDF path
5. the sample blits that generated `RgbaImage` into the reserved gallery slot
6. the exporter writes the final PNG through the existing sample-local PNG writer

No general image component, renderer integration, or production UI package dependency change was introduced.

## Export commands

Default gallery export:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m8m
```

Opt-in proof export:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m8m -IncludeMsdfFontProof
```

Direct sample fallback:

```powershell
dotnet run --project samples/Machina.ComponentGallery.Sample/Machina.ComponentGallery.Sample.csproj -- --export-only --export-dir artifacts\m8m --export-name component-gallery-msdf-proof --include-msdf-font-proof
```

## Visual inspection findings

Inspected artifact:

- `artifacts/m8m/component-gallery-msdf-proof.png`

Observed:

- MSDF proof section appears only in opt-in mode
- MSDF text is visible and non-blank
- text is upright, not vertically inverted or mirrored
- contrast is strong enough for local audit
- baseline spacing is usable for proof comparison
- existing gallery widgets remain readable
- default gallery export remains visually unchanged

Current proof-only rough edges:

- spacing and weight are not final production typography
- the proof lines are slightly soft and uneven compared with a finished text renderer
- the proof card is intentionally utilitarian rather than polished

## What this proves

M8m proves that:

- the existing `Machina.Fonts` CPU reference MSDF string renderer can be consumed from the gallery sample
- the resulting proof image can be embedded into the gallery PNG export without changing the normal text renderer
- bitmap gallery text and MSDF proof text can be inspected side by side in one local artifact

## What this does not prove

M8m does not prove:

- production `TextBlock` rendering
- production `UI.Text` replacement
- button/checkbox/switch/input/card label migration
- renderer integration
- Vulkan or Aurelian integration
- async atlas lifetime or runtime cache policy
- shaping, kerning, fallback, bidi, ligatures, or multiline text layout

## Deferred issues

- the current proof still relies on proof-local line composition rather than a full multiline text layout contract
- proof spacing and thickness tuning are still audit-level only
- the gallery sample now depends on `Machina.Fonts` for this proof mode, but production UI packages still do not
- the sample/test proof hosts carry runtime package closure for `MSDF-Sharp.Core`, `Tomlyn`, and `Typography.OpenFont`; this remains local to the proof host path

## M8n plan

M8n should use the evidence from this gallery proof to decide whether the next step is:

- tightening the atlas/field-origin/orientation contract,
- improving proof layout and compositing ergonomics,
- or beginning a deliberately scoped renderer-facing integration milestone

That next step should still avoid a broad `TextBlock` migration until the production contract is explicit.

## M8n follow-up

M8n uses this proof card exactly as intended: as an opt-in local audit surface, not as production evidence.

- the gallery proof still renders through the CPU reference path only
- default gallery export behavior is unchanged
- proof spacing improves because the CPU renderer now recomputes real field-canvas fit padding
- the gallery still uses `SpaceMono-Regular.ttf`, so it does not become the primary kerning-proof artifact

## M8q follow-up

M8q does not change default gallery behavior and does not promote the proof card into production text integration.

- the gallery MSDF proof remains opt-in and sample-only
- the same proof renderer was revalidated against browser `TextMetrics`
- no gallery-only vertical offset was introduced

The gallery proof continues to be a local audit surface for the standalone proof renderer, now with better baseline documentation behind it.

Real kerning proof now lives in the standalone `artifacts/m8n/msdf-av-to-wa.ppm` and `artifacts/m8n/msdf-spacing-proof.ppm` outputs that use the checked-in proportional `CrimsonText-Regular.ttf` fixture.

## M8q.2 follow-up

M8q.2 keeps the gallery proof opt-in and sample-only, but aligns it with the reference-oracle diagnostics.

- the gallery MSDF proof renderer now enables the same 1 px red baseline guide overlay used by the standalone proof comparison workflow
- this makes the sample proof card easier to compare visually with the browser-oracle artifacts

This is still a tooling overlay for visual diagnosis, not a production text change or rendering fix.

## M8r follow-up

M8r keeps the gallery proof opt-in and sample-only.

- the gallery proof now uses the same proof-path coverage defaults as the standalone oracle: `threshold = 0.54`, `smoothingMultiplier = 0.5`
- the goal is only to keep the sample proof visually aligned with the standalone audit artifacts
- no default gallery behavior changed
- no `UI.Text`, `StandardUI.TextBlock`, or production renderer integration was introduced
