# Copeland

Copeland is a Browser TypeScript-to-CLR compiler experiment.

It is **not** a JavaScript engine, does **not** run arbitrary JavaScript, and does not provide DOM/TSX support yet.

## Pipeline

```text
.ts source
  -> typed bound tree
  -> .cope MIR
  -> generated .g.cs
  -> CLR proof in tests
```

Artifact meanings:

- `.ts` is source input.
- `.cope` is a textual MIR artifact.
- `.g.cs` is generated C# for Roslyn/.NET compilation.

The runtime proof path (Roslyn compile + invoke on CLR) exists in test coverage.

## Current M1 language profile (high level)

- explicit type annotations
- `number`, `string`, `boolean`, `void`
- arrays `T[]`
- fallible signatures `function f(): T ! ErrorType`
- propagation `expr?`
- `if` expressions
- nominal tagged enums
- exhaustive `match`

Profile bans include `null`, `undefined`, implicit `any`, `eval`, `var`, ternary `?:`, optional chaining `?.`, truthy/falsy conditions, and implicit globals.

See `docs/language-profile.md` and `docs/diagnostics.md` for the full M1 checkpoint profile.

## CLI status (M1b artifact probe)

Current CLI command:

- `copeland compile <source-file> --emit mir|csharp [--out <path>]`

The CLI currently emits artifacts only. It does not execute compiled programs or expose host/browser APIs.


## Support matrices

- [Copeland TypeScript Support Matrix](docs/copeland-typescript-support.md)
- [Machina Support Roadmap](docs/machina-support-roadmap.md)
- [Windows Test Triage M5i](docs/copeland-windows-test-triage-m5i.md)
- [Reference Source](reference/README.md)

## Machina samples

- [Machina Component Gallery M7a](docs/machina-component-gallery-m7a.md)
- [Machina Component Gallery Export M7b](docs/machina-component-gallery-export-m7b.md)
- [Machina Component Gallery Known Limitations M7e](docs/machina-component-gallery-known-limitations-m7e.md)
- [Machina Font Atlas Architecture M8a](docs/machina-font-atlas-architecture-m8a.md)
- [Machina.Fonts M8b](docs/machina-fonts-m8b.md)
- [Machina Font Atlas TOML M8c](docs/machina-font-atlas-toml-m8c.md)
- [Machina Font Atlas Artifacts M8d](docs/machina-font-atlas-artifacts-m8d.md)
- [Machina Font MSDF Dependency Audit M8e](docs/machina-font-msdf-dependency-audit-m8e.md)
- [Machina Font Generation Adapters M8f](docs/machina-font-generation-adapters-m8f.md)
- [Machina Typography Outline Adapter M8g](docs/machina-typography-outline-adapter-m8g.md)
- [Machina MSDF-Sharp Generator M8h](docs/machina-msdf-sharp-generator-m8h.md)
- [Machina Distance Field Atlas Packing M8i](docs/machina-distance-field-atlas-packing-m8i.md)
- [Machina CPU MSDF Text Renderer M8k](docs/machina-cpu-msdf-text-renderer-m8k.md)
- [Machina CPU MSDF Reference Renderer M8k](docs/machina-cpu-msdf-reference-renderer-m8k.md)
- [Machina CPU MSDF Text Proof Audit M8l](docs/machina-cpu-msdf-text-proof-audit-m8l.md)
- [Machina CPU MSDF Spacing and Kerning M8n](docs/machina-cpu-msdf-spacing-kerning-m8n.md)
- [Machina MSDF Reference Oracle M8o](docs/machina-msdf-reference-oracle-m8o.md)
- [Machina Glyph Field Placement M8p](docs/machina-glyph-field-placement-m8p.md)
- [Machina MSDF Vertical Metrics M8q](docs/machina-msdf-vertical-metrics-m8q.md)
- [Machina MSDF Baseline Rounding Fix M8q.1](docs/machina-msdf-baseline-rounding-fix-m8q1.md)
- [Machina MSDF Baseline Guide Overlay M8q.2](docs/machina-msdf-baseline-guide-overlay-m8q2.md)
- [Machina MSDF Coverage / Threshold M8r](docs/machina-msdf-coverage-threshold-m8r.md)
- [Machina Component Gallery MSDF Proof M8m](docs/machina-component-gallery-msdf-proof-m8m.md)
- [Machina Component Gallery Local Visual Audit M7a](docs/machina-component-gallery-local-visual-audit-m7a.md)

