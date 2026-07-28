# Copeland TS tool

`Copeland.TS.Tool` installs the `tscl` command. It contains the compiler,
workspace tools, and the language server; the VS Code extension discovers this
single command and launches `tscl language-server`. No repository path or
separate language-server DLL is required by a user project.

Install from the configured Copeland NuGet feed:

```console
dotnet tool install --global Copeland.TS.Tool --version 0.1.0 --add-source <feed>
```
