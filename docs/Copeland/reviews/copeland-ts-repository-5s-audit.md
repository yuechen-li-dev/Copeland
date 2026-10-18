# Copeland TS repository 5S audit

Audited 2026-10-17 at `f9339ee948fa82cd5b6ac53c06033f5c1551f5c5`.
The working tree was clean before the initial audit. That audit changed
navigation and recorded the cleanup sequence without deleting files. Execution
of the first cleanup pass is recorded below. For current contributor guidance,
use [repository maintenance](../repository-maintenance.md).

## Findings

The most useful first cleanup is tracked scratch material and generated delivery
payloads. Moving production compiler directories is lower priority: their current
subsystem ownership is already visible. Several older samples remain dependencies
of current applications and cannot safely be removed by age or milestone name.

All sizes below are logical file bytes, reported in MiB/GiB, not allocated disk
space. Tracked sizes describe the current checkout, not all Git history. Local
measurements exclude `.git` and skip symbolic links.

| Area | Tracked files | MiB | Meaning |
| --- | ---: | ---: | --- |
| Whole repository | 8107 | 518.75 | Context only; most bulk is outside this cleanup scope |
| `artifacts/` | 2274 | 390.98 | Mixed subsystem evidence and generated output; requires classification |
| `src/Copeland/` | 290 | 5.13 | Active compiler/tooling ownership |
| `tests/Copeland/` | 772 | 2.50 | Regression sources, fixtures, and pinned output |
| `samples/copeland-ts/` | 535 | 12.72 | Applications, proofs, historical specimens, and generated payloads |
| `docs/Copeland/` | 523 | 29.81 | References plus evidence and an npm package archive |
| `.tmp/` | 375 | 5.17 | Tracked scratch code, copied compiler, scripts, and intermediate output |

There are also approximately **61.12 GiB of untracked/ignored local files** across
the repository; **6.89 GiB** is under the three Copeland source/test/sample roots.
The whole-repository figure includes about 29,939 MiB under
`artifacts/local/models` and 12,889 MiB under `artifacts/local/trellis2`. Those are
outside this Copeland TS pass; they must not be swept up as disposable TS output.

Git reports 587.47 MiB of packs and 31.25 MiB of loose objects. Removing tracked
files from a future commit will improve the checkout but will not remove their
historical blobs. No history rewrite or garbage collection is proposed here.

### 1. Tracked scratch code is the clearest removal candidate

- `.tmp/m26-baseline/` contains **217 files / 4.15 MiB**, including 213 files
  below its copied `src/` tree. This creates another searchable compiler tree.
- `.tmp/` contains **42 Python scripts**, including source mutation/pinning
  scripts such as `m26-final-fixes.py`, plus intermediate source/emission probes
  and `m26-baseline.zip`. These are development-session machinery, not the
  maintained compiler or regression suite.
- Root `copeland-bench-fixes.patch` contains an eight-file compiler/test patch.
  No tracked consumer was found outside the scratch material. The maintained
  source/tests should own accepted changes, rather than a patch at repository root.
- `.work/consolidate-games.ps1` is another tracked migration script, but belongs
  to the game consolidation lane and is outside this cleanup's write scope.

Recommended disposition: remove the Copeland scratch tree and root patch from
the current tree, add scoped scratch ignores, and retain the real M26 corpus,
benchmark inputs, maintained tooling, and measured results. Use this audit's Git
revision to recover the exact removed files if historical reconstruction is
needed. Do not move an entire copied compiler into another searchable archive
directory. Check for unique regression specimens before removing the probes.

### 2. Generated packages and published applications are mixed into source/docs

`samples/copeland-ts/browser-wasm-m0/publish/` has **46 tracked files / 11.39 MiB**:
.NET runtime WASM, compressed alternatives, JavaScript, data, and generated HTML.
The sample has authored project/source files and documents its `dotnet publish`
command. Keep those inputs and the discovery record; remove the delivery tree
only after recreating and exercising the browser bridge. Add a fixture-local
publish ignore. This is not evidence that the WASM implementation is obsolete.

Two tracked archives both identify as `@copeland/tscl` version
`0.1.0-preview.1`:

