# CTS-VSCODE-M0 review

## Status

CTS-VSCODE-M0 adds the first thin VS Code client at
`src/Copeland/Copeland.TS.VSCode`. The client consumes generated ownership
metadata, launches the existing LSP, and deliberately leaves conventional
TypeScript editing to VS Code. It contains no compiler, parser, project
resolver, or glob evaluator.

## Routing design

The client reads schema-1
`obj/copeland/workspace/editor-ownership.generated.json` and maps only its
explicit `files` entries. A `tscl` source is dynamically reassigned with
`vscode.languages.setTextDocumentLanguage` to `copeland-typescript` or
`copeland-typescriptreact`; a later transfer restores its original built-in
TypeScript ID. `tsc` files are never reassigned.

This is intentionally not static file association. Static `*.ts` association
would claim the entire workspace and cannot express generated per-file
ownership. The VS Code API specifies that language reassignment emits a close
followed by open event, which is why the routing code is idempotent and the LSP
has a selector limited to Copeland IDs. Built-in TypeScript support selects its
own TypeScript IDs, so it does not publish semantic diagnostics for an owned
Copeland buffer. The generated TypeScript configuration remains the companion
solution for external `tsc` consumers.

The TextMate grammar adds only Copeland keywords and reuses the built-in
TypeScript/TSX scopes for baseline coloring. Accurate classification is served
by the LSP semantic-token capability.

## Lifecycle and compatibility

One `LanguageClient` is created per workspace folder containing `tscl` files;
it starts `tscl language-server` with stdio and passes workspace root,
ownership path, project, client version, expected server version, and trace
level as initialization options. Logs use the `Copeland TS Language Server`
Output channel. The client checks `tscl language-server --version` before
launch and optionally compares an explicit project Copeland version.

The M0 VSIX discovers a matching installed `tscl` rather than bundling or
downloading binaries. This keeps the extension small and makes compiler/LSP
version ownership explicit. It means a normal installed toolchain is required;
distribution packaging and marketplace delivery remain a later milestone.

Metadata changes reroute open documents without a VS Code restart. The existing
server observes the same metadata before requests, while a language-ID close
clears diagnostics for a source transferred back to `tsc`; no client cache
becomes an ownership source of truth. Missing metadata is reported once with a
sync action; mismatch and unavailable-server states remain visibly surfaced in
the active-file status item rather than as fake parser diagnostics.

## Proof and validation

The accepted `samples/copeland-ts/workspace-m0` fixture now contains valid
Copeland-only `record table` and `match` syntax in `Domain.ts`, plus a normal
TypeScript type error in `Legacy.ts`. Its project is runnable through a tiny
host `Program.cs`, allowing the extension build/run command path to use the
real associated project.

The extension-host suite opens that fixture with the locally built `tscl`,
proves the custom Copeland language ID and empty diagnostic set for the valid
Copeland buffer, proves the legacy buffer stays `typescript` without Copeland
diagnostics, tests unsaved Copeland diagnostics and clearing after repair, and
changes the generated ownership file to prove open-buffer transfer in both
directions. Unit coverage rejects unsupported metadata rather than falling
back to globs.

## Additional work performed

- Added a dedicated runnable mixed-workspace fixture host; this makes the
  required extension Build/Run command proof use a real project.
- Added a normal TypeScript error to the legacy fixture and Copeland-only table
  syntax to the owned fixture; this gives the editor tests a genuine competing
  diagnostics case.
- Normalized VS Code's escaped Windows file URIs in the language server and
  advertised full-document synchronization. This fixes real unsaved-buffer
  diagnostics without making the extension inspect or reimplement ownership.
- Preserved parser diagnostics in the project compilation path and drained
  MSBuild child-process output asynchronously; these small server/project
  fixes prevent diagnostics loss and an evaluation-pipe deadlock.
- Added precise extension artifact ignores while retaining source, grammars,
  package lock, fixtures, and canonical metadata files.

The standalone-web compiler payload remains generated and ignored under the
existing artifact law; this milestone neither removes nor changes it. The
repository still has unrelated tracked publish binaries under the authoring
food release artifacts and `samples/copeland-ts/react-components-m1/tspack.exe`;
they remain separate cleanup debt.

## Deferred work

Visual Studio support, a debugger, formatter, test explorer, refactorings,
web/remote LSP, marketplace publication, automatic toolchain installation,
and a final distribution installer remain out of scope. The recommended next
step is a distribution/compatibility milestone that packages a signed matching
toolchain and defines update policy before Visual Studio work.
