# Machina Direct-Outline Text Proof M9e

## Purpose

M9e brings the M9d direct-outline static text backend into the component gallery as an explicit proof path.

The goal is to show real Machina UI-ish strings through `DirectOutlineStaticTextRenderer` without changing the production default text renderer.

## Why this is proof integration

M9e is sample/tooling-only work.

- the proof lives in `samples/Machina.ComponentGallery.Sample`
- it is enabled only through explicit proof flags
- `UI.Text`, `Standard.Text`, and production raster text behavior stay unchanged by default
- MSDF remains a separate explicit experimental/scalable comparison path

## What renders through DirectOutlineStatic

The proof renders these strings through `DirectOutlineStaticTextRenderer`:

- `Hello Machina`
- `Machina UI`
- `Settings`
- `Direct outline static text`
- `AV To Ta Wa Yo`
- `Aa0 1234567890`
- `The quick brown fox jumps over the lazy dog.`

Current proof sizes:

- `16px`
- `24px`
- `32px`

Primary proof font:

- `CrimsonText-Regular.ttf`

## Presenter/gallery behavior

The canonical integration target is `samples/Machina.ComponentGallery.Sample`.

Current opt-in behavior:

- default gallery export stays unchanged
- `--include-direct-outline-text-proof` adds a new `Direct Outline Static Text Proof` section
- the proof section contains a large direct-outline sample panel plus a backend comparison panel
- `--include-msdf-font-proof` remains explicit and may be combined with direct-outline proof to show `MSDF experimental` in the comparison panel

The presenter sample is intentionally unchanged in M9e.

## Export commands

Component gallery proof export:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m9e -IncludeDirectOutlineTextProof
```

Direct-outline plus explicit MSDF comparison:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m9e -IncludeDirectOutlineTextProof -IncludeMsdfFontProof
```

Diagnostic toolkit export:

```powershell
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9e-font-diagnostics -Preset cad-debug -TextBackend DirectOutlineStatic -Clean
```

## Comparison against current bitmap text

The gallery proof comparison panel labels the backends explicitly:

- `Bitmap/current`
- `DirectOutlineStatic`
- `MSDF experimental`

If MSDF proof is not requested, the comparison stays on bitmap/current vs direct-outline and the section caption states that MSDF remains an explicit experimental workflow.

## MSDF remains experimental

M9e does not repair MSDF and does not make MSDF a default.

## M9f follow-up

M9f repairs the explicit MSDF proof/comparison path without changing the M9e production boundary.

- `DirectOutlineStatic` remains the static/UI-text proof oracle.
- MSDF remains explicit experimental/scalable beside it.
- the gallery/sample proof path can now reuse scalable field sizing for larger MSDF proof text without switching the production UI default.

- `msdf-debug` remains explicit
- `IncludeMsdfFontProof` remains opt-in
- browser kerning is still not the target oracle

## M9g follow-up

M9g builds on the M9e proof bridge by defining a deterministic text-in-rect layout contract for `DirectOutlineStatic`.

- the gallery now has a separate opt-in `Direct Outline Text Box Layout Proof` section
- proof-only layout now covers padding, content rects, horizontal alignment, vertical alignment, clipping, and explicit newline splitting
- production UI text behavior still does not change
- MSDF still remains explicit experimental/scalable only

## M9h follow-up

M9h keeps the M9e proof-only boundary and adds a renderer-facing bridge contract above the M9g layout layer.

- the gallery now has a separate opt-in `DirectOutlineStatic Render Bridge Proof` section
- proof hosts can describe text intent through `StaticTextRenderRequest` and choose the direct-outline backend explicitly
- production UI text behavior still does not change
- `Machina.Fonts.Tooling` remains diagnostic-only and is not added as a production dependency

## What changed

- `GalleryProgramOptions` and `Export-MachinaComponentGallery.ps1` now accept `IncludeDirectOutlineTextProof`
- the gallery sample can render direct-outline proof images with deterministic standalone exports
- direct-outline proof artifacts now include cropped proof and backend-comparison PNGs
- tests now cover renderer reuse, opt-in proof integration, artifact creation, and backend labeling

## What did not change

- no production default text renderer switch
- no `Standard.Text` semantic change
- no `Machina.Core` document model change
- no MSDF generation/sampling/atlas change
- no Vulkan/Aurelian integration
- no browser oracle promotion

## Future production integration path

M9e answers whether direct-outline can render real UI-ish strings cleanly and whether that proof can live in the gallery safely.

If production static UI text later moves to direct-outline, the remaining work should be a separate milestone that:

- chooses an actual runtime integration point
- replaces current bitmap/static text only by explicit product decision
- proves sizing, clipping, wrapping, and control-label behavior in real StandardUI surfaces
- keeps MSDF as a separate scalable-text decision rather than conflating the two paths
