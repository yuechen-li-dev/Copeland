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
- [Machina Component Gallery Local Visual Audit M7a](docs/machina-component-gallery-local-visual-audit-m7a.md)

Current gallery audit workflow:

```powershell
.\tools\Export-MachinaComponentGallery.ps1
```

Default outputs:

- `artifacts/m7e/component-gallery-default.png`
- `artifacts/m7e/component-gallery-interactive.png`

These PNGs are deterministic local visual audit aids. They are not a committed pixel-diff baseline.

## Reference source

`reference/dominatus` is a reference-only Git submodule for source inspection. Active Copeland and Machina builds continue to use the NuGet `Dominatus.Core` and `Dominatus.OptFlow` `0.4.0` packages.