| Location | MiB | SHA-256 |
| --- | ---: | --- |
| `docs/Copeland/copeland-tscl-0.1.0-preview.1.tgz` | 7.33 | `1b8862b2eda3b3dee1eda240cd3e3bccf61d2161643684425a5ec60f8b9a830f` |
| `artifacts/releases/0.1.0-preview.1/npm/copeland-tscl-0.1.0-preview.1.tgz` | 7.33 | `3570392a8a6b7386a5e9873a851eb6eec3b498dba0e118c6b1e6427fafbe1ac7` |

These are **not identical copies**: 12 of their 76 file members differ,
including the launcher, compiler assemblies, and LICENSE. Do not select one
merely by its filename or location. Resolve release provenance against the
release manifest before removing either archive or changing distribution claims.
Keep release hashes/manifests in Git and delivery payloads in a release/artifact
store once their retention is established. Public registry contents were not
queried in this local audit.

`outputs/<conversation-id>/` contains the café Excel comparison workbook,
inspection data, and two previews: four files / 0.27 MiB. These are motivating
comparison evidence, not arbitrary disposable output. Give them a stable,
documented evidence location when doing the evidence pass; preserve the workbook.

### 3. Evidence is useful, but repeated generated source is expensive

`docs/Copeland/reviews/evidence/` accounts for **241 files / 20.34 MiB**.
Several `compiler-report.json` files are 1.5–2.33 MiB each. For example,
`mixed-islands-m10/react-regression/compiler-report.json` contains 55 results
with full generated JavaScript, alongside diagnostics and source hashes. Later
milestones repeat earlier regression output under new evidence roots.

For future runs, retain result expectations, diagnostics, source/compiler hashes,
output hashes, versions, commands, and small outcome summaries. Put verbose
generated source in ignored qualification output. Keep exact emitted source where
it is an intentional executable golden fixture under `tests/Copeland`.

Do not mass-trim historical JSON without checking its consumers and preserving
the original artifact's provenance. Screenshot evidence of a native/browser
claim also has a different purpose from a rebuildable assembly or source dump.

### 4. Old samples still provide current inputs

The M15 café compiler harness imports M12's workbook, M13's projection, and
M14's price policy/editor. M13–M15 runners also materialize the browser runtime
through `react-friction-m0`, with a compatibility input from
`copeland-website-m0/runtime/browser-v1.js`. Deleting those older samples would
break the latest qualification path.

Three runners (`tablescript-application-m13`, `tablescript-editing-m14`, and
`tablescript-rows-m15`) have byte-identical `run.ps1` and `tooling/server.ts`
files. This is a good candidate for shared qualification plumbing, while leaving
each sample's domain source and assertions independent. Runtime semantics stay
with the existing browser runtime; do not create another runtime to simplify tests.

`table-dogfood-m0-fresh-d87e6471ea394fd4a27ca6db79964644` is a seven-file copied
sample with no external tracked path reference found. Its workbook, consumer,
and README differ from the maintained `table-dogfood-m0`; four other files match.
Treat it as a retirement candidate, not a byte-identical duplicate. Migrate any
unique assertions into the maintained corpus before removing it.

Likewise, `tools/copeland-tablescript-{ellipsis-audit,row-authoring,key-edits,scenarios,typed-summaries}`
contain assertions, invalid-input checks, and/or explicit C#/JS probes. They are
referenced by milestone reviews. Classify each check against current tests, then
consolidate useful execution into a maintained qualification entry point. A file
called `qualify.ps1` is not automatically a one-off patch script.

### 5. Local caches should have explicit lifetimes

The VS Code extension's ignored `.vscode-test/` tree alone is **3940 MiB**, with
three downloaded editor versions and many timestamped user-data directories.
It is the clearest local TS disk-space candidate. Preserve any needed failure
logs, verify no extension host is using it, then remove old test profiles/builds
through an explicit path list and rerun the relevant extension integration test.

Sample dependency directories are another substantial, reproducible cache:
M12's `node_modules` is 326.58 MiB; the M13/M14/M15 trees are each about
55.62 MiB. Keep package manifests and locks. Do not consolidate package managers
or change dependency versions just to reclaim these directories.

A dry run of `git clean -ndX` restricted to the extension `.vscode-test` and
M15 `node_modules` reports exactly those two directories. No removal was run.
Never use repository-wide `git clean -fdx` as this project's cleanup procedure:
ignored directories also hold model inputs, evidence, and expensive toolchains.

## Repository checks need repair before a large move

