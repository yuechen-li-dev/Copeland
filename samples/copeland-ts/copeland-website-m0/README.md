# CTS-MACHINA-LAYOUT-PROFILES-M1

This is a Copeland TS React website proof with three explicit root layouts.
React owns semantic browser DOM, TSPack owns materialization/build/host/browser
scenario/shutdown, `src/Main.ts` owns the typed reducer, and `src/App.tsx`
contains shared components and the one selected composition.

`machina/LayoutProfiles.machina.ts` authors `DesktopLayout`, `TabletLayout`,
and `MobileLayout`. The profile law is `<600` mobile, `600-1023` tablet, and
`>=1024` desktop. The generator resolves each native tree and emits namespaced
React class accessors plus CSS. React renders only the active root; the reducer
preserves copy feedback/selected section while safely closing the mobile menu
on exit from mobile. CSS handles local adaptation (tokens, chip wrapping,
touch spacing, focus, reduced motion), never root identity.

Run the current sibling TSPack source:

```powershell
cd C:\Users\yuech\source\repos\tspack
go run ./cmd/tspack update --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0
go run ./cmd/tspack sync --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0
go run ./cmd/tspack run --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0 generate-machina --once
go run ./cmd/tspack build --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0 browser
go run ./cmd/tspack run --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0 site --once
go run ./cmd/tspack scenario C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0\scenarios\layout-profiles-m1.json --run site --root C:\Users\yuech\source\repos\Copeland\samples\copeland-ts\copeland-website-m0
```

Scenario screenshots are generated proof, ignored by Git, under
`artifacts/cts-machina-layout-profiles-m1/`.
