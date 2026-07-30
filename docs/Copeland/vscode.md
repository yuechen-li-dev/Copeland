# Copeland TS for VS Code

Copeland TS is a project-aware VS Code extension for workspaces that use
`tsconfig.tsx`. It does not replace VS Code TypeScript globally. The generated
workspace metadata decides which implementation owns each source file.

For a TSPack workspace rooted at `manifest.tsx`, the language server instead
opens the materialized manifest project context under
`.tspack/build-manifests`. It sees the same sources, npm contracts, browser
contracts, and TSX profile as the TSPack compiler invocation. Open buffers are
immutable text overlays on that context, so stream/layout hover and diagnostics
update before a document is saved. TSPack materialization remains explicit: the
language server never installs packages or starts a browser.

## Install and build

From the repository checkout:

```powershell
cd src/Copeland/Copeland.TS.VSCode
npm install
npm run package
code --install-extension dist/copeland-ts-0.1.0.vsix
```

M0 discovers an installed matching `tscl` command from `PATH`. Set
`copeland.tsclPath` only when the command is not on `PATH` (for example, a
local development `Copeland.Cli.exe`). The VSIX deliberately does not bundle or
download a compiler: the installed toolchain remains the source of the language
server and avoids silent duplicate compiler versions.

## Workspace ownership

Run this once after adding or changing `tsconfig.tsx`:

```text
tscl workspace sync
```

It creates `obj/copeland/workspace/editor-ownership.generated.json`. That file
is the sole routing input read by the extension. It is not reconstructed from
include/exclude glob patterns and source syntax is never inspected to decide
ownership.

For each listed source, the extension applies exactly one route:

| Generated owner | VS Code language ID | Semantic provider |
| --- | --- | --- |
| `tsc` | original `typescript` or `typescriptreact` | built-in TypeScript extension / tsserver |
| `tscl` | `copeland-typescript` or `copeland-typescriptreact` | Copeland LSP |

The two Copeland language IDs are assigned dynamically only to metadata-listed
`tscl` buffers. They have no `*.ts` or `*.tsx` file association, so normal
TypeScript files outside a Copeland workspace are unchanged. Reassigning an
open document triggers VS Code's documented close/open language lifecycle; the
extension preserves unsaved editor buffers and starts no per-document server.

The language-ID boundary also keeps `tscl` buffers out of the built-in
TypeScript extension's TypeScript selectors. This is the supported M0
false-squiggle suppression mechanism; the extension never disables TypeScript
validation globally. `tscl workspace sync` additionally produces a stable
`tsconfig.generated.json` with only `tsc` files for external TypeScript tools.

`tsconfig.tsx` itself is opened with the Copeland TSX language ID so the
language server can provide its bounded manifest completion and hover support.
The extension does not infer TS-XML profiles: the server evaluates the
metadata-declared project for each Copeland project snapshot.

## Commands and status

The active TypeScript editor shows either `TypeScript: tsc` or `Copeland TS:
tscl` in the status bar. Click it to view the generated rule and project.

- `Copeland: Workspace Sync` and `Copeland: Workspace Validate` run the
  existing `tscl workspace` commands in the workspace root.
- `Copeland: Reload Workspace Ownership` reloads the generated metadata and
  switches open documents without restarting VS Code.
- `Copeland: Build Project` and `Copeland: Run Project` run `dotnet build` or
  `dotnet run` for the active `tscl` file's generated project. They refuse to
  guess when there is no associated project; set `copeland.projectPath` for an
  explicit override.
- `Copeland: Restart Language Server` restarts the one server for the active
  workspace. `Copeland: Show Language Server Output` opens the shared output
  channel. Server stderr and optional protocol traces go there; protocol stdout
  remains clean.
- `Copeland: Open tsconfig.tsx` opens the workspace authority.

`copeland.workspace.autoSync` is off by default. When enabled, changing
`tsconfig.tsx` invokes the same explicit workspace-sync command; it never
edits user settings globally.

## Compatibility and troubleshooting

At startup the client runs `tscl language-server --version`. The extension and
server must agree on major/minor version. If the evaluated project declares a
Copeland package/property version, it must agree as well. A mismatch leaves
the file on its Copeland language ID (therefore still prevents false tsserver
diagnostics) and shows one actionable toolchain-update message instead of
parser noise.

If generated ownership is absent or invalid, VS Code shows one notification:
`Copeland workspace metadata is missing. Run: tscl workspace sync`. Use its
button or the command palette. Metadata, `tsconfig.tsx`, and associated project
changes are watched. A successful sync reloads the map, reroutes open files,
and clears stale Copeland diagnostics when a buffer transfers back to `tsc`;
it does not require an editor restart.

M0 supports workspace folders independently, one language server per folder
that has `tscl` files. It intentionally does not provide remote/web LSP,
debugging, formatting, renames, refactorings, a test explorer, or marketplace
publication.

## Incremental adoption

A project can use `tsconfig.tsx` while retaining every source file under
`tsc`. Copeland migration is optional: move one explicit include boundary to
`tscl`, run sync, and VS Code will switch just that metadata-listed boundary.