These were executed in the initial audit, not inferred from old reports.
The topology and release-closure failures are repaired in the first pass below;
the cross-system ownership and artifact-policy work remain separate.

| Check | Initial audit result | Required bounded repair |
| --- | --- | --- |
| `Validate-CopelandTsTopology.ps1` | Fails while scanning `artifacts/local/vd-wgsl-x0`: an absolute reference is joined to the local project's directory | Discover maintained projects from the tracked/declared graph, exclude generated/scratch projects, and resolve absolute references correctly |
| `Test-CopelandReleaseClosure.ps1` | Fails on missing `Copeland.SpanAllocation` solution membership | Add the actual package-root closure; inspection also finds missing `Copeland.Profile` |
| `Validate-DependencyBoundaries.ps1` | Fails with 11 reported edges | Reconcile actual ownership with the doctrine; do not blanket-allow all cross-system references |
| `Test-ArtifactBudget.ps1` with three out-of-place payload paths | Passes after examining **zero** artifact files | Cover prohibited scratch/generated payload locations as well as `artifacts/`, with narrow fixture exceptions |

The three reported production boundary edges are Aurelian.Shaders → Copeland.TS,
Machina.Fonts → Copeland.Profile, and Machina.Presentation →
Copeland.SpanAllocation. The other eight are Aurelian-owned tests referencing
integration projects. These are architectural decisions, not safe dead-code
deletions. Keep their resolution separate from the Copeland TS housekeeping pass.

The artifact-budget probe supplied `.tmp/m26-baseline.zip`, the docs npm archive,
and the sample's published WASM runtime. Its path filter considers only
`artifacts/*`, so it does not prevent these forms of sprawl. The existing guard
should be extended rather than adding an unrelated parallel checker.

## Five bounded passes

| Pass | 5S purpose | Scope | Acceptance |
| --- | --- | --- | --- |
| 1 (complete) | Sort | Copeland tracked scratch/patch removal, scoped ignores; repair topology discovery and release closure | No copied compiler/scratch mutation scripts in maintained source; full Copeland build/tests and the two repaired guards pass; retained M26 corpus works |
| 2 | Set in order | Index current examples and classify qualification tools; consolidate the three identical browser runners; retire the fresh-copy sample after porting unique checks | M15 and affected sample qualifications still run from declared source inputs; no runtime ownership changes |
| 3 | Shine | Regenerate WASM publish output, settle package provenance, remove delivery payloads from tracked source/docs; prune explicitly selected TS caches | Fresh regeneration/browser or extension proof succeeds; release hashes remain attributable; measured before/after bytes |
| 4 | Standardize | Compact future evidence; give comparison inputs stable locations; extend the existing artifact policy | Small evidence retains assertions/provenance; fixtures and intentional binaries still work; guard rejects misplaced payload examples |
| 5 | Sustain | Make relevant guards part of routine validation; document owners and regeneration commands | A normal contribution has one obvious source, test, sample, and evidence destination; checks report only the remaining explicit architectural issues |

Do these as separate reviewable changes. Moving every milestone into a new
directory hierarchy at once would create link churn before lifecycle and
dependency ownership are clear. No production experiment is certified dead by
this audit; removing one requires its unique behavior to be retained or explicitly
retired.

## Navigation changes in this pass

The root README now has three primary sections: Copeland compiler with Copeland
TS first, Machina.UI for C#, and Aurelian application runtime. Empty headings and
the obsolete blanket claim that React integration does not exist were removed.
Language development capability is distinguished from preview release scope.
The Copeland documentation entry point now links file profiles, application
tables, atomic row changes, the latest café sample, and this audit.

## Validation

Raw inventory and local command logs are under the ignored
`artifacts/local/copeland-ts-5s-audit/`. No inventory tool or one-off source
mutation script was added to the maintained tree. The inventory used tracked Git
paths, filesystem sizes, project references, text references, byte comparisons,
and package-member hashes; absence of a text reference alone does not prove code
unreachable. No external repository was modified.

`dotnet build Copeland.slnx -m:1 --nologo` passes with zero warnings and errors.
`dotnet test Copeland.slnx --no-build -m:1 --nologo` passes all **2463 tests**
with zero failures or skips. All 60 local Markdown link targets in the three
changed documents exist, and the root README has exactly the three requested
primary sections. `git diff --check` passes. No browser, native runtime, or
package-publication qualification was rerun for these documentation-only edits.
Frozen Zig suites were skipped because no legacy Zig compiler or build/test
infrastructure changed.

