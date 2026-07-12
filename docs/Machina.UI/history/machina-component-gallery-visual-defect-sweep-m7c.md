# Machina Component Gallery Visual Defect Sweep M7c

## Purpose

M7c uses the deterministic Machina component gallery export from M7b as a local visual ruler.

The goal is to inspect the default and interactive PNG artifacts, classify visible issues, fix only small shared defects with evidence, and leave broader renderer/text limitations clearly deferred.

M7d follow-up note:

- the deferred badge intrinsic-size / label-placement defect from this sweep is now fixed locally in `docs/Machina.UI/history/machina-badge-intrinsic-layout-m7d.md`

## Export commands

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m7c
```

No manual fallback command path was needed in this sweep because the M7b export script completed successfully.

## Initial artifacts inspected

- `artifacts/m7c/component-gallery-default.png`
- `artifacts/m7c/component-gallery-interactive.png`

Inspection method:

- local deterministic export through `.\tools\Export-MachinaComponentGallery.ps1`
- visual inspection of both PNGs in Codex image view

## Findings

### B. Defer

- Badge text in `StandardUI.Badge` sits too close to the top edge of the shell in the current gallery export.
  Investigation showed that small local badge-layout tweaks quickly collide with current auto-sized layout behavior and can overflow the badge row itself. This needs a cleaner intrinsic-size/layout follow-up instead of a brittle local patch.
- Primitive uppercase bitmap text remains visually coarse for long captions and body copy.
  This is acceptable for now, but improving lowercase fidelity, shaping, or richer font behavior would require broader renderer/text backend work.
- Inline code and rich text styling remain limited by the current raster text bridge.
  The gallery is readable enough to audit geometry and contrast, but stylistic fidelity is not an M7c fix target.

### C. Gallery-only adjustment

- No gallery-only card or section size adjustment was required in this sweep.

### D. No issue

- Default and interactive exports are visibly different in the intended probes.
- Checkbox checked marks are visible in both default and themed probes.
- Switch on/off states are visually distinct.
- Input placeholder/value states are readable and not unexpectedly clipped.
- Theme probe contrast is acceptable for the current palette.
- No unsupported glyph fallback (`?`) was observed in the exported gallery.
- No section overlap or clipping was observed at the current gallery dimensions.

## Fixes applied

- No code fix was merged in M7c.
- The gallery export contract from M7b was preserved.
- The sweep isolated one visible badge defect, but local fixes did not converge without breaking badge-row layout; the issue is deferred explicitly instead of hidden by sample-only changes.

## Deferred issues

- Broader primitive text readability limits in the current raster renderer.
- Richer inline text styling fidelity.
- Any future migration of button/checkbox/switch/input labels to richer text surfaces.
- Pixel-diff automation and scroll/resize-oriented gallery UX.

## Final artifacts inspected

- `artifacts/m7c/component-gallery-default.png`
- `artifacts/m7c/component-gallery-interactive.png`

The final inspection reconfirmed the badge-label top-edge issue while also confirming that the rest of the gallery remains readable, non-overlapping, and contract-stable under the M7b export path.

Historical note after M7d:

- the badge issue documented here is resolved by a Badge-local intrinsic sizing + label-region contract
- M7d preserves the rest of this sweep's deferrals and does not change general layout semantics

Historical note after M7e:

- the gallery remains stable enough to keep using as the canonical local workbench
- current residual roughness is captured in `docs/Machina.UI/history/machina-component-gallery-known-limitations-m7e.md` instead of being treated as active M7c defects

## Validation results

Validation executed after the fix:

- gallery export script
- targeted gallery and Standard tests during implementation
- full requested solution build/test/boundary validation

See the task close-out for the exact commands and pass/fail status.

## Conclusion

M7c completed as a focused visual audit and triage pass.

The gallery export workflow remained stable, both artifacts were inspected, and the most obvious remaining badge defect was isolated with concrete evidence. Because small local fixes did not converge cleanly, the sweep ends in an honest documented stop rather than forcing a brittle patch.
