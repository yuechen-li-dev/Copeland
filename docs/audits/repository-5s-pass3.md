# Repository-wide 5S pass 3

This pass follows the Copeland-focused tools/artifacts and samples cleanup.
Three Luna agents covered Machina.UI, Aurelian and repository tools; the parent
handled integration, retention, documentation and serial execution. The current
[maintenance guide](../repo/maintenance.md), [retention policy](../repo/artifact-retention.md)
and [documentation index](../INDEX.md) are the continuing user-facing entry points.
This record describes this pass, not a claim that all repository debt is removed.

## Consolidated owners

| Former duplication | Owner and preserved boundary |
| --- | --- |
| Independent tooling and font-test PNG encoders | Internal `Machina.Fonts.ReferenceRendering.RgbaPngWriter`; tooling and the existing test assembly have explicit friend access |
| Browser/path/query/JSON/process helpers in two font exporters | `tools/font-reference/FontReference.Common.ps1`; two thin workflows retain their distinct diagnostics filters and output sets |
| General output policy repeated inside Copeland guidance | `docs/repo/artifact-retention.md`; the Copeland page retains its domain-specific evidence and recovery details |

The PNG extraction preserves RGBA8 format, row order, unfiltered scanlines,
NoCompression zlib and CRC handling, with checked buffer sizing. Two compiled
regressions protect RGBA/alpha and row order plus existing invalid-input failures.
The class stays internal; the friend relationship adds no public API.

Font exporters now default to ignored `artifacts/local/font-reference/{diff,comparison}`.
Their browser helper drains stdout/stderr concurrently, uses argument vectors,
has a finite 30-second timeout, kills the process tree on timeout and includes
captured output in failure diagnostics. Seven maintained PowerShell regressions
cover output/error capture, nonzero exit, timeout, canonical/missing font inputs, query escaping and both JSON
payloads, and run in the existing hygiene CI workflow.

Real browser execution exposed a stale font path shared by both scripts. They
now resolve the existing canonical `Aurelian.Assets/Defaults/Fonts` source rather
than the removed test fixture copy. Missing input or missing rendered JSON now
reports the expected font/URL/output element and captured browser stderr.

Visual inspection of the real exporter also exposed a separate harness defect:
`ReferenceDiffWorkflow_ScriptWorkflowExportsArtifacts` overwrote the supplied
browser capture JSON with synthetic rectangles. The script now preserves and
consumes its supplied capture file, asserts byte preservation, and uses the
synthetic fixture only when running standalone without an external capture.
Both paths were executed. The corrected comparison shows the actual browser
kerning text alongside Machina text, rather than a synthetic white block.

## Retirements and preservation

- `.work/consolidate-games.ps1` was a spent one-off migration: its old source
  directories are gone, every mapped destination exists, and no active workflow
  invokes it. The script is removed and root `.work/` is ignored. The existing
  scratch guard already rejects reintroduction; root user-authored `cleanup.ps1`
  is preserved.
- Unused `ResourcePoolDiagnostic` and `ResourcePoolDiagnosticCodes` definitions
  were removed. Searches found only their definitions and original historical
  inventory; live resource-pool creation/reuse/fence/telemetry assertions remain.
  The orphan constants also reused `AGR1001/AGR1002`, which active render-pass
  diagnostics already own. Those active codes and behavior are unchanged.
  The removed types were public: this intentionally retires that unused source
  surface and does not promise compatibility for unknown external callers.
- Two unreferenced VN alpha-authoring buffers, **7,864,320 bytes**, were removed.
  RGBA bytes exactly match the retained original `rei-soft.png`; alpha bytes
  exactly match the retained finished `rei-soft-cutout.png`. The
  [retirement record](../../artifacts/repo-hygiene/retired-alpha-intermediates.json)
  pins both buffer hashes, retained PNG hashes, dimensions, exact reconstruction
  method and recovery commit. Git recovery was independently hash-checked.
  All asset sources, native captures, original prompts/manifests and save
  witnesses remain. The retired intermediate directory is now ignored.

