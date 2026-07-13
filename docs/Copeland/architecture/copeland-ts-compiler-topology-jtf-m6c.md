# Copeland TS compiler topology (JTF-M6c)

## Current topology

```text
Copeland.TS
  TypeScript-shaped syntax, binding, diagnostics, and lowering
        |
        v
Copeland.TS.Mir
  Cope MIR model and deterministic .cope text writer
        |
        +--> Copeland.TS.Backend.CSharp -> generated .g.cs proof output
        |
        +--> future Copeland.TS.Backend.JavaScript -> generated .g.js output
```

`Copeland.Cli` is the composition host. It obtains Cope MIR from `Copeland.TS`, writes MIR directly for `--emit mir`, and invokes the C# backend for `--emit csharp`. The C# backend does not reference the frontend; it consumes only `Copeland.TS.Mir`.

The three-project split is justified by the real boundary: `MirProgram` and `MirTextWriter` have no TypeScript, frontend diagnostic, Roslyn, CLI, Markdown, Aurelian, or Machina dependency. The former `MirType.From(TypeSymbol)` helper and lowering result diagnostics remain frontend-local. The C# backend has its own backend diagnostic record, which avoids a reverse frontend dependency. No universal backend, pass, or IR interface is introduced.

## File doctrine

| Pattern | Owner and meaning |
| --- | --- |
| `*.ts` | Copeland TS program source. |
| `*.cope` | Deterministic textual projection of Cope MIR; currently expected MIR output. |
| `*.g.cs` | Generated output from the C# proof backend. |
| `*.g.js` | Reserved future JavaScript output. |
| `*.xtest.tsx` | TSPack-owned executable test source, including TSX `<Fact>` declarations and related vocabulary. |

`.cope` is neither a source-test dialect nor a universal compiler input. There is no `.cope` parser or verifier. Add one only when a real MIR interchange, debugging, or toolchain consumer requires it.

## Tests and fixtures

The Copeland TS corpus is owned by `tests/Copeland/Copeland.TS.Tests/TestData/Corpus`. Program sources are `.ts`; each case may carry `.tokens.txt`, `.diagnostics.txt`, `.tree.txt`, `.cope`, and `.g.cs` artifacts. The frontend tests own source-to-MIR coverage. `Copeland.TS.Backend.CSharp.Tests` owns expected C# output plus the single Roslyn/runtime proof path. CLI subprocess tests stay in `Copeland.Cli.Tests`.

Use the focused lane for ordinary compiler work:

```powershell
dotnet build Copeland.TS.slnx
dotnet test Copeland.TS.slnx --no-build
```

`Copeland.TS.slnx` intentionally excludes Markdown, Aurelian, Machina, samples, and CLI tests so a future JavaScript backend can iterate independently.

## Compatibility and semantics

No published or external consumer was found: all prior use was inside this repository (CLI, tests, and the retired M6b probe). The proof-era Script identity was therefore cleanly renamed to `Copeland.TS`; no forwarding assembly or compatibility shim remains. Existing language semantics and deterministic MIR/C# output are unchanged.
