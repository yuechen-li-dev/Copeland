# CTS-COPLAND-WEBSITE-M0

This is a Copeland TS React website proof. React owns semantic browser DOM,
and TSPack owns dependency materialization, build, RunTarget hosting, browser
inspection, and shutdown. `src/Main.ts` owns the small typed reducer and
`src/App.tsx` owns the component tree.

The desktop hero is authored in native Copeland Machina source at
`machina/Hero.machina.ts`. The `generate-machina` TSPack target compiles it to
MIR, resolves its `Anchor`/`VStack`/`HStack` geometry, and emits React-facing
class accessors plus CSS. React attaches those classes to semantic elements and
keeps ownership of DOM, event handling, and accessibility. The rest of the
page remains tokenized CSS while the native profile gains responsive variants,
wrapping, and a broader semantic vocabulary.

Run the current sibling TSPack source:

```powershell
cd C:\Users\yuech\source\repos\tspack
go run ./cmd/tspack update --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0
go run ./cmd/tspack sync --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0
go run ./cmd/tspack run --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0 generate-machina --once
go run ./cmd/tspack build --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0 browser
go run ./cmd/tspack run --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0 site --once
```