The old Oblivion cleanup policy and footprint are explicitly historical.
`JointTaskForce.slnx` is documented accurately as a selected cross-system lane,
not the union of every project in all subsystem solutions. Old timing targets
are labeled historical rather than presented as current measurements.

## Remaining ownership findings

The existing dependency guard still reports **11** previously present edges.
No exceptions were added and the guard was not weakened:

- Production: `Aurelian.Shaders -> Copeland.TS`,
  `Machina.Fonts -> Copeland.Profile`, and
  `Machina.Presentation -> Copeland.SpanAllocation`.
- Eight adapter references in Aurelian-owned Audio (2), Cloth3D (2), Effects2D (1),
  GameHost (1) and Inference (2) test projects belong to integration-owned coverage.

Those require explicit production-boundary design or splitting mixed test
projects; directory moves alone would not resolve authority. They remain a
concrete follow-up rather than being hidden by this cleanup. Broad renaming of
milestone suffixes, model/toolchain/cache removal, release payload relocation and
native/GPU/model requalification are outside this pass.

## Validation

All .NET build/test commands were serial. Full logs are under ignored
`artifacts/local/repository-5s-pass3/`; default exporter outputs are under
`artifacts/local/font-reference/`. Counts below are per command and overlap.

| Path | Actual result |
| --- | --- |
| `Machina.UI.slnx` | Build succeeds; 756 tests pass, zero failures/skips, including both new PNG cases |
| `Copeland.slnx` | Build succeeds with zero warnings/errors; 2464 tests pass, zero failures/skips |
| `Aurelian.Graphics.Tests` | Build succeeds; 291 tests pass, zero failures/skips |
| `Machina.Fonts.Tooling.Tests` | Build succeeds; 13 tests pass, zero failures/skips |
| `JointTaskForce.slnx` | Build succeeds with zero warnings/errors; initial full run: 4532 pass, one missing-Naga failure, zero skips |
| Corrected shader project | Existing pinned installer installs Naga 27.0.0 locally; all 378 shader tests pass with `AURELIAN_NAGA` set |
| Real font comparison and diff scripts | Both default entry points pass their distinct diagnostics case with Chrome 155.0.8059.84 and canonical source font; five browser fixtures each |
| Standalone diff harness | Its synthetic fallback case passes independently without supplied browser input |

The failed shader project's rerun resolves the single cross-system test failure;
the full cross-system solution was not repeated afterward. The current lane's
external toolchain prerequisite is now documented; no test was silently skipped.
The first Machina build reports 14 warnings in unchanged third-party OpenFont
source. That frozen source was not edited or suppressed. An idle MSBuild node
initially locked compiler task assemblies; `dotnet build-server shutdown` and
serial `-nr:false` builds resolved that environment issue.

**Full Aurelian qualification is blocked at restore:** 19 projects report
`NU1902` for transitive MessagePack 3.1.8 from the external Deliverance project.
The owning chain is `Aurelian.GameSaves -> ../Deliverance/src/Deliverance.Core`.
No audit suppression, arbitrary consumer override or sibling-repository source
edit was introduced. The changed Graphics project's direct build/tests pass;
that does not establish a passing complete `Aurelian.slnx` build.

Topology and preview release closure pass. All 14 artifact-policy cases and
seven font-helper cases pass; the latter are wired to CI, but remote CI was not
run. Changed/new paths pass the compact artifact guard. All 50 newly exported
PNGs pass independent PNG validation; comparison and corrected kerning captures
were visually inspected. Both workflow reports retain a real browser path and
all five actual fixture captures/metrics. Chrome is the qualified channel here;
an Edge probe returned empty DOM on this machine and is not claimed qualified.

All 122 local Markdown targets in changed/new documents resolve, and whitespace
checks pass. No native/GPU execution, model inference, release publication or other
framework/browser qualification is inferred from these build and exporter results.
