# CTS-SIDECAR-M0a: Octxiliary and runtime-partition audit

**Status:** completed documentation-only audit. No Oct or Copeland production code changed.

## Repository state and inspected inventory

| Repository | Revision / branch | Upstream divergence | Initial worktree | Evidence |
| --- | --- | --- | --- | --- |
| Copeland | `021d206d27b8b831cd71ebc441a19709e027b241` / `main` | `0 0` vs `origin/main` | clean | compiler, CLI, MIR, C#/JS backends, TSON, tests, docs, validators |
| Oct | `b07c8849efa00fe0455e827e9a162856f389878f` / `main` | `0 0` vs `origin/main` | clean and read only | current protocol/helper/tests, Rust binding/example, sidecar commands, `.octagon`, designs |

Exact Oct evidence: `internal/octxiliary/{protocol.go,protocol_test.go,rust-sdk/src/lib.rs}`, `pkg/octxiliary/{octxiliary.go,octxiliary_test.go}`, `examples/ChimeraOctxHello/rust-sidecar/src/main.rs`, `cmd/octxiliary-*/main.go`, `examples/Octagon/laser_experiment.octagon`, and `docs/internal/{octxiliary.md,octxiliary_transport_m12.md,octxiliary_handle_transport_m17.md,chimera_octx_rust_sdk_design_m1.md,uibridge_octagon_transport_recon_uib1.md}`. Go implementation/tests are current authority; Rust is binding evidence; designs/UIBridge are historical or proposed.

Copeland production evidence: `src/Copeland/Copeland.TS/{Compiler,Semantics,Lowering,Tson}`, `src/Copeland/Copeland.TS.Mir`, both `Copeland.TS.Backend.*`, and `src/Copeland/Copeland.Cli/Program.cs`. Test evidence: `tests/Copeland/Copeland.TS.Tests`, both backend test projects, CLI tests, `tools/Validate-CopelandTsTopology.ps1`, and `tools/Validate-DependencyBoundaries.ps1`.

## Behavioral comparison

| Concern | Current Octxiliary | Current Copeland | Recommended M1 |
| --- | --- | --- | --- |
| Contract | manifests/family/function strings | no sidecar declaration | module-owned compiler contract |
| Identity | ABI and names, no schema digest | `$schema`; local MIR IDs unstable | URI plus semantic digest |
| Codecs | hand-maintained generic value codecs | direct generated TSON encoders | generated schema-directed encode/decode plans |
| Dispatch | serial handler map | no interop surface | generated proxy/dispatcher, correlated requests |
| Framing | handshake + unbounded length-prefix text | none | bounded framing below schema semantics |
| Values | scalars, arrays, records, bytes, handles | nominal data/TSON/table carriers | closed DTO algebra; no handles/classes/callables |
| Failures | string error or process failure | Result and runtime invariant checks | declared, transport, terminal layers |
| Packaging | host-launched local subprocess | no launcher/manifest | CLR-owned Node child first, browser adapter later |

## Reuse, obsolete assumptions, gaps, and risks

Reuse Oct’s explicit runtime boundary, handshake-before-call, framing separation, direct typed traversal, and generated/binding dispatcher shape. Do not copy its bespoke unbounded parser, string-only error model, generic hand-maintained type union, serial-only semantics, opaque handles, legacy wrapper fields, or trusted-subprocess assumptions.

The decisive Copeland gap is intentional runtime-decoding absence. It can encode canonical TSON but has no runtime decoder/public `TsonValue`; a real two-way sidecar requires bounded decoding. Remaining gaps are CTS-ASYNC, partition/contract symbols, operation identities, schema plans, lifecycle/process runtime, manifest/stale-output policy, and browser trust handling.

| Risk | Mitigation |
| --- | --- |
| Pretending runtimes share objects | closed DTO algebra and generated codecs only |
| Recreating a language frontend | canonical generated-data decoder, never authored-document parser |
| Contract drift | one compiler generates both ends and handshake digest |
| Resource attack | reject bounded frames/values before allocation |
| Disposable synchronous API | CTS-ASYNC before sidecar M1 |
| Browser lifecycle hiding protocol defects | Node stdio proof first |
| Local IDs on wire | stable URI/schema digest only |

## Recommended implementation sequence

1. Ratify CTS-SIDECAR-M0a owner decisions.
2. Implement CTS-ASYNC once as the reusable computation/cancellation law.
3. Implement one CTS-SIDECAR-M1: CLR-to-Node framed stdio, generated proxy/dispatcher/codecs, bounded schema-directed canonical textual TSON decode, lifecycle, manifest, and end-to-end fixtures.
4. Add one browser/WebView transport adapter beneath unchanged contract/plans.

## Validation performed

Oct was inspected read-only. The intended final diff is documentation-only; no build/test run is required unless that changes. Final validation checks changed-file scope, local links/paths, Markdown tables/fences, UTF-8 without BOM/control characters/trailing whitespace, both PowerShell topology/dependency validators, and `git diff --check`.
