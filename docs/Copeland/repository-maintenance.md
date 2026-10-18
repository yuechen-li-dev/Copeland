# Maintaining the Copeland tree

Start with the [compiler topology](architecture/compiler-topology.md) for source
ownership and build commands. The [5S audit](reviews/copeland-ts-repository-5s-audit.md)
records the initial inventory and remaining cleanup passes; its measurements
describe that audit's checkout, not current disk usage.

## Where work belongs

| Material | Destination and lifetime |
| --- | --- |
| Compiler/tooling behavior | `src/Copeland`, with regressions under `tests/Copeland` |
| Necessary executable goldens | The owning test corpus, paired with source and exact assertions |
| Reusable application source | `libraries`; samples consume the existing owner |
| A maintained application or integration specimen | `samples/copeland-ts`, with a documented entry point and prerequisites |
| Maintained qualification or measurement program | `tools`, named for its responsibility, with a runnable command |
| Current user/maintainer guidance | The relevant language, tooling, or architecture page linked from the documentation entry point |
| Historical milestone decisions/results | `docs/Copeland/reviews`, `history`, or the existing historical record |
| Scratch edits, copied baselines, generated hosts, verbose logs | `artifacts/local/<purpose>` or ignored root `.tmp`; never maintained source |

Name production types, defaults, tools, and directories by what they do.
Milestone identifiers belong in historical records. Existing serialized protocol
or vocabulary identifiers are compatibility contracts: changing them requires
a versioning/migration decision, not a cosmetic search-and-replace.

Root `.tmp/` and `tmp/` are ignored. They are not test corpora, and copied source
trees there are not another compiler owner. The retired root
`copeland-bench-fixes.patch` is also ignored to prevent reintroduction. Accepted
changes live in source/tests; Git history retains the old patch and scratch files.
The original M26 baseline's revision and reconstruction procedure remain in the
[instinct proof documentation](../../tools/copeland-typescript-instinct-m26/README.md).

Use the [maintained tool index](../../tools/README.md) for executable qualification
and benchmark entry points. [Artifact retention](reference/artifact-retention.md)
defines what remains in Git and what belongs in local output.

## Validate a change

```powershell
dotnet build Copeland.slnx -m:1
dotnet test Copeland.slnx --no-build -m:1
pwsh -NoProfile -File tools/Validate-CopelandTsTopology.ps1
pwsh -NoProfile -File tools/Test-CopelandReleaseClosure.ps1
pwsh -NoProfile -File tests/Repository/Test-ArtifactBudget.ps1
git diff --check
```

Run builds and tests sequentially to avoid replacing assemblies under an active
test runner. Also run the affected sample/tool through its documented entry point
when changing its layout or wiring. The release publishing workflow has further
package checks; the commands above do not certify a publication.

Do not delete a fixture merely because its name contains an old milestone.
Current TableScript applications reuse older workbook/editor sources and browser
materialization inputs. Trace consumers, preserve unique assertions, and run the
affected qualifications before retiring a specimen.

## Local output and remaining cleanup

Cleanup must name exact repository-local paths and distinguish generated output
from expensive inputs or unique evidence. Use a dry run first. Repository-wide
`git clean -fdx` is unsuitable: ignored directories also contain ML model inputs,
toolchains, browser evidence, and unrelated subsystem work.

The first pass removed tracked Copeland scratch files, repaired topology/release
closure checks, and normalized active benchmark/default names. The tools/artifacts
pass consolidates TableScript qualification, normalizes the current emission
benchmark, retires earlier characterization scripts, and replaces raw TableScript bundles
with compact recovery provenance. It also resolves the same-version npm conflict
against the release manifest and removes the mismatching delivery copy from docs.
Both obsolete ignored build directories from
the first pass have since been removed by the user and verified absent.

Published WASM payloads, remaining release delivery payloads, other verbose
historical evidence, browser-runner consolidation, and old extension caches remain
separate bounded passes. Remaining milestone wording in user-facing diagnostics needs a
separate pass that preserves diagnostic codes, useful repair text, and asserted
behavior. Cross-system ownership findings remain with the architecture lane.
