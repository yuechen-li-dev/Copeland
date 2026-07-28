# Copeland M0 version compatibility

M0 uses a single release train, currently `0.1.0`.

| Component | Compatibility law |
| --- | --- |
| `Copeland.TS.Tool` compiler and language server | Packaged together; their complete versions must match. |
| `Copeland.TS.Sdk` and `tscl` | Major and minor versions must match. The SDK emits `COPE-DIST-0001` before compilation for a declared mismatch. |
| VS Code extension, server, and project SDK | Major and minor versions must match. The extension refuses to attach and says to update the toolchain. |
| Workspace ownership schema | Independently validated as schema version 1 by the language server. |
| Copeland NuGet and npm contract schemas | Independently validated as schema version 1 by their existing package/contract readers. |

For example, a project that requests `0.2.x` while `0.1.x` is installed must
report: `Project requires Copeland TS 0.2.x. Installed compiler is 0.1.x.
Update the Copeland toolchain.` It must not continue into unrelated compiler or
MSBuild failures.

`tscl install-info --format json` exposes the tool, compiler, language-server,
and schema values as stable machine-readable data. `tscl doctor --format json`
adds project and workspace observations.
