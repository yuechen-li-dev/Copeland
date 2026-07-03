# Copeland

Copeland is a Browser TypeScript-to-CLR compiler experiment.

It is **not** a JavaScript engine, does **not** run arbitrary JavaScript, and does not provide DOM/TSX support yet.

## Monorepo direction

This repository is now evolving toward a broader `Visionary` monorepo umbrella, while the existing subsystem names remain meaningful and distinct:

- `Copeland`: compiler infrastructure, frontends, diagnostics, MIR, and backend lowering.
- `Machina`: UI document model, layout, renderer-facing presenter/workbench shell, and samples.
- `Oblivion`: notebook/card/workbench layer hosted in the Machina presenter.
- `Aurelian`: rendering infrastructure, render contracts, shader/compiler work, and Vulkan-oriented backend work.
- `Dominatus`: orchestration, lifecycle, and effect-routing infrastructure.
- `Leviathan`: future web/auth/payment/social/networked application layer.

The imported `Aurelian` source tree and `docs/Aurelian` content are present as a separate subsystem lane. M13b stabilizes its build topology without deep integration: `Aurelian.slnx` remains separate, Aurelian now uses Dominatus NuGet packages instead of `vendor/Dominatus` or `reference/dominatus` project references, SDSL-V has not been merged into Copeland, Machina is not wired to Aurelian, and Vulkan runtime integration is still deferred. See [Aurelian Build Topology M13b](docs/Aurelian/aurelian-build-topology-m13b.md).

M13c follows that with test normalization and docs dogfood only: the remaining Aurelian shader test failure is fixed by line-ending normalization at the test assertion boundary, and selected `docs/Aurelian/...` files now dogfood through the existing `Copeland.Markdown` and `Oblivion -> Docs` path. This still does not move SDSL-V into Copeland, add a `Machina.Aurelian` bridge, or add Vulkan presenter integration.

M13d is architecture doctrine only: Copeland is now documented as the compiler workshop for Visionary. It supports explicit compiler lanes, shared primitives promoted only after repeated concrete use, and no universal-IR mandate. This still does not move `Aurelian.Shaders`, create `Copeland.Shaders`, implement GPU TypeScript or PTX packages, wire Machina to Aurelian/Vulkan, or rename the repo.

M13e is recon only: it audits the active `Aurelian.Shaders` SDSL-V lane, documents the exact current `SDSL-V -> HLSL -> DXC -> SPIR-V` path, records backend-neutral versus HLSL/DXC-specific concepts, identifies hidden MIR-shaped pressure already present in lowering/emission/artifact code, and documents one common GPU MIR as the starting assumption for future work. M13e does not move SDSL-V into Copeland, implement `GpuMir`, implement Slang or PTX, or split Shader MIR from Kernel MIR.

M13f is doctrine only: the future common GPU-oriented MIR is now named `VD-MIR` (`Visual Direct MIR`). `VD-MIR` is defined as backend-lowering-shaped, source-provenance-preserving, target-aware, capability-checkable, and artifact-friendly, while HLSL/DXC, Slang, and PTX are defined as backends from `VD-MIR` rather than semantic centers. M13f does not implement `VD-MIR`, does not migrate SDSL-V, does not change current HLSL/DXC behavior, does not split Shader MIR from Kernel MIR, and does not wire `samples/Aurelian.VisibleTriangle` to `VD-MIR`.

M13g is audit/topology only: `samples/Aurelian.VisibleTriangle` is now inspected as the future visible proof target, its current Aurelian-owned shader/runtime/render path is documented, and it is restored to `Aurelian.slnx` because the sample project is present and builds cleanly. M13g does not implement `VD-MIR`, does not migrate SDSL-V, does not change HLSL/DXC emission, does not add Slang/PTX backends, and does not wire Machina, Aurelian, or Vulkan together beyond the sample's existing path.

M14d then routes `samples/Aurelian.VisibleTriangle` through `PresenterScreenStack` using `VisibleTriangleWorldScreen` on the semantic `world` layer while preserving the existing Aurelian frame-loop/compositor/present path. The familiar sample command remains, no new render contract is introduced, and local present-path runs passed without claiming human pixel confirmation.

M14e is closeout/handoff only: the Aurelian migration arc is now documented as golden-pathed enough for separate future continuation, future Aurelian and `VD-MIR` work moves to explicit reviewer lanes, and primary active focus returns to Machina and Oblivion. The sample still does not route through `VD-MIR`, SDSL-V still has not moved into Copeland, no Copeland `VD-MIR` package is created, no Slang/PTX backend is added, and no runtime behavior changes in M14e.

Historical note: exploratory `src/Aurelian.Shaders/Language/VdMir` code and `artifacts/m14a/` remain in-tree from earlier compiler-only work, but the active visible-triangle golden path documented by M14e does not depend on that slice.

M15a is audit-only and re-enters the Machina/Oblivion workbench lane. The current presenter remains extremely fast, but the workbench is still blocked by usability failures: fixed startup window sizing, no live layout recomposition on resize, unreadable compact card previews, missing intentional wrap/elision rules, and dark-on-dark preview text in part of the Markdown path. The recommended next implementation milestone is M15b: presenter resizing plus readable card previews, without reopening compiler or rendering lanes.

