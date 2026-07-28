# Copeland installation troubleshooting

- `tscl` is not recognized: reopen the terminal after `dotnet tool install`,
  or configure `copeland.tsclPath` in VS Code to the installed command.
- Workspace metadata is missing: run `tscl workspace sync` from the folder
  containing `tsconfig.tsx`.
- The extension reports a version mismatch: run `tscl install-info`, update
  the tool, SDK package, and VSIX to the same M0 major/minor train, then run
  **Copeland: Restart Language Server**.
- Restore cannot find `Copeland.TS.Sdk`: add the local feed to `NuGet.config`,
  or pass `--source <feed>` during restore.
- A browser template that declares `manifest.tsx` fails doctor: extract the
  separate `TSPack.Tool` archive and place `tspack` on PATH. Pure CLR and
  workspace-only templates do not require it.
- A Copeland file still has TypeScript diagnostics: run workspace sync, then
  **Copeland: Reload Workspace Ownership**. Verify the file with
  `tscl workspace owner path/to/file.ts`.
- Ownership appears stale: delete only the generated
  `obj/copeland/workspace` directory, run workspace sync, and reload ownership.
