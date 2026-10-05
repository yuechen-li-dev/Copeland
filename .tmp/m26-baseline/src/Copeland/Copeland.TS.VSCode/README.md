# Copeland TS for VS Code

Preview language support for writing TypeScript-shaped Copeland source and C# in
one normal .NET project.

## Install

1. Install .NET 10 and VS Code 1.99 or newer.
2. Install the matching compiler:

   ```powershell
   dotnet tool install --global Copeland.TS.Tool --version 0.1.0-preview.1
   ```

3. Install this VSIX:

   ```powershell
   code --install-extension copeland-ts-0.1.0-preview.1.vsix
   ```

4. Open a folder containing `tsconfig.tsx`.

The status bar reports whether the current TypeScript file is owned by `tscl`
or ordinary TypeScript (`tsc`). Copeland uses the workspace's local .NET tool
manifest when it contains `Copeland.TS.Tool`; otherwise it uses global `tscl`.
An explicit `copeland.tsclPath` setting takes precedence over both.

Use **Copeland: Show File Owner** for ownership details, **Copeland: Restart
Language Server** after changing tools, and **Copeland: Show Language Server
Output** for the selected executable, version, project, and failures.

Copeland activates only in workspaces containing `tsconfig.tsx`. It assigns a
separate VS Code language identity only to `tscl`-owned files, so built-in
TypeScript remains active for `tsc`-owned files and unrelated projects.

This Preview supports specifically contracted npm packages; it does not promise
general Node or `tsconfig.json` compatibility.