M15b now lands that controlled follow-through. Runtime presenter resizing is now a constrained `16:9` letterboxed surface with a `960x540` minimum and a `1280x720` default runtime surface, layout recomputes from the live effective presenter surface, adaptive shell mode resolves from that live width, and compact Oblivion card previews now use bounded wrap-or-elide behavior with explicit readable preview contrast. M15b still does not implement arbitrary freeform `2D` layout, editor/execution work, Aurelian work, or `VD-MIR` work.

M15c now makes the stack itself the reading surface. Oblivion Markdown cards have explicit page-local expansion state, collapsed cards stay compact and scannable, expanded cards render the Markdown body inline with local body scrolling, and the inspector remains the metadata/actions/diagnostics/artifacts surface rather than the only place to read the body. M15c still does not implement Markdown editing, notebook execution, Aurelian work, or `VD-MIR` work.

M15d hardens that reading surface. Expanded Markdown cards are now treated as document reading surfaces with explicit readable contrast on a dark surface, document-scale expanded height, preserved local body scrolling, and a shared immutable reading-style record rather than scattered renderer-local colors. The inspector no longer renders formatted Markdown body content; it now shows raw Markdown source text in a bounded scrollable source block while remaining the metadata/actions/diagnostics/artifacts surface. M15d still does not implement Markdown editing, notebook execution, Aurelian work, `VD-MIR` work, CSS-like styling, or arbitrary `2D` layout solving.

M15e hardens scrolling and viewport behavior. The main card stack and inspector now behave as independent panes with independent scrollbars, nested scroll regions use explicit wheel/pointer routing plus direct thumb dragging, inspector raw Markdown source is actually scrollable, and document viewport culling now keeps partially visible Markdown content renderable instead of blanking whole blocks. M15e still does not implement Markdown editing, notebook execution, Aurelian work, `VD-MIR` work, or any browser-like event/layout system.

M15f is regression stabilization only. It traces and fixes the M15e main-card-stack regression by separating wide main-stack scroll from the generic page-scroll clamp path, and it investigates the inspector scroll lag with render/layout evidence. The safe fix keeps the current narrow presenter architecture but caches prepared raw-source layout so repeated inspector scroll ticks do not rebuild that work over and over. M15f still does not add new features, Markdown editing, notebook execution, Aurelian work, or `VD-MIR` work.

M15g is closeout and planning only. The M15 reading-surface arc is now documented as a baseline: the runtime uses a controlled resizable `16:9` presenter surface, collapsed cards are scannable, one Markdown card can expand inline per page, rendered Markdown reads inline in the stack, the inspector remains an independent metadata/actions/diagnostics/raw-source pane, and scrolling is independent even though selection still couples stack and inspector content. M15g does not change runtime behavior, does not add new features, and does not continue speculative scroll churn; it records the remaining UX backlog and recommends `M16a — Oblivion reading navigation and focus affordances` as the next main direction.

M16a added the internal deterministic playback MVP for the Machina presenter. M16b follows by stabilizing input parity for `main-stack` and `raw-source` wheel playback so starter scenarios now pass through the same internal presenter input/routing path that real user interaction uses. M16c then turns that harness into a regression suite with starter/regression cassette organization, suite manifests, directory/manifest batch runs, deterministic aggregate reports, and milestone manifests. M16d then wires the same playback core into normal xUnit discovery/execution so C# owns scenario selection, loops, guards, and failure formatting while TOML remains a cassette. Playback scenarios remain TOML artifacts with required assertion reasons plus normalized scenario/trace/manifest/final-PNG outputs, and this lane still does not implement native OS automation, TOML scripting, or pixel-golden screenshot diffing.

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

## Copeland Markdown M12a status

M12a adds `src/Copeland.Markdown`, a small compiler-style Markdown frontend for existing `.md` docs.

Current Copeland Markdown pipeline:

```text
.md source
  -> lexer/scanner
  -> parser
  -> Markdown AST
  -> Document MIR
  -> deterministic text/json dump
```

Copeland Markdown is intentionally not full CommonMark. `.md` remains the practical dogfood extension, predictable compilation is prioritized over dialect compatibility, and no external Markdown parser dependency is added.

Current supported subset:

- ATX headings
- paragraphs
- single-level bullet lists
- ordered lists
- fenced code blocks
- thematic breaks
- inline code
- strong
- emphasis
- inline links

Current CLI workflow:

```powershell
dotnet run --project src/Copeland.Cli -- markdown parse README.md --emit mir --format json
.\tools\Export-CopelandMarkdownCorpus.ps1 -OutputDir artifacts\m12a
```

This is frontend/MIR dogfooding only. It adds no Markdown editor, no production Oblivion rendering path yet, and no Roslyn or xUnit notebook execution.

## Oblivion Markdown M12b/M12g status

M12b integrates that frontend into Oblivion as a text-card body path. M12c then makes the Markdown body visibly useful for dogfooding. M12d then points that dogfood path at selected existing repo docs under `Oblivion -> Docs`. M12e formalizes the card-as-applet contract so the shell keeps navigation, selection, scrolling, routing, ordering, and persistence loading while each card kind owns its model, local state, diagnostics, artifacts, views, and future effect metadata. M12f then adds the non-executing action/effect routing skeleton so cards can create localized effect requests and the shell can route them generically to deferred results. M12g then extends the presenter input seam with backend-neutral keyboard input so navigation, shortcuts, and future editor routing have a clean shell-owned path. M12h then adds adaptive shell modes by resolving one top-level width breakpoint into either a wide shell document or a compact shell document.

