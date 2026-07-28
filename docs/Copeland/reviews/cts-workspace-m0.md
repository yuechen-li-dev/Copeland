# CTS-WORKSPACE-M0 review

## Outcome

M0 adds `tsconfig.tsx`, a restricted declarative workspace manifest and the
`tscl workspace` command group. It proves all-tsc minimum adoption, mixed
ownership, and Copeland-only ownership shapes. `tsconfig.tsx` is authoritative;
generated conventional configuration is never a peer source of truth.

## Laws implemented

- Every discovered TypeScript source has exactly one declared owner, `tsc` or
  `tscl`; strict workspaces reject unowned sources.
- Overlap, include/exclude contradiction, invalid/out-of-root globs, missing
  `tscl` projects, unsupported options, generated-path inclusion, and obvious
  direct cross-owner source imports produce stable `COPE-WORKSPACE-*`
  diagnostics.
- `sync` renders all artifacts before atomically replacing
  `obj/copeland/workspace`; unchanged content is not rewritten.
- Generated `tsconfig.generated.json` has an explicit tsc file list and
  workspace-relative option paths projected correctly from its `obj` location.
- Generated `tscl-files.generated.props` feeds explicit `CopelandCompile`
  items into the existing MSBuild seam. No second project system was added.

## Commands and artifacts

`tscl workspace validate`, `sync`, `status`, and `owner <path>` support text
and stable JSON results. `sync` produces `tsconfig.generated.json`,
`tscl-files.generated.props`, and `editor-ownership.generated.json` (schema
version 1) beneath `obj/copeland/workspace`.

## Proof and validation

`samples/copeland-ts/workspace-m0` keeps `src/legacy/**` on `tsc` and moves
`src/copeland/**` to `tscl`. Its generated config passed TypeScript 5.9.2, and
its generated MSBuild item import built `App.csproj` with the Copeland task.
Focused CLI tests cover deterministic no-op sync, path normalization, tsc/tscl
partitioning, machine-readable owner output, overlap failure, and strict
unowned failure.

## Additional work performed

- Added a small restricted object-literal parser because the existing TS-XML
  manifest binder is purpose-built for TSPack package/deployment manifests;
  sharing it would have created a second compiler-selection authority.
- Added a deterministic workspace glob resolver and atomic artifact publisher;
  these are required to make ownership inspectable and safe for automation.
- Added the sample project import of generated MSBuild items so the fixture
  proves the native `.csproj` path.

## Deferred consciously

M0 does not add an LSP/editor extension, root `tsconfig.json` overwrite or
import wizard, broad tsconfig-option support, a general cross-compiler build
planner/cycle solver, source migration, compatibility syntax, or TSPack manifest
merging. The recommended next step is an LSP/editor reader for
`editor-ownership.generated.json`, including authored-rule diagnostic spans.
