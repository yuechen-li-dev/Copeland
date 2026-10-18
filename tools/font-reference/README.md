# Machina font reference workflows

These PowerShell entry points keep two separate font-reference workflows. Both
run their own named diagnostics test and share browser discovery, DOM capture,
URI encoding, query construction, and JSON extraction from
`FontReference.Common.ps1`.

```powershell
./tools/Export-MachinaFontReferenceDiff.ps1
./tools/Export-MachinaFontReferenceComparison.ps1
```

The diff workflow runs `ReferenceDiffWorkflow_ScriptWorkflowExportsArtifacts`
and writes its artifacts to `artifacts/local/font-reference/diff` by default.
The comparison workflow runs
`FontReferenceOracleWorkflow_ScriptWorkflowExportsArtifacts` and writes its
artifacts to `artifacts/local/font-reference/comparison`. These tests and
output sets remain separate because they qualify different evidence. Each
script accepts `-OutputDir`, `-Configuration`, and `-BrowserPath`.

Browser launches capture standard output and error concurrently and stop the
browser process tree after 30 seconds. Nonzero exits and timeouts include the
captured output in the diagnostic.

Both workflows still emit Machina proof artifacts when no compatible headless
browser is installed; browser reference images and metrics then require manual
capture as described in the generated instructions. The browser reference uses
`src/Aurelian/Aurelian.Assets/Defaults/Fonts/CrimsonText-Regular.ttf`; each
runner checks that canonical input before launching the browser.

The subprocess/timeout and payload-format regression cases run without a browser:

```powershell
./tests/Repository/Test-FontReferenceTools.ps1
```

The two export commands above qualify the real browser/font/Machina paths and
require the .NET 10 SDK and a compatible browser for automated comparison.