Current doctrine:

```text
Oblivion page
  -> stack of typed cards

Text/note card
  -> Copeland Markdown body
  -> note-card handler runtime model
  -> compact card + inspector

Single-file Markdown
  -> future export/import target only
```

Actions and effects remain non-executing in M12g. Text input now translates through the shell backend seam, but there is still no Roslyn execution, no xUnit notebook execution, no Markdown editor, no file watcher, and no Visionary implementation here.

Adaptive-shell doctrine in M12h:

```text
Window width
  -> ShellMode

ShellMode
  -> Wide shell document
  -> Compact shell document

Cards
  -> receive bounded regions only
```

This is not CSS/flex/grid-style responsive solving and not continuous scaling.

Current storage split:

```text
workspace.oblivion.json
  -> sections/pages/card references

*.card.toml
  -> typed card metadata

body/*.md
  -> text-card body content
```

Current state:

- external `copeland-markdown` body loading
- `DocumentMir` attachment on loaded cards
- compact Markdown previews that surface headings, summaries, code, and diagnostics
- inspector rendering that distinguishes headings, paragraphs, lists, code fences, inline code, strong/emphasis, and links
- readable Markdown diagnostics with line/column data
- sample `.md` dogfood bodies, including a curated doc-derived sample
- curated existing repo docs loaded as generated cards with preserved repo-relative source paths
- a synthetic docs index/status card that summarizes loaded docs and diagnostics
- selected Aurelian docs loaded through the same dogfood path with preserved source paths, per-doc diagnostics, and separate Aurelian counts in the index

Still not included:

- Markdown editor
- live editing or file watcher behavior
- single-file Markdown export/import implementation
- Roslyn/xUnit execution
- Visionary
- full CommonMark compatibility

## Test suites

Regular solution test coverage:

```powershell
dotnet test Copeland.slnx
```

Slow tooling coverage that is intentionally excluded from the regular solution path:

```powershell
dotnet test Copeland.Slow.slnx
```

Machina M11b keeps the normal loop on `Copeland.slnx`, moves fast font-tooling unit coverage into `tests/Machina.Fonts.Tooling.Unit.Tests`, and keeps full export/MSDF/smoke coverage in `Copeland.Slow.slnx`.

`[Fact]` / `[Theory]` execution as notebook/runtime behavior is still deferred to M12 or later. M11b changes test topology only.


## Support matrices

