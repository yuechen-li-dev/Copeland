# CTS-MANIFEST-M1 TSPack manifest profile migration record

**Status:** complete bounded manifest semantic profile.

The frontend now preserves generic import and `export default` syntax wrappers needed by TSPack-style static documents. `CopelandProject.LoadRootManifest` is the exclusive profile-selection and project-loading seam: it resolves the root `manifest.tsx`, parses normal TS-XML, binds the manifest vocabulary, and returns a backend-neutral immutable `CopelandManifest`. File naming alone never selects a profile for ordinary compiler calls.

The implemented profile accepts TSPack's `tspack/manifest` import + restricted `const` + `define(<Workspace ...>)` shape, root/project elements, package build declarations, declarative run targets, structured JSON-like rows, and the established static helper family. The profile creates no React calls, host objects, runtime helper invocation, shell command, build action, process launch, or sidecar transport.

TSPack has no native sidecar declaration. M1 therefore preserves its existing root `Package`/`RunTargets` deployment-adjacent data with a stable logical binding identity and separate runtime/argv fields; dependency context rejects `RunTargets` to prevent authority escalation. Split package references are validated as data but not recursively loaded.

Focused fixtures prove positive binding, expression-valued fields, root project discovery, unknown/duplicate/nesting/type/path diagnostics with spans, restricted-expression rejection, dependency authority, and neutral non-manifest `.tsx`. Broad Copeland validation remains required after this record.

Deferred: split package loading/merge, package annotations, all executable TSPack behavior, lockfiles, materialization, sidecar/process transport, and CTS-SIDECAR-M1a.