Current gallery audit workflow:

```powershell
.\tools\Export-MachinaComponentGallery.ps1
```

Default outputs:

- `artifacts/m7e/component-gallery-default.png`
- `artifacts/m7e/component-gallery-interactive.png`

These PNGs are deterministic local visual audit aids. They are not a committed pixel-diff baseline.

Opt-in MSDF proof export:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m8m -IncludeMsdfFontProof
```

Proof output:

- `artifacts/m8m/component-gallery-msdf-proof.png`

This proof mode is experimental, local, and sample-only. It does not replace `UI.Text`, `StandardUI.TextBlock`, or the current raster text renderer.

Current font proof audit workflow:

```powershell
.\tools\Export-MachinaFontProofs.ps1
```

Default outputs:

- `artifacts/m8l/msdf-machina.ppm`
- `artifacts/m8l/msdf-aa0.ppm`
- `artifacts/m8l/msdf-a-space-a.ppm`
- `artifacts/m8l/msdf-machina-0.ppm`
- `artifacts/m8l/msdf-hello-machina.ppm`
- `artifacts/m8n/msdf-av-to-wa.ppm`
- `artifacts/m8n/msdf-spacing-proof.ppm`

These PPMs are deterministic local audit aids for standalone `Machina.Fonts`. M8n keeps them proof-path only: no `TextBlock` integration, no production renderer integration, no shaping engine adoption, and no arbitrary tracking hack as the primary spacing fix.

Current reference-oracle workflow:

```powershell
.\tools\Export-MachinaFontReferenceComparison.ps1
```

Default outputs:

- `artifacts/m8o/reference-machina.png`
- `artifacts/m8o/reference-hello-machina.png`
- `artifacts/m8o/reference-kerning.png`
- `artifacts/m8o/machina-msdf-machina.ppm`
- `artifacts/m8o/machina-msdf-machina.png`
- `artifacts/m8o/machina-msdf-hello-machina.ppm`
- `artifacts/m8o/machina-msdf-kerning.ppm`
- `artifacts/m8o/compare-machina.png`
- `artifacts/m8o/compare-hello-machina.png`
- `artifacts/m8o/compare-kerning.png`
- `artifacts/m8o/glyph-placement-report.txt`
- `artifacts/m8o/glyph-placement-report.json`

These M8o outputs remain local debug artifacts only. They are intended to bootstrap evidence for the next proof-path placement fix, not to introduce production text integration or an automated visual gate.

M8r is the current proof-path coverage audit/tuning pass. The current recommended proof export is:

```powershell
.\tools\Export-MachinaFontReferenceComparison.ps1 -OutputDir artifacts\m8r
```

Current M8r outputs include:

- `artifacts/m8r/browser-text-metrics.json`
- `artifacts/m8r/reference-machina.png`
- `artifacts/m8r/reference-hello-machina.png`
- `artifacts/m8r/reference-kerning.png`
- `artifacts/m8r/machina-msdf-machina.ppm`
- `artifacts/m8r/machina-msdf-machina.png`
- `artifacts/m8r/compare-machina.png`
- `artifacts/m8r/glyph-placement-report.txt`
- `artifacts/m8r/glyph-placement-report.json`
- `artifacts/m8r/coverage-experiment.json`

M8r keeps the work proof-only. It preserves the red baseline-guide overlay, adds browser/Machina coverage metrics, and tunes proof threshold/smoothing without changing baseline placement, kerning, `TextBlock`, or any production renderer path.

## Reference source

`reference/dominatus` is a reference-only Git submodule for source inspection. Active Copeland and Machina builds continue to use the NuGet `Dominatus.Core` and `Dominatus.OptFlow` `0.4.0` packages.