- [Copeland Docs Index](docs/Copeland/README.md)
- [Copeland Compiler Workshop Architecture M13d](docs/Copeland/copeland-compiler-workshop-architecture-m13d.md)
- [Copeland Compiler Lane Taxonomy M13d](docs/Copeland/copeland-compiler-lane-taxonomy-m13d.md)
- [Oblivion Independent Scroll Panes M15e](docs/Oblivion/oblivion-independent-scroll-panes-m15e.md)
- [Machina Document Viewport Culling M15e](docs/Machina/machina-document-viewport-culling-m15e.md)
- [Oblivion Scroll Regression Stabilization M15f](docs/Oblivion/oblivion-scroll-regression-stabilization-m15f.md)
- [Machina Scroll Region Routing M15f](docs/Machina/machina-scroll-region-routing-m15f.md)
- [Oblivion Reading Surface Closeout M15g](docs/Oblivion/oblivion-reading-surface-closeout-m15g.md)
- [Machina/Oblivion UX Backlog M15g](docs/Machina/machina-oblivion-ux-backlog-m15g.md)
- [Machina Presenter Playback M16a](docs/Machina/machina-presenter-playback-m16a.md)
- [Machina Playback Scenario Format M16a](docs/Machina/machina-playback-scenario-format-m16a.md)
- [Machina Playback Input Parity M16b](docs/Machina/machina-playback-input-parity-m16b.md)
- [Machina Playback Regression Suite M16c](docs/Machina/machina-playback-regression-suite-m16c.md)
- [Machina Playback xUnit Integration M16d](docs/Machina/machina-playback-xunit-integration-m16d.md)
- [Oblivion Playback Regression Coverage M16c](docs/Oblivion/oblivion-playback-regression-coverage-m16c.md)
- [Aurelian SDSL-V Lane Audit M13e](docs/Aurelian/aurelian-sdslv-lane-audit-m13e.md)
- [Copeland GPU MIR Target Analysis M13e](docs/Copeland/copeland-gpu-mir-target-analysis-m13e.md)
- [Aurelian.VisibleTriangle Topology Audit M13g](docs/Aurelian/aurelian-visible-triangle-topology-audit-m13g.md)
- [VD-MIR Architecture Doctrine M13f](docs/Copeland/vd-mir-architecture-doctrine-m13f.md)
- [VD-MIR Visible Triangle Proof Boundary M13g](docs/Copeland/vd-mir-visible-triangle-proof-boundary-m13g.md)
- [Aurelian World Screen M14d](docs/Aurelian/aurelian-world-screen-m14d.md)
- [Aurelian Migration Closeout M14e](docs/Aurelian/aurelian-migration-closeout-m14e.md)
- [Visionary Subsystem Handoff M14e](docs/Visionary/visionary-subsystem-handoff-m14e.md)
- [Machina/Oblivion Usability Re-entry Audit M15a](docs/Machina/machina-oblivion-usability-reentry-audit-m15a.md)
- [Oblivion Card Readability Audit M15a](docs/Oblivion/oblivion-card-readability-audit-m15a.md)
- [Machina Presenter 16:9 Resizing M15b](docs/Machina/machina-presenter-16x9-resizing-m15b.md)
- [Oblivion Readable Card Previews M15b](docs/Oblivion/oblivion-readable-card-previews-m15b.md)
- [Machina Card Stack Reading Flow M15c](docs/Machina/machina-card-stack-reading-flow-m15c.md)
- [Oblivion Expandable Markdown Cards M15c](docs/Oblivion/oblivion-expandable-markdown-cards-m15c.md)
- [Machina Markdown Reading Style M15d](docs/Machina/machina-markdown-reading-style-m15d.md)
- [Oblivion Expanded Markdown Reading Surface M15d](docs/Oblivion/oblivion-expanded-markdown-reading-surface-m15d.md)
- [Machina Document Viewport Culling M15e](docs/Machina/machina-document-viewport-culling-m15e.md)
- [Oblivion Independent Scroll Panes M15e](docs/Oblivion/oblivion-independent-scroll-panes-m15e.md)
- [Copeland Roadmap](docs/Copeland/copeland-roadmap.md)
- [Copeland TypeScript Support Matrix](docs/copeland-typescript-support.md)
- [Machina Support Roadmap](docs/Machina/machina-support-roadmap.md)
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
- [Machina MSDF Reference Diff Overlay M8r](docs/machina-msdf-reference-diff-overlay-m8r.md)
- [Machina MSDF Three-Way Shape Diff M8s](docs/machina-msdf-three-way-shape-diff-m8s.md)
- [Machina Font Toolkit M9a](docs/machina-font-toolkit-m9a.md)
- [Machina Font Toolkit Layers M9b](docs/machina-font-toolkit-layers-m9b.md)
- [Machina Font Toolkit Export Hygiene M9c](docs/machina-font-toolkit-export-hygiene-m9c.md)
- [Machina Direct-Outline Static Text M9d](docs/machina-direct-outline-static-text-m9d.md)
- [Machina Direct-Outline Text Proof M9e](docs/machina-direct-outline-text-proof-m9e.md)
- [Machina MSDF Alignment Repair M9f](docs/machina-msdf-alignment-repair-m9f.md)
- [Machina Direct-Outline Text Layout Contract M9g](docs/machina-direct-outline-text-layout-contract-m9g.md)
- [Machina Direct-Outline Render Bridge M9h](docs/machina-direct-outline-render-bridge-m9h.md)
- [Machina Presenter Navigation Shell M10a](docs/machina-presenter-navigation-shell-m10a.md)
- [Machina Presenter Navigation Interaction M10b](docs/machina-presenter-navigation-interaction-m10b.md)
- [Machina Presenter Page Organization M10c](docs/machina-presenter-page-organization-m10c.md)
- [Machina Presenter Stabilization M10d](docs/machina-presenter-stabilization-m10d.md)
- [Machina Oblivion Card Model M11a](docs/machina-oblivion-card-model-m11a.md)
- [Machina Test Suite Topology M11b](docs/machina-test-suite-topology-m11b.md)
- [Machina Presenter Scrollbar State Machine M11c](docs/machina-presenter-scrollbar-state-machine-m11c.md)
- [Machina Oblivion Workspace Persistence M11d](docs/machina-oblivion-workspace-persistence-m11d.md)
- [Machina Presenter Card Hardening M11e](docs/machina-presenter-card-hardening-m11e.md)
- [Machina Oblivion Card Inspector M11f](docs/machina-oblivion-card-inspector-m11f.md)
- [Machina Oblivion Phase Closeout M11g](docs/machina-oblivion-phase-closeout-m11g.md)
- [Copeland Markdown Frontend M12a](docs/copeland-markdown-frontend-m12a.md)
- [Machina Oblivion Markdown Body Integration M12b](docs/machina-oblivion-markdown-body-integration-m12b.md)
- [Machina Oblivion Markdown Rendering M12c](docs/machina-oblivion-markdown-rendering-m12c.md)
- [Machina Oblivion Docs Dogfood M12d](docs/machina-oblivion-docs-dogfood-m12d.md)
- [Aurelian Test Normalization and Docs Dogfood M13c](docs/Aurelian/aurelian-test-normalization-docs-dogfood-m13c.md)
- [Machina Oblivion Agentic Card Contract M12e](docs/machina-oblivion-agentic-card-contract-m12e.md)
- [Machina Oblivion Card Effect Routing M12f](docs/machina-oblivion-card-effect-routing-m12f.md)
- [Machina Presenter Keyboard Input M12g](docs/machina-presenter-keyboard-input-m12g.md)
- [Machina Presenter Adaptive Shell Modes M12h](docs/machina-presenter-adaptive-shell-modes-m12h.md)
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

