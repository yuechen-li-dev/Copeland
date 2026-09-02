# Current proof and artifact retention policy

Canonical playback is a small, stable behavioral regression suite for the current supported product surface. It is not a request to regenerate every screenshot produced by an earlier proof of concept.

## Retention law

| Evidence | Checked in | Regenerated | Authority |
|---|---:|---:|---|
| Source fixtures, assertions, and canonical suite definitions | yes | when the contract changes | current contract |
| Canonical reports and captures | no by default | on validation | current execution evidence |
| Milestone reports and screenshots under `artifacts/mXX/` | no | never by later milestones | compact index plus source/history docs |
| Superseded PoC scenarios | source may remain beside its history document | no | historical evidence only |
| Duplicate or transient build output | no | as needed locally | none |

The current Oblivion canonical command is:

```powershell
./tools/Invoke-OblivionCanonicalPlayback.ps1 -OutputDirectory artifacts/canonical-playback
```

It runs the real `Oblivion.Standalone` host and protects five deliberately small scenarios: Markdown Card, Diagram Card, Table Card, Function Card, and combined viewport/appearance behavior. The report is written exactly once to `<output>/playback-suite-report.json`.

The M15/M16 Presenter scenarios and `m16c-oblivion-playback-suite.machina-playback-suite.toml` remain discoverable as historical interaction evidence. They are not canonical current-product authority and must not be copied into future milestone bundles. Their former 14-scenario count is not preserved as a target.

Generated top-level milestone bundles were removed and are represented by one compact `artifacts/repo-hygiene/historical-artifact-index.json`. The index records the former directory, file count, byte count, and tracked-file count; milestone docs and source fixtures retain the explanation and reproduction path. `.gitignore` rejects future `artifacts/m*/` and derived-render cache sediment.

This cleanup removed 3,748 tracked generated artifact files plus ignored local build/proof output. The local `artifacts` footprint fell from 5,573,124,973 bytes to 35,434,542 bytes. Tracked removals remain recoverable from Git history; ignored generated outputs are reproducible rather than retained.

The report relocation special case for folders named `playback` was removed. Presenter suite reports now follow the same exact-output-directory law as the canonical Standalone report.
