# Copeland TS generated artifacts and contracts

Artifacts are projections, not semantic owners. Determinism means the same
resolved project graph and compiler inputs produce byte-stable ordered output
apart from explicitly external tool output.

| Artifact | Producer | Consumer | Contract/version | Source truth | Inspection/lifecycle |
| --- | --- | --- | --- | --- | --- |
| Cope MIR text (`.cope`) | MIR lowerer/writer | tests, diagnostics | textual projection, not source input | bound/MIR | `tscl compile --emit mir` |
| generated C# | C# backend | .NET compiler/runtime | backend format | Cope MIR | backend corpus/runtime tests |
| generated JavaScript | JS backend | Node/browser launcher | emission profile | Cope MIR | backend corpus/runtime tests |
| layout CSS | layout JS projection | browser | generated stylesheet | normalized layout facts | browser build |
| `attachments.json` | `AttachmentPlanArtifactEmitter` | TSPack/browser runtime | **schema v1** | `HostAttachmentMir` | SHA-256 in browser materialization |
| `component-frames.js` | `ComponentFrameArtifactEmitter` | TSPack browser runtime | default-exported component-frame envelope **schema v1** | bound state/presentation facts | fixed executor registration at browser start |
| resolved project descriptor | TSPack | compiler/CLI/LSP | descriptor shape, fingerprint | manifest resolution | `.tspack/build-manifests` |
| `browser-materialization.json` | TSPack | diagnostics/tooling | schema v1 | resolved packages + artifact hashes | browser output |
| projected JSON table envelope | CLI | users/scripts | read-only relation schema | bound normalized facts | `tscl table` |
| templates/distribution assets | template compiler/package projects | new workspace | template contract | template source | templates tooling |

## Compatibility rules

`attachments.json` is the only currently formal browser semantic transport
schema. Breaking changes require a new schema version, validation in both
producer and TSPack, and a compatibility policy. It must never transport
absolute paths, DOM nodes, React roots, generated source, or arbitrary bound
objects.

`component-frames.js` is now a versioned V1 default-exported data envelope.
Copeland emits component/frame/event/transition/branch/attachment meaning;
TSPack executes it through one fixed browser runtime executor. Existing
unversioned side-effect registration modules remain a bounded compatibility
bridge, but new builds do not install executable transition or projection
closures.

The generated browser host is not itself source-of-truth. TSPack's canonical
runtime source is `cmd/tspack/runtime/browser-v1/index.js`; Go materializes and
configures it without separately implementing browser lifecycle semantics.
Generated `dist/` files are disposable materializations.
