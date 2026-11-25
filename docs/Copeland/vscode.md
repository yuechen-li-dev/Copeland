# Copeland TS for VS Code

Copeland TS is a project-aware VS Code extension for workspaces that use
`tsconfig.tsx`. It does not replace VS Code TypeScript globally. The generated
workspace metadata decides which implementation owns each source file.

For a TSPack workspace rooted at `manifest.tsx`, the language server uses the
first declared target with a materialized descriptor under
`.tspack/build-manifests`. It takes package contracts from that target and
source ownership from the current workspace. Open buffers overlay the disk
contents so hover and diagnostics update before a save. With no descriptor,
the server uses the standalone project context.

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

The extension normally runs `tscl workspace sync` when a Copeland workspace
opens and when source files are added or removed. Use the command manually to
refresh ownership on demand.

```text
tscl workspace sync
```

It creates `obj/copeland/workspace/editor-ownership.generated.json`. That file
records the compiler's ownership decisions. Explicit file profiles are also
checked by the compiler. The extension does not guess ownership from syntax.

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

`tsconfig.tsx` and the root TSPack `manifest.tsx` use the Copeland TSX
language ID. The language server checks the TSPack manifest with Copeland's
manifest parser.
The extension does not infer TS-XML profiles: the server evaluates the
metadata-declared project for each Copeland project snapshot.

## Commands and status

The active TypeScript editor shows either `TypeScript: tsc` or `Copeland TS:
tscl` in the status bar. Click it to view the generated rule and project.

- `Copeland: Workspace Sync` and `Copeland: Workspace Validate` run the
  existing `tscl workspace` commands in the workspace root.
- `Copeland: Reload Workspace Ownership` reloads the generated metadata and
  switches open documents without restarting VS Code.
- `Copeland: Generate NuGet Bindings` restores the declared NuGet packages
  and writes `src/generated/Nuget.ts`. A missing generated import offers the
  same action as a quick fix. Open files refresh when generation finishes.
- `Copeland: Build Project` and `Copeland: Run Project` run `dotnet build` or
  `dotnet run` for the active `tscl` file's generated project. They refuse to
  guess when there is no associated project; set `copeland.projectPath` for an
  explicit override.
- `Copeland: Restart Language Server` restarts the one server for the active
  workspace. `Copeland: Show Language Server Output` opens the shared output
  channel. Server stderr and optional protocol traces go there; protocol stdout
  remains clean.
- `Copeland: Open tsconfig.tsx` opens the workspace authority.

`copeland.workspace.autoSync` is on by default. Set it to `false` to refresh
source ownership manually.

## Linters in mixed workspaces

ESLint's default language selectors cover ordinary TypeScript and TSX, so it
continues to report warnings in those files while Copeland reports diagnostics
in Copeland buffers. This boundary also applies to other extensions that use
language IDs. For a linter or formatter that scans by path, scope its inputs
to the traditional TypeScript source paths. A terminal TypeScript check can
use `tsconfig.generated.json`, which lists the `tsc` partition.
[ESLint extension settings](https://github.com/microsoft/vscode-eslint#settings-options)

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
