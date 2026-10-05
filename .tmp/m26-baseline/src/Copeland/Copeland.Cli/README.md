# Copeland TS tool

`Copeland.TS.Tool` installs the `tscl` command. It contains the compiler,
workspace tools, and the language server; the VS Code extension discovers this
single command and launches `tscl language-server`. No repository path or
separate language-server DLL is required by a user project.

After publication, install from NuGet.org:

```console
dotnet tool install --global Copeland.TS.Tool --version 0.1.0-preview.1
tscl --version
```