Opt-in direct-outline static proof export:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m9e -IncludeDirectOutlineTextProof
```

Proof outputs:

- `artifacts/m9e/component-gallery-direct-outline-text-proof.png`
- `artifacts/m9e/component-gallery-text-backend-comparison.png`
- `artifacts/m9e/direct-outline-static-text-proof.png`

This proof mode is also local and sample-only. It proves `DirectOutlineStatic` on real UI-ish strings without changing the production UI text default, and MSDF stays explicit experimental/scalable only.

Opt-in direct-outline text layout proof export:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m9g -IncludeDirectOutlineTextProof -IncludeDirectOutlineTextLayoutProof
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9g -Preset cad-debug -TextBackend DirectOutlineStatic -GridStep 8 -ShowUnitLabels -ShowBounds -Clean
```

Proof outputs:

- `artifacts/m9g/component-gallery-direct-outline-text-layout-proof.png`
- `artifacts/m9g/direct-outline-text-box-layout-proof.png`
- `artifacts/m9g/direct-outline-text-alignment-grid.png`
- `artifacts/m9g/font-diagnostic-export-manifest.txt`
- `artifacts/m9g/font-diagnostic-export-manifest.json`

This M9g proof remains local and sample-only. It formalizes a deterministic text-in-rect layout contract for `DirectOutlineStatic`, adds padding/alignment/clipping and explicit newline layout to the proof path, keeps production UI text behavior unchanged, and leaves MSDF explicit experimental/scalable.

Opt-in direct-outline render bridge proof export:

```powershell
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9h -Preset cad-debug -TextBackend DirectOutlineStatic -GridStep 8 -ShowUnitLabels -ShowBounds -Clean
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m9h -IncludeDirectOutlineRenderBridgeProof
```

Proof outputs:

- `artifacts/m9h/component-gallery-direct-outline-render-bridge-proof.png`
- `artifacts/m9h/direct-outline-render-bridge-proof.png`
- `artifacts/m9h/direct-outline-render-bridge-layout-grid.png`
- `artifacts/m9h/font-diagnostic-export-manifest.txt`
- `artifacts/m9h/font-diagnostic-export-manifest.json`

This M9h proof is still local and sample-only. It adds a renderer-facing bridge contract in `Machina.Fonts.ReferenceRendering`, keeps production UI text behavior unchanged, keeps `DirectOutlineStatic` as the static/reference path, and keeps MSDF explicit experimental/scalable.

Font phase closeout workflow (M9i):

```powershell
dotnet test Copeland.slnx
dotnet build Copeland.slnx --no-restore
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\font-current -Preset cad-debug -TextBackend DirectOutlineStatic -GridStep 8 -ShowUnitLabels -ShowBounds -Clean
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\font-current -IncludeDirectOutlineRenderBridgeProof
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\font-current\presenter-direct-outline.png -IncludeDirectOutlineRenderBridgeProof
.\tools\Write-MachinaFontPhaseCloseoutManifest.ps1 -OutputDir artifacts\m9i
```

Canonical closeout artifacts:

- `artifacts/m9i/component-gallery-direct-outline-render-bridge-proof.png`
- `artifacts/m9i/presenter-direct-outline-render-bridge-proof.png`
- `artifacts/m9i/font-phase-closeout-manifest.json`
- `artifacts/m9i/font-phase-closeout-manifest.txt`

M9i is still proof-only. It adds an opt-in presenter proof through `DirectOutlineStaticTextRenderBridge`, keeps `DirectOutlineStatic` as the static/reference path, keeps MSDF explicit experimental/scalable, keeps browser kerning out of the oracle role, defers word wrapping and production integration, and leaves production UI text defaults unchanged.

M10a begins presenter organization work after that closeout. The new presenter shell is opt-in and sample-local:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10a\presenter-navigation-shell-overview.png -IncludeNavigationShell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10a\presenter-navigation-shell-components.png -IncludeNavigationShell -NavigationPage components.controls
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10a\presenter-navigation-shell-text.png -IncludeNavigationShell -NavigationPage text.direct-outline-static -IncludeDirectOutlineRenderBridgeProof
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10a\presenter-navigation-shell-scrolled.png -IncludeNavigationShell -NavigationPage components.controls -ScrollPage components.controls:120
```

Representative M10a outputs:

- `artifacts/m10a/presenter-navigation-shell-overview.png`
- `artifacts/m10a/presenter-navigation-shell-components.png`
- `artifacts/m10a/presenter-navigation-shell-text.png`
- `artifacts/m10a/presenter-navigation-shell-scrolled.png`
- `artifacts/m10a/presenter-navigation-shell-manifest.json`
- `artifacts/m10a/presenter-navigation-shell-manifest.txt`

This milestone adds sidebar section navigation, tabs local to the selected sidebar item, a scrollable page viewport, and deterministic sample-local scrollbar visuals. It does not resume font work or change production UI text defaults.

M10b layers interaction wiring onto that shell while keeping Avalonia sample-scoped:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10b\presenter-navigation-interaction-overview.png -IncludeNavigationShell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10b\presenter-navigation-interaction-components-selected.png -IncludeNavigationShell -SelectedSection components
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10b\presenter-navigation-interaction-tab-selected.png -IncludeNavigationShell -SelectedSection components -SelectedTab controls
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10b\presenter-navigation-interaction-scrolled.png -IncludeNavigationShell -SelectedSection components -SelectedTab controls -ScrollPage components.controls:120
```

Representative M10b outputs:

- `artifacts/m10b/presenter-navigation-interaction-overview.png`
- `artifacts/m10b/presenter-navigation-interaction-components-selected.png`
- `artifacts/m10b/presenter-navigation-interaction-tab-selected.png`
- `artifacts/m10b/presenter-navigation-interaction-scrolled.png`
- `artifacts/m10b/presenter-navigation-interaction-manifest.json`
- `artifacts/m10b/presenter-navigation-interaction-manifest.txt`

This is still proof/sample-level input work. Avalonia remains only the current sample input backend, navigation state/actions remain backend-independent, M9 font work stays closed unless a concrete integration need appears, and no production renderer behavior changed.

M10c turns that shell into the canonical presenter sample surface:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10c\presenter-shell-overview.png
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10c\presenter-shell-components-controls.png -SelectedSection components -SelectedTab controls
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10c\presenter-shell-text-direct-outline.png -SelectedSection text -SelectedTab direct-outline -IncludeDirectOutlineRenderBridgeProof
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10c\presenter-shell-diagnostics-layout.png -SelectedSection diagnostics -SelectedTab layout
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10c\presenter-shell-legacy-m1e-card.png -SelectedSection legacy -SelectedTab m1e-card
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10c\presenter-shell-scrolled.png -SelectedSection components -SelectedTab controls -ScrollPage components.controls:120
```

Representative M10c outputs:

- `artifacts/m10c/presenter-shell-overview.png`
- `artifacts/m10c/presenter-shell-components-controls.png`
- `artifacts/m10c/presenter-shell-text-direct-outline.png`
- `artifacts/m10c/presenter-shell-diagnostics-layout.png`
- `artifacts/m10c/presenter-shell-legacy-m1e-card.png`
- `artifacts/m10c/presenter-shell-scrolled.png`
- `artifacts/m10c/presenter-shell-manifest.json`
- `artifacts/m10c/presenter-shell-manifest.txt`

M10c makes the navigation shell the default presenter run/export surface, preserves the old M1e card as a `Legacy` page, keeps M9 font work closed, does not add new component families, and does not change production renderer/core/layout behavior.

M10d stabilizes that existing shell:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10d\presenter-stabilized-components-controls.png -SelectedSection components -SelectedTab controls
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10d\presenter-stabilized-components-controls-scrolled.png -SelectedSection components -SelectedTab controls -ScrollPage components.controls:344
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10d\presenter-stabilized-text-current.png -SelectedSection text -SelectedTab current
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10d\presenter-stabilized-text-direct-outline.png -SelectedSection text -SelectedTab direct-outline -IncludeDirectOutlineRenderBridgeProof
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10d\presenter-stabilized-text-proofs.png -SelectedSection text -SelectedTab proofs -IncludeDirectOutlineRenderBridgeProof
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10d\presenter-stabilized-diagnostics-layout.png -SelectedSection diagnostics -SelectedTab layout
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10d\presenter-stabilized-legacy-m1e-card.png -SelectedSection legacy -SelectedTab m1e-card
```

Representative M10d outputs:

- `artifacts/m10d/presenter-stabilized-components-controls.png`
- `artifacts/m10d/presenter-stabilized-components-controls-scrolled.png`
- `artifacts/m10d/presenter-stabilized-text-current.png`
- `artifacts/m10d/presenter-stabilized-text-direct-outline.png`
- `artifacts/m10d/presenter-stabilized-text-proofs.png`
- `artifacts/m10d/presenter-stabilized-diagnostics-layout.png`
- `artifacts/m10d/presenter-stabilized-legacy-m1e-card.png`
- `artifacts/m10d/presenter-stabilization-manifest.json`
- `artifacts/m10d/presenter-stabilization-manifest.txt`

This remains stabilization-only: no new component families, no resumed font-phase work, and no production renderer/core/layout behavior change.

M11a adds the first static Oblivion workbench-card proof on top of that shell:

```text
Oblivion:
  notebook/card/workbench layer

Visionary:
  future code editor/source workspace layer
```

Representative M11a exports:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11a\presenter-oblivion-cards.png -SelectedSection oblivion -SelectedTab cards
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11a\presenter-oblivion-execution-roadmap.png -SelectedSection oblivion -SelectedTab execution-roadmap
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11a\presenter-oblivion-artifacts.png -SelectedSection oblivion -SelectedTab artifacts
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11a\presenter-oblivion-scrolled.png -SelectedSection oblivion -SelectedTab cards -ScrollPage oblivion.cards:220
```

Representative M11a outputs:

- `artifacts/m11a/presenter-oblivion-cards.png`
- `artifacts/m11a/presenter-oblivion-execution-roadmap.png`
- `artifacts/m11a/presenter-oblivion-artifacts.png`
- `artifacts/m11a/presenter-oblivion-scrolled.png`
- `artifacts/m11a/oblivion-card-model-manifest.json`
- `artifacts/m11a/oblivion-card-model-manifest.txt`

M11a is static proof only. It adds no Roslyn execution, no xUnit execution runtime, no markdown editor, no Visionary editor behavior, and it keeps the M9 font phase closed while M10 remains the shell host.

M11c refactors presenter scrollbar interaction and scroll rendering before more Oblivion work continues:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11c\presenter-scrollbar-state-machine-components.png -SelectedSection components -SelectedTab controls
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11c\presenter-scrollbar-state-machine-scrolled.png -SelectedSection components -SelectedTab controls -ScrollPage components.controls:344
```