At the initial audit, topology, release-closure, and dependency-boundary checks
failed as recorded above. The artifact-budget probe exited successfully but
demonstrated a coverage gap. These are initial observations, not the current
first-pass status below.

## First cleanup pass

The first pass removes **376 tracked scratch/patch files, 5,450,550 logical
bytes**: the 375 `.tmp/m26-*` files and root `copeland-bench-fixes.patch`.
The maintained 68-case instinct corpus, benchmark inputs/tool, baseline harness
evidence, and documented baseline reconstruction at commit
`0bffd10afe0f9e5f3a5c1281e7713d799f5c0d3c` remain. The five named diagnostic
probes were matched to their existing corpus cases before removal. This reverses
active source duplication without pretending to shrink historical Git storage.
Root `.tmp/`, `tmp/`, and the retired patch path now have scoped ignores.

The active `CtsJitM0` project is now `tools/Copeland.Benchmarks.Jit`, including
its namespace, assembly, workload registry, generated host, tests, and solution
references. Solutions show it under `/tools/`, not `/src/` or `/product/`.
Its default output is `artifacts/local/copeland-jit-benchmarks`. The compiler's
`StaticEvaluationLimits.M1` default is now `StaticEvaluationLimits.Default`;
all six numeric limits and their behavior are unchanged. Historical reports
retain historical names. Existing wire/protocol identifiers retain their bytes.

The topology guard now discovers maintained tracked/new unignored inputs and
their Copeland project-reference closure. It accepts absolute references and
ignores local build/scratch projects. Reconciliation also removed stale rules:
the compiler's explicit service dependencies and frontend-colocated backend
Manifest contracts are documented in the current topology; TSX fixture spelling
is recognized; ordinary array `.map` is not mistaken for a source-map file;
and the runtime TSON API law examines emitted encoders rather than banning
unrelated host-side JSON/reflection or explicit browser JSON boundaries.
MIR isolation, source/fixture ownership, missing references, cycles, unsupported
backend dependencies, and forbidden tooling/runtime APIs remain checked.

`Copeland.Release.slnx` now includes its existing Profile and SpanAllocation
dependencies. The release-closure guard also resolves absolute references
correctly. Neither guard suppresses the separate cross-system ownership check.

Current maintenance and compiler-topology pages now provide source destinations,
ordinary build/check commands, and explicit qualification boundaries. The older
JTF-M6c topology page is marked historical and links to the maintained page.
The benchmark tool has its own executable quick-start documentation.

An additional attempt to remove obsolete ignored build-cache directories
`tools/CtsJitM0` and `.tmp/m26-baseline` was rejected by automatic approval review
with "blocked by policy." Those ignored caches were left in place; no alternative
deletion method or repository-wide cleanup was attempted. They are excluded from
maintained source discovery. No cache-space reclamation is claimed.

Qualification logs and the removed-path/hash manifest are local ignored output
under `artifacts/local/copeland-ts-5s-pass1/`.

First-pass validation:

- Full `Copeland.slnx` build: zero warnings/errors; all **2463 tests pass**,
  with zero failures/skips. `Copeland.Release.slnx` also builds with zero
  warnings/errors after the solution membership/folder changes.
- Retained instinct corpus: **68 cases pass** through its existing runtime and
  diagnostic assertions. No golden-output or corpus expectation was relaxed.
- Both repaired guards pass. Four completed graph probes accept a valid absolute
  reference, reject a missing reference and a cycle, and pass maintained-input
  discovery with existing ignored local projects present.
- The renamed JIT tool completes all five workloads with one cold run, one warmup,
  and one measured round, agreeing between C# and Symbolic JavaScript. Checksums:
  numeric `294727`, layout subset `859540320`, reducer `1000001`, records
  `999766`, strings `12248977`. All five workload source files are byte-identical
  to their originals. This is execution evidence, not new performance evidence.
- All **74 local link targets** in the nine changed/new Markdown documents exist;
  `git diff --check` passes. No external repository, package publication,
  browser/native qualification, or legacy Zig lane was involved.

Additional temporary API/release-failure probes were also rejected by automatic
approval review with "blocked by policy" before execution. They were not retried
or counted as verification. The four completed graph probes and ordinary
compiler/runtime tests above are the actual evidence.

