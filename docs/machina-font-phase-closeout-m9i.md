# Machina Font Phase Closeout M9i

## Current status

M9i is the last proof-integration and closeout-hygiene step for the current Machina font phase.

- `DirectOutlineStatic` is the current static/reference path.
- the presenter sample now has an opt-in direct-outline render-bridge proof
- the component gallery proof remains opt-in
- MSDF remains explicit experimental/scalable after the M9f repair
- production UI text defaults remain unchanged

## Golden path

Use `DirectOutlineStatic` when the goal is deterministic static/reference proof output.

That path is now covered through:

- raw direct-outline renderer
- `DirectOutlineTextBoxLayout`
- `DirectOutlineStaticTextRenderBridge`
- component gallery opt-in proof
- presenter sample opt-in proof

Browser kerning is not the oracle.

## What to run

Build and test:

```powershell
dotnet test Copeland.slnx
dotnet build Copeland.slnx --no-restore
```

Canonical direct-outline diagnostics:

```powershell
.\tools\Export-MachinaFontDiagnostics.ps1 -OutputDir artifacts\font-current -Preset cad-debug -TextBackend DirectOutlineStatic -GridStep 8 -ShowUnitLabels -ShowBounds -Clean
```

Canonical component gallery proof:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\font-current -IncludeDirectOutlineRenderBridgeProof
```

Canonical presenter proof:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\font-current\presenter-direct-outline.png -IncludeDirectOutlineRenderBridgeProof
```

Canonical closeout manifest:

```powershell
.\tools\Write-MachinaFontPhaseCloseoutManifest.ps1 -OutputDir artifacts\m9i
```

Done means:

- targeted tests/build pass
- component gallery proof exports
- presenter proof exports
- closeout manifest is written
- no production renderer default changed

## Canonical artifacts

- `artifacts/m9i/component-gallery-direct-outline-render-bridge-proof.png`
- `artifacts/m9i/presenter-direct-outline-render-bridge-proof.png`
- `artifacts/m9i/font-phase-closeout-manifest.json`
- `artifacts/m9i/font-phase-closeout-manifest.txt`

The PNGs are local proof artifacts.
The manifest files are the lightweight checked-in closeout record.

## DirectOutlineStatic status

`DirectOutlineStatic` is good enough to stop on for the current phase unless a concrete production UI integration need appears.

- static/reference backend: yes
- bridge exercised: yes
- text-box layout exercised: yes
- presenter proof exercised: yes
- default production renderer switched: no

## MSDF status

MSDF is structurally repaired enough for proof comparison after M9f, but it remains explicit experimental/scalable.

- default path: no
- comparison path: yes
- production integration: deferred
- browser parity oracle: no

## Font toolkit status

The toolkit is now mainly a proof and diagnostic surface.

- canonical commands are documented
- canonical artifact locations are documented
- manifest/status output is documented
- no new production dependency on `Machina.Fonts.Tooling` was introduced

## Presenter proof status

The presenter sample now has an opt-in `DirectOutlineStatic Presenter Proof` card.

- enabled by `--include-direct-outline-render-bridge-proof`
- default presenter behavior remains unchanged
- proof text is rendered through `DirectOutlineStaticTextRenderBridge`
- deterministic PNG export is available through `.\tools\Export-MachinaPresenter.ps1`

## What is intentionally not solved yet

- word wrapping
- production renderer integration
- caller-positioned baseline anchors
- MSDF coverage/reconstruction polish
- MSDF smoothing/polish work
- browser-based oracle work

## Deferred work

- decide a real production integration point only when a concrete UI need exists
- add wrapping/truncation policy as a separate milestone
- add baseline-anchor input only when control integration needs it
- continue MSDF polish only if scalable text becomes an active requirement

## Next non-font work recommendation

Stop expanding the font subsystem for now.

The next useful work should be non-font product integration or UI capability work that consumes the existing direct-outline proof seams only if needed.
