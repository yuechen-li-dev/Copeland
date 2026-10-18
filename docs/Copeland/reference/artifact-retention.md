# Artifact retention

Source and necessary regression fixtures belong in Git. Generated delivery
payloads and repeatable diagnostic output need a different lifetime. The
[tool index](../../../tools/README.md) identifies maintained producers and their
entry points; [repository maintenance](../repository-maintenance.md) explains
source ownership and the normal verification sequence.

## Retention rules

| Material | Retention |
| --- | --- |
| Authored source and executable golden fixtures | Keep in the owning source/test tree with meaningful assertions |
| Qualification result needed to explain a historical claim | Keep a compact summary, compiler/source identity, command/protocol, and relevant versions |
| Verbose CLI JSON, copied scenarios, generated C#/JS/MIR, build logs, probe projects | Regenerate under ignored `artifacts/local/<purpose>`; preserve a failed run locally when useful |
| Release packages and published application trees | Use release/artifact storage; Git retains source and reviewed hashes/manifests |
| A screenshot or workbook necessary to substantiate a claim | Keep an explicitly owned evidence artifact; do not confuse it with a disposable assembly or log |
| Model weights, toolchains, and unrelated subsystem inputs | Preserve their owner and acquisition cost; a local cleanup cannot classify them as temporary by directory name |

Historical evidence is not current capability. For example, old TableScript
summaries reported JavaScript as unsupported; current compiler/browser
qualification has a different boundary. Retain old observations as historical
data rather than silently editing them into new claims.

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

## Enforced policy

`tools/Test-ArtifactBudget.ps1` checks added, copied, modified, and renamed paths
against a base commit or the index. `-Paths` accepts repository-relative paths
for an explicit review. It rejects path traversal, scratch trees, build/package
caches, tracked `artifacts/local` output, and misplaced Copeland delivery
payloads under source, docs, and samples.

Compact budgets apply to `artifacts/<purpose>` and
`docs/Copeland/reviews/evidence/<purpose>`: normally at most 16 changed files per
purpose and 256 KiB per file, using the allowed compact text formats. Intentional
golden fixtures retain their existing extension exception. Necessary exceptions
must be narrow, reviewed entries in `.github/artifact-budget-allowlist.txt`.
Unchanged historical bundles are not automatically deleted or certified by this
change-oriented check.

From the repository root:

```powershell
./tests/Repository/Test-ArtifactBudget.ps1
./tools/Test-ArtifactBudget.ps1 -BaseRef <review-base>
```

The policy tests run in the artifact-budget CI workflow. They cover compact
evidence, explicit goldens, rejected generated source/payload/cache/scratch paths,
path traversal, missing artifacts, size/count limits, and ordinary source/binary
test-fixture eligibility. This extends the existing guard rather than adding a
second artifact-policy implementation.
