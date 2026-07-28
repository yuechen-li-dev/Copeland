# Copeland M0 installation

Copeland M0 is packaged for a local feed only. It is not on public NuGet or the
VS Code Marketplace.

1. Build the local artifacts from the repository:

   ```powershell
   .\tools\Invoke-CopelandDistributionProof.ps1
   ```

   The feed is `artifacts/cts-distribution-m0/packages` and contains the
   NuGet packages, `copeland-ts-0.1.0.vsix`, and the separate
   `TSPack.Tool.0.1.7-win-x64.zip`. The script canonicalizes package ZIP
   metadata so identical local builds are byte-stable.

2. Install the tool, templates, and VSIX:

   ```console
   dotnet tool install --global Copeland.TS.Tool --version 0.1.0 --add-source <feed>
   dotnet new install Copeland.TS.Templates@0.1.0 --nuget-source <feed>
   code --install-extension <feed>/copeland-ts-0.1.0.vsix
   ```

   For the React template, extract the separate TSPack archive and add its
   folder to PATH. Pure CLR and mixed-workspace templates do not need it.

   ```powershell
   Expand-Archive <feed>/TSPack.Tool.0.1.7-win-x64.zip <tools>
   $env:Path = "<tools>/tspack-windows-amd64;$env:Path"
   ```

3. Create and run an application:

   ```console
   dotnet new copeland-react -n HelloCopeland
   cd HelloCopeland
   tscl workspace sync
   tspack run web
   ```

`tspack run web` starts and supervises ASP.NET Core, prints the local URL, and
cleans up the host. The button calls an API backed by a Copeland-compiled
function. `dotnet run` remains useful for direct CLR debugging; the normal
browser lifecycle belongs to TSPack. The console and library templates have no
Node.js or TSPack dependency.

Open a generated folder with `code .`. A mixed workspace requires one
additional explicit operation after creation or ownership changes:

```console
tscl workspace sync
```

Then the VS Code extension uses the generated ownership map to route only
`tscl`-owned files to Copeland. Normal TypeScript files remain owned by the
built-in TypeScript extension.

Update and uninstall:

```console
dotnet tool update --global Copeland.TS.Tool --version 0.1.0 --add-source <feed>
dotnet new update
code --install-extension <feed>/copeland-ts-0.1.0.vsix --force
dotnet tool uninstall --global Copeland.TS.Tool
dotnet new uninstall Copeland.TS.Templates
code --uninstall-extension copeland.copeland-ts
```
