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

## Standalone Node project checks

`tscl check --standalone <project-root>` loads the project's manifest and
`tsconfig.tsx`, checks the Copeland-owned sources, and reports diagnostics.
The command stops before MIR lowering and writes no build artifacts.

The standalone NativeAOT executable provides the same check command without
requiring a .NET runtime on the destination machine. Publish it into its own
directory to keep the full managed tool available for build, CLR interop, and
language-server commands.

```powershell
dotnet publish src/Copeland/Copeland.Cli.Check -c Release -r win-x64 -o artifacts/local/tscl-native-check
artifacts/local/tscl-native-check/tscl.exe check --standalone ../jsdiff/copeland-port/m3
```

On Windows, `scripts/Benchmark-CopelandNativeCheck.py` runs the native command
against JsDiff's pinned tsgo and tsrs with fresh processes and records the raw
wall time and peak working-set samples. It uses the sibling JsDiff checkout
and its installed benchmark dependencies by default.

```powershell
python scripts/Benchmark-CopelandNativeCheck.py --native artifacts/local/tscl-native-check/tscl.exe
```