## Tools and artifact cleanup

The user removed both obsolete ignored build-cache trees after the first pass;
this pass verified `tools/CtsJitM0` and `.tmp/m26-baseline` absent. The untracked
user-created `cleanup.ps1` was preserved.

The five TableScript tools are now one maintained
[qualification suite](../../../tools/Copeland.Qualification.TableScript/README.md).
Shared CLI invocation, timeout, result/error assertions, source-preserving
rejection checks, and probe-project setup have one owner. Five independently
selectable cases retain authoring/CSV, keyed edits, initializer forms, typed
summaries, and snapshot-relative calculation checks. The sibling-repository
ellipsis source inspection was retired as historical research; bounded invalid
initializer cases remain in the compiler tests. Stale unexecuted JavaScript
"unsupported" fields were removed from new result generation, while original
historical summaries retain their exact observations.

`Copeland.CtsOptM2.Measure` is now the maintained
[emission benchmark](../../../tools/Copeland.Benchmarks.Emission/README.md).
It compares current compiler output with reachability off/on, writes local
measurements, and uses a unique generated workspace. The two earlier
`cts-opt-m1-*.ps1` characterization scripts were retired: one measured frozen
JavaScript, and the other used synthetic table shapes/source-literal payload
estimates. Their original protocols remain historical, not a claim that the new
tool repeats the same dimension sweep. The
[tool index](../../../tools/README.md) distinguishes maintained entry points from
historical/domain evidence producers.

Seven raw TableScript bundles, **223 files / 657391 bytes**, were removed after
retaining original summaries and per-file paths, byte lengths, hashes and recovery
commit in [compact provenance](evidence/tablescript-retired-artifacts/provenance.json).
The café and optional Excel builder now default to ignored local output instead
of overwriting tracked historical runs. The original Excel comparison and native
or browser captures were preserved. Historical reviews link the current runner
and retention record instead of pretending their old output paths are current.

The same-version npm conflict is also resolved locally. The existing release
manifest/checksums pin the release-folder archive. The unreferenced docs archive
has a different hash and 12 differing members, so that **7685016-byte** misplaced
copy was removed. [Package provenance](evidence/package-provenance/npm-preview-conflict.json)
retains both identities, the manifest hash, and recovery commit. Release payloads
and their original unpublished/not-publicly-verified status were preserved; no
new publication or package qualification is claimed.

The existing artifact guard now covers changed files (including modifications
and renames), preserves leading dots when normalizing paths, rejects traversal,
and checks prohibited scratch/cache/local-output and misplaced Copeland delivery
paths. Compact budgets also cover Copeland review evidence. Fourteen maintained
policy cases are wired into the existing artifact-budget CI workflow. This
replaces the earlier zero-file blind spot without adding a second policy engine.

Current instructions and remaining work are in
[repository maintenance](../repository-maintenance.md) and
[artifact retention](../reference/artifact-retention.md). Validation logs are
ignored under `artifacts/local/copeland-tools-cleanup/`; final results follow.

Tools/artifacts validation:

- Full `Copeland.slnx` build: zero warnings/errors; **2463 tests pass**, with
  zero failures/skips. Compiler/language semantics and golden expectations were
  not changed.
- Consolidated TableScript suite: **372 checks** across five suites. The canonical
  café runner also passes its **66 checks** with default local output, preserving
  the 31210/12710-cent baseline/edit results and authored workbook hash.
- Emission benchmark: all four current fixtures have identical Node output with
  reachability disabled/enabled. This run emits 96244 versus 86091 total bytes,
  removing 16 generated definitions. These are local observations, not universal
  performance claims or C# execution evidence.
- Artifact policy: **14 cases pass**, including rejection cases; both retained
  provenance records and the complete changed/new path set pass the guard.
  Topology and preview release closure also pass. Remote CI was wired but not run.
- All **98 local link targets** in the 15 changed/new Markdown documents resolve;
  `git diff --check` passes. The Excel builder's path-only edit passes Node syntax
  checking; workbook generation, browser/native execution, package publication,
  and the unchanged legacy Zig lane were not rerun.

The remaining ownership violations and other subsystems' evidence/tools were not
silently reclassified or deleted. This completes the TableScript/tooling artifact
slice, not every later cleanup pass in the initial audit.
