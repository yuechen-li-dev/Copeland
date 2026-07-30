# CTS project-context unification M0

## Outcome

The website dogfood context is now a reusable compiler-visible project context,
rather than a TSPack-only input. `CopelandProjectContext` reopens the resolved
TSPack request descriptor, validates its project-relative sources, restores npm
and configured browser contracts, and creates immutable compiler snapshots.
Its `Fingerprint` is a stable semantic projection of source texts/logical
identities, runtime and TSX profile, and package-contract state.

> A Copeland project has one compiler-visible world. Build, inspection, tables,
> layout tooling, and the language server are different operations over that
> same world.

> TSPack owns browser materialization and lifecycle. It does not own a private
> version of Copeland’s module graph.

> Layout inspection must not become correct by knowing special facts about
> React. It becomes correct by compiling the actual project.

## Context and snapshot boundary

TSPack still resolves `manifest.tsx`, packages, targets, and browser build
state. Its existing `.tspack/build-manifests/*.request.json` is now the
canonical materialized compiler-context descriptor. `tscl build` consumes it
through `CopelandProjectContext`, and CLI/LSP consumers reopen that same
descriptor without invoking TSPack, starting a browser, or installing packages.

`CopelandProjectContext.CreateSnapshot(overlays)` applies an immutable map of
open-document text over the resolved source set. An overlay can change imports
and normal semantic binding re-runs against the fixed context; it cannot invent
packages, backends, aliases, or compiler options. Manifest or descriptor write
times invalidate the LSP context and rebuild the snapshot.

## Discovery and CLI selection

The normal forms are:

```console
tscl table list --project ./manifest.tsx
tscl table rows layout::Boxes --project ./manifest.tsx --format json
tscl layout inspect CopelandDesktop --project ./manifest.tsx --json
```

`--source ./src/App.tsx` discovers `manifest.tsx` upward. If a manifest is
found, the selected materialized context must include the source; the command
does not fall back to naked scanning. `--project` may also name a specific
resolved `.request.json` when a manifest intentionally has several targets.
Projects without a manifest retain source-only inspection, with no injected npm
or browser/CLR contracts.

Diagnostics are stable for missing/unsupported project inputs, missing
materialized context state, source exclusion, ambiguous context selection, and
conflicting source/project manifests. They include the relevant path and a
TSPack materialization remediation where applicable.

## Website proof

The real `copeland-website-m0` context is inspectable by ordinary commands.
The projected tables report 3 layouts, 37 boxes, 30 bindings, 12 bounded
collection items, 8 derivations, and 45 source records. Desktop, Tablet, and
Mobile roots resolve without React/package unresolved-symbol failures. Their
bindings refer to the same component symbols as the TSPack build; derivation
rows retain `CenterXIn`, `ExpandFrom`, `PlaceRightOf`, and `PlaceBelow` facts.

The table-list and all profile-inspect JSON envelopes report the identical
fingerprint:

```text
fe897dfce7442f462d62b34a95602c8dce3b0c9a0bb09894601ed6814e9d14e8
```

The browser proof from CTS-WEBSITE-TABLE-LAYOUT-M0 remains the realization
correlate: projected `semanticPath` values are emitted as neutral semantic hosts
and asserted by Desktop/Tablet/Mobile rectangle scenarios. No positional class
identity is used.

## LSP behavior

When its workspace root discovers `manifest.tsx`, the language server uses the
same context rather than generated MSBuild ownership metadata. It derives its
owned Copeland documents from context sources, preserves React/browser package
contracts, and rebuilds an overlay snapshot on open/change/close. Existing
hover, completion, definition, stream/layout summaries, and semantic tokens
therefore operate over the normal project graph. Existing MSBuild workspace
loading remains the fallback for non-manifest projects.

## Deliberate limits

The descriptor is materialization state, not a replacement manifest parser.
Read-only inspection does not perform implicit TSPack sync/install. A manifest
with multiple target descriptors requires a target-specific descriptor unless a
supplied source selects exactly one. Persistent cross-process caches and a
general monorepo resolver remain out of scope; the in-process LSP cache is
invalidated by manifest and descriptor directory changes.

## Validation

Focused CLI and LSP protocol tests cover explicit project selection, source
discovery, deterministic fingerprint emission, React stream binding, and an
unsaved invalid overlay. The website has additionally been exercised through
`table list`, `layout::Layouts`, `layout::Boxes`, and `layout inspect` for all
three profiles with the materialized website context. `dotnet test
Copeland.slnx --configuration Debug --no-restore` passed (all 1,556 reported
tests), as did the TSPack website build, scenario suite, browser rectangle
proof, and `git diff --check` in both repositories.