Representative M11c outputs:

- `artifacts/m11c/presenter-scrollbar-state-machine-components.png`
- `artifacts/m11c/presenter-scrollbar-state-machine-scrolled.png`
- `artifacts/m11c/presenter-scrollbar-state-machine-manifest.json`
- `artifacts/m11c/presenter-scrollbar-state-machine-manifest.txt`

M11c is scrollbar/input/composition refactor only. Dominatus orchestration ladder guidance is applied to interaction ownership and lifecycle clarity, Avalonia remains the current sample-only input backend, page scroll should not full-rerender page content or shell chrome, and `[Fact]` / `[Theory]` execution as notebook/runtime behavior remains deferred to M12+.

M11d then lands workspace persistence for Oblivion without adding execution:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11d\presenter-oblivion-workspace-cards.png -SelectedSection oblivion -SelectedTab cards
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11d\presenter-oblivion-workspace-roadmap.png -SelectedSection oblivion -SelectedTab execution-roadmap
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11d\presenter-oblivion-workspace-artifacts.png -SelectedSection oblivion -SelectedTab artifacts
```

Representative M11d outputs:

- `artifacts/m11d/presenter-oblivion-workspace-cards.png`
- `artifacts/m11d/presenter-oblivion-workspace-roadmap.png`
- `artifacts/m11d/presenter-oblivion-workspace-artifacts.png`
- `artifacts/m11d/oblivion-workspace-persistence-manifest.json`
- `artifacts/m11d/oblivion-workspace-persistence-manifest.txt`

M11d makes the root workspace graph/tree JSON and the card/page/artifact metadata TOML, following a `.sln` root plus `.csproj`-like asset analogy. It adds no Roslyn execution, no xUnit notebook/runtime execution, no markdown editor, and no Visionary editor behavior.

M11e then hardens presenter and Oblivion card layout authoring without reopening runtime work:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11e\presenter-card-hardening-oblivion-cards.png -SelectedSection oblivion -SelectedTab cards
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11e\presenter-card-hardening-oblivion-roadmap.png -SelectedSection oblivion -SelectedTab execution-roadmap
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11e\presenter-card-hardening-components-controls.png -SelectedSection components -SelectedTab controls
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11e\presenter-card-hardening-components-controls-bottom-scroll.png -SelectedSection components -SelectedTab controls -ScrollPage components.controls:9999
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11e\presenter-card-hardening-legacy-m1e-card.png -SelectedSection legacy -SelectedTab m1e-card
```

Representative M11e outputs:

- `artifacts/m11e/presenter-card-hardening-oblivion-cards.png`
- `artifacts/m11e/presenter-card-hardening-oblivion-roadmap.png`
- `artifacts/m11e/presenter-card-hardening-components-controls.png`
- `artifacts/m11e/presenter-card-hardening-components-controls-bottom-scroll.png`
- `artifacts/m11e/presenter-card-hardening-legacy-m1e-card.png`
- `artifacts/m11e/presenter-card-hardening-manifest.json`
- `artifacts/m11e/presenter-card-hardening-manifest.txt`

M11e is bug-fixing and authoring-hardening only. Presenter/Oblivion cards now derive body geometry from shared card-layout helpers, hosted legacy content no longer paints a full-width dark body background, overflowing pages keep the scrollbar thumb clamped inside the visible track, JSON/TOML persistence stays unchanged, and Roslyn plus notebook/runtime `[Fact]` / `[Theory]` execution remain deferred.

