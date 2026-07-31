# Copeland TS Preview 1 quickstart

Copeland TS `0.1.0-preview.1` lets one normal .NET 10 project compile Copeland
`.ts`, language-native `.tsx`, `.tsxtest`, and authored C# together.

## Available after publication

Install the .NET 10 SDK, Node.js/npm, VS Code 1.99 or newer, and then run:

```powershell
dotnet tool install --global Copeland.TS.Tool `
    --version 0.1.0-preview.1

Invoke-WebRequest `
    https://github.com/yuechen-li-dev/Copeland/releases/download/v0.1.0-preview.1/BootstrapTemplate.tsx `
    -OutFile BootstrapTemplate.tsx

tscl template materialize BootstrapTemplate.tsx `
    --entry BootstrapTemplate `
    --name HelloCopeland `
    --output ./HelloCopeland

cd HelloCopeland
npm install
dotnet build
dotnet test
dotnet run
code .
```

Install the matching editor package before opening the project:

```powershell
Invoke-WebRequest `
    https://github.com/yuechen-li-dev/Copeland/releases/download/v0.1.0-preview.1/copeland-ts-0.1.0-preview.1.vsix `
    -OutFile copeland-ts-0.1.0-preview.1.vsix
code --install-extension ./copeland-ts-0.1.0-preview.1.vsix --force
```

Open a `.ts`, `.tsx`, or `.tsxtest` file. The Copeland status item and
**Copeland TS** output channel show `tscl` ownership, the selected tool, and the
language-server version. Use **Copeland: Show File Owner** when ownership is not
obvious.

For project-local CLI use, install the npm launcher instead of relying on a
global command:

```powershell
npm install --save-dev @copeland/tscl@0.1.0-preview.1
npx tscl --version
```

Preview 1's npm package is validated on Windows x64 and requires .NET 10.

## Local release-candidate testing before publication

The prepared artifact directory contains every input needed for an isolated
dry run. From the repository root:

```powershell
./tools/Test-CopelandPreviewPackages.ps1 `
    -ReleaseRoot ./artifacts/releases/0.1.0-preview.1
```

This command installs only from the packed NuGet and npm artifacts, generates
from the release copy of `BootstrapTemplate.tsx`, builds/tests/runs the result,
and installs the VSIX into an isolated profile. It does not publish anything.

Do not use candidate-feed flags in the public workflow. Before publication,
the public-registry commands are expected to fail because the version does not
yet exist on NuGet.org or npm.

## Troubleshooting

- `tscl --version`, `npx tscl --version`, the SDK, and the VSIX must all report
  `0.1.0-preview.1`.
- The npm launcher explains how to install .NET 10 when the runtime is absent.
- A workspace `.config/dotnet-tools.json` containing `Copeland.TS.Tool` takes
  precedence over global `tscl`; `copeland.tsclPath` is an explicit override.
- `tsconfig.tsx` is the typed workspace and ownership authority. Copeland does
  not use `tsconfig.json` for the generated project.
- The lodash package contract in the bootstrap is intentional and does not
  imply arbitrary Node package compatibility.
