# Copeland TS Preview quickstart

This walkthrough uses the packaged artifacts only. The sample is
`samples/copeland-ts/CopelandHello`.

## Install

Install .NET 10, Node.js/npm, VS Code 1.99 or newer, and a normal C# extension
if you want C# editor features.

Install the matching Copeland tool and VSIX:

```powershell
dotnet tool install --global Copeland.TS.Tool --version 0.1.0-preview.1
code --install-extension copeland-ts-0.1.0-preview.1.vsix
```

For a local release-candidate feed, add `--add-source <package-directory>` to
the `dotnet tool install` command.

## Open, build, and run

```powershell
cd samples/copeland-ts/CopelandHello/CopelandHello
npm install
dotnet restore
dotnet build
dotnet run
```

Open `samples/copeland-ts/CopelandHello` or its `CopelandHello.slnx` in VS
Code. Open `CopelandHello/src/copeland/Program.ts`.

The status bar says **Copeland TS: tscl** for Copeland-owned source and
**TypeScript: tsc** for `src/traditional/Traditional.ts`. The separate
`copeland-typescript` language identity prevents built-in TypeScript from
validating tscl-owned source. It does not disable TypeScript globally.

`tsconfig.tsx` is the only ownership authority. Its `tscl.include` selects
Copeland files, its `tsc.include` selects traditional TypeScript files, and
strict ownership makes overlap an error. The compiler, SDK-generated ownership
metadata, language server, and extension all use this resolution.

## Troubleshooting

- Run **Copeland: Show File Owner** to explain the current file.
- Run **Copeland: Restart Language Server** after changing tools.
- Open **Copeland: Show Language Server Output** to see the selected executable,
  discovery source, version, project path, and errors.
- Missing tool: run
  `dotnet tool install --global Copeland.TS.Tool --version 0.1.0-preview.1`.
- Version mismatch: update the VSIX, tool, and `Copeland.TS.Sdk` package to the
  same `0.1` preview train.
- A workspace `dotnet-tools.json` (or legacy `.config/dotnet-tools.json`)
  containing `Copeland.TS.Tool` is used before global PATH.
  `copeland.tsclPath` is an explicit override.
- npm support in this preview is contract-based. The sample's
  `contracts/lodash-es.json` is intentional; arbitrary npm packages are not
  implied.

There is no `tsconfig.json`, repository checkout, extension-development host,
manual language-server command, TSPack step, or generated props import in this
workflow.
