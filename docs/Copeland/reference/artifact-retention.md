# Artifact retention

Source and necessary regression fixtures belong in Git. Generated delivery
payloads and repeatable diagnostic output need a different lifetime. The
[tool index](../../../tools/README.md) identifies maintained producers and their
entry points; [repository maintenance](../repository-maintenance.md) explains
source ownership and the normal verification sequence.

General retention and enforcement rules have one repository-wide owner in
[repository artifact retention](../../repo/artifact-retention.md). This page
retains the Copeland-specific output and recovery details.

## TableScript output

The maintained [TableScript suite](../../../tools/Copeland.Qualification.TableScript/README.md)
writes detailed output and `summary.json` under
`artifacts/local/tablescript-qualification/`. The café comparison runner writes to
`artifacts/local/tablescript-cafe/`; its optional Excel builder writes beneath
that local output tree. The original checked-in Excel comparison is preserved.

Retired raw bundles are represented by
[historical provenance](../reviews/evidence/tablescript-retired-artifacts/provenance.json):
original summaries, file paths, byte lengths, SHA-256 hashes, and the Git commit
that contains every original file. Recover one with:

```powershell
git show <sourceCommit>:<originalRoot>/<relativeFilePath>
```

Recover into local qualification output when needed, not another tracked copied
compiler/probe tree. Recovery relies on preserving the referenced Git history;
the provenance file is not an independent backup of all raw bytes.

The [npm preview conflict record](../reviews/evidence/package-provenance/npm-preview-conflict.json)
documents a separate delivery cleanup: the retained release-folder archive matches
the existing release manifest/checksums; the unreferenced docs-folder archive did
not. Its hash and differing members remain recoverable from Git. This establishes
local artifact ownership only. It does not change the manifest's unpublished and
not-publicly-verified status or requalify those older compiler binaries.

## Check a change

The existing guard and policy cases described in
[repository artifact retention](../../repo/artifact-retention.md#enforced-boundary)
run in artifact-budget CI. Copeland output paths above remain local; the compact
historical provenance records remain tracked.
