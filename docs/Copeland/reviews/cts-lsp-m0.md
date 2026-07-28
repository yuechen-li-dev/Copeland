# CTS-LSP-M0 review

## Status

CTS-LSP-M0 establishes the editor-independent Copeland language-server process and its ownership/document-synchronization core. It does not include a VS Code or Visual Studio client and it does not replace `tsserver` for `tsc`-owned files.

## Architecture

`tscl workspace sync` produces `obj/copeland/workspace/editor-ownership.generated.json`. The server loads that artifact at initialization and treats it as the routing law. It does not infer ownership from extensions or source syntax. Open buffers overlay disk content in a resident `CopelandProjectSnapshot`, then invoke the existing project compiler path.

`CopelandProjectCompiler.CreateSnapshot` is the public boundary. It holds resolved source inputs and compiler options (including TS-XML profile, npm graph, package contracts, CLR references, target/backend options, and asset source), accepts immutable unsaved overlays, and returns project diagnostics plus per-module bound facts, imports, exports, source spans, and nominal identities. The MSBuild compiler task and the LSP both call this boundary; the LSP does not own a separate parser, binder, or module resolver.

`CopelandProjectModelLoader` invokes the SDK's read-only `CopelandWriteLanguageServiceModel` target through `dotnet msbuild`. That target depends on `ResolveReferences` and serializes the exact evaluated `@(CopelandCompile)`, `@(ReferencePath)`, `@(ReferencePathWithRefAssemblies)`, `@(CopelandPackageContract)`, and `@(CopelandNpmContract)` inputs used by normal compilation. The loader turns those facts into snapshot sources, CLR references, package contracts, npm/materialization contracts, and the evaluated `CopelandTsXmlProfile`.

The runnable artifacts are `Copeland.TS.LanguageServer.dll` and the CLI command:

```text
tscl language-server
tscl language-server --version
```

The transport is standard LSP JSON-RPC over stdio; logs are reserved for stderr.

## Initialization

The supported initialization options are:

```json
{
  "workspaceRoot": "C:/work/App",
  "ownershipFile": "C:/work/App/obj/copeland/workspace/editor-ownership.generated.json",
  "tsXmlProfile": "react-m0"
}
```

`workspaceRoot` (or LSP `rootUri`) is required. The ownership path defaults to the generated workspace path. Missing or unsupported metadata produces one actionable ownership diagnostic: `Run tscl workspace sync`.

## Implemented protocol surface

Supported methods are `initialize`, `shutdown`, `exit`, `textDocument/didOpen`, `didChange`, `didClose`, `hover`, `completion`, `definition`, `documentSymbol`, `semanticTokens/full`, and basic `signatureHelp`.

The server publishes canonical parser/binder diagnostics for `tscl` documents only. It ignores stale document versions and accepts one full-text change per `didChange`, avoiding accidental mutation of disk files. A `tsc` document is cleanly declined by publishing no Copeland diagnostics or features.

Hover, completion, definition, document symbols, and tokens are built from compiler syntax/bound module declarations. Syntax declarations add record/enum/table/column visibility even where the current binder has no public type-symbol lookup API. TS-XML uses the explicit `react-m0` initialization profile; `.tsx` alone continues to select no React profile. `tsconfig.tsx` is treated as a workspace-manifest document with bounded workspace vocabulary completion and ownership guidance.

## Measured validation

The process protocol test starts the compiled server, initializes a generated mixed-ownership workspace, opens an unsaved multi-module Copeland buffer, requests imported-symbol completion and definition, changes it to an invalid buffer, and opens a `tsc` file. The focused test completed in 215 ms on the current development machine (including process launch and test harness). The direct snapshot test proves an overlay affects the shared project compilation without disk writes.

Ownership metadata and the evaluated `.csproj` timestamp are checked before requests. Either change rebuilds the snapshot. This handles `tscl workspace sync` and project-profile/reference changes without a server restart.

## Artifact hygiene

The standalone-web fixture previously copied the CLI payload to the tracked `frontend/compiler/` directory on every solution build. It now stages that reproducible payload at `frontend/.copeland/compiler/`, and its TSPack manifest uses that path. The narrow ignore rules are:

- `samples/copeland-ts/standalone-web-m0/frontend/.copeland/compiler/` — current build staging payload, regenerated from the CLI project reference;
- `samples/copeland-ts/standalone-web-m0/frontend/compiler/*` — prevents new files in the legacy payload directory (including LSP sidecar files) from being accidentally added before its tracked contents can be removed in a dedicated cleanup commit.

The existing tracked `frontend/compiler/Copeland.*` files are reproducible normal build binaries, not canonical fixture inputs. They require a later explicit repository removal commit; this milestone does not remove or reset them. `manifest.tsx` and `ts-lock.toml` remain canonical fixture inputs.

## Current limitations and next milestone

This is a coherent server foundation, not an honestly complete implementation of every CTS-LSP-M0 capability. The server now evaluates real Copeland projects and automatically loads source items, CLR references, native package contracts, npm/materialization contracts, and the selected TS-XML profile. It compiles the whole owned project and supports imported local module completion and definition. Semantic tokens remain basic syntax/bound classifications, and manifest diagnostics are bounded vocabulary validation rather than the full workspace resolver diagnostic set. CLR completion is deliberately limited to `using Namespace.` and CLR navigation has no source location for binary-only members. Row-view member completion also remains follow-up work.

The recommended next milestone is CTS-LSP-M1: deepen context-aware completion, add binary metadata navigation for CLR members, and broaden workspace-manifest diagnostics before any editor client is built.

## Additional work performed

- Added a standalone executable LSP host and `tscl language-server` route; required to give future clients a deterministic process boundary.
- Added generated-ownership consumption and in-memory document overlays; required to prevent fake TypeScript diagnostics and avoid save-to-diagnose behavior.
- Added a process-level JSON-RPC protocol test; required to validate the actual stdio transport without an editor.
- Added the public compiler project snapshot and moved MSBuild project compilation onto it; required to make normal builds and language-server project analysis share one source/module model.
- Moved standalone-web compiler staging to an ignored deterministic location; required to stop normal builds from changing tracked fixture binaries.
- Added a read-only evaluated MSBuild project-model target and loader; required to supply the LSP with the exact source/reference/package/profile inputs that normal project compilation receives.

### Follow-up: evaluated npm and CLR contract surface

`CopelandNpmContract` is now an evaluated MSBuild item. It is passed to normal
`CopelandCompile` and serialized by `CopelandWriteLanguageServiceModel`; the
loader reads the same versioned JSON contract into `CopelandProjectSnapshot`.
The resident server therefore gets npm materialization state, function exports,
and React component contracts without inspecting `node_modules`. Package and
npm definitions navigate to their declared contract file. CLR completion uses
the existing compiler metadata resolver only within an authored `using X.`
context, and CLR hover reports a uniquely resolved visible type.
