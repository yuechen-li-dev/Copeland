# Repository artifact retention

Source and necessary regression fixtures belong in Git. Repeatable diagnostics,
shipping payloads and authoring intermediates have different lifetimes. This
policy applies across Copeland, Machina.UI, Aurelian and their integrations.
See [repository maintenance](maintenance.md) for ownership and qualification.

| Material | Retention |
| --- | --- |
| Authored source and executable regression fixtures | Keep with the owning source/test project and meaningful assertions |
| Evidence needed to explain a historical claim | Keep compact results, source/compiler identity, protocol and versions |
| Screenshots, workbooks and save witnesses needed for a claim | Preserve explicitly owned evidence; do not delete by extension or age |
| Verbose CLI output, generated C#/JS/MIR, logs and probe hosts | Regenerate under ignored `artifacts/local/<purpose>` |
| Release packages and published application trees | Release/artifact storage; Git keeps reviewed identities and provenance |
| Intermediate raw pixels, copied assets or converted temporary inputs | Retire only after proving reconstruction or identifying the retained authority |
| Model weights, toolchains and third-party inputs | Preserve the owner, acquisition cost and licensing/provenance boundaries |

Historical evidence is not current capability. Do not overwrite an older report
with a new run or change its recorded limitations. A compact retirement record
must identify the recovery commit, original paths, byte lengths and SHA-256
hashes. When reconstruction is exact, name the retained source and method too.
Git recovery depends on keeping the referenced history; a hash manifest is not an
independent backup.

The [VN pixel-intermediate record](../../artifacts/repo-hygiene/retired-alpha-intermediates.json)
records two unreferenced raw authoring buffers exactly reconstructible from the
retained original/finished PNG assets. Native captures, asset prompts/hashes and
save witnesses remain intact. The
[TableScript retention record](../Copeland/reference/artifact-retention.md)
covers retired raw compiler bundles and a conflicting npm delivery copy.

## Enforced boundary

`tools/Test-ArtifactBudget.ps1` checks added, copied, modified and renamed paths.
It rejects traversal, scratch trees, caches, tracked local output and misplaced
Copeland delivery payloads. Compact budgets apply to `artifacts/<purpose>` and
`docs/Copeland/reviews/evidence/<purpose>`: normally at most 16 changed files per
purpose, 256 KiB per file, and compact text formats. Executable goldens have the
existing exception; other necessary exceptions must be narrow reviewed entries
in `.github/artifact-budget-allowlist.txt`.

This is a change-oriented guard. Unchanged historical bundles are neither
silently deleted nor certified. It is not a comprehensive binary-payload policy
for every subsystem source directory. Current explicit captures remain valuable;
new generated evidence should default to local output and be deliberately
promoted only when needed to substantiate a claim.

```powershell
./tests/Repository/Test-ArtifactBudget.ps1
./tools/Test-ArtifactBudget.ps1 -BaseRef <review-base>
```

Use exact repository-local targets for local cleanup and inspect them first.
Repository-wide `git clean -fdx` can destroy inputs and evidence unrelated to the
task. Never retry a policy-rejected deletion through a different tool or shell.