M11g then closes out the current Oblivion substrate and explicitly shifts the next phase to Markdown-first document cards instead of code execution:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11g\presenter-oblivion-closeout-status.png -SelectedSection oblivion -SelectedTab cards -SelectedCard oblivion-substrate-status
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11g\presenter-oblivion-markdown-roadmap.png -SelectedSection oblivion -SelectedTab execution-roadmap -SelectedCard markdown-first-roadmap
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11g\presenter-oblivion-execution-deferred.png -SelectedSection oblivion -SelectedTab execution-roadmap -SelectedCard execution-deferred
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11g\presenter-oblivion-visionary-future.png -SelectedSection oblivion -SelectedTab execution-roadmap -SelectedCard visionary-future
```

Representative M11g outputs:

- `artifacts/m11g/presenter-oblivion-closeout-status.png`
- `artifacts/m11g/presenter-oblivion-markdown-roadmap.png`
- `artifacts/m11g/presenter-oblivion-execution-deferred.png`
- `artifacts/m11g/presenter-oblivion-visionary-future.png`
- `artifacts/m11g/oblivion-phase-closeout-manifest.json`
- `artifacts/m11g/oblivion-phase-closeout-manifest.txt`

M11g adds no Roslyn execution, no `[Fact]` / `[Theory]` runtime, no Markdown editor, no full Markdown renderer, and no Visionary editor. M11 now closes as a static persisted-card substrate, M12 should focus on Markdown document/card support, and trusted local C# execution is deferred to M13+ or later unless explicitly re-prioritized.

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

Current browser-vs-Machina diff-overlay workflow:

```powershell
.\tools\Export-MachinaFontReferenceDiff.ps1 -OutputDir artifacts\m8r
```

Current M8r outputs include:

- `artifacts/m8r/browser-machina.png`
- `artifacts/m8r/machina-msdf-machina.png`
- `artifacts/m8r/overlay-machina.png`
- `artifacts/m8r/diff-machina.png`
- `artifacts/m8r/diff-threshold-machina.png`
- `artifacts/m8r/wireframe-machina.png`
- `artifacts/m8r/browser-hello-machina.png`
- `artifacts/m8r/overlay-hello-machina.png`
- `artifacts/m8r/diff-hello-machina.png`
- `artifacts/m8r/wireframe-hello-machina.png`
- `artifacts/m8r/browser-kerning.png`
- `artifacts/m8r/overlay-kerning.png`
- `artifacts/m8r/diff-kerning.png`
- `artifacts/m8r/wireframe-kerning.png`
- `artifacts/m8r/diff-report.txt`
- `artifacts/m8r/diff-report.json`

M8r is diagnostic tooling only. It adds direct overlays, threshold/absolute diff artifacts, wireframes, and structured metrics without changing the proof renderer contract or any production text path.

Current three-way browser/direct-outline/MSDF shape-diff workflow:

```powershell
.\tools\Export-MachinaFontShapeDiff.ps1 -OutputDir artifacts\m8s
```

Current M8s outputs include:

- `artifacts/m8s/32/browser-machina.png`
- `artifacts/m8s/32/direct-outline-machina.png`
- `artifacts/m8s/32/msdf-machina.png`
- `artifacts/m8s/32/diff-browser-vs-direct-machina.png`
- `artifacts/m8s/32/diff-direct-vs-msdf-machina.png`
- `artifacts/m8s/32/diff-browser-vs-msdf-machina.png`
- `artifacts/m8s/32/overlay-three-way-machina.png`
- `artifacts/m8s/32/wireframe-machina.png`
- `artifacts/m8s/48/...`
- `artifacts/m8s/64/...`
- `artifacts/m8s/shape-diff-report.txt`
- `artifacts/m8s/shape-diff-report.json`

M8s is diagnostic tooling only. It adds a direct-outline raster oracle, multi-size mask metrics, and three-way overlays without changing MSDF sampling, baseline placement, kerning behavior, or any production text path.

Current consolidated M9d font-toolkit workflow:

```powershell
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9d -Preset cad-debug -GridStep 8 -ShowUnitLabels -ShowBounds -Clean
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\m9d-msdf -Preset msdf-debug -GridStep 8 -ShowUnitLabels -ShowBounds -Clean
```

Current M9d outputs include:

- `artifacts/m9d/32/direct-outline-hello-machina.png`
- `artifacts/m9d/32/m9d-cad-debug-hello-machina.png`
- `artifacts/m9d/32/m9d-direct-vs-msdf-hello-machina.png`
- `artifacts/m9d/shape-diff-report.txt`
- `artifacts/m9d/shape-diff-report.json`
- `artifacts/m9d/font-diagnostic-export-manifest.txt`
- `artifacts/m9d/font-diagnostic-export-manifest.json`

M9d is still diagnostic tooling only. It formalizes direct-outline as the default static/UI-text proof backend, keeps MSDF explicit as scalable/experimental, and does not change production UI text behavior or attempt an MSDF fix.

Current M9f alignment-repair workflow:

```powershell
.\tools\Export-MachinaMsdfAlignmentRepairM9f.ps1 -OutputDir artifacts\m9f -Clean
```

Current M9f outputs include:

- `artifacts/m9f/m9f-direct-vs-msdf-hello-machina.png`
- `artifacts/m9f/m9f-direct-vs-msdf-machina.png`
- `artifacts/m9f/m9f-direct-vs-msdf-settings.png`
- `artifacts/m9f/m9f-before-after-direct-vs-msdf-hello-machina.png`
- `artifacts/m9f/shape-diff-report.txt`
- `artifacts/m9f/font-diagnostic-export-manifest.txt`
- `artifacts/m9f/msdf-alignment-report.txt`

M9f is still proof/tooling-only. It keeps `DirectOutlineStatic` as the geometry oracle, repairs MSDF-side alignment in the explicit experimental/scalable path, keeps browser kerning out of the target contract, and does not change production UI text behavior.

M8q.2 remains the baseline-guide overlay pass that preceded M8r. Its current proof export is:

```powershell
.\tools\Export-MachinaFontReferenceComparison.ps1 -OutputDir artifacts\m8q2
```

Current M8q.2 outputs include:

- `artifacts/m8q2/browser-text-metrics.json`
- `artifacts/m8q2/reference-machina.png`
- `artifacts/m8q2/reference-hello-machina.png`
- `artifacts/m8q2/reference-kerning.png`
- `artifacts/m8q2/machina-msdf-machina.ppm`
- `artifacts/m8q2/machina-msdf-machina.png`
- `artifacts/m8q2/compare-machina.png`
- `artifacts/m8q2/glyph-placement-report.txt`
- `artifacts/m8q2/glyph-placement-report.json`

M8q.2 keeps the work proof-only. It adds a red baseline-guide overlay to the browser oracle, Machina proof, compare artifacts, and gallery proof export so vertical mismatch is easier to inspect. It is a tooling upgrade, not another rendering fix, and no production text path changed.

## Reference source

`reference/dominatus` is a reference-only Git submodule for source inspection. Active Copeland and Machina builds continue to use the NuGet `Dominatus.Core` and `Dominatus.OptFlow` `0.4.0` packages.
