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

Copeland refreshes workspace ownership on open and after files are added or
removed. It gives `tscl` sources and the project manifests a Copeland language
ID. Built-in TypeScript and linters such as ESLint continue to serve the
traditional TypeScript files. Linters that scan by file path can use the
traditional TypeScript source paths as their input.

Use **Copeland: Generate NuGet Bindings** when the editor offers a quick fix
for a missing generated NuGet import. The command restores the declared
packages and refreshes the open files.
